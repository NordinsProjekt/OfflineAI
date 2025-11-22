using Dapper;
using Entities;
using Microsoft.Data.SqlClient;
using Services.Repositories;

namespace Infrastructure.Data.Dapper;

/// <summary>
/// Dapper-based repository for LLM data.
/// Manages LLM models in SQL Server.
/// </summary>
public class LlmRepository : ILlmRepository
{
    private readonly string _connectionString;
    private const string LlmsTable = "LLMs";

    public LlmRepository(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task InitializeDatabaseAsync()
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        // Check if table exists
        var tableExists = await connection.ExecuteScalarAsync<int>(
            $"SELECT COUNT(1) FROM sys.tables WHERE name = '{LlmsTable}'") > 0;

        if (!tableExists)
        {
            // Create new table with correct schema
            var createTableSql = $@"
                CREATE TABLE [{LlmsTable}] (
                    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
                    LlmName NVARCHAR(500) NOT NULL UNIQUE,
                    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE()
                );

                CREATE INDEX IX_{LlmsTable}_LlmName ON [{LlmsTable}](LlmName);
                CREATE INDEX IX_{LlmsTable}_CreatedAt ON [{LlmsTable}](CreatedAt);";

            await connection.ExecuteAsync(createTableSql);
        }
        else
        {
            // Table exists - check if LlmName column exists
            var llmNameColumnExists = await connection.ExecuteScalarAsync<int>($@"
                SELECT COUNT(1) 
                FROM sys.columns 
                WHERE object_id = OBJECT_ID('{LlmsTable}') 
                AND name = 'LlmName'") > 0;

            if (!llmNameColumnExists)
            {
                // Check if there's an old column name (e.g., 'Name' or 'ModelName')
                var oldNameColumnExists = await connection.ExecuteScalarAsync<int>($@"
                    SELECT COUNT(1) 
                    FROM sys.columns 
                    WHERE object_id = OBJECT_ID('{LlmsTable}') 
                    AND name IN ('Name', 'ModelName', 'llmname')") > 0;

                if (oldNameColumnExists)
                {
                    // Get the old column name
                    var oldColumnName = await connection.ExecuteScalarAsync<string>($@"
                        SELECT TOP 1 name 
                        FROM sys.columns 
                        WHERE object_id = OBJECT_ID('{LlmsTable}') 
                        AND name IN ('Name', 'ModelName', 'llmname')");

                    if (!string.IsNullOrEmpty(oldColumnName))
                    {
                        // Rename the column
                        await connection.ExecuteAsync($@"
                            EXEC sp_rename '{LlmsTable}.{oldColumnName}', 'LlmName', 'COLUMN'");
                        
                        Console.WriteLine($"[MIGRATION] Renamed column '{oldColumnName}' to 'LlmName' in {LlmsTable} table");
                    }
                }
                else
                {
                    // Add the missing column
                    await connection.ExecuteAsync($@"
                        ALTER TABLE [{LlmsTable}] 
                        ADD LlmName NVARCHAR(500) NOT NULL DEFAULT ''");
                    
                    Console.WriteLine($"[MIGRATION] Added LlmName column to {LlmsTable} table");
                }

                // Ensure the unique constraint and index exist
                var indexExists = await connection.ExecuteScalarAsync<int>($@"
                    SELECT COUNT(1) 
                    FROM sys.indexes 
                    WHERE object_id = OBJECT_ID('{LlmsTable}') 
                    AND name = 'IX_{LlmsTable}_LlmName'") > 0;

                if (!indexExists)
                {
                    try
                    {
                        await connection.ExecuteAsync($@"
                            CREATE UNIQUE INDEX IX_{LlmsTable}_LlmName ON [{LlmsTable}](LlmName)");
                    }
                    catch
                    {
                        // If unique constraint fails, try non-unique index
                        await connection.ExecuteAsync($@"
                            CREATE INDEX IX_{LlmsTable}_LlmName ON [{LlmsTable}](LlmName)");
                    }
                }
            }
        }
    }

    public async Task<Guid> AddOrGetLlmAsync(string llmName)
    {
        if (string.IsNullOrWhiteSpace(llmName))
            throw new ArgumentException("LLM name cannot be empty", nameof(llmName));

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        // Check if LLM already exists
        var existingLlm = await connection.QuerySingleOrDefaultAsync<LlmEntity>(
            $"SELECT * FROM [{LlmsTable}] WHERE LlmName = @LlmName",
            new { LlmName = llmName });

        if (existingLlm != null)
        {
            return existingLlm.Id;
        }

        // Insert new LLM
        var newLlm = new LlmEntity
        {
            Id = Guid.NewGuid(),
            LlmName = llmName,
            CreatedAt = DateTime.UtcNow
        };

        await connection.ExecuteAsync(
            $@"INSERT INTO [{LlmsTable}] (Id, LlmName, CreatedAt)
               VALUES (@Id, @LlmName, @CreatedAt)",
            newLlm);

        return newLlm.Id;
    }

    public async Task<List<LlmEntity>> GetAllLlmsAsync()
    {
        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync<LlmEntity>(
            $"SELECT * FROM [{LlmsTable}] ORDER BY CreatedAt DESC");

        return results.AsList();
    }

    public async Task<LlmEntity?> GetLlmByNameAsync(string llmName)
    {
        using var connection = new SqlConnection(_connectionString);
        return await connection.QuerySingleOrDefaultAsync<LlmEntity>(
            $"SELECT * FROM [{LlmsTable}] WHERE LlmName = @LlmName",
            new { LlmName = llmName });
    }

    public async Task<LlmEntity?> GetLlmByIdAsync(Guid id)
    {
        using var connection = new SqlConnection(_connectionString);
        return await connection.QuerySingleOrDefaultAsync<LlmEntity>(
            $"SELECT * FROM [{LlmsTable}] WHERE Id = @Id",
            new { Id = id });
    }

    public async Task<bool> LlmExistsAsync(string llmName)
    {
        using var connection = new SqlConnection(_connectionString);
        var count = await connection.ExecuteScalarAsync<int>(
            $"SELECT COUNT(1) FROM [{LlmsTable}] WHERE LlmName = @LlmName",
            new { LlmName = llmName });

        return count > 0;
    }

    public async Task DeleteLlmAsync(Guid id)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(
            $"DELETE FROM [{LlmsTable}] WHERE Id = @Id",
            new { Id = id });
    }
}
