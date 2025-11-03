using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;
using Services.Models;

namespace Services.Repositories;

/// <summary>
/// Repository for managing MemoryFragments in MSSQL database using Dapper.
/// Handles CRUD operations and bulk inserts for vector embeddings.
/// </summary>
public class VectorMemoryRepository : IVectorMemoryRepository
{
    private readonly string _connectionString;
    
    public VectorMemoryRepository(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }
    
    /// <summary>
    /// Initialize database schema. Call this once during setup.
    /// </summary>
    public async Task InitializeDatabaseAsync()
    {
        const string createTableSql = @"
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'MemoryFragments')
            BEGIN
                CREATE TABLE MemoryFragments (
                    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
                    CollectionName NVARCHAR(255) NOT NULL,
                    Category NVARCHAR(500) NOT NULL,
                    Content NVARCHAR(MAX) NOT NULL,
                    Embedding VARBINARY(MAX) NULL,
                    EmbeddingDimension INT NULL,
                    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                    UpdatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                    SourceFile NVARCHAR(1000) NULL,
                    ChunkIndex INT NULL
                );

                CREATE INDEX IX_MemoryFragments_CollectionName ON MemoryFragments(CollectionName);
                CREATE INDEX IX_MemoryFragments_Category ON MemoryFragments(Category);
                CREATE INDEX IX_MemoryFragments_CreatedAt ON MemoryFragments(CreatedAt);
            END";
        
        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(createTableSql);
    }
    
    /// <summary>
    /// Save a single memory fragment with its vector embedding.
    /// </summary>
    public async Task<Guid> SaveAsync(MemoryFragmentEntity entity)
    {
        const string sql = @"
            INSERT INTO MemoryFragments 
                (Id, CollectionName, Category, Content, Embedding, EmbeddingDimension, 
                 CreatedAt, UpdatedAt, SourceFile, ChunkIndex)
            VALUES 
                (@Id, @CollectionName, @Category, @Content, @Embedding, @EmbeddingDimension,
                 @CreatedAt, @UpdatedAt, @SourceFile, @ChunkIndex)";
        
        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, entity);
        
        return entity.Id;
    }
    
    /// <summary>
    /// Bulk insert for performance when loading many fragments.
    /// Significantly faster than individual inserts.
    /// </summary>
    public async Task BulkSaveAsync(IEnumerable<MemoryFragmentEntity> entities)
    {
        const string sql = @"
            INSERT INTO MemoryFragments 
                (Id, CollectionName, Category, Content, Embedding, EmbeddingDimension, 
                 CreatedAt, UpdatedAt, SourceFile, ChunkIndex)
            VALUES 
                (@Id, @CollectionName, @Category, @Content, @Embedding, @EmbeddingDimension,
                 @CreatedAt, @UpdatedAt, @SourceFile, @ChunkIndex)";
        
        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, entities);
    }
    
    /// <summary>
    /// Load all fragments for a collection (including embeddings).
    /// </summary>
    public async Task<List<MemoryFragmentEntity>> LoadByCollectionAsync(string collectionName)
    {
        const string sql = @"
            SELECT Id, CollectionName, Category, Content, Embedding, EmbeddingDimension,
                   CreatedAt, UpdatedAt, SourceFile, ChunkIndex
            FROM MemoryFragments
            WHERE CollectionName = @CollectionName
            ORDER BY ChunkIndex, CreatedAt";
        
        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync<MemoryFragmentEntity>(sql, new { CollectionName = collectionName });
        
        return results.AsList();
    }
    
    /// <summary>
    /// Load fragments with pagination support.
    /// </summary>
    public async Task<List<MemoryFragmentEntity>> LoadByCollectionPagedAsync(
        string collectionName, 
        int pageNumber, 
        int pageSize)
    {
        const string sql = @"
            SELECT Id, CollectionName, Category, Content, Embedding, EmbeddingDimension,
                   CreatedAt, UpdatedAt, SourceFile, ChunkIndex
            FROM MemoryFragments
            WHERE CollectionName = @CollectionName
            ORDER BY ChunkIndex, CreatedAt
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";
        
        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync<MemoryFragmentEntity>(
            sql, 
            new { 
                CollectionName = collectionName, 
                Offset = (pageNumber - 1) * pageSize, 
                PageSize = pageSize 
            });
        
        return results.AsList();
    }
    
    /// <summary>
    /// Get count of fragments in a collection.
    /// </summary>
    public async Task<int> GetCountAsync(string collectionName)
    {
        const string sql = @"
            SELECT COUNT(1) 
            FROM MemoryFragments 
            WHERE CollectionName = @CollectionName";
        
        using var connection = new SqlConnection(_connectionString);
        return await connection.ExecuteScalarAsync<int>(sql, new { CollectionName = collectionName });
    }
    
    /// <summary>
    /// Check if embeddings exist for a collection.
    /// </summary>
    public async Task<bool> HasEmbeddingsAsync(string collectionName)
    {
        const string sql = @"
            SELECT COUNT(1) 
            FROM MemoryFragments 
            WHERE CollectionName = @CollectionName AND Embedding IS NOT NULL";
        
        using var connection = new SqlConnection(_connectionString);
        var count = await connection.ExecuteScalarAsync<int>(sql, new { CollectionName = collectionName });
        
        return count > 0;
    }
    
    /// <summary>
    /// Get all unique collection names.
    /// </summary>
    public async Task<List<string>> GetCollectionsAsync()
    {
        const string sql = @"
            SELECT DISTINCT CollectionName 
            FROM MemoryFragments 
            ORDER BY CollectionName";
        
        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync<string>(sql);
        
        return results.AsList();
    }
    
    /// <summary>
    /// Delete all fragments in a collection.
    /// </summary>
    public async Task DeleteCollectionAsync(string collectionName)
    {
        const string sql = "DELETE FROM MemoryFragments WHERE CollectionName = @CollectionName";
        
        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, new { CollectionName = collectionName });
    }
    
    /// <summary>
    /// Delete a specific fragment by ID.
    /// </summary>
    public async Task DeleteAsync(Guid id)
    {
        const string sql = "DELETE FROM MemoryFragments WHERE Id = @Id";
        
        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, new { Id = id });
    }
    
    /// <summary>
    /// Update fragment content (preserves embedding).
    /// </summary>
    public async Task UpdateContentAsync(Guid id, string content)
    {
        const string sql = @"
            UPDATE MemoryFragments 
            SET Content = @Content, UpdatedAt = GETUTCDATE() 
            WHERE Id = @Id";
        
        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, new { Id = id, Content = content });
    }
    
    /// <summary>
    /// Check if a collection exists.
    /// </summary>
    public async Task<bool> CollectionExistsAsync(string collectionName)
    {
        const string sql = @"
            SELECT COUNT(1) 
            FROM MemoryFragments 
            WHERE CollectionName = @CollectionName";
        
        using var connection = new SqlConnection(_connectionString);
        var count = await connection.ExecuteScalarAsync<int>(sql, new { CollectionName = collectionName });
        
        return count > 0;
    }
}
