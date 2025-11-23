using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Entities;
using Services.Utilities;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Services.Memory;

/// <summary>
/// Processes PDF files into semantic chunks suitable for embedding.
/// Extracts text using UglyToad.PdfPig, preserves structure, and creates meaningful fragments.
/// Automatically cleans extracted text to remove special tokens and control characters.
/// </summary>
public class PdfFragmentProcessor
{
    private readonly DocumentChunker.ChunkOptions _chunkOptions;

    public PdfFragmentProcessor(DocumentChunker.ChunkOptions? options = null)
    {
        _chunkOptions = options ?? new DocumentChunker.ChunkOptions
        {
            MaxChunkSize = 1000,      // Good size for embeddings
            OverlapSize = 200,        // Preserve context between chunks
            MinChunkSize = 100,       // Avoid tiny fragments
            KeepHeaders = true,       // Include section titles
            AddMetadata = true        // Track page numbers
        };
    }

    /// <summary>
    /// Process a PDF file into memory fragments ready for embedding.
    /// Enhanced to preserve PDF structure and detect sections even without markdown headers.
    /// </summary>
    public async Task<List<MemoryFragment>> ProcessPdfFileAsync(
        string pdfPath, 
        string? collectionName = null)
    {
        if (!File.Exists(pdfPath))
            throw new FileNotFoundException($"PDF file not found: {pdfPath}");

        var fileName = Path.GetFileNameWithoutExtension(pdfPath);
        collectionName ??= fileName;

        // Extract text from PDF
        var pdfText = await ExtractTextFromPdfAsync(pdfPath);
        
        // Extract metadata
        var metadata = await ExtractPdfMetadataAsync(pdfPath);
        
        // Chunk the text using semantic boundaries with LOWER MinChunkSize
        // This allows smaller sections like "Items", "Equipment" to be preserved
        var chunkOptions = new DocumentChunker.ChunkOptions
        {
            MaxChunkSize = _chunkOptions.MaxChunkSize,
            OverlapSize = _chunkOptions.OverlapSize,
            MinChunkSize = 200,  // Reduced from 500 to allow smaller sections
            KeepHeaders = _chunkOptions.KeepHeaders,
            AddMetadata = _chunkOptions.AddMetadata
        };
        
        var chunks = DocumentChunker.ChunkByHierarchy(pdfText, chunkOptions);
        
        // Convert to MemoryFragments
        var fragments = new List<MemoryFragment>();
        
        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            
            // Determine category with better logic
            string category;
            
            // Try to extract a meaningful section name from the chunk content
            var detectedSection = DetectSectionFromContent(chunk.Content);
            
            if (!string.IsNullOrWhiteSpace(detectedSection))
            {
                // Use detected section (e.g., "Items", "Equipment", "Combat")
                category = $"{collectionName} - {detectedSection}";
            }
            else if (!string.IsNullOrWhiteSpace(chunk.SectionTitle) && 
                     chunk.SectionTitle != "General")
            {
                // Use DocumentChunker's detected title
                category = $"{collectionName} - {chunk.SectionTitle}";
            }
            else
            {
                // Fallback to chunk number
                category = $"{collectionName} - Chunk {chunk.ChunkIndex + 1}";
            }
            
            // Optionally prepend metadata to content for better context
            var enhancedContent = chunk.Content;
            if (_chunkOptions.AddMetadata && metadata.TotalPages > 0)
            {
                var metadataHeader = $"[Source: {fileName}.pdf, Total Pages: {metadata.TotalPages}]\n\n";
                enhancedContent = metadataHeader + chunk.Content;
            }
            
            // CLEAN the text to remove special tokens and control characters
            category = MemoryFragmentCleaner.CleanText(category);
            enhancedContent = MemoryFragmentCleaner.CleanText(enhancedContent);
            
            var fragment = new MemoryFragment(
                category: category,
                content: enhancedContent
            );
            
            fragments.Add(fragment);
        }
        
        Console.WriteLine($"[?] Extracted {fragments.Count} chunks from {metadata.TotalPages} pages: {fileName}.pdf");
        
        // Debug: Show first few categories
        if (fragments.Count > 0)
        {
            Console.WriteLine($"[DEBUG] Sample categories:");
            foreach (var frag in fragments.Take(5))
            {
                Console.WriteLine($"  - {frag.Category} ({frag.Content.Length} chars)");
            }
        }
        
