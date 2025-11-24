using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Entities;
using Services.UI;
using Services.Utilities;

namespace Services.Memory;

/// <summary>
/// Enhanced file watcher that supports multiple document types including PDFs.
/// Extends the original KnowledgeFileWatcher with multi-format support.
/// </summary>
public class MultiFormatFileWatcher
{
    private readonly string _inboxFolder;
    private readonly string _archiveFolder;
    private readonly PdfFragmentProcessor? _pdfProcessor;

    // Supported file extensions
    private static readonly string[] SupportedExtensions = { ".txt", ".pdf", ".md", ".json" };

    // Maximum chunk size for TXT file embedding (no minimum enforced)
    private const int MaxChunkSize = 1500;

    public MultiFormatFileWatcher(
        string inboxFolder, 
        string archiveFolder,
        PdfFragmentProcessor? pdfProcessor = null)
    {
        _inboxFolder = inboxFolder ?? throw new ArgumentNullException(nameof(inboxFolder));
        _archiveFolder = archiveFolder ?? throw new ArgumentNullException(nameof(archiveFolder));
        _pdfProcessor = pdfProcessor ?? new PdfFragmentProcessor();

        // Ensure folders exist
        Directory.CreateDirectory(_inboxFolder);
        Directory.CreateDirectory(_archiveFolder);
    }

    /// <summary>
    /// Scans the inbox folder for all supported file types.
    /// </summary>
    public async Task<Dictionary<string, string>> DiscoverNewFilesAsync()
    {
        var newFiles = new Dictionary<string, string>();

        foreach (var extension in SupportedExtensions)
        {
            var files = Directory.GetFiles(_inboxFolder, $"*{extension}", SearchOption.TopDirectoryOnly);
            
            foreach (var filePath in files)
            {
                var fileName = Path.GetFileNameWithoutExtension(filePath);
                newFiles[fileName] = filePath;
            }
        }

        return await Task.FromResult(newFiles);
    }

    /// <summary>
    /// Processes any supported file type and returns memory fragments.
    /// Automatically detects file type and uses appropriate processor.
    /// 
    /// For TXT files:
    /// - Game name comes from first line of file
    /// - Categories are "GameName - SectionHeader"
    /// 
    /// For JSON files:
    /// - Structured format with game name, sections, and page numbers
    /// - Categories are "GameName - SectionHeader"
    /// 
    /// For PDF files:
    /// - Game name extracted from filename
    /// - Categories are "FileName - Chunk N" or "FileName - SectionTitle"
    /// </summary>
    public async Task<List<MemoryFragment>> ProcessFileAsync(string documentName, string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        return extension switch
        {
            ".txt" => await ProcessTextFileAsync(documentName, filePath),
            ".md" => await ProcessMarkdownFileAsync(documentName, filePath),
            ".json" => await ProcessJsonFileAsync(documentName, filePath),
            ".pdf" => await ProcessPdfFileAsync(documentName, filePath),
            _ => throw new NotSupportedException($"File type '{extension}' is not supported")
        };
    }

