# Vector Memory Database Persistence Guide

## Overview

This guide explains how to persist your vector embeddings to MSSQL database instead of regenerating them every time. This provides:

? **Fast startup** - Load pre-computed embeddings instead of regenerating  
? **Consistent results** - Same embeddings across sessions  
? **Data persistence** - Knowledge base survives application restarts  
? **Scalability** - Handle large collections efficiently  
? **Version control** - Track when collections were updated  

## Architecture

```
???????????????????????????????????????????????????????????????
?                     Application Layer                        ?
?  ????????????????????????????????????????????????????????  ?
?  ?  AiChatService                                        ?  ?
?  ?  - Uses VectorMemory for semantic search            ?  ?
?  ????????????????????????????????????????????????????????  ?
???????????????????????????????????????????????????????????????
                         ?
???????????????????????????????????????????????????????????????
?                   Business Logic Layer                       ?
?  ????????????????????????????????????????????????????????  ?
?  ?  VectorMemoryPersistenceService                      ?  ?
?  ?  - SaveFragmentsAsync()                              ?  ?
?  ?  - LoadVectorMemoryAsync()                           ?  ?
?  ?  - GetCollectionStatsAsync()                         ?  ?
?  ????????????????????????????????????????????????????????  ?
???????????????????????????????????????????????????????????????
                         ?
???????????????????????????????????????????????????????????????
?                    Data Access Layer                         ?
?  ????????????????????????????????????????????????????????  ?
?  ?  VectorMemoryRepository                              ?  ?
?  ?  - BulkSaveAsync()                                   ?  ?
?  ?  - LoadByCollectionAsync()                           ?  ?
?  ?  - DeleteCollectionAsync()                           ?  ?
?  ????????????????????????????????????????????????????????  ?
???????????????????????????????????????????????????????????????
                         ?
???????????????????????????????????????????????????????????????
?                      Database Layer                          ?
?  ????????????????????????????????????????????????????????  ?
?  ?  MSSQL - MemoryFragments Table                       ?  ?
?  ?  - Id, CollectionName, Category, Content             ?  ?
?  ?  - Embedding (VARBINARY), EmbeddingDimension         ?  ?
?  ?  - CreatedAt, UpdatedAt, SourceFile, ChunkIndex      ?  ?
?  ????????????????????????????????????????????????????????  ?
???????????????????????????????????????????????????????????????
```

## Database Setup

### 1. Create Database

```sql
CREATE DATABASE VectorMemoryDB;
GO
```

### 2. Run Schema Script

Execute the `Database/Schema.sql` file or let the application auto-initialize:

```csharp
var dbConfig = new DatabaseConfig
{
    ConnectionString = "Server=localhost;Database=VectorMemoryDB;Integrated Security=true;TrustServerCertificate=true;",
    AutoInitializeDatabase = true  // Auto-creates schema
};
```

### 3. Connection String Formats

**Windows Authentication:**
```
Server=localhost;Database=VectorMemoryDB;Integrated Security=true;TrustServerCertificate=true;
```

**SQL Server Authentication:**
```
Server=localhost;Database=VectorMemoryDB;User Id=sa;Password=YourPassword;TrustServerCertificate=true;
```

**Azure SQL:**
```
Server=tcp:yourserver.database.windows.net,1433;Database=VectorMemoryDB;User ID=youradmin;Password=YourPassword;Encrypt=true;
```

## Usage Examples

### Basic Usage - Load from Database

```csharp
// Setup
var embeddingService = new LocalLlmEmbeddingService(llmPath, modelPath);
var repository = new VectorMemoryRepository(connectionString);
var persistenceService = new VectorMemoryPersistenceService(repository, embeddingService);

// Initialize database (first time only)
await persistenceService.InitializeDatabaseAsync();

// Load from database (fast! embeddings already computed)
var vectorMemory = await persistenceService.LoadVectorMemoryAsync("game-rules");

// Use in your AI service
var aiService = new AiChatService(vectorMemory, conversationMemory, llmPath, modelPath);
```

### First-Time Setup - Save to Database

```csharp
// Load fragments from files
var fileReader = new FileMemoryLoaderService();
var fragments = new List<MemoryFragment>();

// Load your knowledge files
var chunksLoaded = await fileReader.LoadFromManualSectionsAsync(
    "trhunt_rules.txt", 
    tempMemory,
    defaultCategory: "Treasure Hunt",
    autoNumberSections: true);

// Save to database (generates and stores embeddings)
await persistenceService.SaveFragmentsAsync(
    fragments,
    collectionName: "game-rules",
    sourceFile: "trhunt_rules.txt",
    replaceExisting: true);  // Replace if exists

Console.WriteLine("? Saved with embeddings to database!");
```

