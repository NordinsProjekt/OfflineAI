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
    
    // Blazor dispatcher callback for thread-safe UI updates
    private Func<Action, Task>? _invokeAsync;
    
    /// <summary>
    /// Set the InvokeAsync callback from a Blazor component to enable thread-safe UI updates.
    /// This should be called once during initialization from a ComponentBase.
    /// </summary>
    public void SetInvokeAsync(Func<Action, Task> invokeAsync)
    {
        _invokeAsync = invokeAsync;
    }

    // Specialized services
    public GenerationSettingsService SettingsService { get; }
    public ModelManagementService ModelService { get; }
    public CollectionManagementService? CollectionService { get; private set; }
    public InboxProcessingService? InboxService { get; private set; }
    public BotPersonalityService? PersonalityService { get; private set; }
    
    private DashboardChatService? _chatService;
    public DashboardChatService? ChatService 
    { 
        get => _chatService;
        set
        {
            _chatService = value;
            
            // If we have a chat service, inject the model name provider
            if (_chatService != null)
            {
                _chatService.SetCurrentModelNameProvider(() => ModelService.CurrentModel);
            }
        }
    }

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

    // Section collapse states
    private readonly Dictionary<string, bool> _sectionCollapseState = new()
    {
        { "modes", true },           // Collapsed by default
        { "personality", false },    // Expanded by default (new feature!)
        { "generation", true },      // Collapsed by default
        { "rag", true },            // Collapsed by default
        { "model", false },         // Expanded by default (keep visible)
        { "collection", false },    // Expanded by default (needed for bot selection)
        { "domains", true },        // Collapsed by default
        { "files", true },          // Collapsed by default
        { "knowledge", true },      // Collapsed by default
        { "table", true }           // Collapsed by default
    };

    public bool IsSectionCollapsed(string sectionKey)
    {
        return _sectionCollapseState.TryGetValue(sectionKey, out var collapsed) && collapsed;
    }

    public void ToggleSection(string sectionKey)
    {
        if (_sectionCollapseState.ContainsKey(sectionKey))
        {
            _sectionCollapseState[sectionKey] = !_sectionCollapseState[sectionKey];
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
        AppConfiguration? appConfig,
        BotPersonalityService? personalityService = null)
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
        
        if (personalityService != null)
        {
            PersonalityService = personalityService;
            PersonalityService.OnChange += NotifyStateChanged;
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
        if (CollectionService == null)
        {
            StatusMessage = "[ERROR] Collection service not available";
            return;
        }

        var (success, message) = await CollectionService.ValidateCollectionAsync(collectionName);
        
        // IMPORTANT: Update the DatabaseVectorMemory to use the new collection
        if (success && ChatService != null)
        {
            ChatService.UpdateCollectionName(collectionName);
        }
        
        StatusMessage = success ? $"[OK] {message}" : $"[ERROR] {message}";
        
        // Note: DatabaseVectorMemory queries collections on-demand, no need to load into memory
    }

    // Inbox operations (delegating to InboxService)
    public async Task ReloadInboxAsync()
    {
        if (InboxService == null || CollectionService == null)
        {
            StatusMessage = "[ERROR] Services not available";
            return;
        }

        // Hook up domain auto-registration callback BEFORE processing files
        // This ensures "Webhallen" gets registered when importing "Webhallen - Köpvillkor" fragments
        InboxService.OnDomainDiscovered = async (category, categoryType) =>
        {
            // Extract domain name from category (e.g., "Webhallen" from "##Webhallen - Köpvillkor")
            // The DomainDetector will strip the "##" prefix
            var domainDetector = _chatService?.DomainDetector;
            if (domainDetector != null)
            {
                await domainDetector.RegisterDomainFromCategoryAsync(category, categoryType);
            }
        };

        var (success, message, filesProcessed, fragmentsCreated) = 
            await InboxService.ProcessInboxAsync(CollectionService.CurrentCollection);

        if (success && filesProcessed > 0)
        {
            await RefreshCollectionsAsync();
        }

        StatusMessage = success ? $"[OK] {message}" : $"[ERROR] {message}";
    }

    public async Task ConvertPdfToTxtInInboxAsync()
    {
        if (InboxService == null)
        {
            StatusMessage = "[ERROR] Inbox service not available";
            return;
        }

        var (success, message, filesConverted) = await InboxService.ConvertPdfToTxtAsync();
        StatusMessage = success ? $"[OK] {message}" : $"[ERROR] {message}";
    }
    
    // Personality operations
    public async Task RefreshPersonalitiesAsync()
    {
        if (PersonalityService == null)
        {
            StatusMessage = "[ERROR] Personality service not available";
            return;
        }

        var (success, message) = await PersonalityService.RefreshPersonalitiesAsync();
        if (!success)
        {
            StatusMessage = $"[ERROR] {message}";
        }
    }
    
    public async Task SelectPersonalityAsync(string personalityId)
    {
        if (PersonalityService == null)
        {
            StatusMessage = "[ERROR] Personality service not available";
            return;
        }

        var (success, message) = await PersonalityService.SelectPersonalityAsync(personalityId);
        
        // If a personality with a default collection is selected, switch to that collection
        if (success && PersonalityService.CurrentPersonality?.DefaultCollection != null && CollectionService != null)
        {
            var collectionName = PersonalityService.CurrentPersonality.DefaultCollection;
            CollectionService.CurrentCollection = collectionName;
            await LoadCollectionAsync(collectionName);
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
                PersonalityService?.CurrentPersonality,
                SettingsService.UseGpu,
                SettingsService.GpuLayers,
                SettingsService.TimeoutSeconds);
        }
        catch (Exception ex)
        {
            return $"[ERROR] Failed to send message: {ex.Message}";
        }
    }

    // QuickAsk-specific method that always disables RAG
    public async Task<string> SendQuickAskAsync(string message)
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
                ragMode: false,  // Always disable RAG for QuickAsk
                SettingsService.DebugMode,
                SettingsService.PerformanceMetrics,
                genSettings,
                PersonalityService?.CurrentPersonality,
                SettingsService.UseGpu,
                SettingsService.GpuLayers,
                SettingsService.TimeoutSeconds);  // Use global timeout setting
        }
        catch (Exception ex)
        {
            return $"[ERROR] Failed to send message: {ex.Message}";
        }
    }

    private void NotifyStateChanged()
    {
        if (_invokeAsync != null)
        {
            // Run on Blazor dispatcher thread
            _ = _invokeAsync.Invoke(() => OnChange?.Invoke());
        }
        else
        {
            // Fallback for synchronous context (may cause issues if called from background thread)
            OnChange?.Invoke();
        }
    }
}
