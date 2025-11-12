# Vector Memory Quick Reference Card

## ?? Quick Start
```csharp
// Setup
var embedding = new LocalLlmEmbeddingService(llmPath, modelPath);
var memory = new VectorMemory(embedding, "collection-name");

// Load with smart chunking (RECOMMENDED)
var loader = new FileMemoryLoaderService();
await loader.LoadFromFileWithSmartChunkingAsync("file.txt", memory, 500);

// Search
var results = await memory.SearchRelevantMemoryAsync("query", topK: 5, minRelevanceScore: 0.5);
```

## ?? Chunking Methods Comparison

| Method | Use When | Chunk Count | Best For |
|--------|----------|-------------|----------|
| `LoadFromFileAsync` | File has `#` headers | Low (5-20) | Structured docs |
| `LoadFromFileWithChunkingAsync` | Unstructured text | High (50-200) | Raw text files |
| `LoadFromFileWithSmartChunkingAsync` ? | Any file | Medium (20-100) | **Everything** |

## ?? Configuration Guide

### Chunk Size Selection
```csharp
// FAQs, definitions
maxChunkSize: 200

// Game rules, how-tos (DEFAULT)
maxChunkSize: 500

// Stories, long articles
maxChunkSize: 1000
```

### Search Parameters
```csharp
// Precise (fewer, better results)
topK: 3, minRelevanceScore: 0.7

// Balanced (DEFAULT)
topK: 5, minRelevanceScore: 0.5

// Broad (more context)
topK: 10, minRelevanceScore: 0.3
```

## ?? Relevance Score Interpretation

| Score | Meaning | Action |
|-------|---------|--------|
| 0.9-1.0 | Perfect match | ? Use it! |
| 0.7-0.9 | Highly relevant | ? Great context |
| 0.5-0.7 | Somewhat related | ?? Maybe useful |
| < 0.5 | Not relevant | ? Filter out |

## ?? Debug Commands

```sh
# Test semantic search
> /debug What are combat rules?

# View relevance scores
=== Relevant Memory Fragments ===
[Relevance: 0.924]
[Combat Rules_chunk_2]
Roll 2d6 to attack...
=================================
```

## ?? Best Practices

### ? DO
- Use `LoadFromFileWithSmartChunkingAsync` for most cases
- Set `maxChunkSize: 500` as default
- Test with `/debug` to verify chunk quality
- Load multiple knowledge files into same VectorMemory
- Check relevance scores (aim for > 0.7)

### ? DON'T
- Load entire files as single fragments
- Make chunks too large (> 1000 chars)
- Make chunks too small (< 100 chars)
- Ignore low relevance scores
- Skip testing with `/debug`

## ?? Common Patterns

### Multiple Files
```csharp
var memory = new VectorMemory(embedding, "rpg-rules");
await loader.LoadFromFileWithSmartChunkingAsync("combat.txt", memory);
await loader.LoadFromFileWithSmartChunkingAsync("magic.txt", memory);
await loader.LoadFromFileWithSmartChunkingAsync("items.txt", memory);
```

### Custom Chunk Sizes by Content Type
```csharp
// Short FAQs
await loader.LoadFromFileWithChunkingAsync("faq.txt", memory, 200, 20);

// Standard rules
await loader.LoadFromFileWithSmartChunkingAsync("rules.txt", memory, 500);

// Long lore documents
await loader.LoadFromFileWithSmartChunkingAsync("lore.txt", memory, 1000);
```

### AiChatService Integration
```csharp
var service = new AiChatService(
    vectorMemory,     // ? Uses semantic search
    conversationMemory,  // ? Stores conversation history
    llmPath,
    modelPath
);

var response = await service.SendMessageStreamAsync("How do I attack?");
// ? AI gets only relevant combat chunks, not entire rulebook!
```

## ?? Troubleshooting

### Problem: Low relevance scores (all < 0.5)
**Solution:** Chunks might be too large. Reduce `maxChunkSize`:
```csharp
await loader.LoadFromFileWithSmartChunkingAsync(file, memory, maxChunkSize: 300);
```

### Problem: Too many chunks (> 200)
**Solution:** Increase `maxChunkSize` or use section-based loading:
```csharp
await loader.LoadFromFileAsync(file, memory); // Sections only
```

### Problem: Answers missing information
**Solution:** Increase `topK` and lower `minRelevanceScore`:
```csharp
await memory.SearchRelevantMemoryAsync(query, topK: 10, minRelevanceScore: 0.3);
```

### Problem: Irrelevant results in search
**Solution:** Increase `minRelevanceScore`:
```csharp
await memory.SearchRelevantMemoryAsync(query, topK: 5, minRelevanceScore: 0.7);
```

## ?? Required Packages
```xml
<PackageReference Include="Microsoft.SemanticKernel" Version="1.66.0" />
<PackageReference Include="Microsoft.SemanticKernel.Connectors.InMemory" Version="1.66.0-preview" />
```

## ?? Learn More
- `Docs/VectorMemoryChunking.md` - Detailed chunking guide
- `Docs/ChunkingVisualization.md` - Visual explanations
- `Docs/VectorMemoryImplementationSummary.md` - Complete implementation details

## ?? Example Session
```sh
$ OfflineAI.exe
Select mode: 2

Initializing embedding service...
Loaded 23 chunks from trhunt_rules.txt
Loaded 45 chunks from munchkin_panic.txt
? Vector memory initialized!

> How do critical hits work?
Loading: .....
Response: Critical hits occur when you roll a natural 12 on your attack roll. 
They deal double damage and ignore armor...

> /debug critical hits
[Relevance: 0.924] [Combat_chunk_5] Critical hits on natural 12s...
[Relevance: 0.876] [Combat_chunk_6] Some weapons increase crit range...
[Relevance: 0.732] [Advanced_chunk_2] Crit multipliers stack...
```

---

**Remember:** Smart chunking + Vector search = Better AI answers! ??
