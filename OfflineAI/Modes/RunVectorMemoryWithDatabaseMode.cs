using Application.AI.Chat;
using Application.AI.Embeddings;
using Application.AI.Models;
using Application.AI.Pooling;
using Entities;
using Microsoft.Extensions.DependencyInjection;
using Services.Configuration;
using Services.Interfaces;
using Services.Memory;
using Services.Repositories;
using Services.UI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OfflineAI.Modes;

internal static class RunVectorMemoryWithDatabaseMode
{
    internal static async Task RunAsync(IServiceProvider serviceProvider)
    {
        DisplayService.ShowVectorMemoryDatabaseHeader();

        // Setup paths and services
        var config = GetConfiguration(serviceProvider);
        var services = GetRequiredServices(serviceProvider);

        // Initialize system
        await InitializeSystemAsync(services);

        // Process new files from inbox
        await ProcessInboxFilesAsync(config, services);

        // Load or create vector memory
        var vectorMemory = await LoadOrCreateVectorMemoryAsync(services, config);

        // Initialize model pool
        await InitializeModelPoolAsync(services.ModelPool);

        // Run chat loop
        await RunChatLoopAsync(config, services, vectorMemory);

        // Cleanup
        CleanupResources(services.ModelPool);
    }

    #region Configuration

    private static AppConfiguration GetConfiguration(IServiceProvider serviceProvider)
    {
        return serviceProvider.GetRequiredService<AppConfiguration>();
    }

    private static (
        DatabaseConfig DbConfig,
        SemanticEmbeddingService EmbeddingService,
        VectorMemoryPersistenceService PersistenceService,
        ModelInstancePool ModelPool
    ) GetRequiredServices(IServiceProvider serviceProvider)
    {
        return (
            DbConfig: serviceProvider.GetRequiredService<DatabaseConfig>(),
            EmbeddingService: serviceProvider.GetRequiredService<SemanticEmbeddingService>(),
            PersistenceService: serviceProvider.GetRequiredService<VectorMemoryPersistenceService>(),
            ModelPool: serviceProvider.GetRequiredService<ModelInstancePool>()
        );
    }

    #endregion

    #region System Initialization

    private static async Task InitializeSystemAsync(
        (DatabaseConfig DbConfig,
         SemanticEmbeddingService EmbeddingService,
         VectorMemoryPersistenceService PersistenceService,
         ModelInstancePool ModelPool) services)
    {
        // Display embedding service info
        DisplayService.ShowInitializingEmbeddingService();
        DisplayService.WriteLine("Using BERT embeddings for semantic search...");

        // Test database connection
        await TestDatabaseConnectionAsync(services.DbConfig);

        // Initialize database schema if needed
        await InitializeDatabaseSchemaAsync(services.DbConfig, services.PersistenceService);
    }

    private static async Task TestDatabaseConnectionAsync(DatabaseConfig dbConfig)
    {
        DisplayService.ShowTestingDatabaseConnection();
        var canConnect = await Services.Utilities.DatabaseConnectionTester.TestConnectionAsync(dbConfig.ConnectionString);
        
        if (!canConnect)
        {
            DisplayService.ShowDatabaseConnectionFailed();
            DisplayService.WaitForKeyPress();
            Environment.Exit(1);
        }
    }

    private static async Task InitializeDatabaseSchemaAsync(
        DatabaseConfig dbConfig,
        VectorMemoryPersistenceService persistenceService)
    {
        if (dbConfig.AutoInitializeDatabase)
        {
            DisplayService.ShowInitializingDatabaseSchema();
            await persistenceService.InitializeDatabaseAsync();
            DisplayService.ShowDatabaseSchemaReady();
        }
    }

    #endregion

    #region File Processing

    private static async Task ProcessInboxFilesAsync(
        AppConfiguration config,
        (DatabaseConfig DbConfig,
         SemanticEmbeddingService EmbeddingService,
         VectorMemoryPersistenceService PersistenceService,
         ModelInstancePool ModelPool) services)
    {
        DisplayService.ShowSmartFileProcessing();
        
        var fileWatcher = new KnowledgeFileWatcher(config.Folders.InboxFolder, config.Folders.ArchiveFolder);
        var newFiles = await fileWatcher.DiscoverNewFilesAsync();
        
        if (newFiles.Any())
        {
            DisplayNewFilesFound(newFiles);
            await ProcessNewFilesAsync(newFiles, services.PersistenceService, fileWatcher, config);
        }
        else
        {
            DisplayNoNewFiles(config.Folders.InboxFolder);
        }
    }

    private static void DisplayNewFilesFound(Dictionary<string, string> newFiles)
    {
        DisplayService.WriteLine($"\n[*] Found {newFiles.Count} new file(s) in inbox:");
        foreach (var file in newFiles.Keys)
        {
            DisplayService.WriteLine($"    - {file}.txt");
        }
        DisplayService.WriteLine("\n[*] Processing and vectorizing new files...");
    }

    private static void DisplayNoNewFiles(string inboxFolder)
    {
        DisplayService.WriteLine($"\n[+] No new files in inbox folder: {inboxFolder}");
        DisplayService.WriteLine("    Place .txt files there to auto-process them!");
    }

    #endregion

    #region Memory Loading

    private static async Task<VectorMemory> LoadOrCreateVectorMemoryAsync(
        (DatabaseConfig DbConfig,
         SemanticEmbeddingService EmbeddingService,
         VectorMemoryPersistenceService PersistenceService,
         ModelInstancePool ModelPool) services,
        AppConfiguration config)
    {
        // Show existing collections
        await DisplayExistingCollectionsAsync(services.PersistenceService);

        // Load or create vector memory
        VectorMemory vectorMemory;
        var collectionName = config.Debug.CollectionName;
        
        if (await services.PersistenceService.CollectionExistsAsync(collectionName))
        {
            DisplayService.WriteLine($"\n[+] Loading existing collection: {collectionName}");
            vectorMemory = await services.PersistenceService.LoadVectorMemoryAsync(collectionName);
        }
        else
        {
            DisplayService.ShowCollectionNotFound(collectionName);
            DisplayService.WriteLine("Creating empty collection. Add files to inbox folder to populate.");
            vectorMemory = new VectorMemory(services.EmbeddingService, collectionName);
        }

        return vectorMemory;
    }

