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
/// Dapper-based repository for managing bot personalities in SQL Server.
/// </summary>
public class BotPersonalityRepository : IBotPersonalityRepository
{
    private readonly string _connectionString;
    
    public BotPersonalityRepository(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }
    
    public async Task InitializeDatabaseAsync()
    {
        const string createTableSql = @"
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'BotPersonalities')
            BEGIN
                CREATE TABLE [BotPersonalities] (
                    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
                    PersonalityId NVARCHAR(100) NOT NULL UNIQUE,
                    DisplayName NVARCHAR(200) NOT NULL,
                    Description NVARCHAR(1000) NOT NULL,
                    SystemPrompt NVARCHAR(MAX) NOT NULL,
                    Language NVARCHAR(50) NOT NULL DEFAULT 'English',
                    DefaultCollection NVARCHAR(255) NULL,
                    Temperature FLOAT NULL,
                    EnableRag BIT NOT NULL DEFAULT 1,
                    Icon NVARCHAR(50) NULL,
                    Category NVARCHAR(100) NOT NULL DEFAULT 'general',
                    IsActive BIT NOT NULL DEFAULT 1,
                    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                    UpdatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE()
                );

                CREATE INDEX IX_BotPersonalities_PersonalityId ON [BotPersonalities](PersonalityId);
                CREATE INDEX IX_BotPersonalities_Category ON [BotPersonalities](Category);
                CREATE INDEX IX_BotPersonalities_IsActive ON [BotPersonalities](IsActive);
            END
            ELSE
            BEGIN
                -- Add Language column if it doesn't exist (migration for existing databases)
                IF NOT EXISTS (SELECT * FROM sys.columns 
                               WHERE object_id = OBJECT_ID('BotPersonalities') 
                               AND name = 'Language')
                BEGIN
                    ALTER TABLE [BotPersonalities] 
                    ADD Language NVARCHAR(50) NOT NULL DEFAULT 'English';
                END
            END";
        
        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(createTableSql);
    }
    
    public async Task<List<BotPersonalityEntity>> GetAllActiveAsync()
    {
        const string sql = @"
            SELECT Id, PersonalityId, DisplayName, Description, SystemPrompt, Language,
                   DefaultCollection, Temperature, EnableRag, Icon, Category, 
                   IsActive, CreatedAt, UpdatedAt
            FROM [BotPersonalities]
            WHERE IsActive = 1
            ORDER BY Category, DisplayName";
        
        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync<BotPersonalityEntity>(sql);
        return results.AsList();
    }
    
    public async Task<List<BotPersonalityEntity>> GetAllAsync()
    {
        const string sql = @"
            SELECT Id, PersonalityId, DisplayName, Description, SystemPrompt, Language,
                   DefaultCollection, Temperature, EnableRag, Icon, Category, 
                   IsActive, CreatedAt, UpdatedAt
            FROM [BotPersonalities]
            ORDER BY Category, DisplayName";
        
        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync<BotPersonalityEntity>(sql);
        return results.AsList();
    }
    
    public async Task<BotPersonalityEntity?> GetByPersonalityIdAsync(string personalityId)
    {
        const string sql = @"
            SELECT Id, PersonalityId, DisplayName, Description, SystemPrompt, Language,
                   DefaultCollection, Temperature, EnableRag, Icon, Category, 
                   IsActive, CreatedAt, UpdatedAt
            FROM [BotPersonalities]
            WHERE PersonalityId = @PersonalityId";
        
        using var connection = new SqlConnection(_connectionString);
        return await connection.QuerySingleOrDefaultAsync<BotPersonalityEntity>(
            sql, 
            new { PersonalityId = personalityId });
    }
    
    public async Task<List<BotPersonalityEntity>> GetByCategoryAsync(string category)
    {
        const string sql = @"
            SELECT Id, PersonalityId, DisplayName, Description, SystemPrompt, Language,
                   DefaultCollection, Temperature, EnableRag, Icon, Category, 
                   IsActive, CreatedAt, UpdatedAt
            FROM [BotPersonalities]
            WHERE Category = @Category AND IsActive = 1
            ORDER BY DisplayName";
        
        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync<BotPersonalityEntity>(
            sql, 
            new { Category = category });
        return results.AsList();
    }
    
    public async Task<List<string>> GetCategoriesAsync()
    {
        const string sql = @"
            SELECT DISTINCT Category 
            FROM [BotPersonalities]
            WHERE IsActive = 1
            ORDER BY Category";
        
        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync<string>(sql);
        return results.AsList();
    }
    
    public async Task<Guid> SaveAsync(BotPersonalityEntity entity)
    {
        const string sql = @"
            MERGE [BotPersonalities] AS target
            USING (SELECT @PersonalityId AS PersonalityId) AS source
            ON target.PersonalityId = source.PersonalityId
            WHEN MATCHED THEN
                UPDATE SET 
                    DisplayName = @DisplayName,
                    Description = @Description,
                    SystemPrompt = @SystemPrompt,
                    Language = @Language,
                    DefaultCollection = @DefaultCollection,
                    Temperature = @Temperature,
                    EnableRag = @EnableRag,
                    Icon = @Icon,
                    Category = @Category,
                    IsActive = @IsActive,
                    UpdatedAt = GETUTCDATE()
            WHEN NOT MATCHED THEN
                INSERT (Id, PersonalityId, DisplayName, Description, SystemPrompt, Language,
                        DefaultCollection, Temperature, EnableRag, Icon, Category, 
                        IsActive, CreatedAt, UpdatedAt)
                VALUES (@Id, @PersonalityId, @DisplayName, @Description, @SystemPrompt, @Language,
                        @DefaultCollection, @Temperature, @EnableRag, @Icon, @Category, 
                        @IsActive, @CreatedAt, @UpdatedAt);";
        
        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, entity);
        return entity.Id;
    }
    
    public async Task DeleteAsync(string personalityId)
    {
        const string sql = "DELETE FROM [BotPersonalities] WHERE PersonalityId = @PersonalityId";
        
        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, new { PersonalityId = personalityId });
    }
    
    public async Task<bool> ExistsAsync(string personalityId)
    {
        const string sql = @"
            SELECT COUNT(1) 
            FROM [BotPersonalities] 
            WHERE PersonalityId = @PersonalityId";
        
        using var connection = new SqlConnection(_connectionString);
        var count = await connection.ExecuteScalarAsync<int>(sql, new { PersonalityId = personalityId });
        return count > 0;
    }
    
    public async Task SeedDefaultPersonalitiesAsync()
    {
        // Check if any personalities exist
        const string countSql = "SELECT COUNT(1) FROM [BotPersonalities]";
        
        using var connection = new SqlConnection(_connectionString);
        var count = await connection.ExecuteScalarAsync<int>(countSql);
        
        if (count > 0)
            return; // Already seeded
        
        // Seed default personalities
        var defaultPersonalities = new[]
        {
            new BotPersonalityEntity
            {
                PersonalityId = "general-assistant",
                DisplayName = "General Assistant",
                Description = "A helpful general-purpose assistant",
                SystemPrompt = "You are a helpful AI assistant. Answer questions accurately and concisely based on the provided information.",
                Language = "English",
                Category = "general",
                Icon = "??",
                EnableRag = true
            },
            new BotPersonalityEntity
            {
                PersonalityId = "rules-bot",
                DisplayName = "Rules Bot",
                Description = "Specialized in explaining rules and regulations",
                SystemPrompt = "You are a rules expert. Provide clear, precise explanations of rules based on the documentation. Always cite specific rule sections when possible.",
                Language = "English",
                Category = "specialized",
                Icon = "??",
                EnableRag = true,
                Temperature = 0.3f // Lower temperature for more precise answers
            },
            new BotPersonalityEntity
            {
                PersonalityId = "support-bot",
                DisplayName = "User Support Bot",
                Description = "Customer service and user support specialist",
                SystemPrompt = "You are a friendly customer support agent. Help users solve problems step-by-step. Be patient, empathetic, and provide clear instructions.",
                Language = "English",
                Category = "support",
                Icon = "??",
                EnableRag = true,
                Temperature = 0.7f
            },
            new BotPersonalityEntity
            {
                PersonalityId = "teacher-bot",
                DisplayName = "Teacher Bot",
                Description = "Educational tutor that explains concepts",
                SystemPrompt = "You are a patient teacher. Explain concepts clearly, provide examples, and check for understanding. Break down complex topics into digestible parts.",
                Language = "English",
                Category = "education",
                Icon = "?????",
                EnableRag = true,
                Temperature = 0.7f
            },
            new BotPersonalityEntity
            {
                PersonalityId = "creative-assistant",
                DisplayName = "Creative Assistant",
                Description = "Helps with creative and brainstorming tasks",
                SystemPrompt = "You are a creative assistant. Think outside the box, provide imaginative suggestions, and help brainstorm ideas while staying grounded in the provided context.",
                Language = "English",
                Category = "creative",
                Icon = "??",
                EnableRag = true,
                Temperature = 0.9f // Higher temperature for more creativity
            }
        };
        
        foreach (var personality in defaultPersonalities)
        {
            await SaveAsync(personality);
        }
    }
}
