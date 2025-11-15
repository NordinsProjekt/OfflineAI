using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Services.Configuration;
using Services.Repositories;
using Services.Memory;
using Entities;

namespace AiDashboard.Services
{
    // Simple UI state and control service for the dashboard.
    // Exposes settings for the LLM and action methods invoked from the UI.
    public class DashboardService
    {
        // Change notification for Blazor components
        public event Action? OnChange;

        /// <summary>
        /// Optional handler that the host can assign to perform the actual model switch.
        /// Signature: modelFullPath, progressCallback -> Task
        /// </summary>
        public Func<string, Action<int,int>?, Task>? SwitchModelHandler { get; set; }

        /// <summary>
        /// Optional chat service for sending messages to the LLM
        /// </summary>
        public DashboardChatService? ChatService { get; set; }

        /// <summary>
        /// Optional vector repository for table management
        /// </summary>
        public IVectorMemoryRepository? VectorRepository { get; set; }

        /// <summary>
        /// Optional persistence service for loading collections
        /// </summary>
        public VectorMemoryPersistenceService? PersistenceService { get; set; }

        /// <summary>
        /// Application configuration for folder paths
        /// </summary>
        public AppConfiguration? AppConfig { get; set; }

        private bool _collapsed;
        public bool Collapsed
        {
            get => _collapsed;
            private set
            {
                if (_collapsed == value) return;
                _collapsed = value;
                NotifyStateChanged();
            }
        }

        private bool _ragMode = true;
        public bool RagMode
        {
            get => _ragMode;
            set
            {
                if (_ragMode == value) return;
                _ragMode = value;
                NotifyStateChanged();
            }
        }

        private bool _performanceMetrics = false;
        public bool PerformanceMetrics
        {
            get => _performanceMetrics;
            set
            {
                if (_performanceMetrics == value) return;
                _performanceMetrics = value;
                NotifyStateChanged();
            }
        }

        private bool _debugMode = false;
        public bool DebugMode
        {
            get => _debugMode;
            set
            {
                if (_debugMode == value) return;
                _debugMode = value;
                NotifyStateChanged();
            }
        }

        private double _temperature = 0.7;
        public double Temperature
        {
            get => _temperature;
            set
            {
                if (Math.Abs(_temperature - value) < 0.0001) return;
                _temperature = value;
                NotifyStateChanged();
            }
        }

        private int _maxTokens = 512;
        public int MaxTokens
        {
            get => _maxTokens;
            set
            {
                if (_maxTokens == value) return;
                _maxTokens = value;
                NotifyStateChanged();
            }
        }

        private int _topK = 40;
        public int TopK
        {
            get => _topK;
            set
            {
                if (_topK == value) return;
                _topK = value;
                NotifyStateChanged();
            }
        }

        private double _topP = 0.95;
        public double TopP
        {
            get => _topP;
            set
            {
                if (Math.Abs(_topP - value) < 0.0001) return;
                _topP = value;
                NotifyStateChanged();
            }
        }

        private double _repeatPenalty = 1.1;
        public double RepeatPenalty
        {
            get => _repeatPenalty;
            set
            {
                if (Math.Abs(_repeatPenalty - value) < 0.0001) return;
                _repeatPenalty = value;
                NotifyStateChanged();
            }
        }

        private double _presencePenalty = 0.0;
        public double PresencePenalty
        {
            get => _presencePenalty;
            set
            {
                if (Math.Abs(_presencePenalty - value) < 0.0001) return;
                _presencePenalty = value;
                NotifyStateChanged();
            }
        }

        private double _frequencyPenalty = 0.0;
        public double FrequencyPenalty
        {
            get => _frequencyPenalty;
            set
            {
                if (Math.Abs(_frequencyPenalty - value) < 0.0001) return;
                _frequencyPenalty = value;
                NotifyStateChanged();
            }
        }

        private int _timeoutSeconds = 30;
        public int TimeoutSeconds
        {
            get => _timeoutSeconds;
            set
            {
                if (_timeoutSeconds == value) return;
                _timeoutSeconds = value;
                NotifyStateChanged();
            }
        }