    private static async Task DisplayExistingCollectionsAsync(VectorMemoryPersistenceService persistenceService)
    {
        var existingCollections = await persistenceService.GetCollectionsAsync();
        DisplayService.ShowExistingCollections(existingCollections.Count);
        
        foreach (var collection in existingCollections)
        {
            var stats = await persistenceService.GetCollectionStatsAsync(collection);
            DisplayService.ShowCollectionInfo(collection, stats.FragmentCount);
        }
    }

    #endregion

    #region Model Pool Initialization

    private static async Task InitializeModelPoolAsync(ModelInstancePool modelPool)
    {
        DisplayService.ShowInstancePool();
        
        await modelPool.InitializeAsync((current, total) =>
        {
            DisplayService.WriteLine($"Loading instance {current}/{total}...");
        });
        
        DisplayService.WriteLine($"\n[+] Model pool ready with {modelPool.AvailableCount} instances");
        DisplayService.WriteLine("Memory usage: ~3-5 GB (model stays loaded)");
    }

    #endregion

    #region Chat Loop

    private static async Task RunChatLoopAsync(
        AppConfiguration config,
        (DatabaseConfig DbConfig,
         SemanticEmbeddingService EmbeddingService,
         VectorMemoryPersistenceService PersistenceService,
         ModelInstancePool ModelPool) services,
        VectorMemory vectorMemory)
    {
        var conversationMemory = new SimpleMemory();
        
        // Create chat service with current RAG mode and generation settings
        AiChatServicePooled CreateChatService(VectorMemory memory) => new AiChatServicePooled(
            memory,
            conversationMemory,
            services.ModelPool,
            config.Generation,
            debugMode: config.Debug.EnableDebugMode,
            enableRag: config.Debug.EnableRagMode,
            showPerformanceMetrics: config.Debug.ShowPerformanceMetrics);
        
        var service = CreateChatService(vectorMemory);

        DisplaySystemReady(vectorMemory, config, services.DbConfig);

        var fileWatcher = new KnowledgeFileWatcher(config.Folders.InboxFolder, config.Folders.ArchiveFolder);
        bool serviceNeedsRecreation = false;
        bool vectorMemoryNeedsReload = false;

        while (true)
        {
            // Reload vector memory if table was switched
            if (vectorMemoryNeedsReload)
            {
                DisplayService.WriteLine("\n[*] Reloading vector memory from new table...");
                vectorMemory = await LoadOrCreateVectorMemoryAsync(services, config);
                service = CreateChatService(vectorMemory);
                DisplaySystemReady(vectorMemory, config, services.DbConfig);
                vectorMemoryNeedsReload = false;
                serviceNeedsRecreation = false;
                continue;
            }
            
            // Recreate service if RAG mode was toggled or model was switched
            if (serviceNeedsRecreation)
            {
                service = CreateChatService(vectorMemory);
                serviceNeedsRecreation = false;
            }
            
            var input = DisplayService.ReadInput("\n> ");

            if (string.IsNullOrWhiteSpace(input)) continue;
            if (input.ToLower() == "exit") break;

            // Check if RAG toggle command
            if (input.Equals("/rag", StringComparison.OrdinalIgnoreCase))
            {
                HandleToggleRagCommand(config);
                serviceNeedsRecreation = true;
                continue;
            }

            // Check if switch model command
            if (input.Equals("/switchmodel", StringComparison.OrdinalIgnoreCase))
            {
                var switched = await HandleSwitchModelCommandAsync(config, services.ModelPool);
                if (switched)
                {
                    serviceNeedsRecreation = true;
                }
                continue;
            }

            // Check if temperature command
            if (input.StartsWith("/temperature ", StringComparison.OrdinalIgnoreCase) || 
                input.StartsWith("/temp ", StringComparison.OrdinalIgnoreCase))
            {
                HandleTemperatureCommand(input, config);
                serviceNeedsRecreation = true;
                continue;
            }

            // Check if tokens command
            if (input.StartsWith("/tokens ", StringComparison.OrdinalIgnoreCase) ||
                input.StartsWith("/maxtokens ", StringComparison.OrdinalIgnoreCase))
            {
                HandleTokensCommand(input, config);
                serviceNeedsRecreation = true;
                continue;
            }

            // Check if settings command
            if (input.Equals("/settings", StringComparison.OrdinalIgnoreCase))
            {
                HandleSettingsCommand(config, services.DbConfig);
                continue;
            }

            // Check if performance toggle command
            if (input.Equals("/perf", StringComparison.OrdinalIgnoreCase))
            {
                HandlePerfCommand(config);
                serviceNeedsRecreation = true;
                continue;
            }

            // Check if table management commands
            if (input.StartsWith("/table", StringComparison.OrdinalIgnoreCase))
            {
                var tableWasSwitched = await HandleTableCommandAsync(input, services.PersistenceService, services.DbConfig, config);
                if (tableWasSwitched)
                {
                    vectorMemoryNeedsReload = true;
                }
                continue;
            }

            // Handle other debug commands
            if (config.Debug.EnableDebugMode && await HandleDebugCommandsAsync(
                input, 
                vectorMemory, 
                services.PersistenceService, 
                fileWatcher,
                services.ModelPool,
                config))
            {
                continue;
            }

            // Process user query
            await ProcessUserQueryAsync(service, input);
        }
    }

    private static void DisplaySystemReady(
        VectorMemory vectorMemory,
        AppConfiguration config,
        DatabaseConfig dbConfig)
    {
        // Display prominent table banner
        DisplayService.ShowActiveTableBanner(dbConfig.ActiveTableName, vectorMemory.Count);
        
        DisplayService.ShowVectorMemoryInitialized(vectorMemory.Count);
        
        // Show RAG mode status
        if (config.Debug.EnableRagMode)
        {
            DisplayService.WriteLine("🔍 RAG Mode: ENABLED (using semantic search with knowledge base)");
        }
        else
        {
            DisplayService.WriteLine("💬 RAG Mode: DISABLED (direct conversation mode - no knowledge base)");
        }
        
        DisplayService.ShowAvailableCommands(config.Debug.EnableDebugMode);
        DisplayService.ShowConfigurationInfo(config.Folders.InboxFolder, config.Folders.ArchiveFolder);
        DisplayService.ShowSystemReady();
    }

