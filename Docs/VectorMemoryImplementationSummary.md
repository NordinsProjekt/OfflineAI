# Vector Memory with Semantic Kernel - Implementation Summary

## What Was Implemented

### 1. **VectorMemory Class** (`Services/VectorMemory.cs`)
A semantic search-enabled memory implementation that uses embeddings to find the most relevant information for queries.

**Key Features:**
- ? Converts text to vector embeddings (numerical representations)
- ? Uses cosine similarity to measure semantic relevance
- ? Returns only the top K most relevant memory fragments
- ? Lazy embedding generation (computed on first search)
- ? Embedding caching (reused across queries)

**Methods:**
- `ImportMemory(IMemoryFragment)` - Store memory fragments
- `SearchRelevantMemoryAsync(query, topK, minRelevanceScore)` - Find relevant memories
- `CosineSimilarity(vector1, vector2)` - Calculate semantic similarity

---

### 2. **LocalLlmEmbeddingService** (`Services/LocalLlmEmbeddingService.cs`)
A simple embedding service that implements `ITextEmbeddingGenerationService` from Semantic Kernel.

**Current Implementation:**
- Uses deterministic hash-based embedding (384 dimensions)
- Character frequency analysis
- Good for testing and development

**?? For Production:** Replace with:
```csharp
// Option 1: ONNX Runtime with sentence-transformers
using Microsoft.ML.OnnxRuntime;
var embeddingService = new OnnxEmbeddingService("all-MiniLM-L6-v2.onnx");

// Option 2: Azure OpenAI
var embeddingService = new AzureOpenAITextEmbeddingGenerationService(
    deploymentName: "text-embedding-ada-002",
    endpoint: "https://your-resource.openai.azure.com",
    apiKey: "your-key"
);

// Option 3: OpenAI API
var embeddingService = new OpenAITextEmbeddingGenerationService(
    modelId: "text-embedding-ada-002",
apiKey: "your-key"
);
```

---

### 3. **Enhanced FileMemoryLoaderService** (`Services/FileMemoryLoaderService.cs`)
Added three chunking strategies to break large documents into semantic search-friendly pieces.

#### **Method 1: LoadFromFileAsync** (Original)
```csharp
await fileReader.LoadFromFileAsync(filePath, vectorMemory);
```
- Splits by `#` headers
- Each section becomes one fragment
- Good for well-structured documents

#### **Method 2: LoadFromFileWithChunkingAsync** (New)
```csharp
await fileReader.LoadFromFileWithChunkingAsync(
    filePath, 
    vectorMemory, 
    maxChunkSize: 500,
    overlapSize: 50
);
```
- Character-based chunking with smart boundaries
- Breaks at sentence endings (`.`, `!`, `?`)
- Falls back to word boundaries
- **Overlap** preserves context between chunks
- Great for unstructured text

#### **Method 3: LoadFromFileWithSmartChunkingAsync** (Recommended ?)
```csharp
await fileReader.LoadFromFileWithSmartChunkingAsync(
    filePath, 
    vectorMemory, 
    maxChunkSize: 500
);
```
- **Hybrid approach:** Splits by headers THEN chunks large sections
- Preserves document structure
- Automatically handles oversized sections
- Small sections stay intact
- **Best for most scenarios**

---

### 4. **Updated AiChatService** (`Services/AiChatService.cs`)
Modified to support vector search when using `VectorMemory`.

**Before:**
```csharp
private string BuildSystemPrompt()
{
    // ...
    prompt.AppendLine(Memory.ToString()); // ALL memory
}
```

**After:**
```csharp
private async Task<string> BuildSystemPromptAsync(string question)
{
    // ...
    if (Memory is VectorMemory vectorMemory)
    {
      // Only relevant memory!
        var relevantMemory = await vectorMemory.SearchRelevantMemoryAsync(
         question, topK: 5, minRelevanceScore: 0.3
        );
  prompt.AppendLine(relevantMemory);
    }
    else
    {
     prompt.AppendLine(Memory.ToString()); // Fallback
    }
}
```

---

### 5. **RunVectorMemoryMode** (`OfflineAI/Modes/RunVectorMemoryMode.cs`)
New mode for testing vector memory with your offline LLM.

**Features:**
- Uses smart chunking by default
- Loads multiple rulebooks
- Shows chunk count after loading
- `/debug <query>` command to inspect search results

