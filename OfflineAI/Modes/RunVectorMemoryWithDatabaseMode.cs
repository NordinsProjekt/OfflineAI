using Services.Interfaces;
using Services.Configuration;
using Services.Repositories;
using Services.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Entities;
using Services.AI.Chat;
using Services.AI.Embeddings;
using Services.Models;
using Services.Memory;
using Services.Pooling;
using Microsoft.Extensions.DependencyInjection;

namespace OfflineAI.Modes;

internal static class RunVectorMemoryWithDatabaseMode
{
    internal static async Task RunAsync(IServiceProvider serviceProvider)
    {
        DisplayService.ShowVectorMemoryDatabaseHeader();

        // Setup paths from configuration
        var llmPath = @"d:\tinyllama\llama-cli.exe";
        var modelPath = @"d:\tinyllama\tinyllama-1.1b-chat-v1.0.Q5_K_M.gguf";
        
        // Smart file processing folders
        var inboxFolder = @"d:\tinyllama\inbox";
        var archiveFolder = @"d:\tinyllama\archive";

        // Get services from DI container
        var dbConfig = serviceProvider.GetRequiredService<DatabaseConfig>();
        var embeddingService = serviceProvider.GetRequiredService<SemanticEmbeddingService>();
        var persistenceService = serviceProvider.GetRequiredService<VectorMemoryPersistenceService>();
        var modelPool = serviceProvider.GetRequiredService<ModelInstancePool>();

        // ✅ BERT embedding service (superior semantic understanding)
        DisplayService.ShowInitializingEmbeddingService();
        DisplayService.WriteLine("Using BERT embeddings for semantic search...");

        // Test database connection
        DisplayService.ShowTestingDatabaseConnection();
        var canConnect = await Services.Utilities.DatabaseConnectionTester.TestConnectionAsync(dbConfig.ConnectionString);
        
        if (!canConnect)
        {
            DisplayService.ShowDatabaseConnectionFailed();
            DisplayService.WaitForKeyPress();
            return;
        }

        // Initialize database schema
        if (dbConfig.AutoInitializeDatabase)
        {
            DisplayService.ShowInitializingDatabaseSchema();
            await persistenceService.InitializeDatabaseAsync();
            DisplayService.ShowDatabaseSchemaReady();
        }

        // ✅ Smart file auto-processing
        DisplayService.WriteLine("\n╔════════════════════════════════════════════════════════╗");
        DisplayService.WriteLine("║           Smart File Auto-Processing                   ║");
        DisplayService.WriteLine("╚════════════════════════════════════════════════════════╝");
        
        var fileWatcher = new KnowledgeFileWatcher(inboxFolder, archiveFolder);
        var newFiles = await fileWatcher.DiscoverNewFilesAsync();
        
        if (newFiles.Any())
        {
            DisplayService.WriteLine($"\n[*] Found {newFiles.Count} new file(s) in inbox:");
            foreach (var file in newFiles.Keys)
            {
                DisplayService.WriteLine($"    - {file}.txt");
            }
            
            DisplayService.WriteLine("\n[*] Processing and vectorizing new files...");
            await ProcessNewFilesAsync(newFiles, persistenceService, fileWatcher);
        }
        else
        {
            DisplayService.WriteLine($"\n[+] No new files in inbox folder: {inboxFolder}");
            DisplayService.WriteLine("    Place .txt files there to auto-process them!");
        }

        // Check what collections exist
        var existingCollections = await persistenceService.GetCollectionsAsync();
        DisplayService.ShowExistingCollections(existingCollections.Count);
        foreach (var collection in existingCollections)
        {
            var stats = await persistenceService.GetCollectionStatsAsync(collection);
            DisplayService.ShowCollectionInfo(collection, stats.FragmentCount);
        }

        // Load or create vector memory
        var collectionName = "game-rules";
        VectorMemory vectorMemory;
        
        if (await persistenceService.CollectionExistsAsync(collectionName))
        {
            DisplayService.WriteLine($"\n[+] Loading existing collection: {collectionName}");
            vectorMemory = await persistenceService.LoadVectorMemoryAsync(collectionName);
        }
        else
        {
            DisplayService.ShowCollectionNotFound(collectionName);
            DisplayService.WriteLine("Creating empty collection. Add files to inbox folder to populate.");
            vectorMemory = new VectorMemory(embeddingService, collectionName);
        }

        // Initialize Model Instance Pool (keeps models loaded in memory)
        DisplayService.WriteLine("\n╔════════════════════════════════════════════════════════╗");
        DisplayService.WriteLine("║         Initializing Model Instance Pool...            ║");
        DisplayService.WriteLine("╚════════════════════════════════════════════════════════╝");
        DisplayService.WriteLine("\nThis keeps the model loaded in memory for faster responses.");
        DisplayService.WriteLine("Pool size: 3 instances (supports 3-10 concurrent users)\n");
        
        await modelPool.InitializeAsync((current, total) =>
        {
            DisplayService.WriteLine($"Loading instance {current}/{total}...");
        });
        
        DisplayService.WriteLine($"\n[+] Model pool ready with {modelPool.AvailableCount} instances");
        DisplayService.WriteLine("Memory usage: ~3-5 GB (model stays loaded)");

        var conversationMemory = new SimpleMemory();

        // Create AI service using the pool
        var service = new AiChatServicePooled(
            vectorMemory,
            conversationMemory,
            modelPool);

        DisplayService.ShowVectorMemoryInitialized(vectorMemory.Count);
        DisplayService.ShowAvailableCommands();
        DisplayService.ShowConfigurationInfo(inboxFolder, archiveFolder);
        
        // Show system is ready for input
        DisplayService.ShowSystemReady();

        while (true)
        {
            var input = DisplayService.ReadInput("\n> ");

            if (string.IsNullOrWhiteSpace(input)) continue;

            if (input.ToLower() == "exit") break;

            // Show relevant context being used
            if (input.StartsWith("/debug", StringComparison.OrdinalIgnoreCase))
            {
                var query = input.Substring(6).Trim();
                if (!string.IsNullOrWhiteSpace(query))
                {
                    DisplayService.ShowRelevantMemoryHeader();
                    var relevantMemory =
                        await vectorMemory.SearchRelevantMemoryAsync(query, topK: 5, minRelevanceScore: 0.4);
                    
                    if (relevantMemory == null)
                    {
                        DisplayService.WriteLine("[!] No relevant fragments found with relevance >= 0.35");
                        DisplayService.WriteLine("    The query does not match any content in the knowledge base.");
                    }
                    else
                    {
                        DisplayService.WriteLine(relevantMemory);
                    }
                    DisplayService.ShowRelevantMemoryFooter();
                }
                continue;
            }

            if (input.StartsWith("/stats", StringComparison.OrdinalIgnoreCase))
            {
                var stats = await persistenceService.GetCollectionStatsAsync(collectionName);
                DisplayService.ShowCollectionStats(
                    stats.CollectionName, 
                    stats.FragmentCount, 
                    stats.HasEmbeddings, 
                    vectorMemory.Count);
                continue;
            }
            
            if (input.StartsWith("/lengths", StringComparison.OrdinalIgnoreCase))
            {
                DisplayService.WriteLine("\n╔════════════════════════════════════════════════════════════╗");
                DisplayService.WriteLine("║  Fragment Length Analysis                                  ║");
                DisplayService.WriteLine("╚════════════════════════════════════════════════════════════╝\n");
                
                var fragments = vectorMemory.GetAllFragments();
                var sortedFragments = fragments
                    .Select(f => new { Fragment = f, Length = (f as MemoryFragment)?.ContentLength ?? f.Content.Length })
                    .OrderByDescending(x => x.Length)
                    .ToList();
                
                // Statistics
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
                
                // Show top 10 longest fragments
                DisplayService.WriteLine("Top 10 Longest Fragments:");
                DisplayService.WriteLine("═══════════════════════════════════════════════════════════");
                foreach (var item in sortedFragments.Take(10))
                {
                    var category = item.Fragment.Category;
                    var truncatedCategory = category.Length > 50 ? category.Substring(0, 47) + "..." : category;
                    DisplayService.WriteLine($"{item.Length,5} chars - {truncatedCategory}");
                }
                DisplayService.WriteLine();
                
                // Show fragments by length bucket
                DisplayService.WriteLine("Distribution by Length:");
                DisplayService.WriteLine("═══════════════════════════════════════════════════════════");
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
                    var bar = new string('█', barWidth);
                    DisplayService.WriteLine($"{bucket.Range,12}: {bar} {count,3} ({count * 100.0 / sortedFragments.Count:F1}%)");
                }
                
                DisplayService.WriteLine();
                continue;
            }

            if (input.StartsWith("/collections", StringComparison.OrdinalIgnoreCase))
            {
                var collections = await persistenceService.GetCollectionsAsync();
                var fragmentCounts = new Dictionary<string, int>();
                foreach (var col in collections)
                {
                    var stats = await persistenceService.GetCollectionStatsAsync(col);
                    fragmentCounts[col] = stats.FragmentCount;
                }
                DisplayService.ShowCollectionsList(collections, fragmentCounts);
                continue;
            }

            if (input.StartsWith("/pool", StringComparison.OrdinalIgnoreCase))
            {
                DisplayService.WriteLine($"\n[*] Pool Status:");
                DisplayService.WriteLine($"    Available: {modelPool.AvailableCount}/{modelPool.MaxInstances}");
                DisplayService.WriteLine($"    In Use: {modelPool.MaxInstances - modelPool.AvailableCount}");
                continue;
            }

            if (input.StartsWith("/reload", StringComparison.OrdinalIgnoreCase))
            {
                DisplayService.WriteLine("\n[*] Checking for new files...");
                var newInboxFiles = await fileWatcher.DiscoverNewFilesAsync();
                
                if (newInboxFiles.Any())
                {
                    DisplayService.WriteLine($"[*] Found {newInboxFiles.Count} new file(s)!");
                    await ProcessNewFilesAsync(newInboxFiles, persistenceService, fileWatcher);
                    
                    // Reload vector memory
                    vectorMemory = await persistenceService.LoadVectorMemoryAsync(collectionName);
                    DisplayService.WriteLine($"[+] Reloaded collection with {vectorMemory.Count} fragments");
                }
                else
                {
                    DisplayService.WriteLine("[+] No new files found in inbox");
                }
                continue;
            }

            DisplayService.Write("Response: ");
            var response = await service.SendMessageAsync(input);
            DisplayService.WriteLine(response + "\n");
        }

