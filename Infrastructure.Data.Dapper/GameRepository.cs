using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Entities;
using Microsoft.Data.SqlClient;
using Services.Repositories;

namespace Infrastructure.Data.Dapper;

/// <summary>
/// Dapper-based repository for game detection data.
/// Manages games and their variants in SQL Server.
/// </summary>
public class GameRepository : IGameRepository
{
    private readonly string _connectionString;
    private const string GamesTable = "Games";
    private const string VariantsTable = "GameVariants";

    public GameRepository(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task InitializeDatabaseAsync()
    {
        var createTablesSql = $@"
            -- Create Games table
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = '{GamesTable}')
            BEGIN
                CREATE TABLE [{GamesTable}] (
                    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
                    GameId NVARCHAR(255) NOT NULL UNIQUE,
                    DisplayName NVARCHAR(500) NOT NULL,
                    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                    UpdatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                    Source NVARCHAR(100) NOT NULL DEFAULT 'manual'
                );

                CREATE INDEX IX_{GamesTable}_GameId ON [{GamesTable}](GameId);
                CREATE INDEX IX_{GamesTable}_CreatedAt ON [{GamesTable}](CreatedAt);
            END

            -- Create GameVariants table
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = '{VariantsTable}')
            BEGIN
                CREATE TABLE [{VariantsTable}] (
                    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
                    GameId UNIQUEIDENTIFIER NOT NULL,
                    VariantText NVARCHAR(500) NOT NULL,
                    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                    FOREIGN KEY (GameId) REFERENCES [{GamesTable}](Id) ON DELETE CASCADE
                );

