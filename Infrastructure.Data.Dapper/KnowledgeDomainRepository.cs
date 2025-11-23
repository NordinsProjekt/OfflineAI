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
/// Dapper-based repository for knowledge domain data.
/// Manages domains and their variants in SQL Server.
/// </summary>
public class KnowledgeDomainRepository : IKnowledgeDomainRepository
{
    private readonly string _connectionString;
    private const string DomainsTable = "KnowledgeDomains";
    private const string VariantsTable = "DomainVariants";

    public KnowledgeDomainRepository(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task InitializeDatabaseAsync()
    {
        var createTablesSql = $@"
            -- Create KnowledgeDomains table
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = '{DomainsTable}')
            BEGIN
                CREATE TABLE [{DomainsTable}] (
                    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
                    DomainId NVARCHAR(255) NOT NULL UNIQUE,
                    DisplayName NVARCHAR(500) NOT NULL,
                    Category NVARCHAR(100) NOT NULL DEFAULT 'general',
                    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                    UpdatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                    Source NVARCHAR(100) NOT NULL DEFAULT 'manual'
                );

                CREATE INDEX IX_{DomainsTable}_DomainId ON [{DomainsTable}](DomainId);
                CREATE INDEX IX_{DomainsTable}_Category ON [{DomainsTable}](Category);
                CREATE INDEX IX_{DomainsTable}_CreatedAt ON [{DomainsTable}](CreatedAt);
            END

            -- Create DomainVariants table
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = '{VariantsTable}')
            BEGIN
                CREATE TABLE [{VariantsTable}] (
                    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
                    DomainId UNIQUEIDENTIFIER NOT NULL,
                    VariantText NVARCHAR(500) NOT NULL,
                    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                    FOREIGN KEY (DomainId) REFERENCES [{DomainsTable}](Id) ON DELETE CASCADE
                );

