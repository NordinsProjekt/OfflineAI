# Migration Guide - Using IVectorMemoryRepository

This guide shows how to update existing code to use the new `IVectorMemoryRepository` interface and factory pattern.

## Quick Migration

### Before (Direct Instantiation)
```csharp
var repository = new VectorMemoryRepository(connectionString);
var persistenceService = new VectorMemoryPersistenceService(repository, embeddingService);
```

### After (Using Factory - Dapper)
```csharp
IVectorMemoryRepository repository = VectorMemoryRepositoryFactory.CreateDapperRepository(connectionString);
var persistenceService = new VectorMemoryPersistenceService(repository, embeddingService);
```

### After (Using Factory - EF Core)
```csharp
IVectorMemoryRepository repository = VectorMemoryRepositoryFactory.CreateEFRepository(connectionString);
var persistenceService = new VectorMemoryPersistenceService(repository, embeddingService);
```

### After (Using Factory with Config)
```csharp
var config = new DatabaseConfig
{
    ConnectionString = connectionString,
    UseEntityFramework = false  // or true for EF Core
};

IVectorMemoryRepository repository = VectorMemoryRepositoryFactory.Create(config);
var persistenceService = new VectorMemoryPersistenceService(repository, embeddingService);
```

## Updated Files

### ? VectorMemoryPersistenceService.cs
Already updated to use `IVectorMemoryRepository` interface.

**Changed:**
```csharp
// OLD
private readonly VectorMemoryRepository _repository;

public VectorMemoryPersistenceService(
    VectorMemoryRepository repository,
    ITextEmbeddingGenerationService embeddingService)

// NEW
private readonly IVectorMemoryRepository _repository;

public VectorMemoryPersistenceService(
    IVectorMemoryRepository repository,
    ITextEmbeddingGenerationService embeddingService)
```

## Example: Update RunVectorMemoryWithDatabaseMode.cs

### Before
```csharp
var repository = new VectorMemoryRepository(connectionString);
await repository.InitializeDatabaseAsync();
var persistenceService = new VectorMemoryPersistenceService(repository, embeddingService);
```

### After (Option 1: Explicit Factory)
```csharp
var config = new DatabaseConfig
{
    ConnectionString = connectionString,
    UseDatabasePersistence = true,
    UseEntityFramework = false  // Change to true for EF Core
};

IVectorMemoryRepository repository = VectorMemoryRepositoryFactory.Create(config);
await repository.InitializeDatabaseAsync();
var persistenceService = new VectorMemoryPersistenceService(repository, embeddingService);
```

### After (Option 2: Configuration-Driven)
```csharp
// Read from appsettings.json or config file
var databaseConfig = configuration.GetSection("Database").Get<DatabaseConfig>();

IVectorMemoryRepository repository = VectorMemoryRepositoryFactory.Create(databaseConfig);
await repository.InitializeDatabaseAsync();
var persistenceService = new VectorMemoryPersistenceService(repository, embeddingService);
```

## Example: Update Tests

### Before
```csharp
[Test]
public async Task TestSaveFragment()
{
    var repository = new VectorMemoryRepository(TestConnectionString);
    // ... test code
}
```

### After (Using Dapper)
```csharp
[Test]
public async Task TestSaveFragment()
{
    IVectorMemoryRepository repository = VectorMemoryRepositoryFactory.CreateDapperRepository(TestConnectionString);
    // ... test code
}
```

### After (Using Mock)
```csharp
[Test]
public async Task TestSaveFragment()
{
    IVectorMemoryRepository repository = new MockVectorMemoryRepository();
    // ... test code - no database needed!
}
```

## Dependency Injection Updates

### Before (Manual Service Creation)
```csharp
public void ConfigureServices(IServiceCollection services)
{
    services.AddSingleton<VectorMemoryRepository>(sp =>
        new VectorMemoryRepository(configuration.GetConnectionString("Default")));
}
```

