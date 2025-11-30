# AI Project - Architecture Decisions Record (ADR)

## Document Purpose
This document captures the key architectural decisions made in the **AI** project, explaining the rationale, trade-offs, and implications for the entire OfflineAI solution.

---

## Decision 1: Model Instance Pooling Pattern

### Context
Running LLM inference requires loading large models (1-7GB) into memory, which can take 5-30 seconds. In a web application with concurrent users, repeatedly loading/unloading models would create unacceptable latency.

### Decision
Implement an **Object Pool pattern** (`ModelInstancePool`) that maintains N pre-loaded LLM process instances in memory, allowing concurrent users to share warm instances.

### Rationale
- **Performance**: Eliminates cold-start overhead (5-30 seconds ? immediate response)
- **Concurrency**: Supports multiple simultaneous users (configurable 2-10 instances)
- **Resource Management**: Limits memory usage by capping pool size
- **Automatic Cleanup**: `PooledInstance` uses IDisposable pattern to automatically return instances to pool

### Trade-offs
- **Memory Cost**: Each instance consumes ~1-1.5GB RAM (for TinyLlama 1.1B)
- **Complexity**: Requires careful lifecycle management and health monitoring
- **Benefits Outweigh Costs**: The performance gain (seconds ? milliseconds) justifies the memory usage

### Impact on Solution
- **AiDashboard**: Can serve multiple concurrent users without degradation
- **Scalability**: Configurable pool size allows tuning for different server capacities
- **User Experience**: Near-instant responses after initial warmup

### Configuration
```json
"Pool": {
  "MaxInstances": 2,      // 2-3 for <10 users, 8-10 for 50-100 users
  "TimeoutMs": 45000      // Per-query timeout (45 seconds)
}
```

---

## Decision 2: Per-Process LLM Execution (Not Interactive Mode)

### Context
llama.cpp supports both:
1. **Interactive mode**: Single persistent process with stdin/stdout communication
2. **Process-per-query**: Spawn new process for each query, model stays in memory

### Decision
Use **process-per-query** approach where `PersistentLlmProcess` spawns a new `llama-cli.exe` process for each request.

### Rationale
- **Reliability**: Process crashes don't corrupt pool state
- **Simpler Implementation**: No need for complex stdin/stdout protocol parsing
- **Model Caching**: OS-level model caching keeps subsequent loads fast
- **Timeout Handling**: Easier to kill hung processes
- **State Isolation**: Each query is independent, reducing state management complexity

### Trade-offs
- **Slight Overhead**: Process spawn (~100-500ms) vs. zero for interactive
- **No True Streaming**: Can't stream tokens in real-time (acceptable for current use case)
- **Alternative Considered**: llama.cpp server mode with HTTP API (future enhancement)

### Impact on Solution
- **Simplicity**: Reduces complexity in process management
- **Robustness**: Individual query failures don't affect other users
- **Maintenance**: Easier to debug and troubleshoot issues

### Future Enhancement
Consider migrating to **llama.cpp server mode** for:
- True token streaming (better UX for long responses)
- Lower per-query overhead
- HTTP API integration
- WebSocket support for real-time updates

---

## Decision 3: Hybrid Search (Vector + Fuzzy + Exact Match)

### Context
Pure vector similarity search with MPNet embeddings showed suboptimal results for:
- Swedish language queries (model is English-optimized)
- Short queries with specific terms (e.g., "adapter", "kulspruta")
- Typos and misspellings (e.g., "leksaksbot" vs. "leksaksbåt")

### Decision
Implement **hybrid search** in `DatabaseVectorMemory` that combines:
1. **Weighted Vector Similarity** (40% category, 30% content, 30% combined)
2. **Exact String Matching** (boost +0.5 for word match, +0.3 for substring)
3. **Fuzzy Matching** (Levenshtein distance, boost +0.4 for 1-char diff, +0.25 for 2-char)
4. **Phrase Preservation** (detect multi-word concepts like "how to win")