        private string _currentModel = "phi-3.5-mini-instruct.gguf";
        public string CurrentModel
        {
            get => _currentModel;
            set
            {
                if (_currentModel == value) return;
                _currentModel = value;
                NotifyStateChanged();
            }
        }

        private string _activeTable = "MemoryFragments";
        public string ActiveTable
        {
            get => _activeTable;
            set
            {
                if (_activeTable == value) return;
                _activeTable = value;
                NotifyStateChanged();
            }
        }

        public string InboxPath { get; set; } = "c:/llm/Inbox";
        public string ArchivePath { get; set; } = "c:/llm/Archive";

        // Folder to search for GGUF model files. Can be overridden by the host or tests.
        public string ModelFolderPath { get; set; }

        private readonly List<string> _availableModels = new();
        public IReadOnlyList<string> AvailableModels => _availableModels.AsReadOnly();

        // Persist the selected model full path here so backend can open it later
        public string? SelectedModelFullPath { get; private set; }

        // Status message for table operations
        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            private set
            {
                _statusMessage = value;
                NotifyStateChanged();
            }
        }

        // Loading state for inbox reload
        private bool _isLoadingInbox = false;
        public bool IsLoadingInbox
        {
            get => _isLoadingInbox;
            private set
            {
                if (_isLoadingInbox == value) return;
                _isLoadingInbox = value;
                NotifyStateChanged();
            }
        }

        private string _loadingMessage = string.Empty;
        public string LoadingMessage
        {
            get => _loadingMessage;
            private set
            {
                _loadingMessage = value;
                NotifyStateChanged();
            }
        }

        // Collection name for saving new documents
        private string _collectionName = "game-rules-mpnet";
        public string CollectionName
        {
            get => _collectionName;
            set
            {
                if (_collectionName == value) return;
                _collectionName = value;
                NotifyStateChanged();
            }
        }

        // List of available collections
        private List<string> _availableCollections = new();
        public IReadOnlyList<string> AvailableCollections => _availableCollections.AsReadOnly();

        public DashboardService()
        {
            // Default model folder: try common locations, fall back to current directory
            var candidates = new[] { "d:/tinyllama", };
            ModelFolderPath = candidates.FirstOrDefault(Directory.Exists) ?? Directory.GetCurrentDirectory();

            RefreshAvailableModels();
        }

        // UI actions - in a real app these would call into application services
        public void ToggleSidebar() => Collapsed = !Collapsed;

        /// <summary>
        /// Scan ModelFolderPath for .gguf files and update AvailableModels.
        /// </summary>
        public Task RefreshAvailableModelsAsync()
        {
            RefreshAvailableModels();
            return Task.CompletedTask;
        }

        private void RefreshAvailableModels()
        {
            try
            {
                _availableModels.Clear();

                if (string.IsNullOrWhiteSpace(ModelFolderPath) || !Directory.Exists(ModelFolderPath))
                    return;

                var files = Directory.EnumerateFiles(ModelFolderPath, "*.gguf", SearchOption.TopDirectoryOnly)
                    .OrderBy(p => p)
                    .Select(Path.GetFileName);

                _availableModels.AddRange(files);

                // If current model isn't in the list, and list is not empty, pick the first
                if (!_availableModels.Contains(CurrentModel, StringComparer.OrdinalIgnoreCase) && _availableModels.Count > 0)
                {
                    CurrentModel = _availableModels[0];
                }

                NotifyStateChanged();
            }
            catch
            {
                // ignore IO errors for UI
            }
        }

        /// <summary>
        /// Returns the full path to the currently selected model, if available.
        /// </summary>
        public string? GetCurrentModelFullPath()
        {
            if (string.IsNullOrWhiteSpace(CurrentModel)) return null;
            var candidate = Path.Combine(ModelFolderPath ?? string.Empty, CurrentModel);
            return File.Exists(candidate) ? candidate : null;
        }

