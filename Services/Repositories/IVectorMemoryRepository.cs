using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Entities;


namespace Services.Repositories;

/// <summary>
/// Interface for vector memory repository implementations.
/// Supports both Dapper (SQL) and EF Core implementations.
/// </summary>
public interface IVectorMemoryRepository
{
    /// <summary>
    /// Initialize database schema. Call this once during setup.
    /// </summary>
    Task InitializeDatabaseAsync();
    
    /// <summary>
    /// Save a single memory fragment with its vector embedding.
    /// </summary>
    Task<Guid> SaveAsync(MemoryFragmentEntity entity);
    
    /// <summary>
    /// Bulk insert for performance when loading many fragments.
    /// Significantly faster than individual inserts.
    /// </summary>
    Task BulkSaveAsync(IEnumerable<MemoryFragmentEntity> entities);
    
    /// <summary>
    /// Load all fragments for a collection (including embeddings).
    /// </summary>
    Task<List<MemoryFragmentEntity>> LoadByCollectionAsync(string collectionName);
    
    /// <summary>
    /// Load fragments with pagination support.
    /// </summary>
    Task<List<MemoryFragmentEntity>> LoadByCollectionPagedAsync(
        string collectionName, 
        int pageNumber, 
        int pageSize);
    
    /// <summary>
    /// Get count of fragments in a collection.
    /// </summary>
    Task<int> GetCountAsync(string collectionName);
    
    /// <summary>
    /// Check if embeddings exist for a collection.
    /// </summary>
    Task<bool> HasEmbeddingsAsync(string collectionName);
    
    /// <summary>
    /// Get all unique collection names.
    /// </summary>
    Task<List<string>> GetCollectionsAsync();
    
    /// <summary>
    /// Delete all fragments in a collection.
    /// </summary>
    Task DeleteCollectionAsync(string collectionName);
    
    /// <summary>
    /// Delete a specific fragment by ID.
    /// </summary>
    Task DeleteAsync(Guid id);
    
    /// <summary>
    /// Update fragment content (preserves embedding).
    /// </summary>
    Task UpdateContentAsync(Guid id, string content);
    
    /// <summary>
    /// Check if a collection exists.
    /// </summary>
    Task<bool> CollectionExistsAsync(string collectionName);
    
    /// <summary>
    /// Get content length statistics for a collection.
    /// </summary>
    Task<ContentLengthStats> GetContentLengthStatsAsync(string collectionName);
    
    /// <summary>
    /// Get fragments grouped by length buckets.
    /// </summary>
    Task<Dictionary<string, int>> GetLengthDistributionAsync(string collectionName);
}