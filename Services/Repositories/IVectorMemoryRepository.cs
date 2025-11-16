using Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

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
    /// Switch to a different table for RAG context.
    /// </summary>
    void SetActiveTable(string tableName);
    
    /// <summary>
    /// Get the currently active table name.
    /// </summary>
    string GetActiveTable();
    
    /// <summary>
    /// Creates a new table with the same structure as MemoryFragments.
    /// This allows switching between different RAG contexts.
    /// </summary>
    Task CreateTableAsync(string tableName);
    
    /// <summary>
    /// List all tables that match the MemoryFragments schema.
    /// </summary>
    Task<List<string>> GetAllTablesAsync();
    
    /// <summary>
    /// Check if a table exists.
    /// </summary>
    Task<bool> TableExistsAsync(string tableName);
    
    /// <summary>
    /// Delete a table (use with caution!).
    /// </summary>
    Task DeleteTableAsync(string tableName);
    
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
    /// Load fragments for a collection filtered by domain IDs.
    /// This is more efficient than loading all fragments and filtering in memory.
    /// </summary>
    /// <param name="collectionName">Collection to load from</param>
    /// <param name="domainFilter">List of domain IDs to filter by (e.g., "munchkin-panic")</param>
    /// <returns>Filtered list of fragments with embeddings</returns>
    Task<List<MemoryFragmentEntity>> LoadByCollectionAndDomainsAsync(
        string collectionName, 
        List<string> domainFilter);
    
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