        public async Task SwitchModelAsync()
        {
            // In a real app, this would notify the backend model manager to switch to the selected model file.
            // For now refresh available models and ensure CurrentModel is valid.
            await RefreshAvailableModelsAsync();

            if (!_availableModels.Contains(CurrentModel, StringComparer.OrdinalIgnoreCase))
            {
                if (_availableModels.Count > 0)
                {
                    CurrentModel = _availableModels[0];
                }
            }

            // Store selected model full path so backend can open it later
            SelectedModelFullPath = GetCurrentModelFullPath();
            NotifyStateChanged();

            // If we have a handler assigned, invoke it to perform the actual backend switch
            if (SwitchModelHandler is not null && SelectedModelFullPath is not null)
            {
                try
                {
                    await SwitchModelHandler.Invoke(SelectedModelFullPath, null);
                    StatusMessage = $"[OK] Switched to model: {CurrentModel}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"[ERROR] Failed to switch model: {ex.Message}";
                }
            }
        }

        public async Task ListTablesAsync()
        {
            if (VectorRepository == null)
            {
                StatusMessage = "[ERROR] Repository not available";
                return;
            }

            try
            {
                var tables = await VectorRepository.GetAllTablesAsync();
                StatusMessage = $"[INFO] Found {tables.Count} tables: {string.Join(", ", tables)}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"[ERROR] Failed to list tables: {ex.Message}";
            }
        }

        public async Task InfoTableAsync()
        {
            if (VectorRepository == null)
            {
                StatusMessage = "[ERROR] Repository not available";
                return;
            }

            if (string.IsNullOrWhiteSpace(ActiveTable))
            {
                StatusMessage = "[ERROR] No table name specified";
                return;
            }

            try
            {
                var exists = await VectorRepository.TableExistsAsync(ActiveTable);
                if (!exists)
                {
                    StatusMessage = $"[ERROR] Table '{ActiveTable}' does not exist";
                    return;
                }

                var count = await VectorRepository.GetCountAsync(ActiveTable);
                var collections = await VectorRepository.GetCollectionsAsync();
                var hasEmbeddings = await VectorRepository.HasEmbeddingsAsync(ActiveTable);

                StatusMessage = $"[INFO] Table: {ActiveTable} | Fragments: {count} | Collections: {collections.Count} | Embeddings: {(hasEmbeddings ? "Yes" : "No")}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"[ERROR] Failed to get table info: {ex.Message}";
            }
        }

        public async Task CreateTableAsync()
        {
            if (VectorRepository == null)
            {
                StatusMessage = "[ERROR] Repository not available";
                return;
            }

            if (string.IsNullOrWhiteSpace(ActiveTable))
            {
                StatusMessage = "[ERROR] No table name specified";
                return;
            }

            try
            {
                var exists = await VectorRepository.TableExistsAsync(ActiveTable);
                if (exists)
                {
                    StatusMessage = $"[WARN] Table '{ActiveTable}' already exists";
                    return;
                }

                await VectorRepository.CreateTableAsync(ActiveTable);
                StatusMessage = $"[OK] Created table: {ActiveTable}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"[ERROR] Failed to create table: {ex.Message}";
            }
        }

        public async Task SwitchTableAsync()
        {
            if (VectorRepository == null)
            {
                StatusMessage = "[ERROR] Repository not available";
                return;
            }

            if (string.IsNullOrWhiteSpace(ActiveTable))
            {
                StatusMessage = "[ERROR] No table name specified";
                return;
            }

            try
            {
                var exists = await VectorRepository.TableExistsAsync(ActiveTable);
                if (!exists)
                {
                    StatusMessage = $"[ERROR] Table '{ActiveTable}' does not exist";
                    return;
                }

                VectorRepository.SetActiveTable(ActiveTable);
                StatusMessage = $"[OK] Switched to table: {ActiveTable}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"[ERROR] Failed to switch table: {ex.Message}";
            }
        }