### Rationale
- **Language Agnostic**: String matching works regardless of embedding quality
- **Precision**: Exact matches are highly relevant, even if semantically "distant"
- **Fault Tolerance**: Fuzzy matching handles user typos gracefully
- **Complementary**: Vector search captures semantic similarity, string matching catches exact terms

### Trade-offs
- **Complexity**: More complex scoring logic to maintain
- **Tuning Required**: Boost values (0.3, 0.4, 0.5) were empirically determined
- **CPU Cost**: String operations add ~10-20ms per query (negligible vs. LLM inference)

### Impact on Solution
- **Accuracy**: Significantly improved retrieval for Swedish queries (70% ? 95% relevant results)
- **User Experience**: Users get correct answers even with typos or natural language variations
- **Robustness**: Less dependent on embedding model quality

### Implementation Details
```csharp
// Phase 1: Vector similarity (baseline)
score = WeightedCosineSimilarity(query, category, content, combined);

// Phase 2: Exact string matching (+0.5 or +0.3 boost)
if (categoryLower.Contains(queryLower)) { score += 0.5; }

// Phase 3: Fuzzy matching (+0.4 or +0.25 boost)
if (LevenshteinDistance(word1, word2) <= 2) { score += fuzzyBoost; }
```

---

## Decision 4: Multiple Embeddings Per Fragment

### Context
Single combined embeddings (category + content together) showed poor discrimination between similar documents.

### Decision
Store **three separate embeddings** per memory fragment:
1. **Category Embedding** (768-dim): Title/heading only
2. **Content Embedding** (768-dim): Body text only  
3. **Combined Embedding** (768-dim): Full text (legacy fallback)

### Rationale
- **Better Matching**: Category embeddings match queries about topics, content embeddings match queries about details
- **Weighted Scoring**: Can prioritize category matches (titles) over content matches (body)
- **Backward Compatibility**: Combined embedding allows legacy collections to work
- **Semantic Precision**: Separating title from body improves semantic focus

### Trade-offs
- **Storage Cost**: 3x embedding storage (~9KB per fragment vs. 3KB)
- **Generation Time**: 3x longer to generate embeddings during ingestion
- **Worth It**: Improved accuracy justifies the storage cost

### Impact on Solution
- **RAG Quality**: 20-30% improvement in retrieval precision
- **Database Size**: ~3x larger VectorMemory tables (still manageable, <1GB for 10K fragments)
- **Ingestion Performance**: Slower initial load, but one-time cost

### Migration Path
- New collections use 3 embeddings automatically
- Legacy collections with single embedding fall back gracefully
- Database schema supports both via nullable columns

---

## Decision 5: ONNX Runtime for Embeddings (Not Native BERT)

### Context
Needed a way to generate semantic embeddings locally without cloud APIs.

### Decision
Use **Microsoft.ML.OnnxRuntime** with pre-trained ONNX models (all-mpnet-base-v2) for embedding generation.

### Rationale
- **Performance**: ONNX Runtime is highly optimized (C++ core)
- **GPU Support**: DirectML (Windows) and CUDA (Linux) acceleration available
- **Model Compatibility**: Can use any HuggingFace model exported to ONNX
- **Memory Efficient**: < 2GB RAM usage even for 768-dim MPNet models
- **Offline-First**: No internet required, no API costs

### Trade-offs
- **Model Size**: 420MB for MPNet model (one-time download)
- **CPU Load**: Embedding generation is CPU-intensive without GPU
- **Alternatives Considered**:
  - **OpenAI Embeddings**: ? Requires internet, API costs
  - **Sentence Transformers (Python)**: ? Cross-language complexity
  - **Custom BERT Training**: ? Requires ML expertise, training infrastructure

### Impact on Solution
- **Privacy**: All processing stays local, no data leaves the machine
- **Cost**: Zero ongoing costs after initial setup
- **Quality**: all-mpnet-base-v2 is state-of-the-art for sentence embeddings (768-dim)
- **Scalability**: Can process thousands of documents locally

