# Vector Memory Repository - Usage Guide

This guide explains how to use the Vector Memory Repository with both Dapper and Entity Framework Core implementations.

## Architecture

- **IVectorMemoryRepository**: Common interface for all implementations
- **VectorMemoryRepository**: Dapper-based implementation (direct SQL queries)
- **VectorMemoryRepositoryEF**: Entity Framework Core implementation (LINQ queries)
- **VectorMemoryRepositoryFactory**: Factory to create instances based on configuration

## Quick Start

### Option 1: Using the Factory with Configuration

```csharp
using Services.Configuration;
using Services.Repositories;

// Configure database
var config = new DatabaseConfig
{
    ConnectionString = "Server=(localdb)\\MSSQLLocalDB;Database=VectorMemoryDB;Integrated Security=true;",
    UseDatabasePersistence = true,
    AutoInitializeDatabase = true,
    UseEntityFramework = false  // false = Dapper, true = EF Core
};

// Create repository using factory
IVectorMemoryRepository repository = VectorMemoryRepositoryFactory.Create(config);

// Initialize database schema
await repository.InitializeDatabaseAsync();
```

### Option 2: Using the Factory with Repository Type

```csharp
using Services.Repositories;
using static Services.Repositories.VectorMemoryRepositoryFactory;

string connectionString = "Server=(localdb)\\MSSQLLocalDB;Database=VectorMemoryDB;Integrated Security=true;";

// Create Dapper implementation
IVectorMemoryRepository dapperRepo = VectorMemoryRepositoryFactory.Create(
    connectionString, 
    RepositoryType.Dapper
);

// Or create EF Core implementation
IVectorMemoryRepository efRepo = VectorMemoryRepositoryFactory.Create(
    connectionString, 
    RepositoryType.EntityFramework
);
```

### Option 3: Direct Instantiation

#### Dapper Implementation
```csharp
using Services.Repositories;

string connectionString = "Server=(localdb)\\MSSQLLocalDB;Database=VectorMemoryDB;Integrated Security=true;";
IVectorMemoryRepository repository = new VectorMemoryRepository(connectionString);
```

#### Entity Framework Core Implementation
```csharp
using Microsoft.EntityFrameworkCore;
using Services.Repositories;

string connectionString = "Server=(localdb)\\MSSQLLocalDB;Database=VectorMemoryDB;Integrated Security=true;";

var optionsBuilder = new DbContextOptionsBuilder<VectorMemoryDbContext>();
optionsBuilder.UseSqlServer(connectionString);

var context = new VectorMemoryDbContext(optionsBuilder.Options);
IVectorMemoryRepository repository = new VectorMemoryRepositoryEF(context);
```

## Common Operations

All operations are identical regardless of implementation:

```csharp
using Services.Models;

// Save a single fragment
var entity = new MemoryFragmentEntity
{
    CollectionName = "game-knowledge",
    Category = "rules",
    Content = "The game is played with 2-4 players",
    SourceFile = "rules.txt"
};

// Set embedding
float[] embeddingArray = new float[384]; // Your embedding vector
entity.SetEmbeddingFromMemory(embeddingArray);

Guid id = await repository.SaveAsync(entity);

// Bulk save (more efficient for many items)
var entities = new List<MemoryFragmentEntity> { entity1, entity2, entity3 };
await repository.BulkSaveAsync(entities);

// Load all fragments in a collection
var fragments = await repository.LoadByCollectionAsync("game-knowledge");

// Load with pagination
var page = await repository.LoadByCollectionPagedAsync("game-knowledge", pageNumber: 1, pageSize: 50);

// Get count
int count = await repository.GetCountAsync("game-knowledge");

// Check if embeddings exist
bool hasEmbeddings = await repository.HasEmbeddingsAsync("game-knowledge");

// Get all collections
var collections = await repository.GetCollectionsAsync();

// Update content
await repository.UpdateContentAsync(id, "Updated content");

// Delete
await repository.DeleteAsync(id);
await repository.DeleteCollectionAsync("game-knowledge");
```

## Implementation Comparison

### Dapper (VectorMemoryRepository)
**Pros:**
- Faster raw query performance
- Lower memory overhead
- Direct control over SQL
- No ORM overhead

**Cons:**
- Manual SQL query writing
- Less type safety
- No automatic change tracking

**Best for:**
- High-performance bulk operations
- Simple CRUD scenarios
- Microservices with minimal complexity

### Entity Framework Core (VectorMemoryRepositoryEF)
**Pros:**
- Strongly typed LINQ queries
- Automatic change tracking
- Database-agnostic (easier to switch databases)
- Built-in migration support
- Better for complex queries

**Cons:**
- Slightly higher overhead
- More memory usage for change tracking
- Can be slower for bulk operations

**Best for:**
- Complex domain models
- Applications requiring migrations
- Teams preferring LINQ over SQL
- Multi-database support

## Configuration in appsettings.json

```json
{
  "Database": {
    "ConnectionString": "Server=(localdb)\\MSSQLLocalDB;Database=VectorMemoryDB;Integrated Security=true;",
    "UseDatabasePersistence": true,
    "AutoInitializeDatabase": true,
    "UseEntityFramework": false
  }
}
```

## Dependency Injection Setup

### For Dapper:
```csharp
services.AddSingleton<IVectorMemoryRepository>(sp =>
{
    var config = sp.GetRequiredService<DatabaseConfig>();
    return VectorMemoryRepositoryFactory.CreateDapperRepository(config.ConnectionString);
});
```

### For Entity Framework Core:
```csharp
services.AddDbContext<VectorMemoryDbContext>(options =>
    options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

services.AddScoped<IVectorMemoryRepository, VectorMemoryRepositoryEF>();
```

### Using Factory (Dynamic):
```csharp
services.AddScoped<IVectorMemoryRepository>(sp =>
{
    var config = sp.GetRequiredService<DatabaseConfig>();
    return VectorMemoryRepositoryFactory.Create(config);
});
```

## Performance Tips

1. **Use BulkSaveAsync** for multiple inserts instead of calling SaveAsync multiple times
2. **Use pagination** when loading large collections
3. **For Dapper**: Consider connection pooling in your connection string
4. **For EF Core**: Use `AsNoTracking()` for read-only queries (not exposed in interface, but can be added if needed)

## Schema

Both implementations use the same database schema:

```sql
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
```

## Migration from Dapper to EF Core (or vice versa)

Simply change the configuration:

```csharp
// Change this:
config.UseEntityFramework = false;  // Using Dapper

// To this:
config.UseEntityFramework = true;   // Using EF Core
```

No code changes required! The interface ensures compatibility.