        public async Task DeleteTableAsync()
        {
            if (VectorRepository == null)
            {
                StatusMessage = "[ERROR] Repository not available";
                return;
            }

            if (string.IsNullOrWhiteSpace(ActiveTable))
            {
                StatusMessage = "[ERROR] No table name specified";
                return;
            }

            // Don't allow deleting the default table
            if (ActiveTable.Equals("MemoryFragments", StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = "[WARN] Cannot delete default table 'MemoryFragments'";
                return;
            }

            try
            {
                var exists = await VectorRepository.TableExistsAsync(ActiveTable);
                if (!exists)
                {
                    StatusMessage = $"[ERROR] Table '{ActiveTable}' does not exist";
                    return;
                }

                await VectorRepository.DeleteTableAsync(ActiveTable);
                StatusMessage = $"[OK] Deleted table: {ActiveTable}";
                
                // Reset to default table
                ActiveTable = "MemoryFragments";
            }
            catch (Exception ex)
            {
                StatusMessage = $"[ERROR] Failed to delete table: {ex.Message}";
            }
        }

        public async Task ReloadInboxAsync()
        {
            if (PersistenceService == null || AppConfig == null)
            {
                StatusMessage = "[ERROR] Services not available";
                return;
            }

            try
            {
                IsLoadingInbox = true;
                LoadingMessage = "Checking for new files in inbox...";
                StatusMessage = "[INFO] Checking for new files in inbox...";
                NotifyStateChanged();

                var inboxFolder = AppConfig.Folders?.InboxFolder ?? InboxPath;
                var archiveFolder = AppConfig.Folders?.ArchiveFolder ?? ArchivePath;

                if (!Directory.Exists(inboxFolder))
                {
                    StatusMessage = $"[ERROR] Inbox folder not found: {inboxFolder}";
                    return;
                }

                var fileWatcher = new KnowledgeFileWatcher(inboxFolder, archiveFolder);
                var newFiles = await fileWatcher.DiscoverNewFilesAsync();

                if (!newFiles.Any())
                {
                    StatusMessage = "[INFO] No new files found in inbox";
                    return;
                }

                LoadingMessage = $"Found {newFiles.Count} file(s). Processing fragments...";
                StatusMessage = $"[INFO] Found {newFiles.Count} new file(s). Processing...";
                NotifyStateChanged();

                // Collect fragments from files
                var allFragments = new List<MemoryFragment>();
                int fileIndex = 0;
                foreach (var (gameName, filePath) in newFiles)
                {
                    fileIndex++;
                    LoadingMessage = $"Processing file {fileIndex}/{newFiles.Count}: {gameName}...";
                    NotifyStateChanged();
                    
                    var fragments = await fileWatcher.ProcessFileAsync(gameName, filePath);
                    allFragments.AddRange(fragments);
                }

                // Save to database using the current collection name
                LoadingMessage = $"Generating embeddings for {allFragments.Count} fragments...";
                NotifyStateChanged();
                
                await PersistenceService.SaveFragmentsAsync(
                    allFragments,
                    CollectionName,  // Use the collection name from UI
                    sourceFile: string.Join(", ", newFiles.Keys),
                    replaceExisting: false);

                // Archive processed files
                LoadingMessage = "Archiving processed files...";
                NotifyStateChanged();
                
                foreach (var filePath in newFiles.Values)
                {
                    await fileWatcher.ArchiveFileAsync(filePath);
                }

                StatusMessage = $"[OK] Processed {newFiles.Count} file(s), {allFragments.Count} fragments saved to '{CollectionName}'";
                
                // Refresh collections list
                await RefreshCollectionsAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = $"[ERROR] Failed to reload inbox: {ex.Message}";
            }
            finally
            {
                IsLoadingInbox = false;
                LoadingMessage = string.Empty;
                NotifyStateChanged();
            }
        }

        /// <summary>
        /// Refresh the list of available collections from the database
        /// </summary>
        public async Task RefreshCollectionsAsync()
        {
            if (VectorRepository == null)
            {
                return;
            }

            try
            {
                _availableCollections.Clear();
                var collections = await VectorRepository.GetCollectionsAsync();
                _availableCollections.AddRange(collections.OrderBy(c => c));
                // Don't call NotifyStateChanged here - let the component handle UI updates
            }
            catch (Exception ex)
            {
                StatusMessage = $"[ERROR] Failed to refresh collections: {ex.Message}";
                // StatusMessage setter will call NotifyStateChanged
            }
        }

        /// <summary>
        /// Get information about a specific collection
        /// </summary>
        public async Task GetCollectionInfoAsync(string collectionName)
        {
            if (VectorRepository == null)
            {
                StatusMessage = "[ERROR] Repository not available";
                return;
            }

            if (string.IsNullOrWhiteSpace(collectionName))
            {
                StatusMessage = "[ERROR] Collection name is required";
                return;
            }

            try
            {
                var exists = await VectorRepository.CollectionExistsAsync(collectionName);
                if (!exists)
                {
                    StatusMessage = $"[INFO] Collection '{collectionName}' does not exist yet";
                    return;
                }

                var count = await VectorRepository.GetCountAsync(collectionName);
                var hasEmbeddings = await VectorRepository.HasEmbeddingsAsync(collectionName);

                StatusMessage = $"[INFO] Collection: {collectionName} | Fragments: {count} | Embeddings: {(hasEmbeddings ? "Yes" : "No")}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"[ERROR] Failed to get collection info: {ex.Message}";
            }
        }

        /// <summary>
        /// Delete a collection from the database
        /// </summary>
        public async Task DeleteCollectionAsync(string collectionName)
        {
            if (VectorRepository == null)
            {
                StatusMessage = "[ERROR] Repository not available";
                return;
            }

            if (string.IsNullOrWhiteSpace(collectionName))
            {
                StatusMessage = "[ERROR] Collection name is required";
                return;
            }

            try
            {
                var exists = await VectorRepository.CollectionExistsAsync(collectionName);
                if (!exists)
                {
                    StatusMessage = $"[INFO] Collection '{collectionName}' does not exist";
                    return;
                }

                await VectorRepository.DeleteCollectionAsync(collectionName);
                StatusMessage = $"[OK] Deleted collection: {collectionName}";
                
                // Refresh collections list
                await RefreshCollectionsAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = $"[ERROR] Failed to delete collection: {ex.Message}";
            }
        }

        /// <summary>
        /// Load a collection from the database into vector memory for RAG queries
        /// </summary>
        public async Task LoadCollectionAsync(string collectionName)
        {
            if (PersistenceService == null || ChatService == null)
            {
                StatusMessage = "[ERROR] Services not available";
                throw new InvalidOperationException("Persistence service or chat service not available");
            }

            if (string.IsNullOrWhiteSpace(collectionName))
            {
                StatusMessage = "[ERROR] Collection name is required";
                throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));
            }

