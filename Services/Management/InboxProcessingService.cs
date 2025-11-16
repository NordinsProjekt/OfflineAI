using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Entities;
using Services.Configuration;
using Services.Memory;

namespace Services.Management;

/// <summary>
/// Service for processing files from an inbox folder into the vector database.
/// Handles file discovery, fragment extraction, embedding generation, and archival.
/// Supports TXT, MD, and PDF files.
/// </summary>
public class InboxProcessingService
{
    // Progress notification for UI components
    public event Action<string>? OnProgressUpdate;
    public event Action? OnProcessingComplete;

    private readonly VectorMemoryPersistenceService _persistenceService;
    private readonly AppConfiguration _appConfig;

    private bool _isProcessing = false;
    public bool IsProcessing => _isProcessing;

    private string _currentStatus = string.Empty;
    public string CurrentStatus => _currentStatus;

    public InboxProcessingService(
        VectorMemoryPersistenceService persistenceService,
        AppConfiguration appConfig)
    {
        _persistenceService = persistenceService ?? throw new ArgumentNullException(nameof(persistenceService));
        _appConfig = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
    }

    /// <summary>
    /// Process all files in the inbox folder and save to the specified collection.
    /// Supports .txt, .md, and .pdf files.
    /// </summary>
    public async Task<(bool Success, string Message, int FilesProcessed, int FragmentsCreated)> ProcessInboxAsync(string collectionName)
    {
        if (_isProcessing)
        {
            return (false, "Processing already in progress", 0, 0);
        }

        if (string.IsNullOrWhiteSpace(collectionName))
        {
            return (false, "Collection name is required", 0, 0);
        }

        try
        {
            _isProcessing = true;
            UpdateStatus("Checking for new files in inbox...");

            var inboxFolder = _appConfig.Folders?.InboxFolder ?? "c:/llm/Inbox";
            var archiveFolder = _appConfig.Folders?.ArchiveFolder ?? "c:/llm/Archive";

            if (!Directory.Exists(inboxFolder))
            {
                return (false, $"Inbox folder not found: {inboxFolder}", 0, 0);
            }

            // Use MultiFormatFileWatcher for TXT, MD, and PDF support
            var pdfProcessor = new PdfFragmentProcessor();
            var fileWatcher = new MultiFormatFileWatcher(inboxFolder, archiveFolder, pdfProcessor);
            var newFiles = await fileWatcher.DiscoverNewFilesAsync();

            if (!newFiles.Any())
            {
                return (true, "No new files found in inbox", 0, 0);
            }

            UpdateStatus($"Found {newFiles.Count} file(s) (.txt, .md, .pdf). Processing fragments...");

            // Collect fragments from files
            var allFragments = new List<MemoryFragment>();
            int fileIndex = 0;
            
            foreach (var (documentName, filePath) in newFiles)
            {
                fileIndex++;
                var fileName = Path.GetFileName(filePath);
                var extension = Path.GetExtension(filePath).ToUpperInvariant();
                
                UpdateStatus($"Processing file {fileIndex}/{newFiles.Count}: {fileName} ({extension})...");
                
                try
                {
                    var fragments = await fileWatcher.ProcessFileAsync(documentName, filePath);
                    allFragments.AddRange(fragments);
                }
                catch (NotImplementedException ex) when (ex.Message.Contains("PDF"))
                {
                    UpdateStatus($"??  PDF support not configured: {fileName}. Install PDF library (e.g., UglyToad.PdfPig)");
                    // Continue processing other files
                }
                catch (Exception ex)
                {
                    UpdateStatus($"??  Failed to process {fileName}: {ex.Message}");
                    // Continue processing other files
                }
            }

            if (!allFragments.Any())
            {
                return (false, "No fragments could be extracted from files", newFiles.Count, 0);
            }

            // Save to database
            UpdateStatus($"Generating embeddings for {allFragments.Count} fragments...");
            
            await _persistenceService.SaveFragmentsAsync(
                allFragments,
                collectionName,
                sourceFile: string.Join(", ", newFiles.Keys),
                replaceExisting: false);

            // Archive processed files
            UpdateStatus("Archiving processed files...");
            
            foreach (var filePath in newFiles.Values)
            {
                await fileWatcher.ArchiveFileAsync(filePath);
            }

            UpdateStatus($"? Processed {newFiles.Count} file(s), {allFragments.Count} fragments saved to '{collectionName}'");
            OnProcessingComplete?.Invoke();
            
            return (true, $"Processed {newFiles.Count} file(s), {allFragments.Count} fragments saved", newFiles.Count, allFragments.Count);
        }
        catch (Exception ex)
        {
            UpdateStatus($"? Failed to process inbox: {ex.Message}");
            return (false, $"Failed to process inbox: {ex.Message}", 0, 0);
        }
        finally
        {
            _isProcessing = false;
        }
    }

    /// <summary>
    /// Get count of files waiting in the inbox (all supported types)
    /// </summary>
    public async Task<(bool Success, int FileCount, Dictionary<string, int> FilesByType)> GetInboxFileCountAsync()
    {
        try
        {
            var inboxFolder = _appConfig.Folders?.InboxFolder ?? "c:/llm/Inbox";
            var archiveFolder = _appConfig.Folders?.ArchiveFolder ?? "c:/llm/Archive";

            if (!Directory.Exists(inboxFolder))
            {
                return (false, 0, new Dictionary<string, int>());
            }

            var pdfProcessor = new PdfFragmentProcessor();
            var fileWatcher = new MultiFormatFileWatcher(inboxFolder, archiveFolder, pdfProcessor);
            var newFiles = await fileWatcher.DiscoverNewFilesAsync();
            var filesByType = fileWatcher.GetPendingFilesByType();
            
            return (true, newFiles.Count, filesByType);
        }
        catch
        {
            return (false, 0, new Dictionary<string, int>());
        }
    }

    private void UpdateStatus(string status)
    {
        _currentStatus = status;
        OnProgressUpdate?.Invoke(status);
    }
}