    private static async Task ProcessUserQueryAsync(AiChatServicePooled service, string input)
    {
        DisplayService.Write("Response: ");
        var response = await service.SendMessageAsync(input);
        DisplayService.WriteLine(response + "\n");
    }

    #endregion

    #region Resource Cleanup

    private static void CleanupResources(ModelInstancePool modelPool)
    {
        modelPool.Dispose();
        DisplayService.WriteLine("\n[+] Model pool disposed");
    }

    #endregion

    #region Debug Commands

    /// <summary>
    /// Handles debug commands like /debug, /stats, /lengths, etc.
    /// Returns true if a command was handled, false otherwise.
    /// </summary>
    private static async Task<bool> HandleDebugCommandsAsync(
        string input,
        VectorMemory vectorMemory,
        VectorMemoryPersistenceService persistenceService,
        KnowledgeFileWatcher fileWatcher,
        ModelInstancePool modelPool,
        AppConfiguration config)
    {
        if (input.StartsWith("/debug", StringComparison.OrdinalIgnoreCase))
        {
            await HandleDebugQueryCommandAsync(input, vectorMemory);
            return true;
        }

        if (input.StartsWith("/stats", StringComparison.OrdinalIgnoreCase))
        {
            await HandleStatsCommandAsync(persistenceService, vectorMemory, config);
            return true;
        }

        if (input.StartsWith("/lengths", StringComparison.OrdinalIgnoreCase))
        {
            HandleLengthsCommandAsync(vectorMemory);
            return true;
        }

        if (input.StartsWith("/collections", StringComparison.OrdinalIgnoreCase))
        {
            await HandleCollectionsCommandAsync(persistenceService);
            return true;
        }

        if (input.StartsWith("/pool", StringComparison.OrdinalIgnoreCase))
        {
            HandlePoolCommandAsync(modelPool);
            return true;
        }

        if (input.StartsWith("/reload", StringComparison.OrdinalIgnoreCase))
        {
            await HandleReloadCommandAsync(fileWatcher, persistenceService, config);
            return true;
        }

        if (input.StartsWith("/regenerate", StringComparison.OrdinalIgnoreCase))
        {
            await HandleRegenerateCommandAsync(persistenceService, config);
            return true;
        }

        return false;
    }

    private static async Task HandleDebugQueryCommandAsync(string input, VectorMemory vectorMemory)
    {
        var query = input.Substring(6).Trim();
        if (!string.IsNullOrWhiteSpace(query))
        {
            DisplayService.ShowRelevantMemoryHeader();
            var relevantMemory = await vectorMemory.SearchRelevantMemoryAsync(
                query, 
                topK: 5, 
                minRelevanceScore: 0.10);

            if (relevantMemory == null)
            {
                DisplayService.WriteLine("[!] No relevant fragments found with relevance >= 0.10");
                DisplayService.WriteLine("    The query does not match any content in the knowledge base.");
            }
            else
            {
                DisplayService.WriteLine(relevantMemory);
            }
            DisplayService.ShowRelevantMemoryFooter();
        }
    }

    private static async Task HandleStatsCommandAsync(
        VectorMemoryPersistenceService persistenceService,
        VectorMemory vectorMemory,
        AppConfiguration config)
    {
        var stats = await persistenceService.GetCollectionStatsAsync(config.Debug.CollectionName);
        DisplayService.ShowCollectionStats(
            stats.CollectionName,
            stats.FragmentCount,
            stats.HasEmbeddings,
            vectorMemory.Count);
    }

    private static void HandleLengthsCommandAsync(VectorMemory vectorMemory)
    {
        DisplayService.WriteLine("\n+------------------------------------------------------------+");
        DisplayService.WriteLine("¦  Fragment Length Analysis                                  ¦");
        DisplayService.WriteLine("+------------------------------------------------------------+\n");

        var fragments = vectorMemory.GetAllFragments();
        var sortedFragments = fragments
            .Select(f => new FragmentWithLength(f, (f as MemoryFragment)?.ContentLength ?? f.Content.Length))
            .OrderByDescending(x => x.Length)
            .ToList();

        DisplayFragmentStatistics(sortedFragments);
        DisplayLongestFragments(sortedFragments);
        DisplayLengthDistribution(sortedFragments);
    }

    private static void DisplayFragmentStatistics(List<FragmentWithLength> sortedFragments)
    {
        var avgLength = sortedFragments.Average(x => x.Length);
        var maxLength = sortedFragments.Max(x => x.Length);
        var minLength = sortedFragments.Min(x => x.Length);
        var longFragments = sortedFragments.Count(x => x.Length > 1000);
        var shortFragments = sortedFragments.Count(x => x.Length < 200);

        DisplayService.WriteLine($"Total fragments: {sortedFragments.Count}");
        DisplayService.WriteLine($"Average length: {avgLength:F0} chars");
        DisplayService.WriteLine($"Longest: {maxLength} chars");
        DisplayService.WriteLine($"Shortest: {minLength} chars");
        DisplayService.WriteLine($"Long (>1000): {longFragments} fragments");
        DisplayService.WriteLine($"Short (<200): {shortFragments} fragments");
        DisplayService.WriteLine();
    }

    private static void DisplayLongestFragments(List<FragmentWithLength> sortedFragments)
    {
        DisplayService.WriteLine("Top 10 Longest Fragments:");
        DisplayService.WriteLine("-----------------------------------------------------------");
        
        foreach (var item in sortedFragments.Take(10))
        {
            var category = item.Fragment.Category;
            var truncatedCategory = category.Length > 50 ? category.Substring(0, 47) + "..." : category;
            DisplayService.WriteLine($"{item.Length,5} chars - {truncatedCategory}");
        }
        
        DisplayService.WriteLine();
    }