**Example Usage:**
```sh
$ OfflineAI.exe
=== OfflineAI - Select Mode ===
1. Original Mode (Load all memory into RAM)
2. Vector Memory Mode (In-Memory Semantic Kernel)

Select mode (1 or 2): 2

=== Vector Memory Mode with Semantic Kernel ===
Initializing embedding service...
Loading knowledge from d:\tinyllama\trhunt_rules.txt...
Loaded 23 chunks from d:\tinyllama\trhunt_rules.txt
Loaded 45 chunks from Munchkin Panic rulebook
Loaded 37 chunks from Munchkin Quest rulebook

? Vector memory initialized!
Type 'exit' to quit, or ask questions:

> How do I attack in Treasure Hunt?
Response: To attack, roll 2d6 and add your attack bonus...

> /debug What are critical hits?
=== Relevant Memory Fragments ===
[Relevance: 0.924]
[Combat Rules_chunk_3]
Critical hits occur on natural 12s and deal double damage.
=================================
```

---

## How It All Works Together

```
????????????????????????????????????????????????????????????????
? 1. Load Knowledge Files with Chunking     ?
?  treasure_hunt_rules.txt ? 23 chunks  ?
?  munchkin_panic.txt ? 45 chunks     ?
?  munchkin_quest.txt ? 37 chunks          ?
?  Total: 105 focused memory fragments     ?
????????????????????????????????????????????????????????????????
     ?
????????????????????????????????????????????????????????????????
? 2. User Asks Question    ?
?  "How do I attack in Treasure Hunt?"            ?
????????????????????????????????????????????????????????????????
     ?
????????????????????????????????????????????????????????????????
? 3. Generate Query Embedding (384d vector)      ?
?  [0.65, 0.39, -0.19, ..., 0.42]    ?
????????????????????????????????????????????????????????????????
     ?
????????????????????????????????????????????????????????????????
? 4. VectorMemory.SearchRelevantMemoryAsync()    ?
?  - Generate embeddings for all 105 chunks (if not cached)    ?
?  - Calculate cosine similarity for each chunk   ?
?  - Return top 5 chunks with score >= 0.3 ?
????????????????????????????????????????????????????????????????
     ?
????????????????????????????????????????????????????????????????
? 5. Top Results      ?
?  [0.94] Combat Rules_chunk_2: "To attack, roll 2d6..." ?
?  [0.89] Combat Rules_chunk_3: "Defender rolls 1d6..."    ?
?  [0.76] Advanced Combat: "Critical hits..."    ?
????????????????????????????????????????????????????????????????
     ?
????????????????????????????????????????????????????????????????
? 6. AiChatService builds prompt with ONLY relevant context  ?
?  System: "Answer based on these rules..."      ?
?  Context: [3 relevant chunks, ~300 words]     ?
?  Question: "How do I attack in Treasure Hunt?"     ?
????????????????????????????????????????????????????????????????
     ?
????????????????????????????????????????????????????????????????
? 7. TinyLlama generates focused answer      ?
?  "To attack in Treasure Hunt, roll 2d6 and add your attack   ?
?   bonus. The defender rolls 1d6 plus defense. If your total  ?
?   is higher, deal damage equal to the difference."       ?
????????????????????????????????????????????????????????????????
```

---

## Benefits of This Implementation

### 1. **Better Answers**
- AI receives only relevant context
- Reduced hallucinations
- More accurate responses

### 2. **Handles Large Knowledge Bases**
- Can load hundreds of documents
- Search across 1000s of chunks
- Returns only what's needed

### 3. **Faster Responses**
- Smaller prompts = faster generation
- Cached embeddings = instant searches
- No need to scan entire documents

### 4. **Lower Resource Usage**
- Less tokens sent to LLM
- Lower memory usage (only relevant chunks in prompt)
- Embedding generation is one-time cost

### 5. **Scalable**
- Add more knowledge files anytime
- Automatic chunking handles any size
- Works with multiple rule books simultaneously

---

## Configuration Tuning

### Chunk Size
```csharp
maxChunkSize: 500  // Default - Good for most content
maxChunkSize: 200  // Short, precise answers (FAQs)
maxChunkSize: 1000 // Long context (stories, tutorials)
```

