# Code Cleanup: Removed Redundant Parameter

## Issue Found

The `LoadFromFilesAndSaveAsync` method had a redundant `embeddingService` parameter that was never used directly.

### Why It Was Redundant

The `embeddingService` was already injected into `persistenceService` during initialization:

```csharp
// In RunAsync() - embeddingService is injected here
var embeddingService = new LocalLlmEmbeddingService(llmPath, modelPath);
var persistenceService = new VectorMemoryPersistenceService(repository, embeddingService);

// Then passed again (redundantly) to the method
var vectorMemory = await LoadFromFilesAndSaveAsync(
    knowledgeFiles, 
    collectionName, 
    embeddingService,      // ? Redundant! persistenceService already has it
    persistenceService);
```

### How Embeddings Are Actually Generated

The embeddings are generated **inside** `VectorMemoryPersistenceService.SaveFragmentsAsync()`:

```csharp
public async Task SaveFragmentsAsync(...)
{
    foreach (var fragment in fragments)
    {
        // Uses the injected _embeddingService (from constructor)
        var embedding = await _embeddingService.GenerateEmbeddingAsync(
            fragment.Content);
        
        // Save to database with embedding
        // ...
    }
}
```

## Changes Made

### Before (Redundant Parameter)
```csharp
private static async Task<VectorMemory> LoadFromFilesAndSaveAsync(
    Dictionary<string, string> knowledgeFiles,
    string collectionName,
    LocalLlmEmbeddingService embeddingService,  // ? Not used!
    VectorMemoryPersistenceService persistenceService)
{
    // ...
    await persistenceService.SaveFragmentsAsync(...);  // Uses injected service
    // ...
}

// Called with:
vectorMemory = await LoadFromFilesAndSaveAsync(
    knowledgeFiles, 
    collectionName, 
    embeddingService,      // ? Passed but never used
    persistenceService);
```

### After (Cleaned Up)
```csharp
private static async Task<VectorMemory> LoadFromFilesAndSaveAsync(
    Dictionary<string, string> knowledgeFiles,
    string collectionName,
    VectorMemoryPersistenceService persistenceService)  // ? embeddingService removed
{
    // ...
    await persistenceService.SaveFragmentsAsync(...);  // Uses injected service
    // ...
}

// Called with:
vectorMemory = await LoadFromFilesAndSaveAsync(
    knowledgeFiles, 
    collectionName, 
    persistenceService);  // ? No longer passing redundant parameter
```

## Files Modified

- `OfflineAI/Modes/RunVectorMemoryWithDatabaseMode.cs`
  - Removed `embeddingService` parameter from `LoadFromFilesAndSaveAsync`
  - Updated 2 call sites to remove the argument

## Impact

### Benefits
- ? **Cleaner code** - No misleading unused parameters
- ? **Better clarity** - Shows that embeddings are handled by persistenceService
- ? **Easier to understand** - Dependency injection pattern is now clearer

### Testing
- ? Build successful
- ? All 16 tests pass (test wrapper doesn't need changes)
- ? No behavior changes - purely a refactoring

## Why This Matters

The redundant parameter could cause confusion:

**? Question:** "Where are embeddings generated?"
- **Confusing (before):** Method takes `embeddingService` but doesn't use it directly
- **Clear (after):** Method only takes `persistenceService`, which handles embeddings internally

## Dependency Injection Pattern

This cleanup makes the dependency injection pattern clearer:

```csharp
// 1. Create services with dependencies
var embeddingService = new LocalLlmEmbeddingService(...);
var repository = new VectorMemoryRepository(...);

// 2. Inject embeddingService into persistenceService
var persistenceService = new VectorMemoryPersistenceService(
    repository, 
    embeddingService);  // ? Injected here!

// 3. Use persistenceService (which internally uses embeddingService)
var vectorMemory = await LoadFromFilesAndSaveAsync(
    knowledgeFiles, 
    collectionName, 
    persistenceService);  // ? No need to pass embeddingService again!
```

## Related Code

The same pattern is used correctly in `LoadFromFilesInMemoryAsync`:

```csharp
private static async Task<VectorMemory> LoadFromFilesInMemoryAsync(
    Dictionary<string, string> knowledgeFiles,
    LocalLlmEmbeddingService embeddingService)  // ? Used directly here!
{
    // Creates VectorMemory with embeddingService
    var vectorMemory = new VectorMemory(embeddingService, "game-rules");
    // ...
}
```

This method DOES need `embeddingService` because it creates `VectorMemory` directly (no persistence layer).

## Summary

Removed a redundant parameter that was never used, making the code cleaner and the dependency injection pattern clearer. This is a pure refactoring with no behavior changes.