    private static void DisplayLengthDistribution(List<FragmentWithLength> sortedFragments)
    {
        DisplayService.WriteLine("Distribution by Length:");
        DisplayService.WriteLine("-----------------------------------------------------------");
        
        var buckets = new[]
        {
            (Range: "0-200", Min: 0, Max: 200),
            (Range: "201-500", Min: 201, Max: 500),
            (Range: "501-1000", Min: 501, Max: 1000),
            (Range: "1001-1500", Min: 1001, Max: 1500),
            (Range: "1500+", Min: 1501, Max: int.MaxValue)
        };

        foreach (var bucket in buckets)
        {
            var count = sortedFragments.Count(x => x.Length >= bucket.Min && x.Length <= bucket.Max);
            var barWidth = (int)((count / (double)sortedFragments.Count) * 40);
            var bar = new string('¦', barWidth);
            DisplayService.WriteLine($"{bucket.Range,12}: {bar} {count,3} ({count * 100.0 / sortedFragments.Count:F1}%)");
        }

        DisplayService.WriteLine();
    }

    private record FragmentWithLength(IMemoryFragment Fragment, int Length);
    
    private static async Task HandleCollectionsCommandAsync(VectorMemoryPersistenceService persistenceService)
    {
        var collections = await persistenceService.GetCollectionsAsync();
        var fragmentCounts = new Dictionary<string, int>();
        
        foreach (var col in collections)
        {
            var stats = await persistenceService.GetCollectionStatsAsync(col);
            fragmentCounts[col] = stats.FragmentCount;
        }
        
        DisplayService.ShowCollectionsList(collections, fragmentCounts);
    }

    private static void HandlePoolCommandAsync(ModelInstancePool modelPool)
    {
        DisplayService.WriteLine($"\n[*] Pool Status:");
        DisplayService.WriteLine($"    Available: {modelPool.AvailableCount}/{modelPool.MaxInstances}");
        DisplayService.WriteLine($"    In Use: {modelPool.MaxInstances - modelPool.AvailableCount}");
    }

    private static async Task HandleReloadCommandAsync(
        KnowledgeFileWatcher fileWatcher,
        VectorMemoryPersistenceService persistenceService,
        AppConfiguration config)
    {
        DisplayService.WriteLine("\n[*] Checking for new files...");
        var newInboxFiles = await fileWatcher.DiscoverNewFilesAsync();

        if (newInboxFiles.Any())
        {
            DisplayService.WriteLine($"[*] Found {newInboxFiles.Count} new file(s)!");
            await ProcessNewFilesAsync(newInboxFiles, persistenceService, fileWatcher, config);
            DisplayService.WriteLine($"[!] Please restart the application to use the new fragments.");
        }
        else
        {
            DisplayService.WriteLine("[+] No new files found in inbox");
        }
    }

    private static async Task HandleRegenerateCommandAsync(
        VectorMemoryPersistenceService persistenceService,
        AppConfiguration config)
    {
        DisplayService.WriteLine("\n⚠️  WARNING: This will DELETE all embeddings and regenerate them with titles included!");
        DisplayService.WriteLine("This requires re-processing files from the archive folder.");
        DisplayService.WriteLine("");
        DisplayService.Write("Type 'yes' to continue or anything else to cancel: ");
        
        var confirmation = Console.ReadLine()?.Trim().ToLower();
        if (confirmation != "yes")
        {
            DisplayService.WriteLine("[!] Regeneration cancelled.");
            return;
        }
        
        DisplayService.WriteLine("\n[1/2] Deleting old collection...");
        await persistenceService.DeleteCollectionAsync(config.Debug.CollectionName);
        DisplayService.WriteLine($"[✓] Collection '{config.Debug.CollectionName}' deleted.");
        
        DisplayService.WriteLine("\n[2/2] Next steps:");
        DisplayService.WriteLine("  1. Move rulebook files from archive back to inbox");
        DisplayService.WriteLine($"     Archive: {config.Folders.ArchiveFolder}");
        DisplayService.WriteLine($"     Inbox:   {config.Folders.InboxFolder}");
        DisplayService.WriteLine("  2. Restart the application");
        DisplayService.WriteLine("  3. Files will be auto-processed with titles in embeddings ✅");
        DisplayService.WriteLine("");
        DisplayService.WriteLine("Or use PowerShell:");
        DisplayService.WriteLine($"  Move-Item \"{config.Folders.ArchiveFolder}\\*.txt\" \"{config.Folders.InboxFolder}\"");
    }

    private static void HandleToggleRagCommand(AppConfiguration config)
    {
        // Note: This intentionally mutates the shared AppConfiguration.Debug.EnableRagMode
        // to allow runtime toggling of RAG mode. The change affects new queries only,
        // not in-flight operations. This is by design for interactive CLI usage.
        config.Debug.EnableRagMode = !config.Debug.EnableRagMode;
        
        DisplayService.WriteLine($"\n[*] RAG Mode: {(config.Debug.EnableRagMode ? "ENABLED 🔍" : "DISABLED 💬")}");
        
        if (config.Debug.EnableRagMode)
        {
            DisplayService.WriteLine("    Using semantic search with knowledge base");
        }
        else
        {
            DisplayService.WriteLine("    Direct conversation mode (no knowledge base retrieval)");
        }
        
        DisplayService.WriteLine("\n[!] Note: This change applies to new queries only");
    }

    private static void HandlePerfCommand(AppConfiguration config)
    {
        config.Debug.ShowPerformanceMetrics = !config.Debug.ShowPerformanceMetrics;
        
        DisplayService.WriteLine($"\n[*] Performance Metrics: {(config.Debug.ShowPerformanceMetrics ? "ENABLED 📊" : "DISABLED")}");
        
        if (config.Debug.ShowPerformanceMetrics)
        {
            DisplayService.WriteLine("    Will show tokens/sec, timing after each response");
        }
        else
        {
            DisplayService.WriteLine("    Performance metrics hidden");
        }
        
        DisplayService.WriteLine("\n[!] Note: This change applies to new queries only");
    }

    private static void HandleTemperatureCommand(string input, AppConfiguration config)
    {
        // Parse temperature value
        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            DisplayService.WriteLine("\n[!] Usage: /temperature <value> or /temp <value>");
            DisplayService.WriteLine($"    Current: {config.Generation.Temperature:F2}");
            DisplayService.WriteLine("    Range: 0.0-2.0 (lower = more focused, higher = more creative)");
            return;
        }

