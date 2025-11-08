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
    /// First line: Game title
    /// Then sections with headers on single lines followed by content
    /// Headers are detected as short lines (< 80 chars) that don't end with punctuation
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
        
        // Extract game title from first line
        string gameTitle = lines[0].Trim();
        
        // Process remaining lines, looking for section headers
        int lineIndex = 1; // Start after game title
        string? currentHeader = null;
        var currentContent = new List<string>();
        
        while (lineIndex < lines.Length)
        {
            var line = lines[lineIndex].Trim();
            
            // Check if this line is a section header
            // Headers are typically:
            // - Short (< 80 chars)
            // - Don't end with sentence-ending punctuation
            // - Not empty
            bool isHeader = !string.IsNullOrWhiteSpace(line) &&
                           line.Length < 80 &&
                           !line.EndsWith('.') &&
                           !line.EndsWith(',') &&
                           !line.EndsWith(';');
            
            if (isHeader && currentHeader != null)
            {
                // Save previous section
                if (currentContent.Count > 0)
                {
                    var content_text = string.Join(Environment.NewLine, currentContent).Trim();
                    if (!string.IsNullOrWhiteSpace(content_text))
                    {
                        fragments.Add(new MemoryFragment($"{gameTitle} - {currentHeader}", content_text));
                    }
                }
                
                // Start new section
                currentHeader = line;
                currentContent.Clear();
            }
            else if (isHeader && currentHeader == null)
            {
                // First header after game title
                currentHeader = line;
                currentContent.Clear();
            }
            else
            {
                // This is content, add to current section
                if (!string.IsNullOrWhiteSpace(line))
                {
                    currentContent.Add(line);
                }
            }
            
            lineIndex++;
        }
        
        // Don't forget the last section
        if (currentHeader != null && currentContent.Count > 0)
        {
            var content_text = string.Join(Environment.NewLine, currentContent).Trim();
            if (!string.IsNullOrWhiteSpace(content_text))
            {
                fragments.Add(new MemoryFragment($"{gameTitle} - {currentHeader}", content_text));
            }
        }

        DisplayService.ShowCollectedSections(fragments.Count, gameTitle);
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