### Configuration
```json
"Embedding": {
  "ModelPath": "path/to/model.onnx",
  "VocabPath": "path/to/vocab.txt",
  "Dimension": 768
}
```

### Performance Characteristics
| Hardware | Speed | Memory |
|----------|-------|--------|
| CPU (4-core) | ~50ms per embedding | 1.5GB |
| CPU (8-core) | ~30ms per embedding | 1.5GB |
| GPU (DirectML) | ~10ms per embedding | 2.0GB |
| GPU (CUDA) | ~5ms per embedding | 2.5GB |

---

## Decision 6: Aggressive Memory Management for CPU Execution

### Context
Running BERT embeddings on CPU can consume 4-6GB RAM if not carefully managed.

### Decision
Implement **aggressive garbage collection and memory optimization** for CPU-only execution:
- Disable memory arena (`EnableCpuMemArena = false`)
- Single-threaded execution (reduce thread overhead)
- Force GC after every embedding generation
- Truncate large texts before tokenization
- Clear temporary arrays immediately after use

### Rationale
- **Target Hardware**: Many users run on modest hardware (8GB RAM total)
- **Shared Resources**: Need memory for LLM, embeddings, and operating system
- **Acceptable Trade-off**: Slightly slower embedding generation (60ms vs. 30ms) for 50% less memory (1.5GB vs. 3GB)

### Trade-offs
- **Performance**: ~2x slower embedding generation
- **Throughput**: Lower concurrent embedding throughput
- **Benefit**: Can run on 8GB RAM systems vs. requiring 16GB+

### Impact on Solution
- **Accessibility**: Runs on typical developer laptops
- **Stability**: No out-of-memory crashes during document ingestion
- **User Experience**: Slight delay during document processing (acceptable for background task)

### Implementation
```csharp
// CPU-only: Strict memory optimization
if (!_isGpuEnabled)
{
    sessionOptions.EnableCpuMemArena = false;      // Disable arena
    sessionOptions.IntraOpNumThreads = 1;          // Single-threaded
    sessionOptions.ExecutionMode = ORT_SEQUENTIAL; // Sequential mode
    
    // Force GC after each embedding
    GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
}
```

---

## Decision 7: Keyword Extraction Before Vector Search

### Context
Natural language queries contain many stop words ("how", "what", "where") that dilute semantic search results.

### Decision
Implement **keyword extraction** that removes stop words and focuses embeddings on core concepts:
- **Swedish Mode**: Aggressive filtering (remove "hur", "jag", "ska", etc.)
- **English Mode**: Preserve important phrases ("how to win", "game setup")
- **Hybrid Mode**: Auto-detect based on query content

### Rationale
- **Semantic Focus**: Query embedding "adapter" is more focused than "hur sorterar jag adapter?"
- **Improved Matches**: Removing filler words improves vector similarity scores
- **Language-Specific**: Different languages have different stop word patterns
- **Phrase Awareness**: Multi-word concepts ("how to win") should stay together

### Trade-offs
- **Edge Cases**: May remove important context in some queries
- **Maintenance**: Stop word lists need updates for new domains/languages
- **Overall Benefit**: 15-20% improvement in retrieval accuracy

### Impact on Solution
- **Swedish Queries**: Massive improvement (50% ? 90% accuracy)
- **English Queries**: Moderate improvement, especially for "how to" questions
- **Extensibility**: Easy to add stop words for new languages

### Implementation
```csharp
// Swedish mode: aggressive filtering
var stopWords = ["hur", "var", "vad", "jag", "ska", ...];
var keywords = words.Where(w => !stopWords.Contains(w) && w.Length > 2);

// English mode: preserve important phrases
var importantPhrases = ["how to win", "how to play", "game setup"];
if (query.Contains(importantPhrase)) { /* gentler filtering */ }
```

---

## Decision 8: Domain-Based Filtering

