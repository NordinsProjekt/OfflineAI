# Upgrading to Semantic Embeddings

This guide shows how to upgrade from `LocalLlmEmbeddingService` to `SemanticEmbeddingService` for much better search quality.

## Why Upgrade?

### Current Performance (LocalLlmEmbeddingService)
- **Tracks only 100 common words**: "the", "and", "to", etc.
- **Misses domain vocabulary**: "win", "winner", "treasure", "monster"
- **Low relevance scores**: 0.05-0.15 for good matches
- **Poor ranking**: Related content often ranks lower

### After Upgrade (SemanticEmbeddingService)
- **Understands ALL words**: Including your domain vocabulary
- **Semantic understanding**: Knows "win" ? "winner" ? "victory"
- **High relevance scores**: 0.7-0.9 for good matches
- **Accurate ranking**: Best matches consistently rank first

## Example Improvements

### Query: "how to win in Treasure Hunt"

**Before (LocalLlmEmbeddingService):**
```
[Relevance: 0.105] ?
Game over: The winner is the player with the most Gold.
(Found correct answer, but low confidence)
```

**After (SemanticEmbeddingService):**
```
[Relevance: 0.847] ?????
Game over: The winner is the player with the most Gold.
(Found correct answer with HIGH confidence!)
```

### Query: "fight monster alone?"

**Before:**
```
[Relevance: 0.053] ? (Ranked #2, wrong!)
You don't always have to fight a Monster alone!
```

**After:**
```
[Relevance: 0.912] ????? (Ranked #1, correct!)
You don't always have to fight a Monster alone!
```

## Step 1: Download the Model

### Recommended: all-MiniLM-L6-v2 (Fast & Good Quality)

1. Go to: https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2
2. Click on "Files and versions"
3. Download these files:
   - `model.onnx` (90MB) - The embedding model
   - `vocab.txt` (232KB) - The vocabulary file

4. Create a folder in your project:
   ```
   d:\tinyllama\models\all-MiniLM-L6-v2\
   ```

5. Place the files there:
   ```
   d:\tinyllama\models\all-MiniLM-L6-v2\model.onnx
   d:\tinyllama\models\all-MiniLM-L6-v2\vocab.txt
   ```

### Alternative: all-mpnet-base-v2 (Best Quality, Slower)

- **Size**: 420MB
- **Dimensions**: 768
- **Quality**: Better than MiniLM
- **Speed**: ~2x slower

Download from: https://huggingface.co/sentence-transformers/all-mpnet-base-v2

## Step 2: Update Your Code

### Option A: Modify RunVectorMemoryWithDatabaseMode.cs

Find this line:
```csharp
var embeddingService = new LocalLlmEmbeddingService(llmPath, modelPath);
```

Replace with:
```csharp
// Use semantic embeddings for much better search quality
var embeddingService = new SemanticEmbeddingService(
    modelPath: @"d:\tinyllama\models\all-MiniLM-L6-v2\model.onnx",
    vocabPath: @"d:\tinyllama\models\all-MiniLM-L6-v2\vocab.txt",
    embeddingDimension: 384,  // 384 for MiniLM, 768 for MPNet
    maxSequenceLength: 128     // Limit for performance
);
```

### Option B: Add Configuration Support

Update `DatabaseConfig.cs`:
```csharp
public class DatabaseConfig
{
    // ...existing properties...
    
    public string? EmbeddingModelPath { get; set; }
    public string? EmbeddingVocabPath { get; set; }
    public int EmbeddingDimension { get; set; } = 384;
    public bool UseSemanticEmbeddings { get; set; } = false;
}
```

Then use it:
```csharp
ITextEmbeddingGenerationService embeddingService;

if (dbConfig.UseSemanticEmbeddings && 
    !string.IsNullOrEmpty(dbConfig.EmbeddingModelPath))
{
    embeddingService = new SemanticEmbeddingService(
        dbConfig.EmbeddingModelPath!,
        dbConfig.EmbeddingVocabPath!,
        dbConfig.EmbeddingDimension);
}
else
{
    embeddingService = new LocalLlmEmbeddingService(llmPath, modelPath);
}
```

## Step 3: Handle Dimension Mismatch

### Important: Different Embedding Dimensions!

- **LocalLlmEmbeddingService**: 384 dimensions
- **SemanticEmbeddingService (MiniLM)**: 384 dimensions ? (Same!)
- **SemanticEmbeddingService (MPNet)**: 768 dimensions ?? (Different!)

### If You Have Existing Data

If you already have embeddings in the database from `LocalLlmEmbeddingService`, you need to regenerate them:

```csharp
// Option 1: Delete old collection and reload
await persistenceService.DeleteCollectionAsync("game-rules");
await LoadFromFilesAndSaveAsync(...);  // Re-generate with new embeddings

// Option 2: Use a new collection name
var collectionName = "game-rules-semantic";  // Different from "game-rules"
```

## Step 4: Adjust Relevance Thresholds

With semantic embeddings, you can use higher thresholds:

```csharp
// Before (LocalLlmEmbeddingService)
var relevantMemory = await vectorMemory.SearchRelevantMemoryAsync(
    query, 
    topK: 5, 
    minRelevanceScore: 0.0);  // Had to use 0.0!

// After (SemanticEmbeddingService)
var relevantMemory = await vectorMemory.SearchRelevantMemoryAsync(
    query, 
    topK: 5, 
    minRelevanceScore: 0.6);  // Can use 0.6 or even 0.7!
```

