using AiDashboard.Components;
using AiDashboard.Services;
using System.IO;
using Application.AI.Pooling;
using Application.AI.Management;
using Application.AI.Embeddings;
using Services.Memory;
using Services.Interfaces;
using Services.Repositories;
using Microsoft.SemanticKernel.Embeddings;
using Infrastructure.Data.Dapper;
using Services.Configuration;

namespace AiDashboard;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        // Register AppConfiguration
        var appConfig = builder.Configuration.GetSection("AppConfiguration").Get<AppConfiguration>() ?? new AppConfiguration();
        builder.Services.AddSingleton(appConfig);

        // Read configuration for LLM
        var llmExe = appConfig.Llm?.ExecutablePath ?? builder.Configuration["AppConfiguration:Llm:ExecutablePath"] ?? string.Empty;
        var llmModel = appConfig.Llm?.ModelPath ?? builder.Configuration["AppConfiguration:Llm:ModelPath"] ?? string.Empty;
        var poolMax = appConfig.Pool?.MaxInstances ?? (int.TryParse(builder.Configuration["AppConfiguration:Pool:MaxInstances"], out var m) ? m : 3);
        var poolTimeout = appConfig.Pool?.TimeoutMs ?? (int.TryParse(builder.Configuration["AppConfiguration:Pool:TimeoutMs"], out var t) ? t : 30000);

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
            Console.WriteLine("??  Configuration Errors:");
            foreach (var error in configErrors)
            {
                Console.WriteLine($"   - {error}");
            }
            Console.WriteLine("\n?? Please update User Secrets to configure required paths.");
            Console.WriteLine("   The application will start but functionality will be limited.\n");
        }

        // Register embedding service (optional for dashboard - only needed if RAG is enabled)
        if (!string.IsNullOrEmpty(embeddingModelPath) && !string.IsNullOrEmpty(embeddingVocabPath))
        {
            try
            {
                builder.Services.AddSingleton(sp => new SemanticEmbeddingService(
                    embeddingModelPath,
                    embeddingVocabPath,
                    embeddingDimension,
                    debugMode: false));

                builder.Services.AddSingleton<ITextEmbeddingGenerationService>(sp =>
                    sp.GetRequiredService<SemanticEmbeddingService>());

                Console.WriteLine("? Embedding service registered (RAG available)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"??  Warning: Failed to register embedding service: {ex.Message}");
                Console.WriteLine("   RAG mode will not be available.");
            }
        }
        else
        {
            Console.WriteLine("??  Embedding service not configured (RAG disabled)");
        }

        // Register Dapper repository (optional - but required for table management and collections)
        IVectorMemoryRepository? repositoryInstance = null;
        if (!string.IsNullOrEmpty(dbConnectionString))
        {
            try
            {
                builder.Services.AddDapperVectorMemoryRepository(dbConnectionString, dbTableName);
                
                // Register KnowledgeDomainRepository for domain-based filtering
                builder.Services.AddDapperKnowledgeDomainRepository(dbConnectionString);

                // Build a temporary service provider to check if services are registered
                using var tempProvider = builder.Services.BuildServiceProvider();
                repositoryInstance = tempProvider.GetService<IVectorMemoryRepository>();
                
                if (repositoryInstance != null)
                {
                    Console.WriteLine("? Database repository registered");
                    
                    // Only register persistence service if we have both repository AND embedding service
                    var embeddingService = tempProvider.GetService<ITextEmbeddingGenerationService>();
                    if (embeddingService != null)
                    {
                        builder.Services.AddSingleton<VectorMemoryPersistenceService>();
                        Console.WriteLine("? Persistence service registered (collection loading available)");
                    }
                    else
                    {
                        Console.WriteLine("??  Persistence service not registered - embedding service missing");
                        Console.WriteLine("   Collection loading will not be available");
                    }
                }
                else
                {
                    Console.WriteLine("??  Database repository registration failed");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"??  Warning: Failed to register database services: {ex.Message}");
                Console.WriteLine("   Table management and collection loading will not be available");
            }
        }
        else
        {
            Console.WriteLine("??  Database not configured");
            Console.WriteLine("   Table management and collection loading disabled");
        }

        // Register DomainDetector (requires KnowledgeDomainRepository)
        builder.Services.AddSingleton<Application.AI.Utilities.IDomainDetector, Application.AI.Utilities.DomainDetector>();

        // Register memory for knowledge base
        builder.Services.AddSingleton<ILlmMemory>(sp =>
        {
            var embeddingService = sp.GetService<ITextEmbeddingGenerationService>();
            var repository = sp.GetService<IVectorMemoryRepository>();
            var collectionName = appConfig.Debug?.CollectionName ?? builder.Configuration["AppConfiguration:Debug:CollectionName"] ?? "game-rules-mpnet";

            if (embeddingService != null && repository != null)
            {
                // Use database-backed vector memory for RAG queries
                Console.WriteLine($"? Database vector memory initialized (collection: {collectionName})");
                return new DatabaseVectorMemory(embeddingService, repository, collectionName);
            }
            else
            {
                // Fallback to simple string memory (RAG not available)
                Console.WriteLine("? Simple memory initialized (RAG not available - database or embedding service missing)");
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
                    Console.WriteLine($"??  LLM executable not found: {llmExe}");
                    Console.WriteLine("   Chat functionality will not be available.");
                }
                else if (!System.IO.File.Exists(llmModel))
                {
                    Console.WriteLine($"??  Model file not found: {llmModel}");
                    Console.WriteLine("   Chat functionality will not be available.");
                }
                else
                {
                    builder.Services.AddSingleton<IModelInstancePool>(sp => new ModelInstancePool(llmExe, llmModel, maxInstances: poolMax, timeoutMs: poolTimeout));
                    builder.Services.AddSingleton<IModelManager>(sp => new ModelManager(sp.GetRequiredService<IModelInstancePool>(), llmExe));
                    Console.WriteLine($"? Model pool registered (will initialize on first use)");
                    Console.WriteLine($"   LLM: {System.IO.Path.GetFileName(llmExe)}");
                    Console.WriteLine($"   Model: {System.IO.Path.GetFileName(llmModel)}");
                    Console.WriteLine($"   Max instances: {poolMax}, Timeout: {poolTimeout}ms");
                }
            }
            else
            {
                Console.WriteLine("??  Skipping model pool registration (missing LLM configuration)");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"??  Warning: Failed to register model pool: {ex.Message}");
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

                Console.WriteLine("? Chat service initialized");
                return new DashboardChatService(vectorMemory, conversationMemory, modelPool, domainDetector);
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
                dashboardState.InitializeServices(repository, persistenceService, config);
                
                // Attach chat service
                var chatService = sp.GetService<DashboardChatService>();
                dashboardState.ChatService = chatService;
                
                if (chatService != null)
                {
                    Console.WriteLine("? Chat service attached to dashboard");
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
                            Console.WriteLine($"? Found {dashboardState.ModelService.AvailableModels.Count} models in {modelFolder}");
                        }
                        catch { }
                    });
                }
                
                Task.Run(async () =>
                {
                    try
                    {
                        await dashboardState.RefreshCollectionsAsync();
                    }
                    catch { }
                });

                Console.WriteLine("? Dashboard state initialized");
                return dashboardState;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"??  Warning during dashboard initialization: {ex.Message}");
                throw;
            }
        });

        var app = builder.Build();

        // Initialize database on startup (non-blocking, optional)
        if (app.Services.GetService<IVectorMemoryRepository>() != null)
        {
            Task.Run(async () =>
            {
                using var scope = app.Services.CreateScope();
                try
                {
                    var repository = scope.ServiceProvider.GetRequiredService<IVectorMemoryRepository>();
                    await repository.InitializeDatabaseAsync();
                    Console.WriteLine("? Database initialized");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"??  Warning: Failed to initialize database: {ex.Message}");
                }
            });
        }

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
                    Console.WriteLine($"? Domain detector initialized ({domainCount} domain(s) in {categories.Count} categor{(categories.Count == 1 ? "y" : "ies")})");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"??  Warning: Failed to initialize domain detector: {ex.Message}");
                Console.WriteLine("   Domain management will not be available");
            }
        });

        Console.WriteLine("\n?? AiDashboard starting...\n");

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