### Context
Large knowledge bases contain multiple domains (e.g., different board games, recycling categories). Searching all domains returns less relevant results.

### Decision
Implement **domain detection and filtering** (`DomainDetector`) that:
1. Detects domains from query keywords (e.g., "Munchkin" ? board-game-munchkin)
2. Filters database queries at SQL level for efficiency
3. Falls back to collection-wide search if no domain detected

### Rationale
- **Relevance**: Filtering to "Munchkin" rules excludes "Settlers of Catan" rules
- **Performance**: SQL-level filtering faster than in-memory filtering
- **User Intent**: Query about specific game shouldn't return irrelevant games
- **Flexibility**: No domain ? search all (backward compatible)

### Trade-offs
- **False Negatives**: May miss relevant content if domain detection fails
- **Maintenance**: Domain list needs updates as new domains added
- **Benefit**: 40-50% reduction in irrelevant results for domain-specific queries

### Impact on Solution
- **User Experience**: More focused, relevant answers
- **Performance**: Faster queries due to smaller result sets
- **Extensibility**: Easy to add new domains via `KnowledgeDomainRepository`

### Configuration
Domains are stored in database (`KnowledgeDomainEntity`):
```csharp
DomainId = "board-game-munchkin"
DisplayName = "Munchkin"
Category = "board-games"
Keywords = ["munchkin", "dungeon", "level up"]
```

---

## Decision 9: Timeout Configuration at Multiple Levels

### Context
LLM inference can hang or take too long, especially on CPU or with large context windows.

### Decision
Implement **configurable timeouts at three levels**:
1. **Process Level** (`PersistentLlmProcess.TimeoutMs`): Per-query execution timeout
2. **Pool Level** (`ModelInstancePool.TimeoutMs`): Global timeout for all instances
3. **Generation Level**: Pause detection (no output for N seconds = done)

### Rationale
- **User Experience**: Don't leave users waiting indefinitely
- **Resource Protection**: Kill hung processes to free memory
- **Flexibility**: Different timeouts for different use cases (CPU vs. GPU, short vs. long answers)
- **Dynamic Adjustment**: Can change timeout at runtime without restarting pool

### Trade-offs
- **False Termination**: May kill slow but valid responses
- **Tuning Required**: Optimal timeout depends on hardware (30s for GPU, 60s for CPU)
- **Mitigation**: Pause detection (no output for 5-10s) allows natural completion

### Impact on Solution
- **Reliability**: System doesn't hang on problematic queries
- **UX**: Users get timely feedback (timeout error) vs. infinite wait
- **Operations**: Easier to diagnose and resolve performance issues

### Configuration
```json
"Pool": {
  "TimeoutMs": 45000  // 45 seconds
}
```

### Dynamic Adjustment
```csharp
// Update timeout for all pool instances
modelPool.TimeoutMs = 60000; // Change to 60 seconds
```

---

## Decision 10: Model Format Detection and Multi-Model Support

### Context
Different LLM architectures (TinyLlama, Llama 3, Mistral, Phi) use different prompt formats and special tokens.

### Decision
Implement **pattern-based output parsing** (`LlmOutputPatterns`) that detects multiple formats:
- Llama 3.2: `<|start_header_id|>assistant<|end_header_id|>`
- TinyLlama/Phi: `<|assistant|>`
- ChatML: `<|im_start|>assistant`
- Generic: `Assistant:`

### Rationale
- **Model Agnostic**: Works with any GGUF model from HuggingFace
- **Hot-Swapping**: Can switch models without code changes
- **Future-Proof**: Easy to add new formats as models evolve
- **Graceful Fallback**: If no pattern detected, return raw output

### Trade-offs
- **Pattern Maintenance**: New models may need new patterns added
- **Detection Order**: Pattern order matters (check specific before generic)
- **Edge Cases**: Custom fine-tuned models may need custom patterns

