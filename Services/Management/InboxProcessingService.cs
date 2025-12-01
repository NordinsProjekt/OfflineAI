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
public class InboxProcessingService(
    VectorMemoryPersistenceService persistenceService,
    AppConfiguration appConfig)
{
    // Progress notification for UI components
    public event Action<string>? OnProgressUpdate;
    public event Action? OnProcessingComplete;

    private readonly VectorMemoryPersistenceService _persistenceService = persistenceService ?? throw new ArgumentNullException(nameof(persistenceService));
    private readonly AppConfiguration _appConfig = appConfig ?? throw new ArgumentNullException(nameof(appConfig));

    private bool _isProcessing = false;
    public bool IsProcessing => _isProcessing;

    private string _currentStatus = string.Empty;
    public string CurrentStatus => _currentStatus;

    /// <summary>
    /// Optional callback for domain registration.
    /// Takes category string and category type (e.g., "game", "product").
    /// </summary>
    public Func<string, string, Task>? OnDomainDiscovered { get; set; }

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

            if (newFiles.Count == 0)
            {
                return (true, "No new files found in inbox", 0, 0);
            }

            UpdateStatus($"Found {newFiles.Count} file(s) (.txt, .md, .pdf). Processing fragments...");

            // Process and save each file individually
            var totalFragmentsCreated = 0;
            var filesProcessed = 0;
            var filesFailed = 0;
            var fileIndex = 0;
            var errorMessages = new List<string>();
            
            foreach (var (documentName, filePath) in newFiles)
            {
                fileIndex++;
                var fileName = Path.GetFileName(filePath);
                var extension = Path.GetExtension(filePath).ToUpperInvariant();
                
                UpdateStatus($"Processing file {fileIndex}/{newFiles.Count}: {fileName} ({extension})...");
                
                try
                {
                    // Process the file
                    var fragments = await fileWatcher.ProcessFileAsync(documentName, filePath);
                    
                    if (fragments.Count == 0)
                    {
                        UpdateStatus($"⚠  No fragments extracted from {fileName}. Skipping...");
                        filesFailed++;
                        errorMessages.Add($"{fileName}: No fragments extracted");
                        continue;
                    }
                    
                    UpdateStatus($"Extracted {fragments.Count} fragment(s) from {fileName}");
                    
                    // Auto-register domain from the first fragment's category (if callback provided)
                    if (OnDomainDiscovered != null && fragments.Count > 0)
                    {
                        try
                        {
                            // Extract domain name from first fragment's category
                            // Categories are typically: "Game Name - Section" or just "Game Name"
                            // Clean any markdown headers (##) that might be present
                            var firstCategory = fragments[0].Category.Replace("##", "").Trim();
                            await OnDomainDiscovered(firstCategory, "general");
                            UpdateStatus($"✓ Registered domain from: {firstCategory}");
                        }
                        catch (Exception domainEx)
                        {
                            // Don't fail the whole process if domain registration fails
                            UpdateStatus($"⚠  Could not register domain: {domainEx.Message}");
                        }
                    }
                    
                    // Save fragments for this specific file
                    UpdateStatus($"Generating embeddings for {fragments.Count} fragments from {fileName}...");
                    
                    try
                    {
                        await _persistenceService.SaveFragmentsAsync(
                            fragments,
                            collectionName,
                            sourceFile: fileName,  // Use individual filename
                            replaceExisting: false);
                        
                        totalFragmentsCreated += fragments.Count;
                        UpdateStatus($"✓ Saved {fragments.Count} fragment(s) from {fileName}");
                    }
                    catch (Exception saveEx)
                    {
                        UpdateStatus($"✗  Failed to save fragments from {fileName}: {saveEx.Message}");
                        filesFailed++;
                        errorMessages.Add($"{fileName}: Save failed - {saveEx.Message}");
                        continue; // Don't archive if save failed
                    }
                    
                    // Archive the file after successful processing and saving
                    try
                    {
                        UpdateStatus($"Archiving {fileName}...");
                        await fileWatcher.ArchiveFileAsync(filePath);
                        filesProcessed++;
                        UpdateStatus($"✓ Completed {fileName}: {fragments.Count} fragments saved and archived");
                    }
                    catch (Exception archiveEx)
                    {
                        UpdateStatus($"⚠  Failed to archive {fileName}: {archiveEx.Message}");
                        errorMessages.Add($"{fileName}: Archive failed - {archiveEx.Message}");
                        filesProcessed++; // Still count as processed since data was saved
                    }
                }
                catch (NotImplementedException ex) when (ex.Message.Contains("PDF"))
                {
                    UpdateStatus($"⚠  PDF support not configured: {fileName}. Install PDF library (e.g., UglyToad.PdfPig)");
                    filesFailed++;
                    errorMessages.Add($"{fileName}: PDF support not configured");
                }
                catch (Exception ex)
                {
                    UpdateStatus($"✗  Failed to process {fileName}: {ex.Message}");
                    filesFailed++;
                    errorMessages.Add($"{fileName}: {ex.Message}");
                }
            }

            // Build final status message
            var resultMessage = $"Processed {filesProcessed} file(s), {totalFragmentsCreated} fragments saved";
            if (filesFailed > 0)
            {
                resultMessage += $". {filesFailed} file(s) failed.";
            }
            
            UpdateStatus($"✓ {resultMessage}");
            
            if (errorMessages.Count != 0)
            {
                UpdateStatus($"Errors: {string.Join("; ", errorMessages)}");
            }
            
            OnProcessingComplete?.Invoke();
            
            return (true, resultMessage, filesProcessed, totalFragmentsCreated);
        }
        catch (Exception ex)
        {
            UpdateStatus($"✗ Failed to process inbox: {ex.Message}");
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

    /// <summary>
    /// Convert all PDF files in the inbox to TXT files.
    /// This allows users to manually edit/clean the extracted text before processing.
    /// </summary>
    public async Task<(bool Success, string Message, int FilesConverted)> ConvertPdfToTxtAsync()
    {
        if (_isProcessing)
        {
            return (false, "Processing already in progress", 0);
        }

        try
        {
            _isProcessing = true;
            UpdateStatus("Looking for PDF files to convert...");

            var inboxFolder = _appConfig.Folders?.InboxFolder ?? "c:/llm/Inbox";

            if (!Directory.Exists(inboxFolder))
            {
                return (false, $"Inbox folder not found: {inboxFolder}", 0);
            }

            var pdfFiles = Directory.GetFiles(inboxFolder, "*.pdf", SearchOption.TopDirectoryOnly);

            if (pdfFiles.Length == 0)
            {
                return (true, "No PDF files found in inbox", 0);
            }

            UpdateStatus($"Found {pdfFiles.Length} PDF file(s). Starting conversion...");

            var pdfProcessor = new PdfFragmentProcessor();
            var filesConverted = 0;
            var errorMessages = new List<string>();

            foreach (var pdfPath in pdfFiles)
            {
                var fileName = Path.GetFileName(pdfPath);
                UpdateStatus($"Converting: {fileName}...");

                try
                {
                    // Extract text from PDF
                    var text = await ExtractTextFromPdfAsync(pdfPath);

                    if (string.IsNullOrWhiteSpace(text))
                    {
                        UpdateStatus($"⚠  No text extracted from {fileName}");
                        errorMessages.Add($"{fileName}: No text content");
                        continue;
                    }

                    // Create TXT file with same name
                    var txtFileName = Path.GetFileNameWithoutExtension(fileName) + ".txt";
                    var txtPath = Path.Combine(inboxFolder, txtFileName);

                    await File.WriteAllTextAsync(txtPath, text);
                    filesConverted++;

                    UpdateStatus($"✓ Converted {fileName} → {txtFileName}");
                }
                catch (Exception ex)
                {
                    UpdateStatus($"✗  Failed to convert {fileName}: {ex.Message}");
                    errorMessages.Add($"{fileName}: {ex.Message}");
                }
            }

            var resultMessage = $"Converted {filesConverted} of {pdfFiles.Length} PDF file(s) to TXT";
            
            if (errorMessages.Count > 0)
            {
                resultMessage += $". {errorMessages.Count} file(s) failed.";
            }

            UpdateStatus($"✓ {resultMessage}");
            OnProcessingComplete?.Invoke();

            return (true, resultMessage, filesConverted);
        }
        catch (Exception ex)
        {
            UpdateStatus($"✗ Failed to convert PDFs: {ex.Message}");
            return (false, $"Failed to convert PDFs: {ex.Message}", 0);
        }
        finally
        {
            _isProcessing = false;
        }
    }

    /// <summary>
    /// Extract raw text from PDF file using PdfPig
    /// </summary>
    private async Task<string> ExtractTextFromPdfAsync(string pdfPath)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var document = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
                var text = new System.Text.StringBuilder();
                
                foreach (var page in document.GetPages())
                {
                    text.AppendLine($"--- Page {page.Number} ---");
                    text.AppendLine();
                    text.AppendLine(page.Text);
                    text.AppendLine();
                }
                
                return text.ToString();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to extract text from PDF: {ex.Message}", ex);
            }
        });
    }

    private void UpdateStatus(string status)
    {
        _currentStatus = status;
        OnProgressUpdate?.Invoke(status);
    }
}