### Check What's in Database

```csharp
// List all collections
var collections = await persistenceService.GetCollectionsAsync();
foreach (var collection in collections)
{
    var stats = await persistenceService.GetCollectionStatsAsync(collection);
    Console.WriteLine($"{collection}: {stats.FragmentCount} fragments, Has embeddings: {stats.HasEmbeddings}");
}
```

### Update Existing Collection

```csharp
// Delete old version
await persistenceService.DeleteCollectionAsync("game-rules");

// Save new version
await persistenceService.SaveFragmentsAsync(newFragments, "game-rules", replaceExisting: true);
```

## Running the Database Mode

### Option 1: Use the New Mode

Update your `Program.cs`:

```csharp
// In OfflineAI/Program.cs
Console.WriteLine("Select mode:");
Console.WriteLine("1. Original Mode (CLI)");
Console.WriteLine("2. Vector Memory (In-Memory)");
Console.WriteLine("3. Vector Memory with Database");  // NEW!
Console.Write("Enter choice (1-3): ");

var choice = Console.ReadLine();

switch (choice)
{
    case "1":
        await RunOriginalModeTinyLlama.RunOriginalMode();
        break;
    case "2":
        await RunVectorMemoryMode.RunAsync();
        break;
    case "3":
        await RunVectorMemoryWithDatabaseMode.RunAsync();  // NEW!
        break;
}
```

### Option 2: Modify Existing Mode

Update `RunVectorMemoryMode.cs` to check database first:

```csharp
// At the start of RunAsync()
var dbConfig = new DatabaseConfig
{
    ConnectionString = "Server=localhost;Database=VectorMemoryDB;Integrated Security=true;TrustServerCertificate=true;",
    UseDatabasePersistence = true
};

if (dbConfig.UseDatabasePersistence)
{
    var repository = new VectorMemoryRepository(dbConfig.ConnectionString);
    var persistenceService = new VectorMemoryPersistenceService(repository, embeddingService);
    
    await persistenceService.InitializeDatabaseAsync();
    
    if (await persistenceService.CollectionExistsAsync("game-rules"))
    {
        Console.WriteLine("Loading from database...");
        vectorMemory = await persistenceService.LoadVectorMemoryAsync("game-rules");
    }
    else
    {
        Console.WriteLine("No database cache found. Loading from files...");
        // Load from files and save
        vectorMemory = new VectorMemory(embeddingService, "game-rules");
        // ... load files ...
        await persistenceService.SaveFragmentsAsync(allFragments, "game-rules");
    }
}
```

## Performance Comparison

| Operation | In-Memory (No DB) | With Database |
|-----------|-------------------|---------------|
| **First Load** (100 fragments) | ~2-5 minutes | ~2-5 minutes |
| **Subsequent Loads** | ~2-5 minutes | **~2-5 seconds** ? |
| **Embedding Consistency** | ? May vary | ? Always same |
| **Survives Restart** | ? No | ? Yes |
| **Disk Usage** | Minimal | ~1-2 MB per 100 fragments |

## Database Schema

### MemoryFragments Table

| Column | Type | Description |
|--------|------|-------------|
| `Id` | UNIQUEIDENTIFIER | Primary key |
| `CollectionName` | NVARCHAR(255) | Group name (e.g., "game-rules") |
| `Category` | NVARCHAR(500) | Section category/title |
| `Content` | NVARCHAR(MAX) | Actual text content |
| `Embedding` | VARBINARY(MAX) | Vector embedding (binary) |
| `EmbeddingDimension` | INT | Number of dimensions (e.g., 384) |
| `CreatedAt` | DATETIME2 | When fragment was created |
| `UpdatedAt` | DATETIME2 | When fragment was updated |
| `SourceFile` | NVARCHAR(1000) | Original file name |
| `ChunkIndex` | INT | Section order |

### Useful Queries

**View all collections:**
```sql
SELECT * FROM VW_CollectionStats;
```

**Get collection stats:**
```sql
EXEC sp_GetCollectionStats @CollectionName = 'game-rules';
```