        return fragments;
    }

    /// <summary>
    /// Process multiple PDF files from a directory
    /// </summary>
    public async Task<List<MemoryFragment>> ProcessPdfDirectoryAsync(
        string directoryPath,
        string searchPattern = "*.pdf",
        bool recursive = false)
    {
        if (!Directory.Exists(directoryPath))
            throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var pdfFiles = Directory.GetFiles(directoryPath, searchPattern, searchOption);

        var allFragments = new List<MemoryFragment>();

        foreach (var pdfFile in pdfFiles)
        {
            try
            {
                var fragments = await ProcessPdfFileAsync(pdfFile);
                allFragments.AddRange(fragments);
                
                Console.WriteLine($"[?] Processed: {Path.GetFileName(pdfFile)} ({fragments.Count} chunks)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[!] Failed to process {Path.GetFileName(pdfFile)}: {ex.Message}");
            }
        }

        return allFragments;
    }

    /// <summary>
    /// Extract text from PDF using UglyToad.PdfPig
    /// </summary>
    private async Task<string> ExtractTextFromPdfAsync(string pdfPath)
    {
        return await Task.Run(() =>
        {
            using var document = PdfDocument.Open(pdfPath);
            var text = new StringBuilder();
            
            foreach (var page in document.GetPages())
            {
                // Add page marker for better context
                text.AppendLine($"\n--- Page {page.Number} ---\n");
                
                // Extract text with proper spacing
                var pageText = page.Text;
                
                // Clean up common PDF extraction issues
                pageText = CleanPdfText(pageText);
                
                text.AppendLine(pageText);
                text.AppendLine();
            }
            
            return text.ToString();
        });
    }

    /// <summary>
    /// Clean up common PDF text extraction issues
    /// </summary>
    private string CleanPdfText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        
        var cleaned = text;
        
        // Fix hyphenated words at line breaks
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"(\w+)-\s*\r?\n\s*(\w+)", "$1$2");
        
        // Remove excessive whitespace but preserve single newlines (paragraph structure)
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"[ \t]+", " ");
        
        // Fix multiple newlines (keep maximum of 2 for paragraph breaks)
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\r?\n\s*\r?\n\s*\r?\n+", "\n\n");
        
        return cleaned.Trim();
    }

    /// <summary>
    /// Extract PDF metadata (title, author, page count, etc.)
    /// </summary>
    private async Task<PdfMetadata> ExtractPdfMetadataAsync(string pdfPath)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var document = PdfDocument.Open(pdfPath);
                var info = document.Information;
                
                // Try to parse creation date if available
                DateTime? creationDate = null;
                if (!string.IsNullOrWhiteSpace(info.CreationDate))
                {
                    if (DateTime.TryParse(info.CreationDate, out var parsedDate))
                    {
                        creationDate = parsedDate;
                    }
                }
                
                return new PdfMetadata
                {
                    TotalPages = document.NumberOfPages,
                    Title = info.Title ?? Path.GetFileNameWithoutExtension(pdfPath),
                    Author = info.Author ?? "Unknown",
                    Subject = info.Subject,
                    CreationDate = creationDate,
                    Producer = info.Producer,
                    Creator = info.Creator
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Failed to extract PDF metadata: {ex.Message}");
                
                return new PdfMetadata
                {
                    TotalPages = 0,
                    Title = Path.GetFileNameWithoutExtension(pdfPath),
                    Author = "Unknown",
                    Subject = null
                };
            }
        });
    }

    private class PdfMetadata
    {
        public int TotalPages { get; set; }
        public string? Title { get; set; }
        public string? Author { get; set; }
        public string? Subject { get; set; }
        public DateTime? CreationDate { get; set; }
        public string? Producer { get; set; }
        public string? Creator { get; set; }
    }

    /// <summary>
    /// Attempts to detect section name from content by looking for title-like patterns.
    /// Looks for: capitalized words, bold text patterns, numbered sections, etc.
    /// Enhanced to work better with content-order extracted text.
    /// </summary>
    private string? DetectSectionFromContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;
        
        // Get first few lines
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Take(5)  // Look at more lines since ContentOrderTextExtractor might have better structure
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();
        
        if (!lines.Any())
            return null;
        
        // Try to find the best header line (not metadata)
        string? bestHeaderLine = null;
        foreach (var line in lines)
        {
            // Skip lines that look like metadata
            if (line.StartsWith("[") || line.StartsWith("Page ") || line.Contains("---"))
                continue;
            
            // This is a candidate for a header
            bestHeaderLine = line;
            break;
        }
        
        if (string.IsNullOrWhiteSpace(bestHeaderLine))
            return null;
        
        var firstLine = bestHeaderLine;
        
        // Pattern 1: All caps or Title Case short line (likely a header)
        // "ITEMS", "Items", "Equipment and Possessions"
        if (firstLine.Length < 60 && firstLine.Length > 2)
        {
            var words = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            
            // Check if it's title case (most words start with capital)
            var titleCaseWords = words.Count(w => w.Length > 0 && char.IsUpper(w[0]));
            if (titleCaseWords >= words.Length * 0.6) // 60% of words are capitalized (lowered threshold)
            {
                // Clean up the title
                var title = firstLine
                    .Replace("---", "")
                    .Replace("___", "")
                    .Replace("[", "")
                    .Replace("]", "")
                    .Trim();
                
                if (title.Length >= 3 && title.Length <= 50)
                    return title;
            }
        }
        
        // Pattern 2: Look for common game manual section keywords
        var sectionKeywords = new[]
        {
            "items", "equipment", "possessions", "inventory",
            "combat", "attack", "defense", "damage",
            "movement", "actions", "turn structure", "setup",
            "components", "objective", "winning", "losing",
            "special abilities", "cards", "tokens", "rules",
            "characters", "investigators", "monsters", "enemies",
            "overview", "introduction", "reference", "glossary"
        };
        
        foreach (var keyword in sectionKeywords)
        {
            // Check if first line contains the keyword (case-insensitive)
            if (firstLine.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // Extract the section name (capitalize properly)
                var title = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(keyword);
                return title;
            }
        }
        
        // Pattern 3: Numbered sections "1. Setup", "2.3 Items"
        var numberedMatch = System.Text.RegularExpressions.Regex.Match(firstLine, @"^[\d\.]+\s+(.{3,50})$");
        if (numberedMatch.Success)
        {
            return numberedMatch.Groups[1].Value.Trim();
        }
        
        return null;
    }
}
