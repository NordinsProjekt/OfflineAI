using MemoryLibrary.Models;
using Services.UI;

namespace Services;

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
    /// </summary>
    public async Task<List<MemoryFragment>> ProcessFileAsync(string gameName, string filePath)
    {
        var fragments = new List<MemoryFragment>();

        DisplayService.ShowLoadingFile(gameName, filePath);

        // Read file content
        var content = await File.ReadAllTextAsync(filePath);
        var sections = content.Split(
            new[] { "\r\n\r\n", "\n\n" },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        int sectionNum = 0;
        foreach (var section in sections)
        {
            if (string.IsNullOrWhiteSpace(section)) continue;

            sectionNum++;
            var lines = section.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            string category;
            string fragmentContent;

            // Check if first line looks like a header
            if (lines.Length > 1 &&
                lines[0].Length < 100 &&
                !lines[0].TrimEnd().EndsWith('.') &&
                !lines[0].TrimEnd().EndsWith(':'))
            {
                category = $"{gameName} - Section {sectionNum}: {lines[0].Trim()}";
                fragmentContent = string.Join(Environment.NewLine, lines.Skip(1)).Trim();
            }
            else
            {
                category = $"{gameName} - Section {sectionNum}";
                fragmentContent = section.Trim();
            }

            if (!string.IsNullOrWhiteSpace(fragmentContent))
            {
                fragments.Add(new MemoryFragment(category, fragmentContent));
            }
        }

        DisplayService.ShowCollectedSections(sectionNum, gameName);
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
