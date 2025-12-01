using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Entities;
using Microsoft.SemanticKernel.Embeddings;
using Services.Repositories;

namespace Services.Memory;

/// <summary>
/// Service for persisting and loading VectorMemory to/from MSSQL database.
/// Handles the conversion between in-memory VectorMemory and database storage.
/// Generates multiple embeddings (category, content, combined) for improved semantic matching.
/// </summary>
public class VectorMemoryPersistenceService
{
    private readonly IVectorMemoryRepository _repository;
    private readonly ITextEmbeddingGenerationService _embeddingService;
    
    public VectorMemoryPersistenceService(
        IVectorMemoryRepository repository,
        ITextEmbeddingGenerationService embeddingService)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
    }
    
    /// <summary>
    /// Initialize the database schema. Call this once during application setup.
    /// </summary>
    public async Task InitializeDatabaseAsync()
    {
        await _repository.InitializeDatabaseAsync();
    }
    
    /// <summary>
    /// Save memory fragments with MULTIPLE embeddings to database for improved matching.
    /// Generates 3 embeddings per fragment:
    /// 1. Category-only (without ## markers)
    /// 2. Content-only
    /// 3. Combined (category + content)
    /// </summary>
    public async Task SaveFragmentsAsync(
        List<MemoryFragment> fragments,
        string collectionName,
        string? sourceFile = null,
        bool replaceExisting = false)
    {
        if (replaceExisting && await _repository.CollectionExistsAsync(collectionName))
        {
            await _repository.DeleteCollectionAsync(collectionName);
        }
        
        Console.WriteLine($"\n???????????????????????????????????????????????????????");
        Console.WriteLine($"?  Generating WEIGHTED Embeddings for {fragments.Count} Fragments");
        Console.WriteLine($"?  Strategy: Category (40%) + Content (30%) + Combined (30%)");
        Console.WriteLine($"???????????????????????????????????????????????????????\n");
        
        var startTime = DateTime.Now;
        var categoryEmbeddings = new List<ReadOnlyMemory<float>>();
        var contentEmbeddings = new List<ReadOnlyMemory<float>>();
        var combinedEmbeddings = new List<ReadOnlyMemory<float>>();
        
        for (int i = 0; i < fragments.Count; i++)
        {
            var fragment = fragments[i];
            
            // Calculate timing estimates
            var elapsed = (DateTime.Now - startTime).TotalSeconds;
            var embeddingsGenerated = i * 3; // 3 embeddings per fragment
            var avgTimePerEmbedding = embeddingsGenerated > 0 ? elapsed / embeddingsGenerated : 0;
            var remainingEmbeddings = (fragments.Count - (i + 1)) * 3;
            var estimatedTimeRemaining = avgTimePerEmbedding > 0 ? remainingEmbeddings * avgTimePerEmbedding : 0;
            
            // Show progress header
            Console.WriteLine($"???????????????????????????????????????????????????????");
            Console.WriteLine($"  Fragment {i + 1}/{fragments.Count}");
            Console.WriteLine($"  Category: {TruncateString(fragment.Category, 50)}");
            Console.WriteLine($"???????????????????????????????????????????????????????");
            
            // Progress bar
            var progressPercent = ((i + 1) * 100.0) / fragments.Count;
            var barWidth = 50;
            var filledWidth = (int)((progressPercent / 100.0) * barWidth);
            var emptyWidth = barWidth - filledWidth;
            
            Console.Write($"  Progress: [");
            Console.Write(new string('?', filledWidth));
            Console.Write(new string('?', emptyWidth));
            Console.WriteLine($"] {progressPercent:F1}%");
            
            // Timing information
            Console.WriteLine();
            Console.WriteLine($"  ??  Elapsed: {elapsed:F1}s");
            if (avgTimePerEmbedding > 0)
            {
                Console.WriteLine($"  ? Avg Time: {avgTimePerEmbedding:F2}s per embedding");
                Console.WriteLine($"  ? Remaining: ~{estimatedTimeRemaining:F0}s ({remainingEmbeddings} embeddings)");
            }
            
            // Memory info
            var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
            var memoryMB = currentProcess.WorkingSet64 / 1024.0 / 1024.0;
            Console.WriteLine($"  ?? Memory: {memoryMB:F0} MB");
            Console.WriteLine();
            
            // Generate 3 embeddings per fragment
            // 1. Category-only embedding (without ## markers for better semantic matching)
            var cleanCategory = fragment.Category.Replace("##", "").Trim();
            Console.Write($"  ?? [1/3] Category embedding... ");
            var categoryStart = DateTime.Now;
            var categoryEmbedding = await _embeddingService.GenerateEmbeddingAsync(cleanCategory);
            categoryEmbeddings.Add(categoryEmbedding);
            Console.WriteLine($"Done ({(DateTime.Now - categoryStart).TotalSeconds:F2}s)");
            
            // 2. Content-only embedding
            Console.Write($"  ?? [2/3] Content embedding... ");
            var contentStart = DateTime.Now;
            var contentEmbedding = await _embeddingService.GenerateEmbeddingAsync(fragment.Content);
            contentEmbeddings.Add(contentEmbedding);
            Console.WriteLine($"Done ({(DateTime.Now - contentStart).TotalSeconds:F2}s)");
            
            // 3. Combined embedding (category + content for balance)
            Console.Write($"  ?? [3/3] Combined embedding... ");
            var combinedStart = DateTime.Now;
            var combinedText = $"{cleanCategory}\n\n{fragment.Content}";
            var combinedEmbedding = await _embeddingService.GenerateEmbeddingAsync(combinedText);
            combinedEmbeddings.Add(combinedEmbedding);
            Console.WriteLine($"Done ({(DateTime.Now - combinedStart).TotalSeconds:F2}s)");
            
            Console.WriteLine();
            
            // Force garbage collection every 2 fragments to control memory (6 embeddings total)
            if ((i + 1) % 2 == 0)
            {
                Console.WriteLine($"  ??? Running garbage collection...");
                GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                
                currentProcess.Refresh();
                memoryMB = currentProcess.WorkingSet64 / 1024.0 / 1024.0;
                Console.WriteLine($"  ? After GC: {memoryMB:F0} MB");
                Console.WriteLine();
            }
        }
        
        var totalTime = (DateTime.Now - startTime).TotalSeconds;
        var totalEmbeddings = fragments.Count * 3;
        Console.WriteLine($"???????????????????????????????????????????????????????");
        Console.WriteLine($"?  ? ALL EMBEDDINGS GENERATED");
        Console.WriteLine($"?");
        Console.WriteLine($"?  Total Embeddings: {totalEmbeddings} ({fragments.Count} × 3)");
        Console.WriteLine($"?  Total Time: {totalTime:F1}s");
        Console.WriteLine($"?  Average: {totalTime / totalEmbeddings:F2}s per embedding");
        Console.WriteLine($"???????????????????????????????????????????????????????");
        Console.WriteLine();
        
        Console.WriteLine($"Creating database entities with weighted embeddings...");
        
        // Create entities with all three embeddings
        var entities = new List<MemoryFragmentEntity>();
        for (int i = 0; i < fragments.Count; i++)
        {
            var fragment = fragments[i];
            
            var entity = MemoryFragmentEntity.FromMemoryFragment(
                fragment,
                combinedEmbeddings[i],      // Primary embedding (combined)
                categoryEmbeddings[i],       // Category-only for domain matching
                contentEmbeddings[i],        // Content-only for detail matching
                collectionName,
                sourceFile,
                chunkIndex: i + 1);
            
            entities.Add(entity);
        }
        
        Console.WriteLine($"Saving {entities.Count} fragments with weighted embeddings to database...");
        await _repository.BulkSaveAsync(entities);
        Console.WriteLine($"? Saved {entities.Count} fragments with 3 embeddings each to collection '{collectionName}'");
        Console.WriteLine($"   - Category embeddings: {entities.Count}");
        Console.WriteLine($"   - Content embeddings: {entities.Count}");
        Console.WriteLine($"   - Combined embeddings: {entities.Count}");
        Console.WriteLine($"   Total storage: {entities.Count * 3} embeddings");
    }
    
    private static string TruncateString(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;
        
        return text.Substring(0, maxLength - 3) + "...";
    }
    
    /// <summary>
    /// Check if a collection exists in the database.
    /// </summary>
    public async Task<bool> CollectionExistsAsync(string collectionName)
    {
        return await _repository.CollectionExistsAsync(collectionName);
    }
    
    /// <summary>
    /// Get all available collection names.
    /// </summary>
    public async Task<List<string>> GetCollectionsAsync()
    {
        return await _repository.GetCollectionsAsync();
    }
    
    /// <summary>
    /// Delete a collection from the database.
    /// </summary>
    public async Task DeleteCollectionAsync(string collectionName)
    {
        await _repository.DeleteCollectionAsync(collectionName);
        Console.WriteLine($"? Deleted collection '{collectionName}'");
    }
    
    /// <summary>
    /// Get statistics about a collection.
    /// </summary>
    public async Task<CollectionStats> GetCollectionStatsAsync(string collectionName)
    {
        var count = await _repository.GetCountAsync(collectionName);
        var hasEmbeddings = await _repository.HasEmbeddingsAsync(collectionName);
        
        return new CollectionStats
        {
            CollectionName = collectionName,
            FragmentCount = count,
            HasEmbeddings = hasEmbeddings
        };
    }
}

/// <summary>
/// Statistics about a collection.
/// </summary>
public record CollectionStats
{
    public string CollectionName { get; init; } = string.Empty;
    public int FragmentCount { get; init; }
    public bool HasEmbeddings { get; init; }
}