### After (Using Factory)
```csharp
public void ConfigureServices(IServiceCollection services)
{
    // Register configuration
    services.Configure<DatabaseConfig>(configuration.GetSection("Database"));
    
    // Register repository using factory
    services.AddSingleton<IVectorMemoryRepository>(sp =>
    {
        var config = sp.GetRequiredService<IOptions<DatabaseConfig>>().Value;
        return VectorMemoryRepositoryFactory.Create(config);
    });
    
    // Register persistence service (automatically gets IVectorMemoryRepository)
    services.AddScoped<VectorMemoryPersistenceService>();
}
```

### After (EF Core with DbContext)
```csharp
public void ConfigureServices(IServiceCollection services)
{
    // Register DbContext
    services.AddDbContext<VectorMemoryDbContext>(options =>
        options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));
    
    // Register repository
    services.AddScoped<IVectorMemoryRepository, VectorMemoryRepositoryEF>();
    
    // Register persistence service
    services.AddScoped<VectorMemoryPersistenceService>();
}
```

## Configuration File Example

### appsettings.json
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

### appsettings.Development.json (EF Core)
```json
{
  "Database": {
    "ConnectionString": "Server=(localdb)\\MSSQLLocalDB;Database=VectorMemoryDB_Dev;Integrated Security=true;",
    "UseDatabasePersistence": true,
    "AutoInitializeDatabase": true,
    "UseEntityFramework": true
  }
}
```

## Testing Strategy

### Unit Tests (No Database)
```csharp
// Use mock implementation
IVectorMemoryRepository mockRepo = new MockVectorMemoryRepository();
var service = new VectorMemoryPersistenceService(mockRepo, mockEmbeddingService);
```

### Integration Tests (Dapper)
```csharp
// Use Dapper for fast integration tests
IVectorMemoryRepository repo = VectorMemoryRepositoryFactory.Create(
    testConnectionString, 
    VectorMemoryRepositoryFactory.RepositoryType.Dapper
);
```

### Integration Tests (EF Core)
```csharp
// Use EF Core for migration testing
IVectorMemoryRepository repo = VectorMemoryRepositoryFactory.Create(
    testConnectionString, 
    VectorMemoryRepositoryFactory.RepositoryType.EntityFramework
);
```

## Breaking Changes

### ?? Constructor Parameter Change
`VectorMemoryPersistenceService` now requires `IVectorMemoryRepository` instead of `VectorMemoryRepository`.

**Impact:** Any code that directly instantiates `VectorMemoryPersistenceService` needs to be updated.

**Fix:** Use the factory to create the repository instance.

### ? No Breaking Changes For
- `MemoryFragmentEntity` - unchanged
- Repository method signatures - unchanged
- Database schema - unchanged
- All existing data - compatible

## Rollback Strategy

If you need to rollback to the old implementation:

1. Change factory calls back to direct instantiation:
   ```csharp
   var repository = new VectorMemoryRepository(connectionString);
   ```

2. Update `VectorMemoryPersistenceService` constructor parameter:
   ```csharp
   // Change back to concrete type
   private readonly VectorMemoryRepository _repository;
   ```

3. All data remains intact - no database changes needed

## Benefits of Migration

? **Flexibility** - Switch between Dapper and EF Core without code changes
? **Testability** - Easy to mock with interface
? **Maintainability** - Interface provides clear contract
? **Future-proof** - Easy to add new implementations (e.g., PostgreSQL, MongoDB)
? **Best Practices** - Follows dependency injection principles
? **No Performance Loss** - Interface adds no runtime overhead

## Need Help?

Refer to:
- `Docs/VectorMemoryRepository-Usage.md` - Detailed usage guide
- `Docs/VectorMemoryRepository-Implementation-Summary.md` - Technical summary
- `Services/Repositories/VectorMemoryRepositoryFactory.cs` - Factory implementation