                CREATE INDEX IX_{VariantsTable}_DomainId ON [{VariantsTable}](DomainId);
                CREATE INDEX IX_{VariantsTable}_VariantText ON [{VariantsTable}](VariantText);
            END";

        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(createTablesSql);
    }

    public async Task<Guid> RegisterDomainAsync(
        string domainId, 
        string displayName, 
        string[] variants,
        string category = "general",
        string source = "manual")
    {
        if (string.IsNullOrWhiteSpace(domainId))
            throw new ArgumentException("Domain ID cannot be empty", nameof(domainId));
        
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Display name cannot be empty", nameof(displayName));

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            // Check if domain already exists
            var existingDomain = await connection.QuerySingleOrDefaultAsync<KnowledgeDomainEntity>(
                $"SELECT * FROM [{DomainsTable}] WHERE DomainId = @DomainId",
                new { DomainId = domainId },
                transaction);

            Guid entityId;

            if (existingDomain != null)
            {
                // Update existing domain
                await connection.ExecuteAsync(
                    $@"UPDATE [{DomainsTable}] 
                       SET DisplayName = @DisplayName,
                           Category = @Category,
                           UpdatedAt = GETUTCDATE(),
                           Source = @Source
                       WHERE DomainId = @DomainId",
                    new { DomainId = domainId, DisplayName = displayName, Category = category, Source = source },
                    transaction);

                entityId = existingDomain.Id;

                // Remove old variants
                await connection.ExecuteAsync(
                    $"DELETE FROM [{VariantsTable}] WHERE DomainId = @DomainId",
                    new { DomainId = entityId },
                    transaction);
            }
            else
            {
                // Insert new domain
                var entity = new KnowledgeDomainEntity
                {
                    Id = Guid.NewGuid(),
                    DomainId = domainId,
                    DisplayName = displayName,
                    Category = category,
                    Source = source
                };

                await connection.ExecuteAsync(
                    $@"INSERT INTO [{DomainsTable}] (Id, DomainId, DisplayName, Category, CreatedAt, UpdatedAt, Source)
                       VALUES (@Id, @DomainId, @DisplayName, @Category, @CreatedAt, @UpdatedAt, @Source)",
                    entity,
                    transaction);

                entityId = entity.Id;
            }

            // Insert variants
            if (variants != null && variants.Length > 0)
            {
                var variantEntities = variants
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Select(v => new DomainVariantEntity
                    {
                        DomainId = entityId,
                        VariantText = v.ToLowerInvariant()
                    })
                    .ToList();

                if (variantEntities.Any())
                {
                    await connection.ExecuteAsync(
                        $@"INSERT INTO [{VariantsTable}] (Id, DomainId, VariantText, CreatedAt)
                           VALUES (@Id, @DomainId, @VariantText, @CreatedAt)",
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

    public async Task<List<KnowledgeDomainEntity>> GetAllDomainsAsync()
    {
        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync<KnowledgeDomainEntity>(
            $"SELECT * FROM [{DomainsTable}] ORDER BY Category, DisplayName");
        
        return results.AsList();
    }

    public async Task<List<KnowledgeDomainEntity>> GetDomainsByCategoryAsync(string category)
    {
        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync<KnowledgeDomainEntity>(
            $"SELECT * FROM [{DomainsTable}] WHERE Category = @Category ORDER BY DisplayName",
            new { Category = category });
        
        return results.AsList();
    }

    public async Task<KnowledgeDomainEntity?> GetDomainByIdAsync(string domainId)
    {
        using var connection = new SqlConnection(_connectionString);
        return await connection.QuerySingleOrDefaultAsync<KnowledgeDomainEntity>(
            $"SELECT * FROM [{DomainsTable}] WHERE DomainId = @DomainId",
            new { DomainId = domainId });
    }

    public async Task<List<DomainVariantEntity>> GetDomainVariantsAsync(Guid domainId)
    {
        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync<DomainVariantEntity>(
            $"SELECT * FROM [{VariantsTable}] WHERE DomainId = @DomainId",
            new { DomainId = domainId });
        
        return results.AsList();
    }

    public async Task<Dictionary<string, List<string>>> GetAllVariantsAsync()
    {
        var sql = $@"
            SELECT d.DomainId, v.VariantText
            FROM [{DomainsTable}] d
            INNER JOIN [{VariantsTable}] v ON d.Id = v.DomainId
            ORDER BY d.DomainId, v.VariantText";

        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync<(string DomainId, string VariantText)>(sql);

        return results
            .GroupBy(r => r.DomainId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.VariantText).ToList()
            );
    }

    public async Task<bool> DomainExistsAsync(string domainId)
    {
        using var connection = new SqlConnection(_connectionString);
        var count = await connection.ExecuteScalarAsync<int>(
            $"SELECT COUNT(1) FROM [{DomainsTable}] WHERE DomainId = @DomainId",
            new { DomainId = domainId });
        
        return count > 0;
    }

    public async Task UpdateDomainDisplayNameAsync(string domainId, string displayName)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(
            $@"UPDATE [{DomainsTable}] 
               SET DisplayName = @DisplayName, UpdatedAt = GETUTCDATE()
               WHERE DomainId = @DomainId",
            new { DomainId = domainId, DisplayName = displayName });
    }

    public async Task UpdateDomainCategoryAsync(string domainId, string category)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(
            $@"UPDATE [{DomainsTable}] 
               SET Category = @Category, UpdatedAt = GETUTCDATE()
               WHERE DomainId = @DomainId",
            new { DomainId = domainId, Category = category });
    }

    public async Task AddVariantAsync(string domainId, string variant)
    {
        if (string.IsNullOrWhiteSpace(variant))
            return;

        using var connection = new SqlConnection(_connectionString);
        
        // Get the domain's GUID
        var domain = await GetDomainByIdAsync(domainId);
        if (domain == null)
            throw new InvalidOperationException($"Domain '{domainId}' not found");

        // Check if variant already exists
        var existingVariant = await connection.ExecuteScalarAsync<int>(
            $@"SELECT COUNT(1) FROM [{VariantsTable}] 
               WHERE DomainId = @DomainId AND VariantText = @VariantText",
            new { DomainId = domain.Id, VariantText = variant.ToLowerInvariant() });

        if (existingVariant > 0)
            return; // Variant already exists

        // Add new variant
        var variantEntity = new DomainVariantEntity
        {
            DomainId = domain.Id,
            VariantText = variant.ToLowerInvariant()
        };

        await connection.ExecuteAsync(
            $@"INSERT INTO [{VariantsTable}] (Id, DomainId, VariantText, CreatedAt)
               VALUES (@Id, @DomainId, @VariantText, @CreatedAt)",
            variantEntity);
    }

    public async Task RemoveVariantAsync(string domainId, string variant)
    {
        using var connection = new SqlConnection(_connectionString);
        
        var domain = await GetDomainByIdAsync(domainId);
        if (domain == null)
            return;

        await connection.ExecuteAsync(
            $"DELETE FROM [{VariantsTable}] WHERE DomainId = @DomainId AND VariantText = @VariantText",
            new { DomainId = domain.Id, VariantText = variant.ToLowerInvariant() });
    }

    public async Task DeleteDomainAsync(string domainId)
    {
        using var connection = new SqlConnection(_connectionString);
        
        // Foreign key cascade will delete variants automatically
        await connection.ExecuteAsync(
            $"DELETE FROM [{DomainsTable}] WHERE DomainId = @DomainId",
            new { DomainId = domainId });
    }

    public async Task<List<string>> DetectDomainsAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<string>();

        var lowerQuery = query.ToLowerInvariant();

        // Get all variants and check which ones match
        var sql = $@"
            SELECT DISTINCT d.DomainId
            FROM [{DomainsTable}] d
            INNER JOIN [{VariantsTable}] v ON d.Id = v.DomainId
            WHERE @Query LIKE '%' + v.VariantText + '%'
            ORDER BY d.DomainId";

        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync<string>(sql, new { Query = lowerQuery });

        return results.AsList();
    }

    public async Task<List<string>> GetCategoriesAsync()
    {
        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync<string>(
            $"SELECT DISTINCT Category FROM [{DomainsTable}] ORDER BY Category");
        
        return results.AsList();
    }

    public async Task SeedDefaultDomainsAsync()
    {
        // Check if domains already exist
        var existingCount = await GetAllDomainsAsync();
        if (existingCount.Count > 0)
            return; // Already seeded

        // Seed default board game domains
        await RegisterDomainAsync(
            "gloomhaven",
            "Gloomhaven",
            new[] { "gloomhaven", "gloom haven" },
            category: "board-game",
            source: "seed");

        await RegisterDomainAsync(
            "munchkin-panic",
            "Munchkin Panic",
            new[] { "munchkin panic", "panic", "castle panic munchkin", "munchkin castle panic" },
            category: "board-game",
            source: "seed");

        await RegisterDomainAsync(
            "munchkin-treasure-hunt",
            "Munchkin Treasure Hunt",
            new[] { "munchkin treasure hunt", "treasure hunt", "munchkin quest", "treasure hunting" },
            category: "board-game",
            source: "seed");

        await RegisterDomainAsync(
            "munchkin",
            "Munchkin",
            new[] { "munchkin deluxe", "munchkin game", "base munchkin" },
            category: "board-game",
            source: "seed");

        Console.WriteLine("[+] Seeded default knowledge domains");
    }
}
