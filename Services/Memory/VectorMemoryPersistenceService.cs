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
    /// Save memory fragments with embeddings to database.
    /// This will generate embeddings if they don't exist.
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
        
        Console.WriteLine($"\n?????????????????????????????????????????????????????????????????");
        Console.WriteLine($"?  Generating Embeddings for {fragments.Count} Fragments");
        Console.WriteLine($"?????????????????????????????????????????????????????????????????\n");
        
        var startTime = DateTime.Now;
        var embeddings = new List<ReadOnlyMemory<float>>();
        
        for (int i = 0; i < fragments.Count; i++)
        {
            var fragment = fragments[i];
            
            // Calculate timing estimates
            var elapsed = (DateTime.Now - startTime).TotalSeconds;
            var avgTimePerEmbedding = i > 0 ? elapsed / i : 0;
            var remaining = fragments.Count - (i + 1);
            var estimatedTimeRemaining = avgTimePerEmbedding > 0 ? remaining * avgTimePerEmbedding : 0;
            
            // Show progress header
            Console.WriteLine($"?????????????????????????????????????????????????????????????");
            Console.WriteLine($"  Fragment {i + 1}/{fragments.Count}");
            Console.WriteLine($"  Category: {TruncateString(fragment.Category, 50)}");
            Console.WriteLine($"?????????????????????????????????????????????????????????????");
            
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
                Console.WriteLine($"  ? Remaining: ~{estimatedTimeRemaining:F0}s ({remaining} fragments)");
            }
            
            // Memory info
            var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
            var memoryMB = currentProcess.WorkingSet64 / 1024.0 / 1024.0;
            Console.WriteLine($"  ?? Memory: {memoryMB:F0} MB");
            
            Console.WriteLine();
            Console.Write($"  ?? Generating embedding... ");
            
            var embeddingStart = DateTime.Now;
            // Include both category (title) and content for better semantic matching
            var textToEmbed = $"{fragment.Category}\n\n{fragment.Content}";
            var embedding = await _embeddingService.GenerateEmbeddingAsync(textToEmbed);
            embeddings.Add(embedding);
            var embeddingTime = (DateTime.Now - embeddingStart).TotalSeconds;
            
            Console.WriteLine($"Done ({embeddingTime:F2}s)");
            Console.WriteLine();
            
            // Force garbage collection every 3 embeddings to control memory
            if ((i + 1) % 3 == 0)
            {
                Console.WriteLine($"  ?? Running garbage collection...");
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
        Console.WriteLine($"?????????????????????????????????????????????????????????????????");
        Console.WriteLine($"?  ? ALL EMBEDDINGS GENERATED");
        Console.WriteLine($"?");
        Console.WriteLine($"?  Total Time: {totalTime:F1}s");
        Console.WriteLine($"?  Average: {totalTime / fragments.Count:F2}s per embedding");
        Console.WriteLine($"?????????????????????????????????????????????????????????????????");
        Console.WriteLine();
        
        Console.WriteLine($"Creating database entities...");
        
        // Now create entities with the pre-generated embeddings
        var entities = new List<MemoryFragmentEntity>();
        for (int i = 0; i < fragments.Count; i++)
        {
            var fragment = fragments[i];
            var embedding = embeddings[i];
            
            var entity = MemoryFragmentEntity.FromMemoryFragment(
                fragment,
                embedding,
                collectionName,
                sourceFile,
                chunkIndex: i + 1);
            
            entities.Add(entity);
        }
        
        Console.WriteLine($"Saving {entities.Count} fragments to database...");
        await _repository.BulkSaveAsync(entities);
        Console.WriteLine($"? Saved {entities.Count} fragments to collection '{collectionName}'");
    }
    
    private static string TruncateString(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;
        
        return text.Substring(0, maxLength - 3) + "...";
    }
    
    /// <summary>
    /// Load a VectorMemory from database with pre-computed embeddings.
    /// This is much faster than regenerating embeddings.
    /// </summary>
    public async Task<VectorMemory> LoadVectorMemoryAsync(string collectionName)
    {
        Console.WriteLine($"Loading collection '{collectionName}' from database...");
        
        var entities = await _repository.LoadByCollectionAsync(collectionName);
        
        if (entities.Count == 0)
        {
            throw new InvalidOperationException(
                $"Collection '{collectionName}' not found or empty in database.");
        }
        
        var vectorMemory = new VectorMemory(_embeddingService, collectionName);
        
        Console.WriteLine($"Importing {entities.Count} fragments with pre-computed embeddings...");
        
        foreach (var entity in entities)
        {
            // Import the fragment
            var fragment = entity.ToMemoryFragment();
            vectorMemory.ImportMemory(fragment);
            
            // Set the pre-computed embedding
            if (entity.Embedding != null)
            {
                var embedding = entity.GetEmbeddingAsMemory();
                vectorMemory.SetEmbeddingForLastFragment(embedding);
            }
        }
        
        Console.WriteLine($"? Loaded {entities.Count} fragments from collection '{collectionName}'");
        
        return vectorMemory;
    }
    
    /// <summary>
    /// Load fragments with pagination for large collections.
    /// </summary>
    public async Task<VectorMemory> LoadVectorMemoryPagedAsync(
        string collectionName, 
        int pageNumber = 1, 
        int pageSize = 100)
    {
        var entities = await _repository.LoadByCollectionPagedAsync(collectionName, pageNumber, pageSize);
        
        var vectorMemory = new VectorMemory(_embeddingService, collectionName);
        
        foreach (var entity in entities)
        {
            var fragment = entity.ToMemoryFragment();
            vectorMemory.ImportMemory(fragment);
            
            if (entity.Embedding != null)
            {
                var embedding = entity.GetEmbeddingAsMemory();
                vectorMemory.SetEmbeddingForLastFragment(embedding);
            }
        }
        
        return vectorMemory;
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