### Impact on Solution
- **Flexibility**: Users can experiment with different models
- **Maintenance**: No hard-coded model-specific logic
- **User Choice**: Support for TinyLlama (1.1B), Mistral (7B), Llama 3 (8B), etc.

### Supported Models
| Model | Size | Format Detected |
|-------|------|-----------------|
| TinyLlama 1.1B | 637MB Q5_K_M | `<|assistant|>` |
| Phi-2 2.7B | 1.7GB Q5_K_M | `<|assistant|>` |
| Mistral 7B | 4.1GB Q5_K_M | `Assistant:` |
| Llama 3.2 3B | 2.0GB Q5_K_M | `<|start_header_id|>assistant` |
| Llama 3.1 8B | 4.9GB Q5_K_M | `<|start_header_id|>assistant` |

---

## Dependencies and Integration Points

### Dependencies FROM AI Project
| Consuming Project | Used Components | Purpose |
|-------------------|-----------------|---------|
| **AiDashboard** | `ModelInstancePool`, `AiChatServicePooled` | Web UI chat functionality |
| **AiDashboard** | `SemanticEmbeddingService` | Embedding generation for RAG |
| **Services** | `IDomainDetector`, `DomainDetector` | Domain-based query filtering |

### Dependencies TO Other Projects
| Dependency Project | Used Components | Purpose |
|--------------------|-----------------|---------|
| **Services** | `ILlmMemory`, `ISearchableMemory` | Memory abstraction layer |
| **Services** | `GenerationSettings`, `LlmSettings` | Configuration models |
| **Services** | `DisplayService`, `EosEofDebugger` | UI and debugging utilities |
| **Entities** | `MemoryFragment`, `IMemoryFragment` | Domain entities |
| **Factories** | `LlmFactory` | Process creation |

---

## Performance Metrics (Real-World)

### Hardware: Intel i7-10700K (8-core), 32GB RAM, RTX 3070

| Scenario | Time | Memory | Config |
|----------|------|--------|--------|
| Model Load (cold) | 8-12s | 1.2GB | TinyLlama Q5_K_M |
| Model Load (warm) | 100-500ms | 1.2GB | OS cache hit |
| Query (RAG, GPU) | 3-8s | +0.5GB | 200 tokens, temp 0.3 |
| Query (RAG, CPU) | 8-20s | +0.3GB | 200 tokens, temp 0.3 |
| Embedding (GPU) | 10ms | +0.3GB | 768-dim MPNet |
| Embedding (CPU) | 50ms | +0.2GB | 768-dim MPNet |
| Pool Warmup | 30-45s | 3.6GB | 3 instances |

### Hardware: Intel i5-8250U (4-core), 16GB RAM, Integrated Graphics

| Scenario | Time | Memory | Config |
|----------|------|--------|--------|
| Model Load (cold) | 15-25s | 1.2GB | TinyLlama Q5_K_M |
| Query (RAG, CPU) | 20-45s | +0.5GB | 200 tokens, temp 0.3 |
| Embedding (CPU) | 80-120ms | +0.3GB | 768-dim MPNet, optimized |
| Pool Warmup | 60-90s | 3.6GB | 3 instances |

---

## Future Enhancements

### Planned
1. **Streaming Responses**: Token-by-token streaming via llama.cpp server mode
2. **Multi-Language Embeddings**: Paraphrase-multilingual-mpnet-base-v2 for better Swedish support
3. **Quantized Embeddings**: INT8 quantization for 75% storage reduction
4. **Embedding Caching**: Cache query embeddings for repeated questions

### Under Consideration
1. **Fine-Tuned Models**: Domain-specific fine-tuning for board games/recycling
2. **Multi-GPU Support**: Distribute LLM instances across multiple GPUs
3. **Distributed Pooling**: Multi-server pool for high-scale deployments
4. **Model Routing**: Route queries to different models based on complexity

---

## Document Version
- **Version**: 1.0
- **Last Updated**: 2024
- **Maintained By**: OfflineAI Development Team
- **Next Review**: After major architectural changes
