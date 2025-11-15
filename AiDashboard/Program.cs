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

        // Register empty VectorMemory as ILlmMemory for knowledge base (no database dependency)
        builder.Services.AddSingleton<ILlmMemory>(sp =>
        {
            var embeddingService = sp.GetService<ITextEmbeddingGenerationService>();
            var repository = sp.GetService<IVectorMemoryRepository>();
            var collectionName = appConfig.Debug?.CollectionName ?? builder.Configuration["AppConfiguration:Debug:CollectionName"] ?? "game-rules-mpnet";

            if (embeddingService != null && repository != null)
            {
                // Use database-backed vector memory for on-demand queries
                Console.WriteLine($"? Database vector memory initialized (collection: {collectionName})");
                return new DatabaseVectorMemory(embeddingService, repository, collectionName);
            }
            else if (embeddingService != null)
            {
                // Fallback to in-memory vector memory
                Console.WriteLine("? Vector memory initialized (in-memory)");
                return new VectorMemory(embeddingService, "dashboard-kb");
            }
            else
            {
                // Fallback to simple string memory if embeddings not available
                Console.WriteLine("? Simple memory initialized (RAG not available)");
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
                    builder.Services.AddSingleton(sp => new ModelInstancePool(llmExe, llmModel, maxInstances: poolMax, timeoutMs: poolTimeout));
                    builder.Services.AddSingleton<IModelManager>(sp => new ModelManager(sp.GetRequiredService<ModelInstancePool>(), llmExe));
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
                var modelPool = sp.GetService<ModelInstancePool>();
                if (modelPool == null)
                {
                    throw new InvalidOperationException("ModelInstancePool not available - check LLM configuration");
                }

                Console.WriteLine("? Chat service initialized");
                return new DashboardChatService(vectorMemory, conversationMemory, modelPool);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to initialize chat service: {ex.Message}");
                Console.WriteLine("   Chat functionality will not be available");
                throw;
            }
        });

        // Register dashboard service and initialize model folder from configuration if present
        builder.Services.AddSingleton<DashboardService>(sp =>
        {
            var svc = new DashboardService();

            try
            {
                var config = sp.GetRequiredService<AppConfiguration>();
                svc.AppConfig = config;

                // Set inbox and archive paths from config
                if (config.Folders != null)
                {
                    svc.InboxPath = config.Folders.InboxFolder ?? svc.InboxPath;
                    svc.ArchivePath = config.Folders.ArchiveFolder ?? svc.ArchivePath;
                }

                var modelPath = config.Llm?.ModelPath ?? builder.Configuration["AppConfiguration:Llm:ModelPath"];
                if (!string.IsNullOrWhiteSpace(modelPath))
                {
                    var dir = Path.GetDirectoryName(modelPath);
                    if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                    {
                        svc.ModelFolderPath = dir;
                        svc.RefreshAvailableModelsAsync().GetAwaiter().GetResult();
                        Console.WriteLine($"? Found {svc.AvailableModels.Count} models in {dir}");
                    }
                    else
                    {
                        Console.WriteLine($"??  Model folder not found: {dir}");
                    }
                }

                // Attach switch handler using model manager
                var mgr = sp.GetService<IModelManager>();
                if (mgr != null)
                {
                    svc.SwitchModelHandler = async (modelFullPath, progress) => await mgr.SwitchModelAsync(modelFullPath, progress);
                }

                // Attach chat service
                var chatService = sp.GetService<DashboardChatService>();
                if (chatService != null)
                {
                    svc.ChatService = chatService;
                    Console.WriteLine("? Chat service attached to dashboard");
                }
                else
                {
                    Console.WriteLine("??  Chat service not available");
                }

                // Attach repository for table management (optional)
                var repository = sp.GetService<IVectorMemoryRepository>();
                if (repository != null)
                {
                    svc.VectorRepository = repository;
                }

                // Attach persistence service for loading collections (optional)
                var persistenceService = sp.GetService<VectorMemoryPersistenceService>();
                if (persistenceService != null)
                {
                    svc.PersistenceService = persistenceService;
                }

                // Set collection name from config
                var collectionName = config.Debug?.CollectionName ?? builder.Configuration["AppConfiguration:Debug:CollectionName"];
                if (!string.IsNullOrWhiteSpace(collectionName))
                {
                    svc.CollectionName = collectionName;
                }

                // Refresh collections list (async but fire-and-forget on startup)
                Task.Run(async () =>
                {
                    try
                    {
                        await svc.RefreshCollectionsAsync();
                    }
                    catch
                    {
                        // Ignore errors during startup
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"??  Warning during dashboard initialization: {ex.Message}");
            }

            return svc;
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