        if (!float.TryParse(parts[1], out float temperature) || temperature < 0 || temperature > 2.0f)
        {
            DisplayService.WriteLine("\n[!] Invalid temperature. Must be between 0.0 and 2.0");
            DisplayService.WriteLine($"    Current: {config.Generation.Temperature:F2}");
            return;
        }

        var oldTemp = config.Generation.Temperature;
        config.Generation.Temperature = temperature;
        
        DisplayService.WriteLine($"\n[*] Temperature: {oldTemp:F2} → {temperature:F2}");
        if (temperature < 0.3f)
            DisplayService.WriteLine("    Very focused and deterministic responses");
        else if (temperature < 0.7f)
            DisplayService.WriteLine("    Balanced responses");
        else if (temperature < 1.2f)
            DisplayService.WriteLine("    Creative and varied responses");
        else
            DisplayService.WriteLine("    Highly creative but may be unpredictable");
        
        DisplayService.WriteLine("\n[!] Note: This change applies to new queries only");
    }

    private static void HandleTokensCommand(string input, AppConfiguration config)
    {
        // Parse tokens value
        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            DisplayService.WriteLine("\n[!] Usage: /tokens <value> or /maxtokens <value>");
            DisplayService.WriteLine($"    Current: {config.Generation.MaxTokens}");
            DisplayService.WriteLine("    Range: 1-2048 (higher = longer responses, slower)");
            return;
        }

        if (!int.TryParse(parts[1], out int tokens) || tokens < 1 || tokens > 2048)
        {
            DisplayService.WriteLine("\n[!] Invalid token count. Must be between 1 and 2048");
            DisplayService.WriteLine($"    Current: {config.Generation.MaxTokens}");
            return;
        }

        var oldTokens = config.Generation.MaxTokens;
        config.Generation.MaxTokens = tokens;
        
        DisplayService.WriteLine($"\n[*] Max Tokens: {oldTokens} → {tokens}");
        if (tokens < 100)
            DisplayService.WriteLine("    Very short responses");
        else if (tokens < 300)
            DisplayService.WriteLine("    Medium-length responses");
        else if (tokens < 500)
            DisplayService.WriteLine("    Long responses");
        else
            DisplayService.WriteLine("    Very long responses (may be slower)");
        
        DisplayService.WriteLine("\n[!] Note: This change applies to new queries only");
    }

    private static void HandleSettingsCommand(AppConfiguration config, DatabaseConfig dbConfig)
    {
        DisplayService.WriteLine("\n╔═══════════════════════════════════════════════════════════════╗");
        DisplayService.WriteLine("║  Current Generation Settings                                 ║");
        DisplayService.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        DisplayService.WriteLine($"\n  Temperature:         {config.Generation.Temperature:F2} (0.0-2.0)");
        DisplayService.WriteLine($"  Max Tokens:          {config.Generation.MaxTokens} (1-2048)");
        DisplayService.WriteLine($"  Top-K:               {config.Generation.TopK}");
        DisplayService.WriteLine($"  Top-P:               {config.Generation.TopP:F2}");
        DisplayService.WriteLine($"  Repeat Penalty:      {config.Generation.RepeatPenalty:F2}");
        DisplayService.WriteLine($"  Presence Penalty:    {config.Generation.PresencePenalty:F2}");
        DisplayService.WriteLine($"  Frequency Penalty:   {config.Generation.FrequencyPenalty:F2}");
        
        DisplayService.WriteLine("\n╔═══════════════════════════════════════════════════════════════╗");
        DisplayService.WriteLine("║  Model Information                                           ║");
        DisplayService.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        var modelFile = string.IsNullOrWhiteSpace(config.Llm.ModelPath) ? "(none)" : Path.GetFileName(config.Llm.ModelPath);
        DisplayService.WriteLine($"\n  Model:               {config.Llm.ModelName ?? modelFile}");
        if (!string.IsNullOrWhiteSpace(config.Llm.ModelType))
            DisplayService.WriteLine($"  Type:                {config.Llm.ModelType}");
        DisplayService.WriteLine($"  RAG Mode:            {(config.Debug.EnableRagMode ? "ENABLED" : "DISABLED")}");
        DisplayService.WriteLine($"  Debug Mode:          {(config.Debug.EnableDebugMode ? "ENABLED" : "DISABLED")}");
        DisplayService.WriteLine($"  Performance Metrics: {(config.Debug.ShowPerformanceMetrics ? "ENABLED" : "DISABLED")}");
        
        DisplayService.WriteLine("\n╔═══════════════════════════════════════════════════════════════╗");
        DisplayService.WriteLine("║  Database & RAG Context                                      ║");
        DisplayService.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        DisplayService.WriteLine($"\n  Active Table:        {dbConfig.ActiveTableName}");
        
        DisplayService.WriteLine("\n  Commands:");
        DisplayService.WriteLine("    /temperature <value>  - Change temperature (0.0-2.0)");
        DisplayService.WriteLine("    /tokens <value>       - Change max tokens (1-2048)");
        DisplayService.WriteLine("    /rag                  - Toggle RAG mode");
        DisplayService.WriteLine("    /perf                 - Toggle performance metrics");
        DisplayService.WriteLine("    /switchmodel          - Switch to a different model");
        DisplayService.WriteLine("    /table                - Manage RAG context tables");
    }

    private static async Task<bool> HandleTableCommandAsync(
        string input, 
        VectorMemoryPersistenceService persistenceService,
        DatabaseConfig dbConfig,
        AppConfiguration appConfig)
    {
        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        // /table - show current table and available commands
        if (parts.Length == 1)
        {
            await ShowTableManagementMenuAsync(persistenceService, dbConfig);
            return false;
        }
        
        var subCommand = parts[1].ToLowerInvariant();
        
        switch (subCommand)
        {
            case "list":
                await HandleTableListCommandAsync(persistenceService, dbConfig);
                return false;
                
            case "create":
                if (parts.Length < 3)
                {
                    DisplayService.WriteLine("\n[!] Usage: /table create <table_name>");
                    DisplayService.WriteLine("    Example: /table create GameRules_SciFi");
                    return false;
                }
                await HandleTableCreateCommandAsync(parts[2], persistenceService);
                return false;
                
            case "switch":
                if (parts.Length < 3)
                {
                    DisplayService.WriteLine("\n[!] Usage: /table switch <table_name>");
                    DisplayService.WriteLine("    Use '/table list' to see available tables");
                    return false;
                }
                return await HandleTableSwitchCommandAsync(parts[2], persistenceService, dbConfig, appConfig);
                
            case "delete":
                if (parts.Length < 3)
                {
                    DisplayService.WriteLine("\n[!] Usage: /table delete <table_name>");
                    DisplayService.WriteLine("    WARNING: This will permanently delete the table!");
                    return false;
                }
                await HandleTableDeleteCommandAsync(parts[2], persistenceService);
                return false;
                
            case "info":
                await HandleTableInfoCommandAsync(persistenceService, dbConfig, appConfig);
                return false;
                
            default:
                DisplayService.WriteLine($"\n[!] Unknown table command: {subCommand}");
                await ShowTableManagementMenuAsync(persistenceService, dbConfig);
                return false;
        }
    }

    private static async Task ShowTableManagementMenuAsync(
        VectorMemoryPersistenceService persistenceService,
        DatabaseConfig dbConfig)
    {
        var repository = await GetRepositoryFromServiceAsync(persistenceService);
        
        DisplayService.WriteLine("\n╔═══════════════════════════════════════════════════════════════╗");
        DisplayService.WriteLine("║  RAG Context Table Management                                 ║");
        DisplayService.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        DisplayService.WriteLine($"\n  Current Table: {dbConfig.ActiveTableName}");
        DisplayService.WriteLine("\n  Available Commands:");
        DisplayService.WriteLine("    /table list           - List all available tables");
        DisplayService.WriteLine("    /table create <name>  - Create new table for RAG context");
        DisplayService.WriteLine("    /table switch <name>  - Switch to different table");
        DisplayService.WriteLine("    /table delete <name>  - Delete a table (cannot delete default)");
        DisplayService.WriteLine("    /table info           - Show detailed info about current table");
        DisplayService.WriteLine("\n  Use Case:");
        DisplayService.WriteLine("    - Create separate tables for different knowledge domains");
        DisplayService.WriteLine("    - Switch contexts without reloading files");
        DisplayService.WriteLine("    - E.g., GameRules_Fantasy, GameRules_SciFi, Programming_Docs");
    }

    private static async Task HandleTableListCommandAsync(
        VectorMemoryPersistenceService persistenceService,
        DatabaseConfig dbConfig)
    {
        var repository = await GetRepositoryFromServiceAsync(persistenceService);
        var tables = await repository.GetAllTablesAsync();
        
        DisplayService.WriteLine("\n╔═══════════════════════════════════════════════════════════════╗");
        DisplayService.WriteLine("║  Available RAG Context Tables                                 ║");
        DisplayService.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        
        if (!tables.Any())
        {
            DisplayService.WriteLine("\n  No tables found. Create one with: /table create <name>");
            return;
        }
        
        DisplayService.WriteLine($"\n  Current: {dbConfig.ActiveTableName}");
        DisplayService.WriteLine("\n  Available Tables:");
        
        foreach (var table in tables)
        {
            var marker = table.Equals(dbConfig.ActiveTableName, StringComparison.OrdinalIgnoreCase) 
                ? " ⭐ (active)" 
                : "";
            
            // Get fragment count for this table
            var currentActive = repository.GetActiveTable();
            repository.SetActiveTable(table);
            var collections = await repository.GetCollectionsAsync();
            var totalFragments = 0;
            foreach (var col in collections)
            {
                totalFragments += await repository.GetCountAsync(col);
            }
            repository.SetActiveTable(currentActive);
            
            DisplayService.WriteLine($"    - {table}{marker}");
            DisplayService.WriteLine($"      Collections: {collections.Count}, Fragments: {totalFragments}");
        }
    }

    private static async Task HandleTableCreateCommandAsync(
        string tableName,
        VectorMemoryPersistenceService persistenceService)
    {
        var repository = await GetRepositoryFromServiceAsync(persistenceService);
        
        try
        {
            // Check if table already exists
            if (await repository.TableExistsAsync(tableName))
            {
                DisplayService.WriteLine($"\n[!] Table '{tableName}' already exists.");
                DisplayService.WriteLine("    Use '/table switch {tableName}' to switch to it.");
                return;
            }
            
            DisplayService.WriteLine($"\n[*] Creating new table: {tableName}");
            DisplayService.WriteLine($"[*] Connecting to database...");
            
            await repository.CreateTableAsync(tableName);
            
            // Verify the table was created
            if (await repository.TableExistsAsync(tableName))
            {
                DisplayService.WriteLine($"[✓] Table '{tableName}' created successfully!");
                DisplayService.WriteLine($"[✓] Table verified in database");
                DisplayService.WriteLine($"\n  Next steps:");
                DisplayService.WriteLine($"    1. Switch to the new table: /table switch {tableName}");
                DisplayService.WriteLine($"    2. Add files to inbox to populate it");
                DisplayService.WriteLine($"    3. Or use /reload to process existing inbox files");
            }
            else
            {
                DisplayService.WriteLine($"[!] WARNING: Table creation completed but verification failed.");
                DisplayService.WriteLine($"    The table might exist but isn't showing up in queries.");
                DisplayService.WriteLine($"    Try: /table list to see all tables");
            }
        }
        catch (Microsoft.Data.SqlClient.SqlException sqlEx)
        {
            DisplayService.WriteLine($"\n[!] Database error creating table:");
            DisplayService.WriteLine($"    Error: {sqlEx.Message}");
            DisplayService.WriteLine($"    Number: {sqlEx.Number}");
            if (sqlEx.Number == 262)
            {
                DisplayService.WriteLine($"\n    This error usually means you don't have CREATE TABLE permissions.");
                DisplayService.WriteLine($"    Please check your database permissions.");
            }
        }
        catch (Exception ex)
        {
            DisplayService.WriteLine($"\n[!] Error creating table: {ex.GetType().Name}");
            DisplayService.WriteLine($"    Message: {ex.Message}");
            if (ex.InnerException != null)
            {
                DisplayService.WriteLine($"    Inner: {ex.InnerException.Message}");
            }
        }
    }

    private static async Task<bool> HandleTableSwitchCommandAsync(
        string tableName,
        VectorMemoryPersistenceService persistenceService,
        DatabaseConfig dbConfig,
        AppConfiguration appConfig)
    {
        var repository = await GetRepositoryFromServiceAsync(persistenceService);
        
        try
        {
            // Check if table exists
            if (!await repository.TableExistsAsync(tableName))
            {
                DisplayService.WriteLine($"\n[!] Table '{tableName}' does not exist.");
                DisplayService.WriteLine("    Use '/table list' to see available tables");
                DisplayService.WriteLine($"    Or create it with: /table create {tableName}");
                return false;
            }
            
            if (tableName.Equals(dbConfig.ActiveTableName, StringComparison.OrdinalIgnoreCase))
            {
                DisplayService.WriteLine($"\n[*] '{tableName}' is already the active table.");
                return false;
            }
            
            DisplayService.WriteLine($"\n[*] Switching from '{dbConfig.ActiveTableName}' to '{tableName}'");
            
            // Update configuration
            dbConfig.ActiveTableName = tableName;
            repository.SetActiveTable(tableName);
            
            // Show info about new table
            var collections = await repository.GetCollectionsAsync();
            var totalFragments = 0;
            foreach (var col in collections)
            {
                totalFragments += await repository.GetCountAsync(col);
            }
            
            DisplayService.WriteLine($"[✓] Switched to table: {tableName}");
            DisplayService.WriteLine($"    Collections: {collections.Count}");
            DisplayService.WriteLine($"    Total Fragments: {totalFragments}");
            DisplayService.WriteLine("\n[*] Vector memory will be reloaded automatically...");
            
            return true; // Signal that table was switched and reload is needed
        }
        catch (Exception ex)
        {
            DisplayService.WriteLine($"\n[!] Error switching table: {ex.Message}");
            return false;
        }
    }

    private static async Task HandleTableDeleteCommandAsync(
        string tableName,
        VectorMemoryPersistenceService persistenceService)
    {
        var repository = await GetRepositoryFromServiceAsync(persistenceService);
        
        try
        {
            // Check if table exists
            if (!await repository.TableExistsAsync(tableName))
            {
                DisplayService.WriteLine($"\n[!] Table '{tableName}' does not exist.");
                return;
            }
            
            // Prevent deleting default table
            if (tableName.Equals("MemoryFragments", StringComparison.OrdinalIgnoreCase))
            {
                DisplayService.WriteLine("\n[!] Cannot delete the default 'MemoryFragments' table.");
                DisplayService.WriteLine("    This table is protected to prevent data loss.");
                return;
            }
            
            DisplayService.WriteLine($"\n⚠️  WARNING: You are about to permanently delete table '{tableName}'");
            DisplayService.WriteLine("   This action cannot be undone!");
            DisplayService.Write("\n   Type 'DELETE' to confirm: ");
            
            var confirmation = Console.ReadLine()?.Trim();
            if (!confirmation.Equals("DELETE", StringComparison.Ordinal))
            {
                DisplayService.WriteLine("[*] Table deletion cancelled.");
                return;
            }
            
            DisplayService.WriteLine($"\n[*] Deleting table: {tableName}");
            await repository.DeleteTableAsync(tableName);
            DisplayService.WriteLine($"[✓] Table '{tableName}' has been deleted.");
        }
        catch (Exception ex)
        {
            DisplayService.WriteLine($"\n[!] Error deleting table: {ex.Message}");
        }
    }

    private static async Task HandleTableInfoCommandAsync(
        VectorMemoryPersistenceService persistenceService,
        DatabaseConfig dbConfig,
        AppConfiguration appConfig)
    {
        var repository = await GetRepositoryFromServiceAsync(persistenceService);
        
        try
        {
            var tableName = dbConfig.ActiveTableName;
            var collections = await repository.GetCollectionsAsync();
            
            DisplayService.WriteLine("\n╔═══════════════════════════════════════════════════════════════╗");
            DisplayService.WriteLine("║  Current Table Information                                    ║");
            DisplayService.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
            DisplayService.WriteLine($"\n  Table Name: {tableName}");
            DisplayService.WriteLine($"  Total Collections: {collections.Count}");
            
            if (collections.Any())
            {
                DisplayService.WriteLine("\n  Collections:");
                foreach (var col in collections)
                {
                    var count = await repository.GetCountAsync(col);
                    var hasEmbeddings = await repository.HasEmbeddingsAsync(col);
                    var embeddingStatus = hasEmbeddings ? "✓" : "✗";
                    DisplayService.WriteLine($"    - {col}: {count} fragments [Embeddings: {embeddingStatus}]");
                }
            }
            else
            {
                DisplayService.WriteLine("\n  No collections found in this table.");
                DisplayService.WriteLine("  Add files to inbox to populate it.");
            }
        }
        catch (Exception ex)
        {
            DisplayService.WriteLine($"\n[!] Error getting table info: {ex.Message}");
        }
    }

    /// <summary>
    /// Helper to get the repository instance from the persistence service.
    /// Uses reflection as a workaround since the repository isn't directly exposed.
    /// </summary>
    private static async Task<IVectorMemoryRepository> GetRepositoryFromServiceAsync(
        VectorMemoryPersistenceService persistenceService)
    {
        // The repository is private in VectorMemoryPersistenceService
        // We need to access it via reflection or add a public property
        var repositoryField = persistenceService.GetType()
            .GetField("_repository", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        if (repositoryField == null)
            throw new InvalidOperationException("Could not access repository from persistence service");
        
        var repository = repositoryField.GetValue(persistenceService) as IVectorMemoryRepository;
        if (repository == null)
            throw new InvalidOperationException("Repository is null");
        
        return repository;
    }

    private static async Task<bool> HandleSwitchModelCommandAsync(AppConfiguration config, ModelInstancePool modelPool)
    {
        try
        {
            // Get the directory containing the current model
            var currentModelPath = config.Llm.ModelPath;
            if (string.IsNullOrWhiteSpace(currentModelPath) || !File.Exists(currentModelPath))
            {
                DisplayService.WriteLine("\n[!] Current model path is not valid or file does not exist.");
                DisplayService.WriteLine($"    Path: {currentModelPath}");
                return false;
            }

            var modelDirectory = Path.GetDirectoryName(currentModelPath);
            if (string.IsNullOrWhiteSpace(modelDirectory) || !Directory.Exists(modelDirectory))
            {
                DisplayService.WriteLine("\n[!] Model directory not found.");
                DisplayService.WriteLine($"    Directory: {modelDirectory}");
                return false;
            }

            // Find all GGUF files in the directory
            var ggufFiles = Directory.GetFiles(modelDirectory, "*.gguf", SearchOption.TopDirectoryOnly)
                .Select(f => new
                {
                    FullPath = f,
                    FileName = Path.GetFileName(f),
                    SizeMB = new FileInfo(f).Length / (1024.0 * 1024.0),
                    IsCurrent = string.Equals(f, currentModelPath, StringComparison.OrdinalIgnoreCase)
                })
                .OrderBy(x => x.FileName)
                .ToList();

            if (!ggufFiles.Any())
            {
                DisplayService.WriteLine("\n[!] No GGUF model files found in the directory.");
                DisplayService.WriteLine($"    Directory: {modelDirectory}");
                return false;
            }

            // Display available models
            DisplayService.WriteLine("\n╔═══════════════════════════════════════════════════════════════╗");
            DisplayService.WriteLine("║  Available GGUF Models                                        ║");
            DisplayService.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
            DisplayService.WriteLine($"\nDirectory: {modelDirectory}\n");
            
            for (int i = 0; i < ggufFiles.Count; i++)
            {
                var model = ggufFiles[i];
                var currentMarker = model.IsCurrent ? " ⭐ (current)" : "";
                DisplayService.WriteLine($"  [{i + 1}] {model.FileName}{currentMarker}");
                DisplayService.WriteLine($"      Size: {model.SizeMB:F2} MB");
            }

            DisplayService.WriteLine($"\n  [0] Cancel");
            DisplayService.Write("\nSelect model number: ");

            var input = Console.ReadLine()?.Trim();
            if (!int.TryParse(input, out int selection) || selection < 0 || selection > ggufFiles.Count)
            {
                DisplayService.WriteLine("[!] Invalid selection. Operation cancelled.");
                return false;
            }

            if (selection == 0)
            {
                DisplayService.WriteLine("[*] Model switch cancelled.");
                return false;
            }

            var selectedModel = ggufFiles[selection - 1];
            
            if (selectedModel.IsCurrent)
            {
                DisplayService.WriteLine($"\n[*] '{selectedModel.FileName}' is already the current model.");
                return false;
            }

            // Update configuration
            DisplayService.WriteLine($"\n[*] Switching to: {selectedModel.FileName}");
            DisplayService.WriteLine("[*] Reinitializing model pool with new model...");
            
            // Update config first
            config.Llm.ModelPath = selectedModel.FullPath;
            config.Llm.ModelName = Path.GetFileNameWithoutExtension(selectedModel.FileName);
            
            // Reinitialize pool with new model (this will dispose old instances and create new ones)
            await modelPool.ReinitializeAsync(
                config.Llm.ExecutablePath,
                selectedModel.FullPath,
                (current, total) => DisplayService.WriteLine($"    Loading instance {current}/{total}..."));

            DisplayService.WriteLine($"\n[✓] Successfully switched to: {selectedModel.FileName}");
            DisplayService.WriteLine($"[+] Model pool ready with {modelPool.AvailableCount} instances");
            DisplayService.WriteLine("[!] Chat service will be recreated for next query");
            
            return true;
        }
        catch (Exception ex)
        {
            DisplayService.WriteLine($"\n[!] Error switching model: {ex.Message}");
            if (config.Debug.EnableDebugMode)
            {
                DisplayService.WriteLine($"[DEBUG] Stack trace: {ex.StackTrace}");
            }
            return false;
        }
    }

    #endregion

    #region File Processing Helper

    /// <summary>
    /// Processes new files from inbox, vectorizes them, saves to database, and archives them.
    /// </summary>
    private static async Task ProcessNewFilesAsync(
        Dictionary<string, string> newFiles,
        VectorMemoryPersistenceService persistenceService,
        KnowledgeFileWatcher fileWatcher,
        AppConfiguration config)
    {
        var allFragments = await CollectFragmentsFromFilesAsync(newFiles, fileWatcher);
        DisplayService.ShowTotalFragmentsCollected(allFragments.Count);

        await SaveFragmentsToDatabaseAsync(allFragments, persistenceService, newFiles, config);
        await ArchiveProcessedFilesAsync(newFiles, fileWatcher);

        DisplayService.WriteLine($"\n[+] Successfully processed and archived {newFiles.Count} file(s)");
    }

    private static async Task<List<MemoryFragment>> CollectFragmentsFromFilesAsync(
        Dictionary<string, string> newFiles,
        KnowledgeFileWatcher fileWatcher)
    {
        var allFragments = new List<MemoryFragment>();
        foreach (var (gameName, filePath) in newFiles)
        {
            var fragments = await fileWatcher.ProcessFileAsync(gameName, filePath);
            allFragments.AddRange(fragments);
        }
        return allFragments;
    }

    private static async Task SaveFragmentsToDatabaseAsync(
        List<MemoryFragment> fragments,
        VectorMemoryPersistenceService persistenceService,
        Dictionary<string, string> newFiles,
        AppConfiguration config)
    {
        DisplayService.ShowSavingToDatabaseHeader();
        await persistenceService.SaveFragmentsAsync(
            fragments,
            config.Debug.CollectionName,
            sourceFile: string.Join(", ", newFiles.Keys),
            replaceExisting: false);
    }

    private static async Task ArchiveProcessedFilesAsync(
        Dictionary<string, string> newFiles,
        KnowledgeFileWatcher fileWatcher)
    {
        DisplayService.WriteLine("\n[*] Archiving processed files...");
        foreach (var filePath in newFiles.Values)
        {
            await fileWatcher.ArchiveFileAsync(filePath);
        }
    }

    #endregion
}

