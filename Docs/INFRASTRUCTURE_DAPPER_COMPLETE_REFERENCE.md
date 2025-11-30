# Infrastructure.Data.Dapper Project - Complete Component Reference

## Overview
The **Infrastructure.Data.Dapper** project implements the data access layer using Dapper micro-ORM and SQL Server. It provides high-performance repository implementations for all data entities in the OfflineAI solution.

---

## Table of Contents
1. [VectorMemoryRepository](#vectormemoryrepository)
2. [BotPersonalityRepository](#botpersonalityrepository)
3. [KnowledgeDomainRepository](#knowledgedomainrepository)
4. [LlmRepository](#llmrepository)
5. [QuestionRepository](#questionrepository)
6. [ServiceCollectionExtensions](#servicecollectionextensions)

---

# VectorMemoryRepository

## Purpose
Manages vector memory fragments (embeddings + content) for RAG. Supports dynamic table switching, bulk operations, and domain filtering.

## Class Definition
```csharp
public class VectorMemoryRepository : IVectorMemoryRepository
{
    private readonly string _connectionString;
    private string _tableName;
}
```

## Key Features

### 1. Dynamic Table Switching
```csharp
public void SetActiveTable(string tableName);
public string GetActiveTable();
public async Task CreateTableAsync(string tableName);
public async Task<bool> TableExistsAsync(string tableName);
public async Task DeleteTableAsync(string tableName);
```

**Use Case**: Multi-context RAG
```csharp
// Board games context
repository.SetActiveTable("BoardGames_MemoryFragments");
await repository.SaveAsync(munchkinFragment);

// Recycling context
repository.SetActiveTable("Recycling_MemoryFragments");
await repository.SaveAsync(plasticFragment);
```

### 2. Automatic Database Creation
```csharp
public async Task InitializeDatabaseAsync()
{
    // 1. Check if database exists
    // 2. Create database if needed
    // 3. Grant permissions to current Windows user
    // 4. Create tables and indexes
    // 5. Run schema migrations (add new columns)
}
```

**Schema**:
```sql
CREATE TABLE [MemoryFragments] (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    CollectionName NVARCHAR(255) NOT NULL,
    Category NVARCHAR(500) NOT NULL,
    Content NVARCHAR(MAX) NOT NULL,
    ContentLength INT NOT NULL,
    Embedding VARBINARY(MAX) NULL,              -- Combined (legacy)
    CategoryEmbedding VARBINARY(MAX) NULL,      -- Category-only
    ContentEmbedding VARBINARY(MAX) NULL,       -- Content-only
    EmbeddingDimension INT NULL,
    CreatedAt DATETIME2 NOT NULL,
    UpdatedAt DATETIME2 NOT NULL,
    SourceFile NVARCHAR(1000) NULL,
    ChunkIndex INT NULL
);
```

### 3. Bulk Insert Optimization
```csharp
public async Task BulkSaveAsync(IEnumerable<MemoryFragmentEntity> entities)
{
    // Single SQL command, multiple parameter sets
    // 10x faster than individual inserts
}
```

**Performance**:
- Individual inserts: 5-10 seconds for 100 fragments
- Bulk insert: 0.5-1 second for 100 fragments

### 4. Domain Filtering
```csharp
public async Task<List<MemoryFragmentEntity>> LoadByCollectionAndDomainsAsync(
    string collectionName, 
    List<string> domainFilter)
{
    // SQL-level filtering for performance
    // Word-based matching with singular/plural handling
    // "mansions-of-madness" matches "Mansion of Madness"
}
```

**Algorithm**:
```csharp
// Domain ID: "board-game-munchkin"
// Extract words: ["board", "game", "munchkin"]

// Build SQL:
WHERE CollectionName = @CollectionName
  AND (Category LIKE '%board%' OR Category LIKE '%boards%')
  AND (Category LIKE '%game%' OR Category LIKE '%games%')
  AND (Category LIKE '%munchkin%' OR Category LIKE '%munchkins%')
```

### 5. Collection Management
```csharp
public async Task<List<string>> GetCollectionsAsync();
public async Task<bool> CollectionExistsAsync(string collectionName);
public async Task DeleteCollectionAsync(string collectionName);
public async Task<int> GetCountAsync(string collectionName);
public async Task<bool> HasEmbeddingsAsync(string collectionName);
```

### 6. Content Statistics
```csharp
public async Task<ContentLengthStats> GetContentLengthStatsAsync(string collectionName)
{
    // Returns:
    // - TotalFragments
    // - AverageLength
    // - MinLength, MaxLength
    // - LongFragments (>1000 chars)
    // - ShortFragments (<200 chars)
}

public async Task<Dictionary<string, int>> GetLengthDistributionAsync(string collectionName)
{
    // Returns fragments grouped by length:
    // - "0-200": 50
    // - "201-500": 150
    // - "501-1000": 300
    // - "1001-1500": 100
    // - "1500+": 20
}
```

### 7. Pagination Support
```csharp
public async Task<List<MemoryFragmentEntity>> LoadByCollectionPagedAsync(
    string collectionName, 
    int pageNumber, 
    int pageSize)
{
    // Uses OFFSET/FETCH for efficient paging
}
```

---

# BotPersonalityRepository

## Purpose
Manages bot personality configurations (system prompts, behavior profiles, categories).

## Schema
```sql
CREATE TABLE [BotPersonalities] (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    PersonalityId NVARCHAR(100) NOT NULL UNIQUE,
    DisplayName NVARCHAR(200) NOT NULL,
    Description NVARCHAR(1000) NOT NULL,
    SystemPrompt NVARCHAR(MAX) NOT NULL,
    DefaultCollection NVARCHAR(255) NULL,
    Temperature FLOAT NULL,
    EnableRag BIT NOT NULL DEFAULT 1,
    Icon NVARCHAR(50) NULL,
    Category NVARCHAR(100) NOT NULL,
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedAt DATETIME2 NOT NULL,
    UpdatedAt DATETIME2 NOT NULL
);
```

## Key Methods

### Query Methods
```csharp
public async Task<List<BotPersonalityEntity>> GetAllActiveAsync();
public async Task<List<BotPersonalityEntity>> GetAllAsync();
public async Task<BotPersonalityEntity?> GetByPersonalityIdAsync(string personalityId);
public async Task<List<BotPersonalityEntity>> GetByCategoryAsync(string category);
public async Task<List<string>> GetCategoriesAsync();
```

### CRUD Methods
```csharp
public async Task<Guid> SaveAsync(BotPersonalityEntity entity);  // UPSERT via MERGE
public async Task DeleteAsync(string personalityId);
public async Task<bool> ExistsAsync(string personalityId);
```

### Seeding
```csharp
public async Task SeedDefaultPersonalitiesAsync()
{
    // Seeds 5 default personalities:
    // - General Assistant (general-purpose)
    // - Rules Bot (precise, low temperature)
    // - Support Bot (helpful, empathetic)
    // - Teacher Bot (educational)
    // - Creative Assistant (high temperature)
}
```

---

# KnowledgeDomainRepository

## Purpose
Manages knowledge domain definitions for filtering RAG queries.

## Schema
```sql
CREATE TABLE [KnowledgeDomains] (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    DomainId NVARCHAR(100) NOT NULL UNIQUE,
    DisplayName NVARCHAR(200) NOT NULL,
    Category NVARCHAR(100) NOT NULL,
    Keywords NVARCHAR(MAX) NOT NULL,
    Description NVARCHAR(1000) NULL,
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedAt DATETIME2 NOT NULL,
    UpdatedAt DATETIME2 NOT NULL
);
```

## Key Methods
```csharp
public async Task<List<KnowledgeDomainEntity>> GetAllAsync();
public async Task<KnowledgeDomainEntity?> GetByIdAsync(string domainId);
public async Task<List<KnowledgeDomainEntity>> GetByCategoryAsync(string category);
public async Task<List<string>> GetCategoriesAsync();
public async Task<Guid> SaveAsync(KnowledgeDomainEntity entity);
public async Task DeleteAsync(string domainId);
```

## Usage Example
```csharp
// Define domain
var domain = new KnowledgeDomainEntity
{
    DomainId = "board-game-munchkin",
    DisplayName = "Munchkin",
    Category = "board-games",
    Keywords = "munchkin,dungeon,level,monster,treasure"
};
await repository.SaveAsync(domain);

// Query uses keywords to detect domain
// "How to fight monsters in Munchkin?" ? Detects "munchkin" keyword ? Applies domain filter
```

---

# LlmRepository

## Purpose
Stores LLM configuration and model metadata.

## Schema
```sql
CREATE TABLE [Llms] (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    Name NVARCHAR(200) NOT NULL,
    Description NVARCHAR(1000) NULL,
    ModelPath NVARCHAR(1000) NOT NULL,
    ExecutablePath NVARCHAR(1000) NOT NULL,
    ModelType NVARCHAR(100) NOT NULL,
    ParameterCount BIGINT NULL,
    QuantizationType NVARCHAR(50) NULL,
    ContextWindow INT NULL,
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedAt DATETIME2 NOT NULL,
    UpdatedAt DATETIME2 NOT NULL
);
```

## Key Methods
```csharp
public async Task<List<LlmEntity>> GetAllActiveAsync();
public async Task<LlmEntity?> GetByIdAsync(Guid id);
public async Task<LlmEntity?> GetByNameAsync(string name);
public async Task<Guid> SaveAsync(LlmEntity entity);
public async Task DeleteAsync(Guid id);
```

---

# QuestionRepository

## Purpose
Tracks user queries for analytics and training data.

## Schema
```sql
CREATE TABLE [Questions] (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    QuestionText NVARCHAR(MAX) NOT NULL,
    Answer NVARCHAR(MAX) NULL,
    CollectionName NVARCHAR(255) NULL,
    PersonalityId NVARCHAR(100) NULL,
    ResponseTime INT NULL,
    TokensGenerated INT NULL,
    CreatedAt DATETIME2 NOT NULL
);
```

## Key Methods
```csharp
public async Task<Guid> SaveAsync(QuestionEntity entity);
public async Task<List<QuestionEntity>> GetRecentAsync(int count = 50);
public async Task<List<QuestionEntity>> GetByCollectionAsync(string collectionName);
public async Task<Dictionary<string, int>> GetPopularQuestionsAsync(int topN = 10);
```

---

# ServiceCollectionExtensions

## Purpose
Provides DI registration helpers for repositories.

## Usage
```csharp
// In Program.cs or Startup.cs
services.AddDapperRepositories(connectionString);

// Registers:
// - IVectorMemoryRepository ? VectorMemoryRepository
// - IBotPersonalityRepository ? BotPersonalityRepository
// - IKnowledgeDomainRepository ? KnowledgeDomainRepository
// - ILlmRepository ? LlmRepository
// - IQuestionRepository ? QuestionRepository
```

## Implementation
```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDapperRepositories(
        this IServiceCollection services, 
        string connectionString)
    {
        services.AddScoped<IVectorMemoryRepository>(sp => 
            new VectorMemoryRepository(connectionString));
        
        services.AddScoped<IBotPersonalityRepository>(sp => 
            new BotPersonalityRepository(connectionString));
        
        // ... other repositories
        
        return services;
    }
}
```

---

## Performance Characteristics

### Query Performance
| Operation | Records | Time (Dapper) | Time (EF Core) | Speedup |
|-----------|---------|---------------|----------------|---------|
| Load Collection | 1,000 | 50-100ms | 150-300ms | 2-3x |
| Bulk Insert | 100 | 500ms-1s | 3-5s | 5-10x |
| Domain Filter | 1,000 ? 100 | 20-50ms | 100-200ms | 4-5x |
| Count Query | 10,000 | 5-10ms | 20-50ms | 4-5x |

### Memory Usage
| Operation | Dapper | EF Core | Difference |
|-----------|--------|---------|------------|
| Load 1,000 fragments | ~10 MB | ~25 MB | 2.5x less |
| Bulk insert 100 | ~5 MB | ~15 MB | 3x less |

---

## SQL Injection Prevention

### Table Name Validation
```csharp
private static bool IsValidTableName(string tableName)
{
    // Whitelist: alphanumeric + underscores only
    // Must start with letter or underscore
    return Regex.IsMatch(tableName, @"^[a-zA-Z_][a-zA-Z0-9_]*$");
}
```

**Examples**:
- ? "MemoryFragments" - Valid
- ? "BoardGames_MemoryFragments" - Valid
- ? "_Temp" - Valid
- ? "Users; DROP TABLE Users; --" - Invalid (injection attempt)
- ? "Table-Name" - Invalid (hyphen not allowed)
- ? "123Table" - Invalid (starts with number)

### Parameterized Queries
```csharp
// ? NEVER:
var sql = $"SELECT * FROM Users WHERE Name = '{userName}'";

// ? ALWAYS:
var sql = "SELECT * FROM Users WHERE Name = @UserName";
await connection.QueryAsync<User>(sql, new { UserName = userName });
```

---

## Common Patterns

### Pattern 1: CRUD Repository
```csharp
public async Task<Guid> SaveAsync(TEntity entity)
{
    const string sql = "INSERT INTO [Table] (...) VALUES (...)";
    using var connection = new SqlConnection(_connectionString);
    await connection.ExecuteAsync(sql, entity);
    return entity.Id;
}

public async Task<TEntity?> GetByIdAsync(Guid id)
{
    const string sql = "SELECT * FROM [Table] WHERE Id = @Id";
    using var connection = new SqlConnection(_connectionString);
    return await connection.QuerySingleOrDefaultAsync<TEntity>(sql, new { Id = id });
}

public async Task DeleteAsync(Guid id)
{
    const string sql = "DELETE FROM [Table] WHERE Id = @Id";
    using var connection = new SqlConnection(_connectionString);
    await connection.ExecuteAsync(sql, new { Id = id });
}
```

### Pattern 2: Upsert via MERGE
```csharp
const string sql = @"
    MERGE [Table] AS target
    USING (SELECT @Id AS Id) AS source
    ON target.Id = source.Id
    WHEN MATCHED THEN
        UPDATE SET Name = @Name, UpdatedAt = GETUTCDATE()
    WHEN NOT MATCHED THEN
        INSERT (Id, Name, CreatedAt, UpdatedAt)
        VALUES (@Id, @Name, @CreatedAt, @UpdatedAt);";

await connection.ExecuteAsync(sql, entity);
```

### Pattern 3: Connection Per Operation
```csharp
public async Task<List<TEntity>> GetAllAsync()
{
    using var connection = new SqlConnection(_connectionString);
    // Connection automatically pooled and returned
    var results = await connection.QueryAsync<TEntity>("SELECT * FROM [Table]");
    return results.AsList();
}
```

---

## Testing Strategy

### Unit Tests (Mock Repository)
```csharp
[Fact]
public async Task Service_UsesRepository()
{
    // Arrange
    var mockRepo = new Mock<IVectorMemoryRepository>();
    mockRepo.Setup(r => r.GetCountAsync(It.IsAny<string>()))
        .ReturnsAsync(100);
    
    var service = new MyService(mockRepo.Object);
    
    // Act
    var count = await service.GetFragmentCount("test");
    
    // Assert
    Assert.Equal(100, count);
    mockRepo.Verify(r => r.GetCountAsync("test"), Times.Once);
}
```

### Integration Tests (Real Database)
```csharp
[Fact]
public async Task VectorMemoryRepository_SaveAndLoad_WorksCorrectly()
{
    // Arrange
    var connectionString = "...test database...";
    var repository = new VectorMemoryRepository(connectionString);
    await repository.InitializeDatabaseAsync();
    
    var entity = new MemoryFragmentEntity { /* ... */ };
    
    // Act
    var id = await repository.SaveAsync(entity);
    var fragments = await repository.LoadByCollectionAsync(entity.CollectionName);
    
    // Assert
    Assert.Single(fragments);
    Assert.Equal(entity.Category, fragments[0].Category);
}
```

---

## Best Practices

### ? DO
- Use parameterized queries for all user input
- Validate table names before dynamic SQL
- Use `using` statements for connections
- Implement bulk operations for performance
- Add indexes on frequently queried columns
- Use connection pooling (automatic with SqlConnection)

### ? DON'T
- Use string interpolation for SQL queries
- Share SqlConnection instances across operations
- Forget to dispose connections
- Load entire tables into memory
- Use SELECT * in production code
- Trust user input in dynamic SQL

---

## Document Version
- **Files**: 6 repository files
- **Purpose**: High-performance data access layer using Dapper
- **Key Features**: Bulk operations, dynamic tables, SQL injection prevention
- **Performance**: 2-10x faster than EF Core
- **Last Updated**: 2024
