using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Entities;
using Services.UI;

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
    private static readonly string[] SupportedExtensions = { ".txt", ".pdf", ".md" };

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
            ".pdf" => await ProcessPdfFileAsync(documentName, filePath),
            _ => throw new NotSupportedException($"File type '{extension}' is not supported")
        };
    }

    /// <summary>
    /// Process plain text file.
    /// Game name is extracted from FIRST LINE of the file.
    /// Categories format: "GameName - SectionHeader"
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
                fragments.Add(new MemoryFragment(gameName, singleFragmentContent));
                DisplayService.ShowCollectedSections(1, gameName);
            }
            return fragments;
        }
        
        // Process sections with headers
        int lineIndex = 1; // Start after game name
        string? currentHeader = null;
        var currentContent = new List<string>();
        
        while (lineIndex < lines.Length)
        {
            var line = lines[lineIndex].Trim();
            
            if (string.IsNullOrWhiteSpace(line))
            {
                lineIndex++;
                continue;
            }
            
            bool isMarkdownHeader = line.StartsWith("#");
            bool isPlainHeader = !isMarkdownHeader &&
                                line.Length < 80 &&
                                !line.Contains(": ") &&
                                line.Count(c => c == ',') < 2 &&
                                !line.EndsWith('.') &&
                                !line.EndsWith(',') &&
                                !line.EndsWith(';') &&
                                !line.StartsWith("- ") &&
                                !line.StartsWith("* ");
            
            bool isHeader = isMarkdownHeader || isPlainHeader;
            
            if (isHeader && currentHeader != null)
            {
                if (currentContent.Count > 0)
                {
                    var content_text = string.Join(Environment.NewLine, currentContent).Trim();
                    if (!string.IsNullOrWhiteSpace(content_text))
                    {
                        // Category format: "GameName - SectionHeader"
                        fragments.Add(new MemoryFragment($"{gameName} - {currentHeader}", content_text));
                    }
                }
                
                currentHeader = isMarkdownHeader ? line.TrimStart('#').Trim() : line;
                currentContent.Clear();
            }
            else if (isHeader && currentHeader == null)
            {
                currentHeader = isMarkdownHeader ? line.TrimStart('#').Trim() : line;
                currentContent.Clear();
            }
            else
            {
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
                fragments.Add(new MemoryFragment($"{gameName} - {currentHeader}", content_text));
            }
        }
        
        // If no sections found, treat entire file (after title) as single section
        if (fragments.Count == 0 && lines.Length > 1)
        {
            var allContent = string.Join(Environment.NewLine, lines.Skip(1)).Trim();
            if (!string.IsNullOrWhiteSpace(allContent))
            {
                fragments.Add(new MemoryFragment(gameName, allContent));
            }
        }

        DisplayService.ShowCollectedSections(fragments.Count, gameName);
        return fragments;
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
}
