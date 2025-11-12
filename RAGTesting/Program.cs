using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Application.AI.Embeddings;
using Services.Memory;
using Services.Configuration;
using Services.Repositories;
using Infrastructure.Data.Dapper;
using Microsoft.SemanticKernel.Embeddings;
using RAGTesting.Models;
using RAGTesting.Services;

namespace RAGTesting;

class Program
{
    static async Task<int> Main(string[] args)
    {
        Console.WriteLine("??????????????????????????????????????????????????????????????");
        Console.WriteLine("?           RAG Quality Testing System                       ?");
        Console.WriteLine("?           Testing Semantic Search & Retrieval             ?");
        Console.WriteLine("??????????????????????????????????????????????????????????????");
        Console.WriteLine();

        try
        {
            // Build host with configuration and DI
            var host = CreateHostBuilder(args).Build();

            // Load test configuration
            var testConfig = LoadTestQueries();
            if (testConfig.TestQueries.Length == 0)
            {
                Console.WriteLine("? No test queries found in test-queries.json");
                return 1;
            }

            Console.WriteLine($"?? Loaded {testConfig.TestQueries.Length} test queries");
            Console.WriteLine();

            // Get services from DI
            var embeddingService = host.Services.GetRequiredService<ITextEmbeddingGenerationService>();
            var appConfig = host.Services.GetRequiredService<AppConfiguration>();

            // Create vector memory
            var vectorMemory = new VectorMemory(
                embeddingService,
                appConfig.Debug.CollectionName);

            // Load embeddings from database
            var repository = host.Services.GetRequiredService<IVectorMemoryRepository>();
            await LoadEmbeddingsFromDatabase(vectorMemory, repository, appConfig.Debug.CollectionName);

            // Create test runner
            var criteria = testConfig.EvaluationCriteria ?? new EvaluationCriteria();
            var testRunner = new RAGTestRunner(
                vectorMemory,
                criteria,
                minRelevanceThreshold: 0.5,
                topK: 5);

            // Run all tests
            Console.WriteLine("?? Running RAG quality tests...");
            Console.WriteLine();

            var summary = await testRunner.RunAllTestsAsync(testConfig.TestQueries);

            // Generate reports
            var outputPath = "test-results";
            var reportGenerator = new ReportGenerator(outputPath);
            await reportGenerator.GenerateAllReportsAsync(summary);

            // Return exit code based on results
            if (summary.FailedTests > 0)
            {
                Console.WriteLine("\n??  Some tests failed. Check the reports for details.");
                return 1;
            }

            if (summary.ThresholdPassRate < 80)
            {
                Console.WriteLine($"\n??  Only {summary.ThresholdPassRate:F1}% of tests passed their minimum threshold.");
                return 1;
            }

            Console.WriteLine("\n? All tests passed!");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n? Fatal error: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    static IHostBuilder CreateHostBuilder(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                // DEBUG: Show environment
                var environment = context.HostingEnvironment.EnvironmentName;
                Console.WriteLine($"?? Environment: {environment}");
                
                // DEBUG: Show configuration sources
                var configRoot = context.Configuration as IConfigurationRoot;
                if (configRoot != null)
                {
                    Console.WriteLine("?? Configuration sources:");
                    foreach (var provider in configRoot.Providers)
                    {
                        Console.WriteLine($"   - {provider.GetType().Name}");
                    }
                }
                
                // Bind configuration
                var appConfig = context.Configuration.GetSection("AppConfiguration").Get<AppConfiguration>()
                                ?? new AppConfiguration();
                var dbConfig = context.Configuration.GetSection("DatabaseConfig").Get<DatabaseConfig>()
                               ?? new DatabaseConfig();

                // DEBUG: Show what was loaded
                Console.WriteLine($"\n?? Loaded configuration:");
                Console.WriteLine($"   Embedding.ModelPath: '{appConfig.Embedding.ModelPath}'");
                Console.WriteLine($"   Embedding.VocabPath: '{appConfig.Embedding.VocabPath}'");
                Console.WriteLine($"   Embedding.Dimension: {appConfig.Embedding.Dimension}");
                Console.WriteLine($"   Debug.CollectionName: '{appConfig.Debug.CollectionName}'");
                Console.WriteLine($"   DatabaseConfig.ConnectionString: '{dbConfig.ConnectionString}'");
                Console.WriteLine();

                services.AddSingleton(appConfig);
                services.AddSingleton(dbConfig);

                // Validate configuration
                ValidateConfiguration(appConfig);

                // Register Dapper repository
                services.AddDapperVectorMemoryRepository(dbConfig.ConnectionString);

                // Register embedding service
                services.AddSingleton(provider =>
                    new SemanticEmbeddingService(
                        appConfig.Embedding.ModelPath,
                        appConfig.Embedding.VocabPath,
                        appConfig.Embedding.Dimension));

                services.AddSingleton<ITextEmbeddingGenerationService>(provider =>
                    provider.GetRequiredService<SemanticEmbeddingService>());
            });
    }

    static void ValidateConfiguration(AppConfiguration config)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(config.Embedding.ModelPath))
            errors.Add("Embedding.ModelPath is required");

        if (string.IsNullOrWhiteSpace(config.Embedding.VocabPath))
            errors.Add("Embedding.VocabPath is required");

        if (errors.Any())
        {
            Console.WriteLine("\n? Configuration Error:");
            foreach (var error in errors)
            {
                Console.WriteLine($"   - {error}");
            }

            Console.WriteLine("\n?? Please configure using:");
            Console.WriteLine("   1. User Secrets: dotnet user-secrets set \"AppConfiguration:Embedding:ModelPath\" \"path\"");
            Console.WriteLine("   2. appsettings.json");
            Console.WriteLine("   3. Environment Variables");

            throw new InvalidOperationException("Configuration validation failed");
        }
    }

    static TestQueriesConfig LoadTestQueries()
    {
        var filename = "test-queries.json";
        if (!File.Exists(filename))
        {
            throw new FileNotFoundException($"Test queries file not found: {filename}");
        }

        var json = File.ReadAllText(filename);
        var config = JsonSerializer.Deserialize<TestQueriesConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return config ?? new TestQueriesConfig();
    }

    static async Task LoadEmbeddingsFromDatabase(
        VectorMemory vectorMemory,
        IVectorMemoryRepository repository,
        string collectionName)
    {
        Console.WriteLine($"?? Loading embeddings from database (collection: {collectionName})...");

        var fragments = await repository.LoadByCollectionAsync(collectionName);

        if (fragments.Count == 0)
        {
            Console.WriteLine("??  No fragments found in database!");
            Console.WriteLine("   Run the main OfflineAI application to import game rules first.");
            throw new InvalidOperationException("No embeddings found in database");
        }

        Console.WriteLine($"? Loaded {fragments.Count} fragments with embeddings");

        // Import fragments into vector memory
        foreach (var fragmentEntity in fragments)
        {
            var memoryFragment = fragmentEntity.ToMemoryFragment();
            vectorMemory.ImportMemory(memoryFragment);
            
            if (fragmentEntity.Embedding != null && fragmentEntity.Embedding.Length > 0)
            {
                var embedding = fragmentEntity.GetEmbeddingAsMemory();
                vectorMemory.SetEmbeddingForLastFragment(embedding);
            }
        }

        Console.WriteLine($"? Vector memory ready with {vectorMemory.Count} fragments");
        Console.WriteLine();
    }
}
