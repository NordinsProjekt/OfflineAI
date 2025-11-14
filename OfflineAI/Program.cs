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
using System.Diagnostics;

namespace OfflineAI
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            // Set up dependency injection with configuration
            var host = CreateHostBuilder(args).Build();

            // Read config to detect llama backend and show model info before other logs
            var appConfig = host.Services.GetRequiredService<AppConfiguration>();
            ShowLlamaBackendStatus(appConfig);
            ShowLlamaModelInfo(appConfig);
            
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

        private static void ShowLlamaBackendStatus(AppConfiguration appConfig)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(appConfig.Llm.ExecutablePath) || !File.Exists(appConfig.Llm.ExecutablePath))
                {
                    return; // nothing to show
                }

                var psi = new ProcessStartInfo
                {
                    FileName = appConfig.Llm.ExecutablePath,
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return;
                string stdout = proc.StandardOutput.ReadToEnd();
                string stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit(3000);

                var text = (stdout + "\n" + stderr).ToLowerInvariant();
                string? backend = null;
                if (text.Contains("cublas") || text.Contains("cuda")) backend = "CUDA";
                else if (text.Contains("hipblas") || text.Contains("rocm")) backend = "ROCm";
                else if (text.Contains("metal")) backend = "Metal";
                else if (text.Contains("opencl")) backend = "OpenCL";
                else if (text.Contains("kompute")) backend = "Vulkan";
                else if (text.Contains("blas")) backend = "CPU BLAS";

                if (!string.IsNullOrEmpty(backend))
                {
                    DisplayService.WriteLine($"✅ Llama runtime backend detected: {backend}");
                }
                else
                {
                    DisplayService.WriteLine("ℹ️ Llama runtime backend: not detected from --version output");
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException || ex is System.IO.IOException)
            {
                // Fail silent; do not block startup if detection fails
            }
        }

        private static void ShowLlamaModelInfo(AppConfiguration appConfig)
        {
            try
            {
                var modelName = appConfig.Llm.ModelName;
                var modelType = appConfig.Llm.ModelType;
                var modelFile = string.IsNullOrWhiteSpace(appConfig.Llm.ModelPath) ? "(none)" : Path.GetFileName(appConfig.Llm.ModelPath);
                if (!string.IsNullOrWhiteSpace(modelName) || !string.IsNullOrWhiteSpace(modelFile))
                {
                    var typePart = string.IsNullOrWhiteSpace(modelType) ? string.Empty : $" ({modelType})";
                    DisplayService.WriteLine($"🧠 LLM model: {modelName}{typePart} [file: {modelFile}]");
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is System.IO.IOException || ex is System.IO.PathTooLongException)
            {
                // Ignore failure to fetch model info
            }
        }
    }
}