        // Clean up the pool
        modelPool.Dispose();
        DisplayService.WriteLine("\n[+] Model pool disposed");
    }

    /// <summary>
    /// Processes new files from inbox, vectorizes them, saves to database, and archives them.
    /// </summary>
    private static async Task ProcessNewFilesAsync(
        Dictionary<string, string> newFiles,
        VectorMemoryPersistenceService persistenceService,
        KnowledgeFileWatcher fileWatcher)
    {
        var collectionName = "game-rules";
        var allFragments = new List<MemoryFragment>();

        // Process each file
        foreach (var (gameName, filePath) in newFiles)
        {
            var fragments = await fileWatcher.ProcessFileAsync(gameName, filePath);
            allFragments.AddRange(fragments);
        }

        DisplayService.ShowTotalFragmentsCollected(allFragments.Count);

        // Save to database with BERT embeddings
        DisplayService.ShowSavingToDatabaseHeader();
        await persistenceService.SaveFragmentsAsync(
            allFragments,
            collectionName,
            sourceFile: string.Join(", ", newFiles.Keys),
            replaceExisting: false); // Don't replace, append new knowledge

        // Archive processed files
        DisplayService.WriteLine("\n[*] Archiving processed files...");
        foreach (var filePath in newFiles.Values)
        {
            await fileWatcher.ArchiveFileAsync(filePath);
        }

        DisplayService.WriteLine($"\n[+] Successfully processed and archived {newFiles.Count} file(s)");
    }
}
