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
        
        // Create chat service with current RAG mode
        AiChatServicePooled CreateChatService() => new AiChatServicePooled(
            vectorMemory,
            conversationMemory,
            services.ModelPool,
            debugMode: config.Debug.EnableDebugMode,
            enableRag: config.Debug.EnableRagMode);
        
        var service = CreateChatService();

        DisplaySystemReady(vectorMemory, config);

        var fileWatcher = new KnowledgeFileWatcher(config.Folders.InboxFolder, config.Folders.ArchiveFolder);
        bool ragModeChanged = false;

        while (true)
        {
            // Recreate service if RAG mode was toggled
            if (ragModeChanged)
            {
                service = CreateChatService();
                ragModeChanged = false;
            }
            
            var input = DisplayService.ReadInput("\n> ");

            if (string.IsNullOrWhiteSpace(input)) continue;
            if (input.ToLower() == "exit") break;

            // Check if RAG toggle command
            if (input.Equals("/rag", StringComparison.OrdinalIgnoreCase))
            {
                HandleToggleRagCommand(config);
                ragModeChanged = true;
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
        AppConfiguration config)
    {
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

