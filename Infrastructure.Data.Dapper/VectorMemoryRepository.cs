using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Entities;
using Microsoft.Data.SqlClient;
using Services.Repositories;

namespace Infrastructure.Data.Dapper;

/// <summary>
/// Repository for managing MemoryFragments in MSSQL database using Dapper.
/// Handles CRUD operations and bulk inserts for vector embeddings.
/// Supports dynamic table switching for different RAG contexts.
/// </summary>
public class VectorMemoryRepository : IVectorMemoryRepository
{
    private readonly string _connectionString;
    private string _tableName;
    
    public VectorMemoryRepository(string connectionString, string tableName = "MemoryFragments")
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _tableName = tableName ?? "MemoryFragments";
    }
    
    /// <summary>
    /// Switch to a different table for RAG context.
    /// </summary>
    public void SetActiveTable(string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Table name cannot be null or empty", nameof(tableName));
        
        _tableName = tableName;
    }
    
    /// <summary>
    /// Get the currently active table name.
    /// </summary>
    public string GetActiveTable() => _tableName;
    
    /// <summary>
    /// Creates a new table with the same structure as MemoryFragments.
    /// This allows switching between different RAG contexts.
    /// </summary>
    public async Task CreateTableAsync(string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Table name cannot be null or empty", nameof(tableName));
        
        // Validate table name to prevent SQL injection
        if (!IsValidTableName(tableName))
            throw new ArgumentException("Invalid table name. Use only alphanumeric characters and underscores.", nameof(tableName));
        
        var createTableSql = $@"
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = '{tableName}')
            BEGIN
                CREATE TABLE [{tableName}] (
                    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
                    CollectionName NVARCHAR(255) NOT NULL,
                    Category NVARCHAR(500) NOT NULL,
                    Content NVARCHAR(MAX) NOT NULL,
                    ContentLength INT NOT NULL DEFAULT 0,
                    Embedding VARBINARY(MAX) NULL,
                    EmbeddingDimension INT NULL,
                    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                    UpdatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                    SourceFile NVARCHAR(1000) NULL,
                    ChunkIndex INT NULL
                );

                CREATE INDEX IX_{tableName}_CollectionName ON [{tableName}](CollectionName);
                CREATE INDEX IX_{tableName}_Category ON [{tableName}](Category);
                CREATE INDEX IX_{tableName}_CreatedAt ON [{tableName}](CreatedAt);
                CREATE INDEX IX_{tableName}_ContentLength ON [{tableName}](ContentLength);
            END";
        
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(createTableSql);
    }
    
    /// <summary>
    /// List all tables that match the MemoryFragments schema.
    /// </summary>
    public async Task<List<string>> GetAllTablesAsync()
    {
        const string sql = @"
            SELECT DISTINCT t.name
            FROM sys.tables t
            INNER JOIN sys.columns c1 ON t.object_id = c1.object_id AND c1.name = 'Embedding'
            INNER JOIN sys.columns c2 ON t.object_id = c2.object_id AND c2.name = 'CollectionName'
            INNER JOIN sys.columns c3 ON t.object_id = c3.object_id AND c3.name = 'Content'
            ORDER BY t.name";
        
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        var results = await connection.QueryAsync<string>(sql);
        return results.AsList();
    }
    
    /// <summary>
    /// Check if a table exists.
    /// </summary>
    public async Task<bool> TableExistsAsync(string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            return false;
        
        if (!IsValidTableName(tableName))
            return false;
        
        const string sql = @"
            SELECT COUNT(1) 
            FROM sys.tables 
            WHERE name = @TableName";
        
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        var count = await connection.ExecuteScalarAsync<int>(sql, new { TableName = tableName });
        return count > 0;
    }
    
    /// <summary>
    /// Delete a table (use with caution!).
    /// </summary>
    public async Task DeleteTableAsync(string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Table name cannot be null or empty", nameof(tableName));
        
        if (!IsValidTableName(tableName))
            throw new ArgumentException("Invalid table name. Use only alphanumeric characters and underscores.", nameof(tableName));
        
        // Prevent deleting the default table
        if (tableName.Equals("MemoryFragments", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot delete the default MemoryFragments table.");
        
        var dropTableSql = $@"
            IF EXISTS (SELECT * FROM sys.tables WHERE name = '{tableName}')
            BEGIN
                DROP TABLE [{tableName}]
            END";
        
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(dropTableSql);
    }
    
    /// <summary>
    /// Validates table name to prevent SQL injection.
    /// </summary>
    private static bool IsValidTableName(string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            return false;
        
        // Allow only alphanumeric characters, underscores, and must start with letter or underscore
        return System.Text.RegularExpressions.Regex.IsMatch(tableName, @"^[a-zA-Z_][a-zA-Z0-9_]*$");
    }
    
    /// <summary>
    /// Initialize database schema. Creates database if it doesn't exist, then creates tables.
    /// Call this once during setup.
    /// </summary>
    public async Task InitializeDatabaseAsync()
    {
        // Step 1: Ensure the database exists
        await EnsureDatabaseExistsAsync();
        
        // Step 2: Create tables if they don't exist
        var createTableSql = $@"
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = '{_tableName}')
            BEGIN
                CREATE TABLE [{_tableName}] (
                    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
                    CollectionName NVARCHAR(255) NOT NULL,
                    Category NVARCHAR(500) NOT NULL,
                    Content NVARCHAR(MAX) NOT NULL,
                    ContentLength INT NOT NULL DEFAULT 0,
                    Embedding VARBINARY(MAX) NULL,
                    EmbeddingDimension INT NULL,
                    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                    UpdatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                    SourceFile NVARCHAR(1000) NULL,
                    ChunkIndex INT NULL
                );

                CREATE INDEX IX_{_tableName}_CollectionName ON [{_tableName}](CollectionName);
                CREATE INDEX IX_{_tableName}_Category ON [{_tableName}](Category);
                CREATE INDEX IX_{_tableName}_CreatedAt ON [{_tableName}](CreatedAt);
                CREATE INDEX IX_{_tableName}_ContentLength ON [{_tableName}](ContentLength);
            END
            ELSE
            BEGIN
                -- Add ContentLength column if it doesn't exist (migration for existing databases)
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('{_tableName}') AND name = 'ContentLength')
                BEGIN
                    ALTER TABLE [{_tableName}] ADD ContentLength INT NOT NULL DEFAULT 0;
                    
                    -- Update existing rows with calculated length
                    UPDATE [{_tableName}] SET ContentLength = LEN(Content);
                    
                    -- Create index on the new column
                    CREATE INDEX IX_{_tableName}_ContentLength ON [{_tableName}](ContentLength);
                END;
            END";
        
        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(createTableSql);
    }
    
    /// <summary>
    /// Ensures the database exists. Creates it if it doesn't and grants access to current user.
    /// </summary>
    private async Task EnsureDatabaseExistsAsync()
    {
        // Parse database name from connection string
        var builder = new SqlConnectionStringBuilder(_connectionString);
        var databaseName = builder.InitialCatalog;
        
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new InvalidOperationException("Connection string must specify a database name (Initial Catalog or Database).");
        }
        
        // Connect to master database to check if our database exists
        builder.InitialCatalog = "master";
        var masterConnectionString = builder.ConnectionString;
        
        const string checkDatabaseSql = @"
            SELECT COUNT(1) 
            FROM sys.databases 
            WHERE name = @DatabaseName";
        
        const string createDatabaseSql = @"
            CREATE DATABASE [{0}]";
        
        // SQL to grant access to current Windows user
        const string grantAccessSql = @"
            USE [{0}];
            IF NOT EXISTS (SELECT * FROM sys.database_principals WHERE name = CURRENT_USER)
            BEGIN
                CREATE USER [CURRENT_USER] FOR LOGIN [CURRENT_USER];
            END;
            ALTER ROLE db_owner ADD MEMBER [CURRENT_USER];";
        
        using var connection = new SqlConnection(masterConnectionString);
        await connection.OpenAsync();
        
        var exists = await connection.ExecuteScalarAsync<int>(checkDatabaseSql, new { DatabaseName = databaseName });
        
        if (exists == 0)
        {
            Console.WriteLine($"[*] Database '{databaseName}' does not exist. Creating it...");
            
            // Create the database
            await connection.ExecuteAsync(string.Format(createDatabaseSql, databaseName));
            
            // Grant access to current user
            try
            {
                // Get current Windows user
                var currentUser = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
                
                var grantSql = $@"
                    USE [{databaseName}];
                    IF NOT EXISTS (SELECT * FROM sys.database_principals WHERE name = '{currentUser}')
                    BEGIN
                        CREATE USER [{currentUser}] FOR LOGIN [{currentUser}];
                    END;
                    ALTER ROLE db_owner ADD MEMBER [{currentUser}];";
                
                await connection.ExecuteAsync(grantSql);
                Console.WriteLine($"[+] Granted access to user '{currentUser}'");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[!] Warning: Could not grant explicit permissions: {ex.Message}");
                Console.WriteLine($"[*] The database was created and should use Windows Authentication by default.");
            }
            
            Console.WriteLine($"[+] Database '{databaseName}' created successfully!");
        }
    }
    
    /// <summary>
    /// Save a single memory fragment with its vector embedding.
    /// </summary>
    public async Task<Guid> SaveAsync(MemoryFragmentEntity entity)
    {
        var sql = $@"
            INSERT INTO [{_tableName}] 
                (Id, CollectionName, Category, Content, ContentLength, Embedding, EmbeddingDimension, 
                 CreatedAt, UpdatedAt, SourceFile, ChunkIndex)
            VALUES 
                (@Id, @CollectionName, @Category, @Content, @ContentLength, @Embedding, @EmbeddingDimension,
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
        var sql = $@"
            INSERT INTO [{_tableName}] 
                (Id, CollectionName, Category, Content, ContentLength, Embedding, EmbeddingDimension, 
                 CreatedAt, UpdatedAt, SourceFile, ChunkIndex)
            VALUES 
                (@Id, @CollectionName, @Category, @Content, @ContentLength, @Embedding, @EmbeddingDimension,
                 @CreatedAt, @UpdatedAt, @SourceFile, @ChunkIndex)";
        
        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, entities);
    }
    
    /// <summary>
    /// Load all fragments for a collection (including embeddings).
    /// </summary>
    public async Task<List<MemoryFragmentEntity>> LoadByCollectionAsync(string collectionName)
    {
        var sql = $@"
            SELECT Id, CollectionName, Category, Content, ContentLength, Embedding, EmbeddingDimension,
                   CreatedAt, UpdatedAt, SourceFile, ChunkIndex
            FROM [{_tableName}]
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
        var sql = $@"
            SELECT Id, CollectionName, Category, Content, ContentLength, Embedding, EmbeddingDimension,
                   CreatedAt, UpdatedAt, SourceFile, ChunkIndex
            FROM [{_tableName}]
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
        var sql = $@"
            SELECT COUNT(1) 
            FROM [{_tableName}] 
            WHERE CollectionName = @CollectionName";
        
        using var connection = new SqlConnection(_connectionString);
        return await connection.ExecuteScalarAsync<int>(sql, new { CollectionName = collectionName });
    }
    
    /// <summary>
    /// Check if embeddings exist for a collection.
    /// </summary>
    public async Task<bool> HasEmbeddingsAsync(string collectionName)
    {
        var sql = $@"
            SELECT COUNT(1) 
            FROM [{_tableName}] 
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
        var sql = $@"
            SELECT DISTINCT CollectionName 
            FROM [{_tableName}] 
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
        var sql = $"DELETE FROM [{_tableName}] WHERE CollectionName = @CollectionName";
        
        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, new { CollectionName = collectionName });
    }
    
    /// <summary>
    /// Delete a specific fragment by ID.
    /// </summary>
    public async Task DeleteAsync(Guid id)
    {
        var sql = $"DELETE FROM [{_tableName}] WHERE Id = @Id";
        
        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, new { Id = id });
    }
    
    /// <summary>
    /// Update fragment content (preserves embedding).
    /// </summary>
    public async Task UpdateContentAsync(Guid id, string content)
    {
        var sql = $@"
            UPDATE [{_tableName}] 
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
        var sql = $@"
            SELECT COUNT(1) 
            FROM [{_tableName}] 
            WHERE CollectionName = @CollectionName";
        
        using var connection = new SqlConnection(_connectionString);
        var count = await connection.ExecuteScalarAsync<int>(sql, new { CollectionName = collectionName });
        
        return count > 0;
    }
    
    /// <summary>
    /// Get content length statistics for a collection.
    /// </summary>
    public async Task<ContentLengthStats> GetContentLengthStatsAsync(string collectionName)
    {
        var sql = $@"
            SELECT 
                COUNT(*) as TotalFragments,
                AVG(CAST(ContentLength AS FLOAT)) as AverageLength,
                MIN(ContentLength) as MinLength,
                MAX(ContentLength) as MaxLength,
                SUM(CASE WHEN ContentLength > 1000 THEN 1 ELSE 0 END) as LongFragments,
                SUM(CASE WHEN ContentLength < 200 THEN 1 ELSE 0 END) as ShortFragments
            FROM [{_tableName}]
            WHERE CollectionName = @CollectionName";
        
        using var connection = new SqlConnection(_connectionString);
        var stats = await connection.QuerySingleOrDefaultAsync<ContentLengthStats>(
            sql, 
            new { CollectionName = collectionName });
        
        return stats ?? new ContentLengthStats();
    }
    
    /// <summary>
    /// Get fragments grouped by length buckets.
    /// </summary>
    public async Task<Dictionary<string, int>> GetLengthDistributionAsync(string collectionName)
    {
        var sql = $@"
            SELECT 
                CASE 
                    WHEN ContentLength <= 200 THEN '0-200'
                    WHEN ContentLength <= 500 THEN '201-500'
                    WHEN ContentLength <= 1000 THEN '501-1000'
                    WHEN ContentLength <= 1500 THEN '1001-1500'
                    ELSE '1500+'
                END as LengthBucket,
                COUNT(*) as Count
            FROM [{_tableName}]
            WHERE CollectionName = @CollectionName
            GROUP BY 
                CASE 
                    WHEN ContentLength <= 200 THEN '0-200'
                    WHEN ContentLength <= 500 THEN '201-500'
                    WHEN ContentLength <= 1000 THEN '501-1000'
                    WHEN ContentLength <= 1500 THEN '1001-1500'
                    ELSE '1500+'
                END
            ORDER BY LengthBucket";
        
        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync<(string LengthBucket, int Count)>(
            sql, 
            new { CollectionName = collectionName });
        
        return results.ToDictionary(r => r.LengthBucket, r => r.Count);
    }
}
