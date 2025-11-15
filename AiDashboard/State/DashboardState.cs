using System;
using System.Threading.Tasks;
using AiDashboard.Services;
using Application.AI.Management;
using Services.Configuration;
using Services.Management;
using Services.Memory;
using Services.Repositories;

namespace AiDashboard.State;

/// <summary>
/// Blazor-specific dashboard state management.
/// Orchestrates the specialized services and provides UI state.
/// </summary>
public class DashboardState
{
    // Change notification for Blazor components
    public event Action? OnChange;

    // Specialized services
    public GenerationSettingsService SettingsService { get; }
    public ModelManagementService ModelService { get; }
    public CollectionManagementService? CollectionService { get; private set; }
    public InboxProcessingService? InboxService { get; private set; }
    public DashboardChatService? ChatService { get; set; }

    // UI-specific state
    private bool _collapsed;
    public bool Collapsed
    {
        get => _collapsed;
        set
        {
            if (_collapsed == value) return;
            _collapsed = value;
            NotifyStateChanged();
        }
    }

    private string _statusMessage = string.Empty;
    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            _statusMessage = value;
            NotifyStateChanged();
        }
    }

    // Table management (for backward compatibility - can be moved to a service later)
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

    public IVectorMemoryRepository? VectorRepository { get; set; }

    public DashboardState(string? modelFolderPath = null)
    {
        SettingsService = new GenerationSettingsService();
        ModelService = new ModelManagementService(modelFolderPath);

        // Subscribe to child service changes
        SettingsService.OnChange += NotifyStateChanged;
        ModelService.OnChange += NotifyStateChanged;
    }

    /// <summary>
    /// Initialize optional services when dependencies are available
    /// </summary>
    public void InitializeServices(
        IVectorMemoryRepository? repository,
        VectorMemoryPersistenceService? persistenceService,
        AppConfiguration? appConfig)
    {
        VectorRepository = repository;

        if (repository != null && persistenceService != null)
        {
            CollectionService = new CollectionManagementService(repository, persistenceService);
            CollectionService.OnChange += NotifyStateChanged;
        }

        if (persistenceService != null && appConfig != null)
        {
            InboxService = new InboxProcessingService(persistenceService, appConfig);
            InboxService.OnProgressUpdate += (status) =>
            {
                StatusMessage = status;
            };
            InboxService.OnProcessingComplete += NotifyStateChanged;
        }
    }

    // UI actions
    public void ToggleSidebar() => Collapsed = !Collapsed;

    // Model operations
    public async Task RefreshModelsAsync()
    {
        await ModelService.RefreshAvailableModelsAsync();
    }

    public async Task SwitchModelAsync()
    {
        var (success, message) = await ModelService.SwitchModelAsync();
        StatusMessage = success ? $"[OK] {message}" : $"[ERROR] {message}";
    }

    // Table operations (delegating to repository)
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

    // Collection operations (delegating to CollectionService)
    public async Task RefreshCollectionsAsync()
    {
        if (CollectionService == null)
        {
            StatusMessage = "[ERROR] Collection service not available";
            return;
        }

        var (success, message) = await CollectionService.RefreshCollectionsAsync();
        if (!success)
        {
            StatusMessage = $"[ERROR] {message}";
        }
    }

    public async Task GetCollectionInfoAsync(string collectionName)
    {
        if (CollectionService == null)
        {
            StatusMessage = "[ERROR] Collection service not available";
            return;
        }

        var (success, message, _, _) = await CollectionService.GetCollectionInfoAsync(collectionName);
        StatusMessage = success ? $"[INFO] {message}" : $"[ERROR] {message}";
    }

    public async Task DeleteCollectionAsync(string collectionName)
    {
        if (CollectionService == null)
        {
            StatusMessage = "[ERROR] Collection service not available";
            return;
        }

        var (success, message) = await CollectionService.DeleteCollectionAsync(collectionName);
        StatusMessage = success ? $"[OK] {message}" : $"[ERROR] {message}";
    }

    public async Task LoadCollectionAsync(string collectionName)
    {
        if (CollectionService == null || ChatService == null)
        {
            StatusMessage = "[ERROR] Services not available";
            return;
        }

        var (success, message, memory) = await CollectionService.LoadCollectionAsync(collectionName);
        
        if (success && memory != null)
        {
            ChatService.SetVectorMemory(memory);
            StatusMessage = $"[OK] {message}";
        }
        else
        {
            StatusMessage = $"[ERROR] {message}";
        }
    }

    // Inbox operations (delegating to InboxService)
    public async Task ReloadInboxAsync()
    {
        if (InboxService == null || CollectionService == null)
        {
            StatusMessage = "[ERROR] Services not available";
            return;
        }

        var (success, message, filesProcessed, fragmentsCreated) = 
            await InboxService.ProcessInboxAsync(CollectionService.CurrentCollection);

        if (success && filesProcessed > 0)
        {
            await RefreshCollectionsAsync();
        }

        StatusMessage = success ? $"[OK] {message}" : $"[ERROR] {message}";
    }

    // Chat operations
    public async Task<string> SendMessageAsync(string message)
    {
        if (ChatService == null)
        {
            return "[ERROR] Chat service not initialized. Please check application configuration.";
        }

        try
        {
            var genSettings = SettingsService.ToGenerationSettings();

            return await ChatService.SendMessageAsync(
                message,
                SettingsService.RagMode,
                SettingsService.DebugMode,
                SettingsService.PerformanceMetrics,
                genSettings,
                SettingsService.TimeoutSeconds);
        }
        catch (Exception ex)
        {
            return $"[ERROR] Failed to send message: {ex.Message}";
        }
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}
