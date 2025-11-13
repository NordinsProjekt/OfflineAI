using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Services.UI;
using OfflineAI.Modes;
using Application.AI.Embeddings;
using Services.Memory;
using Application.AI.Pooling;
using Services.Configuration;
using Infrastructure.Data.Dapper;
using Infrastructure.Data.EntityFramework;
using Microsoft.SemanticKernel.Embeddings;

namespace OfflineAI
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            // Set up dependency injection with configuration
            var host = CreateHostBuilder(args).Build();
            
            DisplayService.ShowVectorMemoryDatabaseHeader();
            DisplayService.WriteLine("\n🚀 Starting OfflineAI with BERT Embeddings + SQL Database...\n");
            
            // Only one mode: Database persistence with BERT embeddings
            await RunVectorMemoryWithDatabaseMode.RunAsync(host.Services);
        }
        
        static IHostBuilder CreateHostBuilder(string[] args)
        {
            return Host.CreateDefaultBuilder(args)
                .ConfigureServices((context, services) =>
                {
                    // Bind configuration sections
                    var appConfig = context.Configuration.GetSection("AppConfiguration").Get<AppConfiguration>() 
                                    ?? new AppConfiguration(); // Use defaults if not configured
                    var dbConfig = context.Configuration.GetSection("DatabaseConfig").Get<DatabaseConfig>() 
                                   ?? new DatabaseConfig 
                                   {
                                       ConnectionString = @"Server=(localdb)\mssqllocaldb;Database=VectorMemoryDB;Integrated Security=true;TrustServerCertificate=true;",
                                       UseDatabasePersistence = true,
                                       AutoInitializeDatabase = true,
                                       UseEntityFramework = false
                                   };
                    
                    // Register configurations
                    services.AddSingleton(appConfig);
                    services.AddSingleton(dbConfig);
                    
                    // Validate configuration
                    ValidateConfiguration(appConfig);
                    
                    // Register repository based on configuration
                    if (dbConfig.UseEntityFramework)
                    {
                        services.AddEntityFrameworkVectorMemoryRepository(dbConfig.ConnectionString);
                    }
                    else
                    {
                        services.AddDapperVectorMemoryRepository(dbConfig.ConnectionString);
                    }
                    
                    // Register embedding service (both as concrete type and interface)
                    services.AddSingleton(provider => 
                        new SemanticEmbeddingService(
                            appConfig.Embedding.ModelPath,
                            appConfig.Embedding.VocabPath,
                            appConfig.Embedding.Dimension,
                            appConfig.Debug.EnableDebugMode));
                    
                    services.AddSingleton<ITextEmbeddingGenerationService>(provider => 
                        provider.GetRequiredService<SemanticEmbeddingService>());
                    
                    // Register persistence service
                    services.AddSingleton<VectorMemoryPersistenceService>();
                    
                    // Register model pool
                    services.AddSingleton(provider => 
                        new ModelInstancePool(
                            appConfig.Llm.ExecutablePath, 
                            appConfig.Llm.ModelPath, 
                            maxInstances: appConfig.Pool.MaxInstances,
                            timeoutMs: appConfig.Pool.TimeoutMs));
                });
        }
        
        private static void ValidateConfiguration(AppConfiguration config)
        {
            var errors = new List<string>();
            
            if (string.IsNullOrWhiteSpace(config.Llm.ExecutablePath))
                errors.Add("Llm.ExecutablePath is required");
            
            if (string.IsNullOrWhiteSpace(config.Llm.ModelPath))
                errors.Add("Llm.ModelPath is required");
            
            if (string.IsNullOrWhiteSpace(config.Embedding.ModelPath))
                errors.Add("Embedding.ModelPath is required");
            
            if (string.IsNullOrWhiteSpace(config.Embedding.VocabPath))
                errors.Add("Embedding.VocabPath is required");
            
            if (string.IsNullOrWhiteSpace(config.Folders.InboxFolder))
                errors.Add("Folders.InboxFolder is required");
            
            if (string.IsNullOrWhiteSpace(config.Folders.ArchiveFolder))
                errors.Add("Folders.ArchiveFolder is required");
            
            if (errors.Any())
            {
                DisplayService.WriteLine("\n❌ Configuration Error:");
                foreach (var error in errors)
                {
                    DisplayService.WriteLine($"   - {error}");
                }
                
                Console.ReadKey();
                Environment.Exit(1);
            }
        }
    }
}