**Find fragments by keyword:**
```sql
SELECT CollectionName, Category, LEFT(Content, 100) AS Preview
FROM MemoryFragments
WHERE Content LIKE '%victory points%';
```

**Check database size:**
```sql
SELECT 
    CollectionName,
    COUNT(*) AS Fragments,
    SUM(DATALENGTH(Embedding)) / 1024.0 / 1024.0 AS EmbeddingSizeMB
FROM MemoryFragments
GROUP BY CollectionName;
```

## Troubleshooting

### "Table already exists" Error
The app auto-creates tables. If you get this error, the table exists. Just continue.

### "Connection failed"
Check your connection string. Ensure:
- SQL Server is running
- Database exists (or auto-create is enabled)
- You have proper permissions
- `TrustServerCertificate=true` for local development

### Slow first load
Generating embeddings is slow (2-5 minutes for 100 fragments). This is normal. Subsequent loads from database are fast.

### Large database size
Each 384-dimension embedding = ~1.5 KB
- 100 fragments ? 150 KB
- 1,000 fragments ? 1.5 MB
- 10,000 fragments ? 15 MB

This is very reasonable!

## Migration Guide

### From In-Memory to Database

1. **Install required packages** (already done if using Services project)
2. **Run your existing code once** to generate embeddings in-memory
3. **Save to database:**
```csharp
// After loading to in-memory vectorMemory
var repository = new VectorMemoryRepository(connectionString);
var persistenceService = new VectorMemoryPersistenceService(repository, embeddingService);
await persistenceService.InitializeDatabaseAsync();

// Save (this will generate embeddings)
await persistenceService.SaveFragmentsAsync(fragments, "game-rules");
```
4. **Next time, load from database:**
```csharp
var vectorMemory = await persistenceService.LoadVectorMemoryAsync("game-rules");
```

## Best Practices

1. **Use collections wisely** - Group related content (e.g., "boardgames", "rules-2024")
2. **Version your collections** - Use dates in names: "game-rules-2024-01"
3. **Clean old data** - Use `sp_DeleteOldCollections` stored procedure
4. **Index appropriately** - Default indexes work for most cases
5. **Backup your database** - Embeddings are expensive to regenerate!
6. **Test connection string** - Use SQL Server Management Studio first
7. **Monitor size** - Check database growth periodically

## Advanced Features

### Pagination for Large Collections

```csharp
// Load in pages (useful for 1000+ fragments)
var page1 = await persistenceService.LoadVectorMemoryPagedAsync("game-rules", pageNumber: 1, pageSize: 100);
```

### Multiple Collections

```csharp
// Save different game rules separately
await persistenceService.SaveFragmentsAsync(treasureHuntFragments, "treasure-hunt");
await persistenceService.SaveFragmentsAsync(munchkinFragments, "munchkin-rules");

// Load specific one
var treasureMemory = await persistenceService.LoadVectorMemoryAsync("treasure-hunt");
```

### Hybrid Approach (Database + New Files)

```csharp
// Load from database
var vectorMemory = await persistenceService.LoadVectorMemoryAsync("game-rules");

// Add new files on the fly (embeddings generated as needed)
await fileReader.LoadFromManualSectionsAsync("new_rules.txt", vectorMemory);

// Optionally save the additions
// await persistenceService.SaveFragmentsAsync(...);
```

## Files Created

```
Services/
??? Models/
?   ??? MemoryFragmentEntity.cs          # Database entity
??? Repositories/
?   ??? VectorMemoryRepository.cs        # Data access layer
??? Configuration/
?   ??? DatabaseConfig.cs                # Configuration model
??? VectorMemoryPersistenceService.cs    # Business logic
??? VectorMemory.cs                      # Updated with SetEmbeddingForLastFragment()

OfflineAI/
??? Modes/
    ??? RunVectorMemoryWithDatabaseMode.cs  # New mode with DB support

Database/
??? Schema.sql                           # Database setup script

Docs/
??? VectorMemoryDatabaseGuide.md         # This file
```

## Next Steps

1. **Setup database** - Create VectorMemoryDB
2. **Configure connection** - Update connection string
3. **First run** - Load from files, save to DB
4. **Enjoy speed** - Subsequent runs load from DB instantly!

## Questions?

- Check `Database/Schema.sql` for database structure
- See `RunVectorMemoryWithDatabaseMode.cs` for full example
- Review `VectorMemoryPersistenceService.cs` for API details
