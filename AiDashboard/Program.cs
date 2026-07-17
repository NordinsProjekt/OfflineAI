using AiDashboard.Components;
using AiDashboard.Services;
using AiDashboard.Services.Interfaces;
using Application.AI.Pooling;
using Application.AI.Management;
using Application.AI.Embeddings;
using Application.AI.Gemma4;
using Services.Memory;
using Services.Interfaces;
using Services.Repositories;
using Services.AgentTools;
using Microsoft.Extensions.AI;
using Infrastructure.Data.Dapper;
using Services.Configuration;
using Services.Management;
using Services.Language;
using Services.FileAgent;
using Services.QuickAsk;
using Services.Workspace;
using Services.BatchJobs;
using Services.GoalAgent;

// Infrastructure.Data.Dapper uses WindowsIdentity to grant DB access; this app only runs on Windows.
[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]

namespace AiDashboard;

public static class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        // Raise the Blazor Server circuit's SignalR message size above the default (~32 KB) so
        // large file uploads (e.g. 100 MB PDFs via AgentFileUpload) don't get rejected before
        // AgentFileUpload's own MaxFileSizeBytes check even runs. Set a bit higher than the
        // largest AgentFileUpload.MaxFileSizeBytes in use (100 MB) to leave transport overhead.
        builder.Services.Configure<Microsoft.AspNetCore.SignalR.HubOptions>(options =>
        {
            options.MaximumReceiveMessageSize = 110 * 1024 * 1024;
        });

        // Companion to the HubOptions change above: if the SignalR circuit ever falls back from
        // WebSockets to long-polling (proxies, some browsers/networks), that transport sends
        // data as regular HTTP request bodies, which Kestrel caps at ~28.6 MB by default —
        // independently of MaximumReceiveMessageSize. Without raising this too, a large upload
        // over long-polling gets its request rejected and the circuit dies ("Rejoin failed").
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = 110 * 1024 * 1024;
        });

        // Register AppConfiguration
        var appConfig = builder.Configuration.GetSection("AppConfiguration").Get<AppConfiguration>() ?? new AppConfiguration();
        builder.Services.AddSingleton(appConfig);
        
        // Register language services for stop words filtering
        builder.Services.AddSingleton<ILanguageStopWordsService, LanguageStopWordsService>();

        // Register LLM response formatter service
        builder.Services.AddSingleton<ILlmResponseFormatterService, LlmResponseFormatterService>();

        // Register QuickAsk service for conversation management
        builder.Services.AddSingleton<IQuickAskService, QuickAskService>();

        // Register the workspace service: manages the list of user-selectable workspace
        // directories (persisted in %AppData%\OfflineAI\workspaces.json) and the active
        // selection. The file agent is always confined to whichever workspace is active.
        builder.Services.AddSingleton<IWorkspaceService>(_ =>
        {
            var defaultAgentDir = !string.IsNullOrWhiteSpace(appConfig.Folders.AgentFilesFolder)
                ? appConfig.Folders.AgentFilesFolder
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "OfflineAI", "AgentFiles");
            // Confine every workspace to the configured root (defaults to the parent of the
            // agent-files folder) so the file agent can never be pointed outside that tree.
            return new WorkspaceService(defaultAgentDir, workspaceRoot: appConfig.Folders.WorkspaceRoot);
        });

        // Register file agent service for /skapa, /fyll, /läs chat commands. Rooted at the
        // active workspace directory; SetBaseDirectory(...) re-confines it whenever the user
        // switches workspaces, so the LLM can never read/write outside the selected directory.
        builder.Services.AddSingleton<IFileAgentService>(sp =>
        {
            var workspaceService = sp.GetRequiredService<IWorkspaceService>();
            var fileAgent = new FileAgentService(workspaceService.GetActiveWorkspace().Path);
            workspaceService.ActiveWorkspaceChanged += workspace => fileAgent.SetBaseDirectory(workspace.Path);
            return fileAgent;
        });

        // Register agent tool registry (used by Gemma 4 CLI tool-calling)
        builder.Services.AddSingleton<IAgentToolRegistry, AgentToolRegistry>();

        // Register the utility tools service for /tid, /datum, and /api <slutpunkt> <instruktion>.
        // API endpoints are resolved only from AppConfiguration.AgentTools.Endpoints — the LLM can
        // never supply an arbitrary URL.
        builder.Services.AddHttpClient("AgentApiTools");
        builder.Services.AddSingleton<IUtilityToolsService, UtilityToolsService>();

        // Register the external tools service: operator-configured local executables the LLM
        // may run by slash command. Tools are resolved only from
        // AppConfiguration.AgentTools.ExternalTools (appsettings/user secrets) — the LLM picks
        // a tool by name and supplies argument text; it can never specify a path.
        builder.Services.AddSingleton<IExternalToolsService, ExternalToolsService>();

        // Register the lightweight, text-based agentic chat service used by QuickAsk and the
        // Dashboard chat: tells the LLM about the IFileAgentService/IUtilityToolsService slash
        // commands and executes any it requests, feeding the result back for a final answer. The
        // tool-call loop always stays internal to this service — only status callbacks and the
        // final answer are meant to reach the user.
        builder.Services.AddSingleton<IAgenticChatService>(sp =>
            new AgenticChatService(
                sp.GetRequiredService<IFileAgentService>(),
                sp.GetRequiredService<IUtilityToolsService>(),
                appConfig.AgentTools.MaxToolCallRounds,
                sp.GetRequiredService<IExternalToolsService>()));

        // Register the batch job queue (Batch Processing page): feeds each queued task's
        // free-text description into IAgenticChatService.SendWithToolsAsync one at a time, so
        // jobs can freely use the same file-agent tools as regular chat. Singleton so the queue
        // survives page navigation (not persisted across an app restart).
        builder.Services.AddSingleton<IBatchJobService, BatchJobService>();

        // Register the TDD-style goal agent (Agent Mode page): breaks a free-text workspace
        // goal into checkable requirements, does file work via IAgenticChatService, verifies
        // each requirement against the workspace, and repeats until everything passes (or the
        // iteration cap is hit). Singleton so a run's progress survives page navigation. The
        // file agent is passed so each run writes a full prompt/response transcript to
        // agentlogg.txt in the active workspace for debugging. The run repository (optional — only
        // registered when a database is configured) additionally records each run as history that
        // survives an app restart, which agentlogg.txt does not: it is overwritten per run.
        builder.Services.AddSingleton<IGoalAgentService>(sp =>
            new GoalAgentService(
                sp.GetRequiredService<IAgenticChatService>(),
                sp.GetRequiredService<IFileAgentService>(),
                appConfig.AgentTools.MaxGoalIterations,
                sp.GetService<IAgentRunRepository>()));

        // Register Gemma 4 CLI service. Prefer an explicit Gemma4Cli section, but when its
        // ModelPath is empty fall back to the main Llm model *if that model is itself a Gemma
        // model* (ModelType "Gemma") — Gemma4CliService builds Gemma's chat template, so pointing
        // it at a non-Gemma model would mis-format the prompt. This lets the templated Gemma path
        // (chat + goal agent) work from a single Llm config, instead of silently falling back to
        // the un-templated pooled path that returns empty output for instruct models.
        var gemma4CliCfg = appConfig.Gemma4Cli;
        var gemma4CliExe = !string.IsNullOrWhiteSpace(gemma4CliCfg.LlamaCliPath)
            ? gemma4CliCfg.LlamaCliPath
            : appConfig.Llm?.ExecutablePath ?? string.Empty;
        var gemma4UsingLlmFallback = string.IsNullOrWhiteSpace(gemma4CliCfg.ModelPath)
            && string.Equals(appConfig.Llm?.ModelType, "Gemma", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(appConfig.Llm?.ModelPath);
        string gemma4CliModel;
        if (!string.IsNullOrWhiteSpace(gemma4CliCfg.ModelPath))
            gemma4CliModel = gemma4CliCfg.ModelPath;
        else if (gemma4UsingLlmFallback)
            gemma4CliModel = appConfig.Llm!.ModelPath;
        else
            gemma4CliModel = string.Empty;
        if (!string.IsNullOrWhiteSpace(gemma4CliModel)
            && !string.IsNullOrWhiteSpace(gemma4CliExe))
        {
            builder.Services.AddSingleton<IGemma4CliService>(sp =>
            {
                var opts = new Gemma4CliOptions
                {
                    LlamaCliPath           = gemma4CliExe,
                    ModelPath              = gemma4CliModel,
                    // In fallback mode inherit the operator's hardware tuning from the Llm section
                    // (they may have deliberately limited GPU layers / context for the GPU).
                    GpuLayers              = gemma4UsingLlmFallback ? appConfig.Llm!.GpuLayers : gemma4CliCfg.GpuLayers,
                    ContextSize            = gemma4UsingLlmFallback ? appConfig.Llm!.ContextSize : gemma4CliCfg.ContextSize,
                    Device                 = !string.IsNullOrWhiteSpace(gemma4CliCfg.Device)
                                                 ? gemma4CliCfg.Device
                                                 : appConfig.Llm?.Device ?? string.Empty,
                    MaxTokens              = gemma4CliCfg.MaxTokens,
                    Temperature            = gemma4CliCfg.Temperature,
                    TopP                   = gemma4CliCfg.TopP,
                    TopK                   = gemma4CliCfg.TopK,
                    TimeoutMs              = gemma4CliCfg.TimeoutMs,
                    PauseTimeoutMs         = gemma4CliCfg.PauseTimeoutMs,
                    MaxToolCallIterations  = gemma4CliCfg.MaxToolCallIterations
                };
                var registry = sp.GetRequiredService<IAgentToolRegistry>();
                _ = registry; // available for future tool-call wiring
                var source = gemma4UsingLlmFallback ? " (from Llm config)" : string.Empty;
                Console.WriteLine($"[+] Gemma 4 CLI service registered (model: {Path.GetFileName(gemma4CliModel)}){source}");
                return new Gemma4CliService(opts);
            });
        }
        else
        {
            Console.WriteLine("[!] Gemma 4 CLI service not configured (AppConfiguration:Gemma4Cli:ModelPath missing)");
        }

        // Register document analysis services
        builder.Services.AddScoped<IDocumentAnalysisService, DocumentAnalysisService>();
        builder.Services.AddScoped<IKursplanAnalysisService, KursplanAnalysisService>();
        builder.Services.AddScoped<IDocumentTypeDetector, DocumentTypeDetector>();

        // Register web scraper service. The named "WebScraper" client disables automatic redirect
        // following so WebScraperService can validate every redirect hop against its SSRF host
        // allow-list instead of letting HttpClient silently follow a redirect to an internal target.
        builder.Services.AddHttpClient();
        builder.Services.AddHttpClient("WebScraper")
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        builder.Services.AddScoped<IWebScraperService, WebScraperService>();

        // Read configuration for LLM
        var llmExe = appConfig.Llm?.ExecutablePath ?? builder.Configuration["AppConfiguration:Llm:ExecutablePath"] ?? string.Empty;
        var llmModel = appConfig.Llm?.ModelPath ?? builder.Configuration["AppConfiguration:Llm:ModelPath"] ?? string.Empty;
        var poolMax = appConfig.Pool?.MaxInstances ?? (int.TryParse(builder.Configuration["AppConfiguration:Pool:MaxInstances"], out var m) ? m : 3);
        var poolTimeout = appConfig.Pool?.TimeoutMs ?? (int.TryParse(builder.Configuration["AppConfiguration:Pool:TimeoutMs"], out var t) ? t : 300000); // 5 minutes default (changed from 30 seconds)

        // Read embedding configuration
        var embeddingModelPath = appConfig.Embedding?.ModelPath ?? builder.Configuration["AppConfiguration:Embedding:ModelPath"] ?? string.Empty;
        var embeddingVocabPath = appConfig.Embedding?.VocabPath ?? builder.Configuration["AppConfiguration:Embedding:VocabPath"] ?? string.Empty;
        var embeddingDimension = appConfig.Embedding?.Dimension ?? (int.TryParse(builder.Configuration["AppConfiguration:Embedding:Dimension"], out var dim) ? dim : 768);

        // Read database configuration
        var dbConnectionString = builder.Configuration["DatabaseConfig:ConnectionString"] 
            ?? @"Server=(localdb)\mssqllocaldb;Database=VectorMemoryDB;Integrated Security=true;TrustServerCertificate=true;";
        var dbTableName = builder.Configuration["DatabaseConfig:ActiveTableName"] ?? "MemoryFragments";

        // Validate configuration
        var configErrors = new List<string>();
        if (string.IsNullOrEmpty(llmExe)) configErrors.Add("AppConfiguration:Llm:ExecutablePath is missing");
        if (string.IsNullOrEmpty(llmModel)) configErrors.Add("AppConfiguration:Llm:ModelPath is missing");

        if (configErrors.Any())
        {
            Console.WriteLine("[!] Configuration Errors:");
            foreach (var error in configErrors)
            {
                Console.WriteLine($"   - {error}");
            }
            Console.WriteLine("\n[!] Please update User Secrets to configure required paths.");
            Console.WriteLine("   The application will start but functionality will be limited.\n");
        }

        // Register embedding service (optional for dashboard - only needed if RAG is enabled)
        var embeddingServiceRegistered = false;
        if (!string.IsNullOrEmpty(embeddingModelPath) && !string.IsNullOrEmpty(embeddingVocabPath))
        {
            try
            {
                builder.Services.AddSingleton(sp => new SemanticEmbeddingService(
                    embeddingModelPath,
                    embeddingVocabPath,
                    embeddingDimension,
                    debugMode: false));

                builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
                    sp.GetRequiredService<SemanticEmbeddingService>());

                embeddingServiceRegistered = true;
                Console.WriteLine("[+] Embedding service registered (RAG available)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[!] Warning: Failed to register embedding service: {ex.Message}");
                Console.WriteLine("   RAG mode will not be available.");
            }
        }
        else
        {
            Console.WriteLine("[!] Embedding service not configured (RAG disabled)");
        }

        // Register Dapper repositories (optional - but required for table management and collections)
        if (!string.IsNullOrEmpty(dbConnectionString))
        {
            try
            {
                builder.Services.AddDapperVectorMemoryRepository(dbConnectionString, dbTableName);

                // Register KnowledgeDomainRepository for domain-based filtering
                builder.Services.AddDapperKnowledgeDomainRepository(dbConnectionString);

                // Register LLM and Question repositories
                builder.Services.AddDapperLlmRepository(dbConnectionString);
                builder.Services.AddDapperQuestionRepository(dbConnectionString);

                // Register BotPersonalityRepository for personality management
                builder.Services.AddDapperBotPersonalityRepository(dbConnectionString);

                // Register AgentRunRepository for goal-agent run history (Agent History page)
                builder.Services.AddDapperAgentRunRepository(dbConnectionString);

                Console.WriteLine("[+] Database repository registered");

                // Only register persistence service if we have both repository AND embedding service
                if (embeddingServiceRegistered)
                {
                    builder.Services.AddSingleton<VectorMemoryPersistenceService>();
                    Console.WriteLine("[+] Persistence service registered (collection loading available)");
                }
                else
                {
                    Console.WriteLine("[!] Persistence service not registered - embedding service missing");
                    Console.WriteLine("   Collection loading will not be available");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[!] Warning: Failed to register database services: {ex.Message}");
                Console.WriteLine("   Table management and collection loading will not be available");
            }
        }
        else
        {
            Console.WriteLine("[!] Database not configured");
            Console.WriteLine("   Table management and collection loading disabled");
        }
        
        // Register LlmSyncService
        if (!string.IsNullOrEmpty(llmModel))
        {
            builder.Services.AddSingleton(sp =>
            {
                var llmRepository = sp.GetRequiredService<ILlmRepository>();
                var llmFolderPath = Path.GetDirectoryName(llmModel) ?? string.Empty;
                return new LlmSyncService(llmRepository, llmFolderPath);
            });
        }

        // Register DomainDetector (requires KnowledgeDomainRepository)
        builder.Services.AddSingleton<Application.AI.Utilities.IDomainDetector, Application.AI.Utilities.DomainDetector>();
        
        // Register BotPersonalityService (requires BotPersonalityRepository)
        builder.Services.AddSingleton<BotPersonalityService>();

        // Register memory for knowledge base
        builder.Services.AddSingleton<ILlmMemory>(sp =>
        {
            var embeddingService = sp.GetService<IEmbeddingGenerator<string, Embedding<float>>>();
            var repository = sp.GetService<IVectorMemoryRepository>();
            var stopWordsService = sp.GetRequiredService<ILanguageStopWordsService>();
            var collectionName = appConfig.Debug?.CollectionName ?? builder.Configuration["AppConfiguration:Debug:CollectionName"] ?? "game-rules-mpnet";

            if (embeddingService != null && repository != null)
            {
                // Use database-backed vector memory for RAG queries
                Console.WriteLine($"[+] Database vector memory initialized (collection: {collectionName})");
                return new DatabaseVectorMemory(embeddingService, repository, stopWordsService, collectionName);
            }
            else
            {
                // Fallback to simple string memory (RAG not available)
                Console.WriteLine("[!] Simple memory initialized (RAG not available - database or embedding service missing)");
                return new StringJoinMemory();
            }
        });

        // Register conversation memory (in-memory, simple) - second ILlmMemory registration
        builder.Services.AddSingleton<ILlmMemory>(sp => new AiDashboard.Services.StringJoinMemory());

        // Register AI model pool and manager (required)
        try
        {
            if (!string.IsNullOrEmpty(llmExe) && !string.IsNullOrEmpty(llmModel))
            {
                // Validate files exist before creating pool
                if (!System.IO.File.Exists(llmExe))
                {
                    Console.WriteLine($"[!] LLM executable not found: {llmExe}");
                    Console.WriteLine("   Chat functionality will not be available.");
                }
                else if (!System.IO.File.Exists(llmModel))
                {
                    Console.WriteLine($"[!] Model file not found: {llmModel}");
                    Console.WriteLine("   Chat functionality will not be available.");
                }
                else
                {
                    builder.Services.AddSingleton<IModelInstancePool>(sp => new ModelInstancePool(llmExe, llmModel, maxInstances: poolMax, timeoutMs: poolTimeout));
                    builder.Services.AddSingleton<IModelManager>(sp => new ModelManager(sp.GetRequiredService<IModelInstancePool>(), llmExe));
                    Console.WriteLine($"[+] Model pool registered (will initialize on first use)");
                    Console.WriteLine($"   LLM: {System.IO.Path.GetFileName(llmExe)}");
                    Console.WriteLine($"   Model: {System.IO.Path.GetFileName(llmModel)}");
                    Console.WriteLine($"   Max instances: {poolMax}, Timeout: {poolTimeout}ms");
                }
            }
            else
            {
                Console.WriteLine("[!] Skipping model pool registration (missing LLM configuration)");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[!] Warning: Failed to register model pool: {ex.Message}");
        }

        // Register DashboardChatService (only if ModelInstancePool is available)
        builder.Services.AddSingleton<DashboardChatService>(sp =>
        {
            try
            {
                // Get both memory instances - first is vector memory, second is conversation memory
                var services = sp.GetServices<ILlmMemory>().ToArray();
                
                if (services.Length < 2)
                {
                    throw new InvalidOperationException($"Not enough memory services registered (found {services.Length}, need 2)");
                }
                
                var vectorMemory = services[0];
                var conversationMemory = services[1];
                
                // Try to get model pool - might not be available
                var modelPool = sp.GetService<IModelInstancePool>();
                if (modelPool == null)
                {
                    throw new InvalidOperationException("ModelInstancePool not available - check LLM configuration");
                }

                // Get DomainDetector for domain filtering
                var domainDetector = sp.GetService<Application.AI.Utilities.IDomainDetector>();
                
                // Get repositories for question/answer storage
                var questionRepository = sp.GetService<IQuestionRepository>();
                var llmRepository = sp.GetService<ILlmRepository>();

                Console.WriteLine("[+] Chat service initialized");
                return new DashboardChatService(
                    vectorMemory,
                    conversationMemory,
                    modelPool,
                    domainDetector,
                    questionRepository,
                    llmRepository,
                    null, // Will be set when DashboardState attaches the service
                    appConfig.Llm?.ContextSize ?? 0);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to initialize chat service: {ex.Message}");
                Console.WriteLine("   Chat functionality will not be available");
                throw;
            }
        });

        // Register DashboardState (replaces DashboardService)
        builder.Services.AddSingleton<AiDashboard.State.DashboardState>(sp =>
        {
            try
            {
                var config = sp.GetRequiredService<AppConfiguration>();
                
                // Determine model folder from config
                var modelPath = config.Llm?.ModelPath ?? builder.Configuration["AppConfiguration:Llm:ModelPath"];
                string? modelFolder = null;
                if (!string.IsNullOrWhiteSpace(modelPath))
                {
                    var dir = Path.GetDirectoryName(modelPath);
                    if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                    {
                        modelFolder = dir;
                    }
                }

                // Create dashboard state
                var dashboardState = new AiDashboard.State.DashboardState(modelFolder);
                
                // Initialize services
                var repository = sp.GetService<IVectorMemoryRepository>();
                var persistenceService = sp.GetService<VectorMemoryPersistenceService>();
                var personalityService = sp.GetService<BotPersonalityService>();
                var workspaceService = sp.GetService<IWorkspaceService>();
                dashboardState.InitializeServices(repository, persistenceService, config, personalityService, workspaceService);
                
                // Attach chat service
                var chatService = sp.GetService<DashboardChatService>();
                dashboardState.ChatService = chatService;

                if (chatService != null)
                {
                    Console.WriteLine("[+] Chat service attached to dashboard");
                }

                // Attach Gemma 4 CLI service (optional). When it's available, make it the active
                // backend by default: it applies Gemma's chat template, whereas the Classic pooled
                // path sends an un-templated prompt that instruct models answer with an immediate
                // stop token (empty reply). The user can still switch back to Classic in the UI.
                var gemma4Cli = sp.GetService<IGemma4CliService>();
                dashboardState.Gemma4CliService = gemma4Cli;
                if (gemma4Cli != null)
                {
                    dashboardState.SelectedBackend = AiDashboard.State.LlmBackend.Gemma4Cli;
                    Console.WriteLine("[+] Gemma 4 CLI service attached to dashboard (selected as active backend)");
                }
                
                // Set model switch handler
                var mgr = sp.GetService<IModelManager>();
                if (mgr != null)
                {
                    dashboardState.ModelService.SwitchModelHandler = 
                        async (modelFullPath, progress) => await mgr.SwitchModelAsync(modelFullPath, progress);
                }
                
                // Refresh models and collections in background
                if (modelFolder != null)
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            await dashboardState.RefreshModelsAsync();
                            Console.WriteLine($"[+] Found {dashboardState.ModelService.AvailableModels.Count} models in {modelFolder}");
                        }
                        catch (Exception ex)
                        {
                            // Background startup refresh — a failure here shouldn't crash the app.
                            Console.WriteLine($"[!] Failed to refresh models: {ex.Message}");
                        }
                    });
                }
                
                Task.Run(async () =>
                {
                    try
                    {
                        await dashboardState.RefreshCollectionsAsync();
                    }
                    catch (Exception ex)
                    {
                        // Background startup refresh — a failure here shouldn't crash the app.
                        Console.WriteLine($"[!] Failed to refresh collections: {ex.Message}");
                    }
                });

                Console.WriteLine("[+] Dashboard state initialized");
                return dashboardState;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[!] Warning during dashboard initialization: {ex.Message}");
                throw;
            }
        });

        var app = builder.Build();

        // Initialize database tables on startup (non-blocking)
        if (app.Services.GetService<IVectorMemoryRepository>() != null)
        {
            Task.Run(async () =>
            {
                using var scope = app.Services.CreateScope();
                try
                {
                    var repository = scope.ServiceProvider.GetRequiredService<IVectorMemoryRepository>();
                    await repository.InitializeDatabaseAsync();
                    Console.WriteLine("[+] Database initialized");
                    
                    // Initialize LLM and Question tables
                    var llmRepository = scope.ServiceProvider.GetService<ILlmRepository>();
                    var questionRepository = scope.ServiceProvider.GetService<IQuestionRepository>();
                    
                    if (llmRepository != null && questionRepository != null)
                    {
                        await llmRepository.InitializeDatabaseAsync();
                        await questionRepository.InitializeDatabaseAsync();
                        Console.WriteLine("[+] LLM and Question tables initialized");
                        
                        // Sync LLMs from folder
                        var llmSyncService = scope.ServiceProvider.GetService<LlmSyncService>();
                        if (llmSyncService != null)
                        {
                            var (added, existing, total) = await llmSyncService.SyncLlmsAsync();
                            if (total > 0)
                            {
                                Console.WriteLine($"[+] LLM sync complete: {added} added, {existing} existing, {total} total");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[!] Warning: Failed to initialize database: {ex.Message}");
                }
            });
        }

        // Initialize the goal-agent run history tables on startup (non-blocking, optional).
        // Kept out of the block above so a failure here can't stop the vector memory / LLM /
        // Question tables from being initialized — nothing else depends on run history.
        Task.Run(async () =>
        {
            using var scope = app.Services.CreateScope();
            try
            {
                var agentRunRepository = scope.ServiceProvider.GetService<IAgentRunRepository>();
                if (agentRunRepository != null)
                {
                    await agentRunRepository.InitializeDatabaseAsync();
                    Console.WriteLine("[+] Agent run history tables initialized");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[!] Warning: Failed to initialize agent run history tables: {ex.Message}");
                Console.WriteLine("   Agent Mode runs will not be recorded in the history view");
            }
        });

        // Initialize DomainDetector on startup (non-blocking, optional)
        Task.Run(async () =>
        {
            using var scope = app.Services.CreateScope();
            try
            {
                var domainDetector = scope.ServiceProvider.GetService<Application.AI.Utilities.IDomainDetector>();
                if (domainDetector != null)
                {
                    await domainDetector.InitializeAsync();
                    var domainCount = (await domainDetector.GetAllDomainsAsync()).Count;
                    var categories = await domainDetector.GetCategoriesAsync();
                    Console.WriteLine($"[+] Domain detector initialized ({domainCount} domain(s) in {categories.Count} categor{(categories.Count == 1 ? "y" : "ies")})");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[!] Warning: Failed to initialize domain detector: {ex.Message}");
                Console.WriteLine("   Domain management will not be available");
            }
        });
        
        // Initialize BotPersonalityService on startup (non-blocking, optional)
        Task.Run(async () =>
        {
            using var scope = app.Services.CreateScope();
            try
            {
                var personalityService = scope.ServiceProvider.GetService<BotPersonalityService>();
                if (personalityService != null)
                {
                    await personalityService.InitializeAsync();
                    await personalityService.RefreshPersonalitiesAsync();
                    var personalityCount = personalityService.AvailablePersonalities.Count;
                    Console.WriteLine($"[+] Bot personality service initialized ({personalityCount} personalit{(personalityCount == 1 ? "y" : "ies")} available)");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[!] Warning: Failed to initialize personality service: {ex.Message}");
                Console.WriteLine("   Personality management will not be available");
            }
        });

        Console.WriteLine("\n[*] AiDashboard starting...\n");

        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
            app.UseHsts();
        }

        app.UseHttpsRedirection();

        app.UseAntiforgery();

        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.Run();
    }
}
