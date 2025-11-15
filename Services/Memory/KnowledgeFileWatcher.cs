using Entities;
using Services.UI;

namespace Services.Memory;

/// <summary>
/// Automatically watches a folder for new knowledge files, processes them,
/// and moves them to an archive folder to prevent re-processing.
/// </summary>
public class KnowledgeFileWatcher
{
    private readonly string _inboxFolder;
    private readonly string _archiveFolder;

    public KnowledgeFileWatcher(string inboxFolder, string archiveFolder)
    {
        _inboxFolder = inboxFolder ?? throw new ArgumentNullException(nameof(inboxFolder));
        _archiveFolder = archiveFolder ?? throw new ArgumentNullException(nameof(archiveFolder));

        // Ensure folders exist
        Directory.CreateDirectory(_inboxFolder);
        Directory.CreateDirectory(_archiveFolder);
    }

    /// <summary>
    /// Scans the inbox folder for new .txt files and returns them.
    /// </summary>
    public async Task<Dictionary<string, string>> DiscoverNewFilesAsync()
    {
        var files = Directory.GetFiles(_inboxFolder, "*.txt", SearchOption.TopDirectoryOnly);
        var newFiles = new Dictionary<string, string>();

        foreach (var filePath in files)
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            newFiles[fileName] = filePath;
        }

        return await Task.FromResult(newFiles);
    }

    /// <summary>
    /// Processes a file by splitting it into memory fragments.
    /// Expected format:
    /// - First line: Document title (if it's not a comma-separated list)
    /// - Then sections with headers (either markdown # headers or standalone short lines)
    /// - Content follows headers
    /// - Lines starting with key: value are treated as content, not headers
    /// - If first line looks like a CSV/list, treat entire file as single fragment
    /// </summary>
    public async Task<List<MemoryFragment>> ProcessFileAsync(string gameName, string filePath)
    {
        var fragments = new List<MemoryFragment>();

        DisplayService.ShowLoadingFile(gameName, filePath);

        // Read file content
        var content = await File.ReadAllTextAsync(filePath);
        var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        
        if (lines.Length == 0)
        {
            return fragments;
        }
        
        // Extract document title from first line
        string firstLine = lines[0].Trim();
        
        // Check if first line looks like a comma-separated list (CSV header or list of items)
        // If it has multiple commas, it's probably a list, not a proper title
        bool firstLineIsCommaSeparated = firstLine.Count(c => c == ',') >= 2;
        
        if (firstLineIsCommaSeparated)
        {
            // Treat entire file as single fragment with filename as title
            // This handles CSV-like formats or comma-separated lists
            var singleFragmentContent = string.Join(Environment.NewLine, lines).Trim();
            if (!string.IsNullOrWhiteSpace(singleFragmentContent))
            {
                fragments.Add(new MemoryFragment(gameName, singleFragmentContent));
                DisplayService.ShowCollectedSections(1, gameName);
            }
            return fragments;
        }
        
        // Normal document processing with sections
        string documentTitle = firstLine;
        
        // Process remaining lines, looking for section headers
        int lineIndex = 1; // Start after document title
        string? currentHeader = null;
        var currentContent = new List<string>();
        
        while (lineIndex < lines.Length)
        {
            var line = lines[lineIndex].Trim();
            
            // Skip empty lines
            if (string.IsNullOrWhiteSpace(line))
            {
                lineIndex++;
                continue;
            }
            
            // Check if this line is a markdown header (starts with #)
            bool isMarkdownHeader = line.StartsWith("#");
            
            // Check if this line is a section header
            // Headers are:
            // - Markdown headers (# Header)
            // OR
            // - Short lines (< 80 chars) that:
            //   - Don't contain ": " (not a key-value pair)
            //   - Don't contain multiple commas (not a list)
            //   - Don't end with sentence-ending punctuation
            //   - Are not obviously indented content
            bool isPlainHeader = !isMarkdownHeader &&
                                line.Length < 80 &&
                                !line.Contains(": ") &&  // Not a key-value pair
                                line.Count(c => c == ',') < 2 &&  // Not a comma-separated list
                                !line.EndsWith('.') &&
                                !line.EndsWith(',') &&
                                !line.EndsWith(';') &&
                                !line.StartsWith("- ") &&  // Not a list item
                                !line.StartsWith("* ");    // Not a list item
            
            bool isHeader = isMarkdownHeader || isPlainHeader;
            
            if (isHeader && currentHeader != null)
            {
                // Save previous section
                if (currentContent.Count > 0)
                {
                    var content_text = string.Join(Environment.NewLine, currentContent).Trim();
                    if (!string.IsNullOrWhiteSpace(content_text))
                    {
                        fragments.Add(new MemoryFragment($"{documentTitle} - {currentHeader}", content_text));
                    }
                }
                
                // Start new section
                currentHeader = isMarkdownHeader ? line.TrimStart('#').Trim() : line;
                currentContent.Clear();
            }
            else if (isHeader && currentHeader == null)
            {
                // First header after document title
                currentHeader = isMarkdownHeader ? line.TrimStart('#').Trim() : line;
                currentContent.Clear();
            }
            else
            {
                // This is content, add to current section
                currentContent.Add(line);
            }
            
            lineIndex++;
        }
        
        // Don't forget the last section
        if (currentHeader != null && currentContent.Count > 0)
        {
            var content_text = string.Join(Environment.NewLine, currentContent).Trim();
            if (!string.IsNullOrWhiteSpace(content_text))
            {
                fragments.Add(new MemoryFragment($"{documentTitle} - {currentHeader}", content_text));
            }
        }
        
        // If no sections were found, treat entire file (after title) as single section
        if (fragments.Count == 0 && lines.Length > 1)
        {
            var allContent = string.Join(Environment.NewLine, lines.Skip(1)).Trim();
            if (!string.IsNullOrWhiteSpace(allContent))
            {
                fragments.Add(new MemoryFragment(documentTitle, allContent));
            }
        }

        DisplayService.ShowCollectedSections(fragments.Count, documentTitle);
        return fragments;
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
    /// Gets the count of files waiting to be processed.
    /// </summary>
    public int GetPendingFileCount()
    {
        return Directory.GetFiles(_inboxFolder, "*.txt", SearchOption.TopDirectoryOnly).Length;
    }
}