                CREATE INDEX IX_{VariantsTable}_GameId ON [{VariantsTable}](GameId);
                CREATE INDEX IX_{VariantsTable}_VariantText ON [{VariantsTable}](VariantText);
            END";

        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(createTablesSql);
    }

    public async Task<Guid> RegisterGameAsync(string gameId, string displayName, string[] variants, string source = "manual")
    {
        if (string.IsNullOrWhiteSpace(gameId))
            throw new ArgumentException("Game ID cannot be empty", nameof(gameId));
        
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Display name cannot be empty", nameof(displayName));

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            // Check if game already exists
            var existingGame = await connection.QuerySingleOrDefaultAsync<GameEntity>(
                $"SELECT * FROM [{GamesTable}] WHERE GameId = @GameId",
                new { GameId = gameId },
                transaction);

            Guid entityId;

            if (existingGame != null)
            {
                // Update existing game
                await connection.ExecuteAsync(
                    $@"UPDATE [{GamesTable}] 
                       SET DisplayName = @DisplayName, 
                           UpdatedAt = GETUTCDATE(),
                           Source = @Source
                       WHERE GameId = @GameId",
                    new { GameId = gameId, DisplayName = displayName, Source = source },
                    transaction);

                entityId = existingGame.Id;

                // Remove old variants
                await connection.ExecuteAsync(
                    $"DELETE FROM [{VariantsTable}] WHERE GameId = @GameId",
                    new { GameId = entityId },
                    transaction);
            }
            else
            {
                // Insert new game
                var entity = new GameEntity
                {
                    Id = Guid.NewGuid(),
                    GameId = gameId,
                    DisplayName = displayName,
                    Source = source
                };

                await connection.ExecuteAsync(
                    $@"INSERT INTO [{GamesTable}] (Id, GameId, DisplayName, CreatedAt, UpdatedAt, Source)
                       VALUES (@Id, @GameId, @DisplayName, @CreatedAt, @UpdatedAt, @Source)",
                    entity,
                    transaction);

                entityId = entity.Id;
            }

            // Insert variants
            if (variants != null && variants.Length > 0)
            {
                var variantEntities = variants
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Select(v => new GameVariantEntity
                    {
                        GameId = entityId,
                        VariantText = v.ToLowerInvariant()
                    })
                    .ToList();

                if (variantEntities.Any())
                {
                    await connection.ExecuteAsync(
                        $@"INSERT INTO [{VariantsTable}] (Id, GameId, VariantText, CreatedAt)
                           VALUES (@Id, @GameId, @VariantText, @CreatedAt)",
                        variantEntities,
                        transaction);
                }
            }

            transaction.Commit();
            return entityId;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<List<GameEntity>> GetAllGamesAsync()
    {
        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync<GameEntity>(
            $"SELECT * FROM [{GamesTable}] ORDER BY DisplayName");
        
        return results.AsList();
    }

    public async Task<GameEntity?> GetGameByIdAsync(string gameId)
    {
        using var connection = new SqlConnection(_connectionString);
        return await connection.QuerySingleOrDefaultAsync<GameEntity>(
            $"SELECT * FROM [{GamesTable}] WHERE GameId = @GameId",
            new { GameId = gameId });
    }

    public async Task<List<GameVariantEntity>> GetGameVariantsAsync(Guid gameId)
    {
        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync<GameVariantEntity>(
            $"SELECT * FROM [{VariantsTable}] WHERE GameId = @GameId",
            new { GameId = gameId });
        
        return results.AsList();
    }

    public async Task<Dictionary<string, List<string>>> GetAllVariantsAsync()
    {
        var sql = $@"
            SELECT g.GameId, v.VariantText
            FROM [{GamesTable}] g
            INNER JOIN [{VariantsTable}] v ON g.Id = v.GameId
            ORDER BY g.GameId, v.VariantText";

        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync<(string GameId, string VariantText)>(sql);

        return results
            .GroupBy(r => r.GameId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.VariantText).ToList()
            );
    }

    public async Task<bool> GameExistsAsync(string gameId)
    {
        using var connection = new SqlConnection(_connectionString);
        var count = await connection.ExecuteScalarAsync<int>(
            $"SELECT COUNT(1) FROM [{GamesTable}] WHERE GameId = @GameId",
            new { GameId = gameId });
        
        return count > 0;
    }

    public async Task UpdateGameDisplayNameAsync(string gameId, string displayName)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(
            $@"UPDATE [{GamesTable}] 
               SET DisplayName = @DisplayName, UpdatedAt = GETUTCDATE()
               WHERE GameId = @GameId",
            new { GameId = gameId, DisplayName = displayName });
    }

    public async Task AddVariantAsync(string gameId, string variant)
    {
        if (string.IsNullOrWhiteSpace(variant))
            return;

        using var connection = new SqlConnection(_connectionString);
        
        // Get the game's GUID
        var game = await GetGameByIdAsync(gameId);
        if (game == null)
            throw new InvalidOperationException($"Game '{gameId}' not found");

        // Check if variant already exists
        var existingVariant = await connection.ExecuteScalarAsync<int>(
            $@"SELECT COUNT(1) FROM [{VariantsTable}] 
               WHERE GameId = @GameId AND VariantText = @VariantText",
            new { GameId = game.Id, VariantText = variant.ToLowerInvariant() });

        if (existingVariant > 0)
            return; // Variant already exists

        // Add new variant
        var variantEntity = new GameVariantEntity
        {
            GameId = game.Id,
            VariantText = variant.ToLowerInvariant()
        };

        await connection.ExecuteAsync(
            $@"INSERT INTO [{VariantsTable}] (Id, GameId, VariantText, CreatedAt)
               VALUES (@Id, @GameId, @VariantText, @CreatedAt)",
            variantEntity);
    }

    public async Task RemoveVariantAsync(string gameId, string variant)
    {
        using var connection = new SqlConnection(_connectionString);
        
        var game = await GetGameByIdAsync(gameId);
        if (game == null)
            return;

        await connection.ExecuteAsync(
            $"DELETE FROM [{VariantsTable}] WHERE GameId = @GameId AND VariantText = @VariantText",
            new { GameId = game.Id, VariantText = variant.ToLowerInvariant() });
    }

    public async Task DeleteGameAsync(string gameId)
    {
        using var connection = new SqlConnection(_connectionString);
        
        // Foreign key cascade will delete variants automatically
        await connection.ExecuteAsync(
            $"DELETE FROM [{GamesTable}] WHERE GameId = @GameId",
            new { GameId = gameId });
    }

    public async Task<List<string>> DetectGamesAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<string>();

        var lowerQuery = query.ToLowerInvariant();

        // Get all variants and check which ones match
        var sql = $@"
            SELECT DISTINCT g.GameId
            FROM [{GamesTable}] g
            INNER JOIN [{VariantsTable}] v ON g.Id = v.GameId
            WHERE @Query LIKE '%' + v.VariantText + '%'
            ORDER BY g.GameId";

        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync<string>(sql, new { Query = lowerQuery });

        return results.AsList();
    }

    public async Task SeedDefaultGamesAsync()
    {
        // Check if games already exist
        var existingCount = await GetAllGamesAsync();
        if (existingCount.Count > 0)
            return; // Already seeded

        // Seed default games
        await RegisterGameAsync(
            "munchkin-panic",
            "Munchkin Panic",
            new[] { "munchkin panic", "panic", "castle panic munchkin", "munchkin castle panic" },
            "seed");

        await RegisterGameAsync(
            "munchkin-treasure-hunt",
            "Munchkin Treasure Hunt",
            new[] { "munchkin treasure hunt", "treasure hunt", "munchkin quest", "treasure hunting" },
            "seed");

        await RegisterGameAsync(
            "munchkin",
            "Munchkin",
            new[] { "munchkin deluxe", "munchkin game", "base munchkin" },
            "seed");

        Console.WriteLine("[?] Seeded default games");
    }
}