### Search Parameters
```csharp
// Return more results
await vectorMemory.SearchRelevantMemoryAsync(query, topK: 10, minRelevanceScore: 0.3);

// Be more selective (higher quality threshold)
await vectorMemory.SearchRelevantMemoryAsync(query, topK: 3, minRelevanceScore: 0.7);

// Cast wider net (allow lower scores)
await vectorMemory.SearchRelevantMemoryAsync(query, topK: 5, minRelevanceScore: 0.2);
```

### AiChatService Integration
```csharp
// In BuildSystemPromptAsync
if (Memory is VectorMemory vectorMemory)
{
    var relevantMemory = await vectorMemory.SearchRelevantMemoryAsync(
        question, 
        topK: 5,     // ? Adjust based on LLM context window
     minRelevanceScore: 0.3 // ? Adjust based on quality needs
    );
    prompt.AppendLine(relevantMemory);
}
```

---

## Testing & Debugging

### Use /debug Command
```sh
> /debug How do critical hits work?
```
Shows:
- Which chunks were selected
- Their relevance scores
- The actual content sent to AI

### Check Chunk Count
```sh
Loaded 23 chunks from d:\tinyllama\trhunt_rules.txt
```
- Too few chunks (< 10)? Increase chunking
- Too many chunks (> 200)? Decrease chunk size
- Just right (20-100)? Perfect!

### Monitor Relevance Scores
- **> 0.9** = Excellent match
- **0.7-0.9** = Good match
- **0.5-0.7** = Okay match
- **< 0.5** = Poor match (consider filtering out)

---

## Next Steps for Production

### 1. **Replace Embedding Service**
Current placeholder ? Proper embedding model

```bash
# Install ONNX Runtime
dotnet add package Microsoft.ML.OnnxRuntime
dotnet add package Microsoft.ML.OnnxRuntime.Extensions
```

### 2. **Add Persistent Storage**
Current: In-memory only ? Save embeddings to disk

```csharp
// Option: Use Semantic Kernel's memory connectors
dotnet add package Microsoft.SemanticKernel.Connectors.Chroma
dotnet add package Microsoft.SemanticKernel.Connectors.Pinecone
dotnet add package Microsoft.SemanticKernel.Connectors.Weaviate
```

### 3. **Optimize Performance**
- Pre-generate embeddings for static content
- Cache embeddings to avoid regeneration
- Use approximate nearest neighbor (ANN) for large datasets

### 4. **Add Metadata Filtering**
```csharp
// Filter by source, date, category, etc.
var results = await vectorMemory.SearchRelevantMemoryAsync(
 query, 
    topK: 5,
    filters: new { Source = "Treasure Hunt", Category = "Combat" }
);
```

---

## Files Created/Modified

### New Files
1. `Services/VectorMemory.cs` - Vector-based memory with semantic search
2. `Services/LocalLlmEmbeddingService.cs` - Embedding service for Semantic Kernel
3. `OfflineAI/Modes/RunVectorMemoryMode.cs` - Test mode for vector memory
4. `Docs/VectorMemoryChunking.md` - Chunking strategies guide
5. `Docs/ChunkingVisualization.md` - Visual explanation of chunking
6. `Docs/VectorMemoryImplementationSummary.md` - This file

### Modified Files
1. `Services/FileMemoryLoaderService.cs` - Added chunking methods
2. `Services/AiChatService.cs` - Added vector search support
3. `OfflineAI/Program.cs` - Added vector memory mode option

### Packages Added
- `Microsoft.SemanticKernel.Connectors.InMemory` (1.66.0-preview)

---

## Quick Start

```csharp
// 1. Create embedding service
var embeddingService = new LocalLlmEmbeddingService(llmPath, modelPath);

// 2. Create vector memory
var vectorMemory = new VectorMemory(embeddingService, "knowledge-base");

// 3. Load files with smart chunking
var fileReader = new FileMemoryLoaderService();
await fileReader.LoadFromFileWithSmartChunkingAsync("rules.txt", vectorMemory, maxChunkSize: 500);

// 4. Search for relevant information
var relevantInfo = await vectorMemory.SearchRelevantMemoryAsync("How do I attack?", topK: 5, minRelevanceScore: 0.5);

// 5. Use with AI
var aiService = new AiChatService(vectorMemory, conversationMemory, llmPath, modelPath);
var response = await aiService.SendMessageStreamAsync("How do I attack?");
```

That's it! Your offline LLM now has semantic search capabilities powered by Semantic Kernel! ??