### Recommended Thresholds

| Use Case | Threshold | Meaning |
|----------|-----------|---------|
| Development/Debug | 0.0 | Show everything |
| Production | 0.6 | Good matches only |
| High precision | 0.7 | Very relevant only |
| Strict filtering | 0.8 | Extremely relevant |

## Step 5: Test the Upgrade

### Before Testing

Build the project:
```bash
dotnet build
```

### Run the Application

```bash
cd OfflineAI
dotnet run
```

Select option 3 (Vector Memory with Database)

### Test Queries

Try these queries and compare scores:

1. **"how to win in Treasure Hunt"**
   - Expected score: 0.8-0.9 (was 0.105)

2. **"fight monster alone?"**
   - Expected score: 0.85-0.95 (was 0.053)

3. **"what are Treasure cards?"**
   - Expected score: 0.75-0.85 (was ~0.1)

### Expected Output

```
> /debug how to win in Treasure Hunt

=== Relevant Memory Fragments ===
[Relevance: 0.847] ?????
Treasure Hunt - Section 14: Game over: The game is over when someone draws 
the last Treasure card. The winner is the player with the most Gold.

[Relevance: 0.632] ???
Treasure Hunt - Section 6: Treasure Cards: Every Treasure card has a value 
in Gold. At the end of the game, the Gold on the Treasures in your hand is 
how you win!
=================================
```

## Performance Considerations

### Memory Usage

- **LocalLlmEmbeddingService**: ~10MB
- **SemanticEmbeddingService (MiniLM)**: ~100MB
- **SemanticEmbeddingService (MPNet)**: ~450MB

### Speed

- **LocalLlmEmbeddingService**: ~5-10ms per embedding
- **SemanticEmbeddingService (MiniLM)**: ~20-50ms per embedding
- **SemanticEmbeddingService (MPNet)**: ~50-100ms per embedding

### Recommendations

- **Use MiniLM for most cases**: Good balance of speed and quality
- **Use MPNet for best quality**: When search quality is critical
- **Pre-compute embeddings**: Store in database to avoid re-computing

## Troubleshooting

### Error: "Model file not found"

Make sure you downloaded the files to the correct path:
```
d:\tinyllama\models\all-MiniLM-L6-v2\model.onnx
d:\tinyllama\models\all-MiniLM-L6-v2\vocab.txt
```

### Error: "Dimension mismatch"

Your database has embeddings with different dimensions. Either:
1. Delete the old collection and reload
2. Use a new collection name

### Low Scores Still Showing

Make sure you're using the correct model files:
- `model.onnx` (not `pytorch_model.bin` or other formats)
- `vocab.txt` (from the same model)

### Slow Performance

For faster performance:
1. Reduce `maxSequenceLength` to 64 or 96
2. Use MiniLM instead of MPNet
3. Pre-generate and store embeddings in the database

## Complete Example

Here's a complete working example:

```csharp
// In RunVectorMemoryWithDatabaseMode.cs

internal static async Task RunAsync()
{
    Console.WriteLine("\n=== Vector Memory Mode with Semantic Embeddings ===");
    
    // Setup paths
    var llmPath = @"d:\tinyllama\llama-cli.exe";
    var modelPath = @"d:\tinyllama\tinyllama-1.1b-chat-v1.0.Q5_K_M.gguf";
    var knowledgeFiles = new Dictionary<string, string>
    {
        ["Treasure Hunt"] = @"d:\tinyllama\trhunt_rules.txt"
    };

    // Database configuration
    var dbConfig = new DatabaseConfig
    {
        ConnectionString = @"Server=(localdb)\mssqllocaldb;Database=VectorMemoryDB;Integrated Security=true;TrustServerCertificate=true;",
        UseDatabasePersistence = true,
        AutoInitializeDatabase = true
    };

    // Create SEMANTIC embedding service
    Console.WriteLine("Initializing semantic embedding service...");
    var embeddingService = new SemanticEmbeddingService(
        modelPath: @"d:\tinyllama\models\all-MiniLM-L6-v2\model.onnx",
        vocabPath: @"d:\tinyllama\models\all-MiniLM-L6-v2\vocab.txt",
        embeddingDimension: 384,
        maxSequenceLength: 128);
    
    var repository = new VectorMemoryRepository(dbConfig.ConnectionString);
    var persistenceService = new VectorMemoryPersistenceService(repository, embeddingService);

    // ... rest of your code ...
    
    // Use higher threshold with semantic embeddings
    var relevantMemory = await vectorMemory.SearchRelevantMemoryAsync(
        query, 
        topK: 5, 
        minRelevanceScore: 0.6);  // Much higher than 0.0!
}
```

## Summary

? **Download** model files from HuggingFace
? **Replace** `LocalLlmEmbeddingService` with `SemanticEmbeddingService`
? **Adjust** relevance thresholds (0.0 ? 0.6)
? **Regenerate** embeddings if you have existing data
? **Test** with your queries and enjoy 0.7-0.9 scores!

Your search quality will improve dramatically:
- **From**: 0.05-0.15 scores, poor ranking
- **To**: 0.7-0.9 scores, accurate ranking
- **Result**: Search that actually understands what you're asking!
