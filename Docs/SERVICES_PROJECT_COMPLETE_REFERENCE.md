# Services Project - Complete Component Reference

## Overview
The **Services** project is the business logic layer containing 25+ files organized into 6 major categories. This document provides comprehensive reference for all major components.

---

## Table of Contents
1. [Memory Components](#memory-components)
2. [Management Components](#management-components)
3. [Configuration Components](#configuration-components)
4. [Utilities Components](#utilities-components)
5. [Repository Interfaces](#repository-interfaces)
6. [UI Components](#ui-components)

---

# Memory Components

## DatabaseVectorMemory.cs

### Purpose
Database-backed vector memory implementing hybrid search (vector + fuzzy + exact string matching) for RAG queries.

### Key Responsibilities
1. **Semantic Search** - Generate embeddings and calculate cosine similarity
2. **Hybrid Matching** - Combine vector, fuzzy, and exact string matching
3. **Domain Filtering** - Filter results by knowledge domain
4. **Context Assembly** - Build formatted context for LLM

### Class Definition
```csharp
public class DatabaseVectorMemory : ISearchableMemory
{
    private readonly ITextEmbeddingGenerationService _embeddingService;
    private readonly IVectorMemoryRepository _repository;
    private string _collectionName;
}
```

### Key Methods

#### `SearchRelevantMemoryAsync()`
**Purpose**: Search database for relevant fragments using hybrid search.

**Signature**:
```csharp
public async Task<string?> SearchRelevantMemoryAsync(
    string query,
    int topK = 5,
    double minRelevanceScore = 0.5,
    List<string>? domainFilter = null,
    int? maxCharsPerFragment = null,
    bool includeMetadata = true)
```

**Search Algorithm**:
```
1. Extract Keywords (remove stop words)
   ?
2. Generate Query Embedding
   ?
3. Load Fragments (with optional domain filter)
   ?
4. Calculate Hybrid Scores:
   - Vector similarity (weighted: 40% cat, 30% content, 30% combined)
   - Exact string match (+0.5 boost)
   - Fuzzy match (+0.25 to +0.4 boost)
   - Important phrase match (+0.4 boost)
   ?
5. Filter by threshold & Take Top-K
   ?
6. Assemble formatted context
```

**Example Usage**:
```csharp
var memory = new DatabaseVectorMemory(embeddingService, repository, "game-rules");

var context = await memory.SearchRelevantMemoryAsync(
    query: "How to win Munchkin?",
    topK: 3,
    minRelevanceScore: 0.5,
    domainFilter: new List<string> { "board-game-munchkin" },
    maxCharsPerFragment: 400,
    includeMetadata: true
);

// Result:
// [Relevance: 0.892]
// [Winning Conditions]
// To win Munchkin, you must reach Level 10...
//
// [Relevance: 0.754]
// [Victory Points]
// Victory is achieved by...
```

### Hybrid Search Details

#### Phase 1: Important Phrase Matching
```csharp
var importantPhrases = new[] {
    "how to win", "how to play", "how to setup", "how to fight"
};

// If query contains "how to win" AND category contains "how to win"
if (originalQueryLower.Contains(phrase) && categoryLower.Contains(phrase))
{
    score += 0.4;  // Strong boost
}
```

#### Phase 2: Exact Word Matching
```csharp
// Check if query word appears as complete word in category
if (categoryWords.Contains(queryWord))
{
    score += 0.5;  // Strong boost for exact match
}
// Or starts with query word (e.g., "leksak" matches "leksaksbåt")
else if (categoryWords.Any(w => w.StartsWith(queryWord)))
{
    score += 0.5;
}
// Substring match only
else if (categoryLower.Contains(queryLower))
{
    score += 0.3;  // Moderate boost
}
```

#### Phase 3: Fuzzy Matching (Levenshtein Distance)
```csharp
var distance = CalculateLevenshteinDistance(queryWord, categoryWord);
var maxAllowedDistance = queryWord.Length >= 6 ? 2 : 1;

if (distance <= maxAllowedDistance)
{
    var fuzzyBoost = distance == 1 ? 0.4 : 0.25;
    score += fuzzyBoost;
}
```

**Example Fuzzy Matches**:
- "leksaksbot" ? "leksaksbåt" (distance=1) ? +0.4 boost
- "adapter" ? "adaptor" (distance=1) ? +0.4 boost
- "munchkin" ? "munchkn" (distance=1) ? +0.4 boost

### Keyword Extraction

#### Swedish Mode
```csharp
var stopWords = new[] {
    "hur", "var", "vad", "när", "varför",
    "ska", "kan", "måste", "bör",
    "sorterar", "sortera", "slänger", "slänga",
    "jag", "vi", "du", "ni", "man"
};

// Input: "Hur sorterar jag plastpåsar?"
// Output: "plastpåsar"
```

#### English Mode
```csharp
var lightStopWords = new[] {
    "the", "a", "an", "in", "on", "at",
    "is", "are", "was", "were"
};

// Input: "How to win in Munchkin?"
// Output: "how to win munchkin"  (phrase preserved)
```

---

## VectorMemoryPersistenceService.cs

### Purpose
Handles document ingestion: embedding generation, chunking, and database persistence.

### Key Responsibilities
1. **Triple Embedding Generation** - Category, content, combined
2. **Batch Processing** - Process multiple fragments efficiently
3. **Progress Tracking** - Real-time progress display
4. **Memory Management** - Aggressive GC every 2 fragments

### Class Definition
```csharp
public class VectorMemoryPersistenceService
{
    private readonly IVectorMemoryRepository _repository;
    private readonly ITextEmbeddingGenerationService _embeddingService;
}
```

### Key Methods

#### `SaveFragmentsAsync()`
**Purpose**: Generate embeddings and save fragments to database.

**Signature**:
```csharp
public async Task SaveFragmentsAsync(
    List<MemoryFragment> fragments,
    string collectionName,
    string? sourceFile = null,
    bool replaceExisting = false)
```

**Process**:
```
For each fragment:
  1. Generate category embedding (clean "##" markers)
  2. Generate content embedding
  3. Generate combined embedding
  4. Track progress and timing
  5. Run GC every 2 fragments
  ?
Create MemoryFragmentEntity with all 3 embeddings
  ?
Bulk save to database
```

**Example Output**:
```
???????????????????????????????????????????????
?  Generating WEIGHTED Embeddings for 10 Fragments
?  Strategy: Category (40%) + Content (30%) + Combined (30%)
???????????????????????????????????????????????

???????????????????????????????????????????????
  Fragment 1/10
  Category: How to Win
???????????????????????????????????????????????
  Progress: [?????????????????????????] 10.0%

  ??  Elapsed: 5.2s
  ? Avg Time: 1.73s per embedding
  ?? Remaining: ~46s (27 embeddings)
  ?? Memory: 1845 MB

  ?? [1/3] Category embedding... Done (1.2s)
  ?? [2/3] Content embedding... Done (2.1s)
  ?? [3/3] Combined embedding... Done (1.9s)

  ??? Running garbage collection...
  ? After GC: 1234 MB
```

**Performance**:
- **Embedding Generation**: ~1.5-2s per embedding (CPU)
- **Total Time**: ~45-60s for 10 fragments (30 embeddings)
- **Memory**: Peaks at ~2GB, GC reduces to ~1.5GB

---

## MultiFormatFileWatcher.cs

### Purpose
Monitors inbox folder for new documents (PDF, TXT) and triggers processing.

### Key Responsibilities
1. **File System Monitoring** - Watch for new/changed files
2. **Duplicate Detection** - Skip already-processed files
3. **Async Processing** - Non-blocking document processing
4. **Error Handling** - Graceful failure with logging

### Class Definition
```csharp
public class MultiFormatFileWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly string _archiveFolder;
    private readonly Func<string, Task> _onFileDetectedAsync;
}
```

### Usage Example
```csharp
var watcher = new MultiFormatFileWatcher(
    inboxFolder: "C:/inbox",
    archiveFolder: "C:/archive",
    onFileDetectedAsync: async (filePath) =>
    {
        Console.WriteLine($"Processing {filePath}...");
        await inboxProcessingService.ProcessFileAsync(filePath);
    }
);

watcher.StartWatching();
// Now monitoring for new files...

// When done:
watcher.Dispose();
```

---

## PdfFragmentProcessor.cs

### Purpose
Extracts text from PDF files and splits into memory fragments.

### Key Responsibilities
1. **PDF Text Extraction** - Using UglyToad.PdfPig
2. **Heading Detection** - Recognize category markers (`## `)
3. **Fragment Creation** - Split into category-content pairs
4. **Error Handling** - Handle corrupted PDFs gracefully

### Example PDF Structure
```
## Winning Conditions
To win Munchkin, you must reach Level 10 by defeating monsters...

## Player Actions
On your turn, you may do one of the following...

## Combat
When you fight a monster...
```

**Output**:
```csharp
[
    new MemoryFragment("Winning Conditions", "To win Munchkin, you must..."),
    new MemoryFragment("Player Actions", "On your turn, you may..."),
    new MemoryFragment("Combat", "When you fight a monster...")
]
```

---

# Management Components

## InboxProcessingService.cs

### Purpose
Orchestrates the document ingestion pipeline from file to database.

### Workflow
```
1. Detect new file (via MultiFormatFileWatcher)
   ?
2. Extract text (PdfFragmentProcessor)
   ?
3. Parse fragments (category + content)
   ?
4. Clean text (MemoryFragmentCleaner)
   ?
5. Generate embeddings (VectorMemoryPersistenceService)
   ?
6. Save to database
   ?
7. Move to archive folder
```

---

## CollectionManagementService.cs

### Purpose
Manage vector memory collections (create, list, delete, stats).

### Key Methods
```csharp
public class CollectionManagementService
{
    public async Task<List<string>> GetCollectionsAsync();
    public async Task<bool> CollectionExistsAsync(string name);
    public async Task DeleteCollectionAsync(string name);
    public async Task<CollectionStats> GetStatsAsync(string name);
}
```

---

## BotPersonalityService.cs

### Purpose
Manage bot personalities (system prompts, behavior profiles).

### Usage
```csharp
var personality = await botPersonalityService.GetActivePersonalityAsync();
Console.WriteLine(personality.SystemPrompt);
// Output: "You are a helpful assistant specialized in board games..."
```

---

# Configuration Components

## AppConfiguration.cs

### Purpose
Strongly-typed application configuration.

### Structure
```csharp
public class AppConfiguration
{
    public LlmSettings Llm { get; set; }
    public EmbeddingSettings Embedding { get; set; }
    public FolderSettings Folders { get; set; }
    public PoolSettings Pool { get; set; }
    public DebugSettings Debug { get; set; }
    public GenerationSettings Generation { get; set; }
}
```

### Example
```json
{
  "AppConfiguration": {
    "Llm": {
      "ExecutablePath": "C:/llama.cpp/llama-cli.exe",
      "ModelPath": "C:/models/tinyllama.gguf"
    },
    "Generation": {
      "MaxTokens": 200,
      "Temperature": 0.3,
      "TopK": 30
    }
  }
}
```

---

## GenerationSettingsService.cs

### Purpose
Runtime management of LLM generation parameters.

### Features
- Update temperature, max tokens, penalties
- Persist changes to configuration
- Notify subscribers of changes

---

# Utilities Components

## VectorExtensions.cs

### Purpose
Vector math operations for embeddings.

### Key Methods

#### `WeightedCosineSimilarity()`
```csharp
public static double WeightedCosineSimilarity(
    ReadOnlyMemory<float> query,
    ReadOnlyMemory<float> categoryEmbedding,  // Weight: 40%
    ReadOnlyMemory<float> contentEmbedding,   // Weight: 30%
    ReadOnlyMemory<float> combinedEmbedding)  // Weight: 30%
{
    double categoryScore = query.CosineSimilarity(categoryEmbedding);
    double contentScore = query.CosineSimilarity(contentEmbedding);
    double combinedScore = query.CosineSimilarity(combinedEmbedding);
    
    return (categoryScore * 0.4) + (contentScore * 0.3) + (combinedScore * 0.3);
}
```

#### `CosineSimilarity()`
```csharp
public static double CosineSimilarity(
    this ReadOnlyMemory<float> vector1,
    ReadOnlyMemory<float> vector2)
{
    double dotProduct = 0;
    for (int i = 0; i < vector1.Length; i++)
        dotProduct += vector1.Span[i] * vector2.Span[i];
    
    return dotProduct;  // Assumes unit vectors
}
```

---

## DocumentChunker.cs

### Purpose
Split large documents into LLM-compatible chunks.

### Strategy
- **Max chunk size**: 1500 characters
- **Overlap**: 150 characters (10%)
- **Boundary detection**: Split on sentences

### Example
```csharp
var chunks = DocumentChunker.ChunkDocument(longText, maxChunkSize: 1500, overlap: 150);

// Input: 5000 characters
// Output:
// Chunk 1: chars 0-1500
// Chunk 2: chars 1350-2850 (150 overlap)
// Chunk 3: chars 2700-4200 (150 overlap)
// Chunk 4: chars 4050-5000 (150 overlap)
```

---

## EosEofDebugger.cs

### Purpose
Detect and clean EOS/EOF markers from LLM output and RAG context.

### Methods
```csharp
public static class EosEofDebugger
{
    // Scan for markers
    public static ScanReport ScanForMarkers(string text, string context);
    
    // Clean markers
    public static string CleanMarkers(string text);
    
    // Validate clean before LLM
    public static void ValidateCleanBeforeLlm(string text, string context);
}
```

### Detected Markers
```csharp
private static readonly string[] EosMarkers = {
    "</s>",              // Llama EOS
    "<|endoftext|>",     // GPT-style
    "<|eot_id|>",        // Llama 3
    "<|end|>",           // TinyLlama
    "<|im_end|>"         // ChatML
};
```

---

## MemoryFragmentCleaner.cs

### Purpose
Clean and normalize text before embedding generation.

### Operations
- Remove null bytes (`\0`)
- Remove replacement characters (`\ufffd`)
- Normalize whitespace
- Remove control characters
- Truncate to max length

---

# Repository Interfaces

## IVectorMemoryRepository.cs

### Purpose
Data access contract for vector memory operations.

### Key Methods
```csharp
public interface IVectorMemoryRepository
{
    Task InitializeDatabaseAsync();
    Task<Guid> SaveAsync(MemoryFragmentEntity entity);
    Task BulkSaveAsync(IEnumerable<MemoryFragmentEntity> entities);
    Task<List<MemoryFragmentEntity>> LoadByCollectionAsync(string collectionName);
    Task<List<MemoryFragmentEntity>> LoadByCollectionAndDomainsAsync(
        string collectionName, List<string> domainFilter);
    Task<int> GetCountAsync(string collectionName);
    Task<bool> CollectionExistsAsync(string collectionName);
    Task DeleteCollectionAsync(string collectionName);
}
```

---

## IKnowledgeDomainRepository.cs

### Purpose
Data access contract for knowledge domain management.

### Example
```csharp
public interface IKnowledgeDomainRepository
{
    Task<List<KnowledgeDomainEntity>> GetAllAsync();
    Task<KnowledgeDomainEntity?> GetByIdAsync(string domainId);
    Task<Guid> SaveAsync(KnowledgeDomainEntity entity);
    Task DeleteAsync(string domainId);
}
```

---

# UI Components

## DisplayService.cs

### Purpose
Console output formatting for debugging and monitoring.

### Methods
```csharp
public static class DisplayService
{
    public static void ShowGenerationSettings(GenerationSettings settings, bool ragEnabled);
    public static void ShowSystemPromptDebug(string prompt, bool debug);
    public static void ShowEmbeddingServiceInitialized(string model, int dimensions, bool gpuEnabled);
    public static void WriteLine(string message);
}
```

---

## ExceptionMessageService.cs

### Purpose
Generate user-friendly error messages.

### Methods
```csharp
public static class ExceptionMessageService
{
    public static string BertModelNotFound(string path);
    public static string EmbeddingGenerationFailed(string exceptionType, string message);
    public static string LlmExecutionFailed(string modelPath, string error);
}
```

---

## Integration Diagram

```
AiDashboard (Presentation)
    ? uses
Services (Business Logic)
    ??? VectorMemoryPersistenceService
    ??? CollectionManagementService
    ??? InboxProcessingService
    ??? DatabaseVectorMemory
    ??? GenerationSettingsService
    ??? Configuration
    ? depends on
Infrastructure.Data.Dapper (Data Access)
    ??? VectorMemoryRepository
    ??? KnowledgeDomainRepository
    ??? BotPersonalityRepository
    ? accesses
SQL Server Database
```

---

## Document Version
- **Files**: 25+ files in Services project
- **Purpose**: Business logic layer for OfflineAI solution
- **Key Features**: Hybrid search, triple embeddings, document processing, configuration
- **Last Updated**: 2024
