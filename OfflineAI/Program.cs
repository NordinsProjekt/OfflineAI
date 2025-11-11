using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Services.UI;
using OfflineAI.Modes;
using OfflineAI.Diagnostics;
using Services.Repositories;
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
            // Check if diagnostics flag is passed
            if (args.Length > 0 && args[0] == "--diagnose-bert")
            {
                await BertDiagnostics.RunDiagnosticsAsync();
                return;
            }
            
            // Check for embedding investigation
            if (args.Length > 0 && args[0] == "--diagnose-embeddings")
            {
                await EmbeddingDiagnostic.RunAsync();
                return;
            }
            
            // Check for Section 24 tokenization diagnostic
            if (args.Length > 0 && args[0] == "--diagnose-section24")
            {
                await Section24TokenizationDiagnostic.RunAsync();
                return;
            }
            
            // Check for multiple pattern tests
            if (args.Length > 0 && args[0] == "--test-patterns")
            {
                await Section24TokenizationDiagnostic.RunMultiplePatternTestsAsync();
                return;
            }
            
            // Check for 2000+ character section test
            if (args.Length > 0 && args[0] == "--test-2000char")
            {
                await Section24TokenizationDiagnostic.Test2000CharacterSectionAsync();
                return;
            }
            
            // Set up dependency injection
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
                    // Configuration
                    var dbConfig = new DatabaseConfig
                    {
                        ConnectionString = @"Server=(localdb)\mssqllocaldb;Database=VectorMemoryDB;Integrated Security=true;TrustServerCertificate=true;",
                        UseDatabasePersistence = true,
                        AutoInitializeDatabase = true,
                        UseEntityFramework = false // Set to true to use EF Core instead of Dapper
                    };
                    
                    services.AddSingleton(dbConfig);
                    
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
                    services.AddSingleton<SemanticEmbeddingService>();
                    services.AddSingleton<ITextEmbeddingGenerationService>(provider => 
                        provider.GetRequiredService<SemanticEmbeddingService>());
                    
                    // Register persistence service
                    services.AddSingleton<VectorMemoryPersistenceService>();
                    
                    // Register model pool
                    var llmPath = @"d:\tinyllama\llama-cli.exe";
                    var modelPath = @"d:\tinyllama\tinyllama-1.1b-chat-v1.0.Q5_K_M.gguf";
                    services.AddSingleton(provider => 
                        new ModelInstancePool(llmPath, modelPath, maxInstances: 3));
                });
        }
    }
}