    /// <summary>
    /// Process plain text file.
    /// Game name is extracted from FIRST LINE of the file.
    /// Categories format: "GameName - SectionHeader"
    /// 
    /// SUPPORTED PAGE NUMBER FORMATS:
    /// - [Page: 5]
    /// - [Page 5]
    /// - --- Page 5 ---
    /// - Page 5
    /// 
    /// HEADER DETECTION RULES:
    /// - Must be on its own line
    /// - < 60 characters
    /// - Title Case (most words start with capital)
    /// - No sentence-ending punctuation (., !, ?)
    /// - Not a bullet point (-, *, •)
    /// 
    /// CHUNK SIZE: Maximum 1500 characters per fragment, no minimum enforced
    /// </summary>
    private async Task<List<MemoryFragment>> ProcessTextFileAsync(string _, string filePath)
    {
        var fragments = new List<MemoryFragment>();

        // Read file content
        var content = await File.ReadAllTextAsync(filePath);
        var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        
        if (lines.Length == 0)
            return fragments;
        
        // FIRST LINE = GAME NAME (not documentName parameter!)
        string gameName = lines[0].Trim();
        
        DisplayService.ShowLoadingFile(gameName, filePath);
        
        // Check if first line looks like a comma-separated list
        bool firstLineIsCommaSeparated = gameName.Count(c => c == ',') >= 2;
        
        if (firstLineIsCommaSeparated)
        {
            var singleFragmentContent = string.Join(Environment.NewLine, lines).Trim();
            if (!string.IsNullOrWhiteSpace(singleFragmentContent))
            {
                // Split into chunks if content is too large
                var chunkedFragments = SplitIntoChunks(singleFragmentContent, gameName, gameName);
                fragments.AddRange(chunkedFragments);
                DisplayService.ShowCollectedSections(fragments.Count, gameName);
            }
            return fragments;
        }
        
        // Process sections with headers and page numbers
        int lineIndex = 1; // Start after game name
        string? currentHeader = null;
        int? currentPageNumber = null;
        var currentContent = new List<string>();
        
        while (lineIndex < lines.Length)
        {
            var line = lines[lineIndex].Trim();
            
            if (string.IsNullOrWhiteSpace(line))
            {
                // Empty line - just skip, don't treat as content
                lineIndex++;
                continue;
            }
            
            // Check if this line is a page number marker
            var pageNumber = ExtractPageNumber(line);
            if (pageNumber.HasValue)
            {
                // Update current page number but don't create new fragment
                currentPageNumber = pageNumber.Value;
                lineIndex++;
                continue;
            }
            
            // Check if this line is a header
            bool isHeader = IsTextFileHeader(line);
            
            if (isHeader)
            {
                // Save previous section if exists
                if (currentHeader != null && currentContent.Count > 0)
                {
                    var content_text = string.Join(Environment.NewLine, currentContent).Trim();
                    if (!string.IsNullOrWhiteSpace(content_text))
                    {
                        // Add page number to content if available
                        if (currentPageNumber.HasValue)
                        {
                            content_text = $"[Page: {currentPageNumber.Value}]\n\n{content_text}";
                        }
                        
                        // Category format: "GameName - SectionHeader"
                        var category = $"{gameName} - {currentHeader}";
                        
                        // Split into chunks if content is too large
                        var chunkedFragments = SplitIntoChunks(content_text, gameName, category);
                        fragments.AddRange(chunkedFragments);
                    }
                }
                
                // Start new section
                currentHeader = line.TrimStart('#').Trim();
                currentContent.Clear();
            }
            else
            {
                // This is content, not a header
                currentContent.Add(line);
            }
            
            lineIndex++;
        }
        
        // Save last section
        if (currentHeader != null && currentContent.Count > 0)
        {
            var content_text = string.Join(Environment.NewLine, currentContent).Trim();
            if (!string.IsNullOrWhiteSpace(content_text))
            {
                // Add page number to content if available
                if (currentPageNumber.HasValue)
                {
                    content_text = $"[Page: {currentPageNumber.Value}]\n\n{content_text}";
                }
                
                var category = $"{gameName} - {currentHeader}";
                
                // Split into chunks if content is too large
                var chunkedFragments = SplitIntoChunks(content_text, gameName, category);
                fragments.AddRange(chunkedFragments);
            }
        }
        
        // If no sections found, treat entire file (after title) as single section
        if (fragments.Count == 0 && lines.Length > 1)
        {
            var allContent = string.Join(Environment.NewLine, lines.Skip(1)).Trim();
            if (!string.IsNullOrWhiteSpace(allContent))
            {
                var chunkedFragments = SplitIntoChunks(allContent, gameName, gameName);
                fragments.AddRange(chunkedFragments);
            }
        }

        DisplayService.ShowCollectedSections(fragments.Count, gameName);
        return fragments;
    }

    /// <summary>
    /// Splits content into chunks of maximum 1500 characters while preserving sentence boundaries.
    /// Each chunk is a separate MemoryFragment.
    /// No minimum chunk size enforced - allows small fragments.
    /// Automatically cleans text to remove special tokens and control characters.
    /// </summary>
    private List<MemoryFragment> SplitIntoChunks(string content, string gameName, string baseCategory)
    {
        var fragments = new List<MemoryFragment>();
        
        // Clean the content before processing
        content = MemoryFragmentCleaner.CleanText(content);
        baseCategory = MemoryFragmentCleaner.CleanText(baseCategory);
        
        // If content fits in one chunk, return it as-is
        if (content.Length <= MaxChunkSize)
        {
            fragments.Add(new MemoryFragment(baseCategory, content));
            return fragments;
        }
        
        // Split into sentences to avoid breaking mid-sentence
        var sentences = System.Text.RegularExpressions.Regex.Split(content, @"(?<=[.!?])\s+");
        
        var currentChunk = new System.Text.StringBuilder();
        int chunkIndex = 1;
        
        foreach (var sentence in sentences)
        {
            // If adding this sentence would exceed max size, save current chunk
            if (currentChunk.Length > 0 && currentChunk.Length + sentence.Length + 1 > MaxChunkSize)
            {
                var category = fragments.Count == 0 ? baseCategory : $"{baseCategory} (Part {chunkIndex})";
                fragments.Add(new MemoryFragment(category, currentChunk.ToString().Trim()));
                currentChunk.Clear();
                chunkIndex++;
            }
            
            // Add sentence to current chunk
            if (currentChunk.Length > 0)
                currentChunk.Append(' ');
            currentChunk.Append(sentence);
        }
        
        // Save final chunk
        if (currentChunk.Length > 0)
        {
            var category = fragments.Count == 0 ? baseCategory : $"{baseCategory} (Part {chunkIndex})";
            fragments.Add(new MemoryFragment(category, currentChunk.ToString().Trim()));
        }
        
        return fragments;
    }

