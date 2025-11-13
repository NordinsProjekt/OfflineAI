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
        DisplayService.ShowHeader();

        try
        {
            // Build host with configuration and DI
            var host = CreateHostBuilder(args).Build();

            // Load test configuration
            var testConfig = LoadTestQueries();
            if (testConfig.TestQueries.Length == 0)
            {
                DisplayService.ShowNoTestQueriesFound();
                return 1;
            }

            DisplayService.ShowLoadedTestQueries(testConfig.TestQueries.Length);

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
            DisplayService.ShowRunningTestsHeader();

            var summary = await testRunner.RunAllTestsAsync(testConfig.TestQueries);

            // Generate reports
            var outputPath = "test-results";
            var reportGenerator = new ReportGenerator(outputPath);
            await reportGenerator.GenerateAllReportsAsync(summary);

            // Return exit code based on results
            if (summary.FailedTests > 0)
            {
                DisplayService.ShowTestsFailedMessage();
                return 1;
            }

            if (summary.ThresholdPassRate < 80)
            {
                DisplayService.ShowLowPassRateMessage(summary.ThresholdPassRate);
                return 1;
            }

            DisplayService.ShowAllTestsPassedMessage();
            return 0;
        }
        catch (Exception ex)
        {
            DisplayService.ShowFatalError(ex);
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
                DisplayService.ShowEnvironment(environment);
                
                // DEBUG: Show configuration sources
                var configRoot = context.Configuration as IConfigurationRoot;
                if (configRoot != null)
                {
                    DisplayService.ShowConfigurationSourcesHeader();
                    foreach (var provider in configRoot.Providers)
                    {
                        DisplayService.ShowConfigurationSource(provider.GetType().Name);
                    }
                }
                
                // Bind configuration
                var appConfig = context.Configuration.GetSection("AppConfiguration").Get<AppConfiguration>()
                                ?? new AppConfiguration();
                var dbConfig = context.Configuration.GetSection("DatabaseConfig").Get<DatabaseConfig>()
                               ?? new DatabaseConfig();

                // DEBUG: Show what was loaded
                DisplayService.ShowConfigurationHeader();
                DisplayService.ShowConfigurationValue("Embedding.ModelPath", appConfig.Embedding.ModelPath);
                DisplayService.ShowConfigurationValue("Embedding.VocabPath", appConfig.Embedding.VocabPath);
                DisplayService.ShowConfigurationValueInt("Embedding.Dimension", appConfig.Embedding.Dimension);
                DisplayService.ShowConfigurationValue("Debug.CollectionName", appConfig.Debug.CollectionName);
                DisplayService.ShowConfigurationValue("DatabaseConfig.ConnectionString", dbConfig.ConnectionString);
                DisplayService.WriteLine();

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
            DisplayService.ShowConfigurationError(errors);
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
        DisplayService.ShowLoadingEmbeddings(collectionName);

        var fragments = await repository.LoadByCollectionAsync(collectionName);

        if (fragments.Count == 0)
        {
            DisplayService.ShowNoFragmentsFound();
            throw new InvalidOperationException("No embeddings found in database");
        }

        DisplayService.ShowLoadedFragments(fragments.Count);

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

        DisplayService.ShowVectorMemoryReady(vectorMemory.Count);
    }
}