            try
            {
                StatusMessage = $"[INFO] Loading collection '{collectionName}'...";
                NotifyStateChanged();

                var vectorMemory = await PersistenceService.LoadVectorMemoryAsync(collectionName);
                
                // Replace the vector memory in ChatService
                ChatService.SetVectorMemory(vectorMemory);
                
                StatusMessage = $"[OK] Loaded collection '{collectionName}' with {vectorMemory.Count} fragments";
            }
            catch (Exception ex)
            {
                StatusMessage = $"[ERROR] Failed to load collection: {ex.Message}";
                throw;
            }
        }

        public async Task<string> SendMessageAsync(string message)
        {
            if (ChatService == null)
            {
                return "[ERROR] Chat service not initialized. Please check application configuration.";
            }

            try
            {
                // Build generation settings from current UI state
                var genSettings = new GenerationSettings
                {
                    Temperature = (float)Temperature,
                    MaxTokens = MaxTokens,
                    TopK = TopK,
                    TopP = (float)TopP,
                    RepeatPenalty = (float)RepeatPenalty,
                    PresencePenalty = (float)PresencePenalty,
                    FrequencyPenalty = (float)FrequencyPenalty
                };

                // Send message with current settings including timeout
                return await ChatService.SendMessageAsync(
                    message,
                    RagMode,
                    DebugMode,
                    PerformanceMetrics,
                    genSettings,
                    TimeoutSeconds);
            }
            catch (Exception ex)
            {
                return $"[ERROR] Failed to send message: {ex.Message}";
            }
        }

        private void NotifyStateChanged() => OnChange?.Invoke();
    }
}