    /// <summary>
    /// Extracts page number from common page marker formats:
    /// - [Page: 5]
    /// - [Page 5]
    /// - --- Page 5 ---
    /// - Page 5 (if on its own line)
    /// </summary>
    private static int? ExtractPageNumber(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;
        
        // Pattern 1: [Page: 5] or [Page 5]
        var match1 = System.Text.RegularExpressions.Regex.Match(line, @"^\[Page:?\s*(\d+)\]$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (match1.Success && int.TryParse(match1.Groups[1].Value, out int page1))
            return page1;
        
        // Pattern 2: --- Page 5 ---
        var match2 = System.Text.RegularExpressions.Regex.Match(line, @"^-+\s*Page\s+(\d+)\s*-+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (match2.Success && int.TryParse(match2.Groups[1].Value, out int page2))
            return page2;
        
        // Pattern 3: Page 5 (standalone, not part of content)
        var match3 = System.Text.RegularExpressions.Regex.Match(line, @"^Page\s+(\d+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (match3.Success && int.TryParse(match3.Groups[1].Value, out int page3))
            return page3;
        
        return null;
    }

    /// <summary>
    /// Process JSON file with structured game knowledge format.
    /// 
    /// Expected JSON structure:
    /// {
    ///   "domainName": "Mansion of Madness",
    ///   "sourceFile": "Rules Reference.pdf",
    ///   "sections": [
    ///     {
    ///       "heading": "Insane Condition",
    ///       "pageNumber": 12,
    ///       "content": "If an investigator has suffered Horror..."
    ///     }
    ///   ]
    /// }
    /// </summary>
    private async Task<List<MemoryFragment>> ProcessJsonFileAsync(string _, string filePath)
    {
        var fragments = new List<MemoryFragment>();

        try
        {
            // Read and parse JSON
            var jsonContent = await File.ReadAllTextAsync(filePath);
            var jsonDoc = System.Text.Json.JsonDocument.Parse(jsonContent);
            var root = jsonDoc.RootElement;

            // Extract domain name (required)
            if (!root.TryGetProperty("domainName", out var domainNameElement))
            {
                throw new InvalidOperationException("JSON file must contain 'domainName' property");
            }

            var domainName = domainNameElement.GetString();
            if (string.IsNullOrWhiteSpace(domainName))
            {
                throw new InvalidOperationException("'domainName' property cannot be empty");
            }

            // Extract source file (required)
            if (!root.TryGetProperty("sourceFile", out var sourceFileElement))
            {
                throw new InvalidOperationException("JSON file must contain 'sourceFile' property");
            }

            var sourceFile = sourceFileElement.GetString();
            if (string.IsNullOrWhiteSpace(sourceFile))
            {
                throw new InvalidOperationException("'sourceFile' property cannot be empty");
            }

            DisplayService.ShowLoadingFile($"{domainName} ({sourceFile})", filePath);

            // Extract sections
            if (!root.TryGetProperty("sections", out var sectionsElement) || sectionsElement.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                throw new InvalidOperationException("JSON file must contain 'sections' array");
            }

            foreach (var section in sectionsElement.EnumerateArray())
            {
                // Extract section heading
                if (!section.TryGetProperty("heading", out var headingElement))
                {
                    Console.WriteLine("[!] Skipping section without 'heading' property");
                    continue;
                }

                var heading = headingElement.GetString();
                if (string.IsNullOrWhiteSpace(heading))
                {
                    Console.WriteLine("[!] Skipping section with empty heading");
                    continue;
                }

                // Extract content
                if (!section.TryGetProperty("content", out var contentElement))
                {
                    Console.WriteLine($"[!] Skipping section '{heading}' without 'content' property");
                    continue;
                }

                var content = contentElement.GetString();
                if (string.IsNullOrWhiteSpace(content))
                {
                    Console.WriteLine($"[!] Skipping section '{heading}' with empty content");
                    continue;
                }

                // Extract optional page number
                int? pageNumber = null;
                if (section.TryGetProperty("pageNumber", out var pageElement))
                {
                    if (pageElement.ValueKind == System.Text.Json.JsonValueKind.Number)
                    {
                        pageNumber = pageElement.GetInt32();
                    }
                }

                // Build fragment content with source and page info
                var fragmentContent = new System.Text.StringBuilder();
                
                // Add source metadata
                fragmentContent.AppendLine($"[Source: {sourceFile}]");
                if (pageNumber.HasValue)
                {
                    fragmentContent.AppendLine($"[Page: {pageNumber.Value}]");
                }
                fragmentContent.AppendLine();
                fragmentContent.Append(content);

                // Create fragment with category: "DomainName - Heading"
                fragments.Add(new MemoryFragment($"{domainName} - {heading}", fragmentContent.ToString()));
            }

            DisplayService.ShowCollectedSections(fragments.Count, domainName);
            return fragments;
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new InvalidOperationException($"Failed to parse JSON file: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Process Markdown file (similar to TXT but with better heading detection).
    /// Game name is extracted from FIRST LINE.
    /// </summary>
    private async Task<List<MemoryFragment>> ProcessMarkdownFileAsync(string _, string filePath)
    {
        var content = await File.ReadAllTextAsync(filePath);
        var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        
        if (lines.Length == 0)
            return new List<MemoryFragment>();
        
        // FIRST LINE = GAME NAME
        string gameName = lines[0].Trim().TrimStart('#').Trim();
        
        DisplayService.ShowLoadingFile(gameName, filePath);
        
        // Remove first line and process rest
        var remainingContent = string.Join(Environment.NewLine, lines.Skip(1));
        
        // Use DocumentChunker for better Markdown parsing
        var chunks = Utilities.DocumentChunker.ChunkByHierarchy(remainingContent, new Utilities.DocumentChunker.ChunkOptions
        {
            MaxChunkSize = 1000,
            OverlapSize = 200,
            KeepHeaders = true
        });

        var fragments = chunks.Select(chunk => new MemoryFragment(
            category: $"{gameName} - {chunk.SectionTitle ?? $"Chunk {chunk.ChunkIndex + 1}"}",
            content: chunk.Content
        )).ToList();

        DisplayService.ShowCollectedSections(fragments.Count, gameName);
        return fragments;
    }

    /// <summary>
    /// Process PDF file using PdfFragmentProcessor.
    /// Game name is extracted from FILENAME (not from PDF content).
    /// Categories format: "FileName - Chunk N" or "FileName - SectionTitle"
    /// </summary>
    private async Task<List<MemoryFragment>> ProcessPdfFileAsync(string documentName, string filePath)
    {
        if (_pdfProcessor == null)
        {
            throw new InvalidOperationException("PDF processor not configured");
        }

        // Extract game name from filename
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var gameName = ExtractGameNameFromFileName(fileName);
        
        DisplayService.ShowLoadingFile(gameName, filePath);

        try
        {
            // Process PDF with game name as collection
            var fragments = await _pdfProcessor.ProcessPdfFileAsync(filePath, gameName);
            DisplayService.ShowCollectedSections(fragments.Count, gameName);
            return fragments;
        }
        catch (NotImplementedException)
        {
            DisplayService.WriteLine($"[!] PDF support not implemented. Install a PDF library (e.g., UglyToad.PdfPig)");
            DisplayService.WriteLine($"    Falling back to filename-only fragment for: {gameName}");
            
            // Create a placeholder fragment with game name from filename
            return new List<MemoryFragment>
            {
                new MemoryFragment(
                    category: gameName,
                    content: $"PDF file: {Path.GetFileName(filePath)} (PDF extraction not configured)"
                )
            };
        }
    }

    /// <summary>
    /// Extracts a clean game name from PDF filename.
    /// Examples:
    /// "Munchkin_Panic_Rules.pdf" -> "Munchkin Panic"
    /// "munchkin-treasure-hunt.pdf" -> "Munchkin Treasure Hunt"
    /// "CastlePanicMunchkin.pdf" -> "Castle Panic Munchkin"
    /// </summary>
    private string ExtractGameNameFromFileName(string fileName)
    {
        // Remove common suffixes
        var cleanName = fileName
            .Replace("_Rules", "", StringComparison.OrdinalIgnoreCase)
            .Replace("-Rules", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Rules", "", StringComparison.OrdinalIgnoreCase)
            .Replace("_Manual", "", StringComparison.OrdinalIgnoreCase)
            .Replace("-Manual", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Manual", "", StringComparison.OrdinalIgnoreCase)
            .Trim();
        
        // Replace separators with spaces
        cleanName = cleanName
            .Replace('_', ' ')
            .Replace('-', ' ');
        
        // Handle camelCase: "MunchkinPanic" -> "Munchkin Panic"
        cleanName = System.Text.RegularExpressions.Regex.Replace(
            cleanName, 
            "([a-z])([A-Z])", 
            "$1 $2"
        );
        
        // Capitalize each word
        var words = cleanName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        cleanName = string.Join(" ", words.Select(w => 
            char.ToUpper(w[0]) + w.Substring(1).ToLower()
        ));
        
        return string.IsNullOrWhiteSpace(cleanName) ? fileName : cleanName;
    }

    /// <summary>
    /// Moves a processed file to the archive folder with a timestamp.
    /// </summary>
    public async Task ArchiveFileAsync(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var archivedFileName = $"{Path.GetFileNameWithoutExtension(fileName)}_{timestamp}{Path.GetExtension(fileName)}";
        var archivePath = Path.Combine(_archiveFolder, archivedFileName);

        File.Move(filePath, archivePath);
        DisplayService.WriteLine($"? Archived: {fileName} ? {archivedFileName}");

        await Task.CompletedTask;
    }

    /// <summary>
    /// Gets the count of all supported files waiting to be processed.
    /// </summary>
    public int GetPendingFileCount()
    {
        return SupportedExtensions
            .SelectMany(ext => Directory.GetFiles(_inboxFolder, $"*{ext}", SearchOption.TopDirectoryOnly))
            .Count();
    }

    /// <summary>
    /// Gets breakdown of pending files by type
    /// </summary>
    public Dictionary<string, int> GetPendingFilesByType()
    {
        return SupportedExtensions.ToDictionary(
            ext => ext,
            ext => Directory.GetFiles(_inboxFolder, $"*{ext}", SearchOption.TopDirectoryOnly).Length
        );
    }

    /// <summary>
    /// Determines if a line is a section header (not content).
    /// 
    /// RULES:
    /// 1. Markdown headers (starts with #) are ALWAYS headers
    /// 2. For plain text:
    ///    - Must be < 60 characters
    ///    - Must NOT end with sentence punctuation (., !, ?)
    ///    - Must NOT start with bullet points (-, *, •)
    ///    - Must NOT contain sentence indicators (: followed by space, or multiple commas)
    ///    - At least 50% of words should start with uppercase (Title Case)
    /// </summary>
    private static bool IsTextFileHeader(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;
        
        // Rule 1: Markdown headers
        if (line.StartsWith("#"))
            return true;
        
        // Rule 2: Plain text header checks
        
        // Length check - headers should be relatively short
        if (line.Length > 60)
            return false;
        
        // Must not end with sentence punctuation
        if (line.EndsWith('.') || line.EndsWith('!') || line.EndsWith('?') || 
            line.EndsWith(',') || line.EndsWith(';'))
            return false;
        
        // Must not start with bullet points
        if (line.StartsWith("- ") || line.StartsWith("* ") || line.StartsWith("• "))
            return false;
        
        // Must not contain colon followed by space (e.g., "Note: this is...")
        if (line.Contains(": "))
            return false;
        
        // Must not have too many commas (likely a list or sentence)
        if (line.Count(c => c == ',') >= 2)
            return false;
        
        // Title Case check - at least 50% of words should start with capital letter
        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return false;
        
        // Single word headers are likely headers if they start with capital
        if (words.Length == 1)
            return char.IsUpper(words[0][0]);
        
        // For multiple words, check if most start with capitals
        var capitalizedWords = words.Count(w => w.Length > 0 && char.IsUpper(w[0]));
        var titleCasePercentage = (double)capitalizedWords / words.Length;
        
        // At least 50% of words should be capitalized for it to be a header
        // This allows headers like "Items and Equipment" (2 of 3 = 66%)
        // But rejects sentences like "When an investigator becomes Insane" (4 of 5 = 80% but has sentence-like structure)
        
        // Additional check: Reject if starts with common sentence-starting words
        var firstWord = words[0].ToLowerInvariant();
        var sentenceStarters = new[] { "when", "if", "an", "the", "to", "for", "with", "by", "each", "any", "all" };
        if (sentenceStarters.Contains(firstWord))
            return false;
        
        return titleCasePercentage >= 0.5;
    }
}
