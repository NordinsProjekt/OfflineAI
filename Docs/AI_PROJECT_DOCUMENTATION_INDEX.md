# AI Project - Documentation Index

## Overview
This directory contains comprehensive documentation for the **AI** project (`Application.AI` namespace) in the OfflineAI solution. The AI project is responsible for all machine learning operations, including LLM inference, embedding generation, and RAG orchestration.

---

## Documentation Structure

### 1. Architecture & Decisions
?? **[AI_PROJECT_ARCHITECTURE_DECISIONS.md](AI_PROJECT_ARCHITECTURE_DECISIONS.md)**
- **Purpose**: Explains WHY key architectural decisions were made
- **Contents**:
  - Model Instance Pooling Pattern
  - Process-Per-Query vs. Interactive Mode
  - Hybrid Search (Vector + Fuzzy + Exact)
  - Multiple Embeddings Per Fragment
  - ONNX Runtime for Embeddings
  - Memory Management Strategies
  - Keyword Extraction
  - Domain-Based Filtering
  - Timeout Configuration
  - Multi-Model Support
- **Audience**: Architects, senior developers, new team members
- **When to Read**: Before making significant architectural changes

### 2. Pooling Components
?? **[AI_POOLING_COMPONENTS.md](AI_POOLING_COMPONENTS.md)**
- **Purpose**: Deep dive into LLM instance pooling for concurrency
- **Contents**:
  - `IModelInstancePool` - Interface definition
  - `ModelInstancePool` - Concrete implementation
  - `PooledInstance` - RAII wrapper for automatic cleanup
  - Configuration guidelines (pool size vs. RAM)
  - Performance characteristics
  - Health monitoring and recovery
  - Integration patterns
- **Audience**: Developers working on concurrency, performance optimization
- **When to Read**: When tuning pool size, debugging instance issues

### 3. Processing Components
?? **[AI_PROCESSING_COMPONENTS.md](AI_PROCESSING_COMPONENTS.md)**
- **Purpose**: LLM process spawning, output parsing, lifecycle management
- **Contents**:
  - `IPersistentLlmProcess` - Interface for LLM operations
  - `PersistentLlmProcess` - Process spawning and management
  - `LlmOutputPatterns` - Multi-format output detection
  - Timeout strategies (overall + pause detection)
  - Error handling and health monitoring
  - GPU/CPU configuration
  - Model compatibility reference
- **Audience**: Developers adding new models, debugging LLM issues
- **When to Read**: When adding new model support, troubleshooting timeouts

### 4. Complete Component Reference
?? **[AI_COMPLETE_COMPONENT_REFERENCE.md](AI_COMPLETE_COMPONENT_REFERENCE.md)**
- **Purpose**: Comprehensive reference for ALL AI components
- **Contents**:
  - **Chat Components**: `AiChatServicePooled` - RAG orchestration
  - **Embedding Components**: `SemanticEmbeddingService` - BERT embeddings
  - **Management Components**: `ModelManager`, `ModelManagementService` - Model switching
  - **Utility Components**: `DomainDetector`, `EmbeddingPooling`, `TextNormalizer`
  - **Extension Components**: `EmbeddingExtensions`, `StringExtensions`
  - **Model Components**: `PerformanceMetrics`, `SimpleMemory`
- **Audience**: All developers, reference documentation
- **When to Read**: When implementing features, understanding data flow

---

## Quick Start Guides

### For New Developers

**Step 1**: Read [SOLUTION_OVERVIEW.md](SOLUTION_OVERVIEW.md) to understand the entire system
```
Location: docs/SOLUTION_OVERVIEW.md
Time: 15-20 minutes
Goal: Understand how OfflineAI works end-to-end
```

**Step 2**: Read [AI_PROJECT_ARCHITECTURE_DECISIONS.md](AI_PROJECT_ARCHITECTURE_DECISIONS.md)
```
Location: docs/AI_PROJECT_ARCHITECTURE_DECISIONS.md
Time: 20-30 minutes
Goal: Understand WHY things are built this way
```

**Step 3**: Read specific component docs based on your task
```
- Working on concurrency? ? AI_POOLING_COMPONENTS.md
- Adding new model? ? AI_PROCESSING_COMPONENTS.md
- Implementing features? ? AI_COMPLETE_COMPONENT_REFERENCE.md
```

### For Troubleshooting

**Problem: Pool exhausted, users waiting for responses**
```
1. Read: AI_POOLING_COMPONENTS.md ? "Configuration Guidelines"
2. Check: pool.AvailableCount during peak load
3. Solution: Increase Pool.MaxInstances or reduce TimeoutMs
```

**Problem: Model not generating responses correctly**
```
1. Read: AI_PROCESSING_COMPONENTS.md ? "LlmOutputPatterns"
2. Test: Run llama-cli.exe manually to see raw output
3. Solution: Add new pattern to LlmOutputPatterns.AssistantPatterns
```

**Problem: Poor embedding quality for Swedish**
```
1. Read: AI_PROJECT_ARCHITECTURE_DECISIONS.md ? "Decision 3: Hybrid Search"
2. Read: AI_COMPLETE_COMPONENT_REFERENCE.md ? "SemanticEmbeddingService"
3. Solution: Consider paraphrase-multilingual-mpnet-base-v2 model
```

**Problem: Out of memory during embedding generation**
```
1. Read: AI_PROJECT_ARCHITECTURE_DECISIONS.md ? "Decision 6: Aggressive Memory Management"
2. Read: AI_COMPLETE_COMPONENT_REFERENCE.md ? "SemanticEmbeddingService" ? "Memory Optimization"
3. Solution: Disable GPU, enable aggressive GC, reduce batch size
```

---

## Component Dependency Graph

```
???????????????????????????????????????????????????????????????
?                    AiDashboard (Web UI)                      ?
???????????????????????????????????????????????????????????????
                                ?
                                ?
???????????????????????????????????????????????????????????????
?              AiChatServicePooled (RAG Orchestrator)          ?
?  - Coordinates memory search and LLM query                   ?
?  - Builds system prompt with context                         ?
?  - Tracks performance metrics                                ?
???????????????????????????????????????????????????????????????
      ?                    ?                 ?
      ?                    ?                 ?
      ?                    ?                 ?
??????????????  ???????????????????  ??????????????????
?DatabaseVec ?  ?ModelInstancePool?  ?DomainDetector  ?
?toryMemory  ?  ?- Pooling        ?  ?- Domain filter ?
?(Services)  ?  ?- Health monitor ?  ?(AI)            ?
??????????????  ???????????????????  ??????????????????
                         ?
                         ?
           ??????????????????????????????
           ?  PersistentLlmProcess      ?
           ?  - Process spawning        ?
           ?  - Output parsing          ?
           ?  - Timeout management      ?
           ??????????????????????????????
                        ?
                        ?
              ???????????????????????
              ?  llama-cli.exe      ?
              ?  (Native LLM)       ?
              ???????????????????????
```

```
???????????????????????????????????????????????????????????????
?            VectorMemoryPersistenceService                    ?
?            (Document Processing - Services)                  ?
???????????????????????????????????????????????????????????????
                                ?
                                ?
???????????????????????????????????????????????????????????????
?          SemanticEmbeddingService (AI)                       ?
?  - BERT tokenization (Microsoft.ML.Tokenizers)               ?
?  - ONNX inference (Microsoft.ML.OnnxRuntime)                 ?
?  - Mean pooling (EmbeddingPooling)                           ?
?  - L2 normalization                                          ?
???????????????????????????????????????????????????????????????
      ?                    ?
      ?                    ?
??????????????  ???????????????????????????
?TextNormal  ?  ?all-mpnet-base-v2.onnx   ?
?izer        ?  ?(BERT Model)             ?
?(AI Utils)  ?  ?768-dim embeddings       ?
??????????????  ???????????????????????????
```

---

## File Organization

### AI Project Structure
```
AI/
??? Chat/
?   ??? AiChatServicePooled.cs         # RAG orchestration, LLM query coordination
?
??? Embeddings/
?   ??? SemanticEmbeddingService.cs    # BERT-based embedding generation (ONNX)
?
??? Extensions/
?   ??? EmbeddingExtensions.cs         # Cosine similarity calculations
?   ??? StringExtensions.cs            # String manipulation helpers
?
??? Management/
?   ??? IModelManager.cs               # Model switching interface
?   ??? ModelManager.cs                # Hot-swap model implementation
?   ??? ModelManagementService.cs      # Model discovery and metadata
?
??? Models/
?   ??? PerformanceMetrics.cs          # LLM generation metrics
?   ??? SimpleMemory.cs                # Basic in-memory storage
?
??? Pooling/
?   ??? IModelInstancePool.cs          # Pool interface for DI
?   ??? ModelInstancePool.cs           # Concrete pool implementation
?   ??? PooledInstance.cs              # RAII wrapper for automatic return
?
??? Processing/
?   ??? IPersistentLlmProcess.cs       # LLM process interface
?   ??? PersistentLlmProcess.cs        # Process spawning and management
?   ??? LlmOutputPatterns.cs           # Multi-format output detection
?
??? Utilities/
    ??? DomainDetector.cs              # Domain detection from queries
    ??? IDomainDetector.cs             # Domain detector interface
    ??? EmbeddingPooling.cs            # Attention-masked mean pooling
    ??? TextNormalizer.cs              # Text cleaning for tokenization
```

### Documentation Files
```
docs/
??? SOLUTION_OVERVIEW.md                     # ?? Entire solution overview
??? AI_PROJECT_ARCHITECTURE_DECISIONS.md      # ??? Why we built it this way
??? AI_POOLING_COMPONENTS.md                  # ?? Concurrency and pooling
??? AI_PROCESSING_COMPONENTS.md               # ?? LLM process management
??? AI_COMPLETE_COMPONENT_REFERENCE.md        # ?? All components reference
??? AI_PROJECT_DOCUMENTATION_INDEX.md         # ?? This file (navigation)
```

---

## Common Tasks & Relevant Docs

| Task | Primary Document | Secondary Document |
|------|------------------|-------------------|
| Add new LLM model | AI_PROCESSING_COMPONENTS.md | AI_PROJECT_ARCHITECTURE_DECISIONS.md |
| Optimize pool size | AI_POOLING_COMPONENTS.md | AI_PROJECT_ARCHITECTURE_DECISIONS.md |
| Improve RAG quality | AI_PROJECT_ARCHITECTURE_DECISIONS.md | AI_COMPLETE_COMPONENT_REFERENCE.md |
| Add new embedding model | AI_COMPLETE_COMPONENT_REFERENCE.md | AI_PROJECT_ARCHITECTURE_DECISIONS.md |
| Debug timeout issues | AI_PROCESSING_COMPONENTS.md | AI_POOLING_COMPONENTS.md |
| Implement domain filtering | AI_COMPLETE_COMPONENT_REFERENCE.md | AI_PROJECT_ARCHITECTURE_DECISIONS.md |
| Reduce memory usage | AI_PROJECT_ARCHITECTURE_DECISIONS.md | AI_COMPLETE_COMPONENT_REFERENCE.md |
| Hot-swap models | AI_COMPLETE_COMPONENT_REFERENCE.md | AI_POOLING_COMPONENTS.md |
| Understand RAG flow | SOLUTION_OVERVIEW.md | AI_COMPLETE_COMPONENT_REFERENCE.md |
| Implement fuzzy search | AI_PROJECT_ARCHITECTURE_DECISIONS.md | (See Services\Memory docs) |

---

## External Dependencies

### NuGet Packages
| Package | Version | Purpose | Documentation |
|---------|---------|---------|---------------|
| Microsoft.SemanticKernel | 1.66.0 | AI orchestration | [Docs](https://learn.microsoft.com/semantic-kernel) |
| Microsoft.ML.OnnxRuntime | 1.20.1 | BERT inference (CPU) | [Docs](https://onnxruntime.ai) |
| Microsoft.ML.OnnxRuntime.DirectML | 1.20.1 | BERT inference (GPU) | [Docs](https://onnxruntime.ai/docs/execution-providers/DirectML-ExecutionProvider.html) |
| Microsoft.ML.Tokenizers | 2.0.0 | BERT tokenization | [Docs](https://learn.microsoft.com/dotnet/api/microsoft.ml.tokenizers) |
| BERTTokenizers | 1.2.0 | Legacy BERT tokenizer | GitHub |

### Native Dependencies
| Dependency | Purpose | Source |
|------------|---------|--------|
| llama-cli.exe | LLM inference | [llama.cpp](https://github.com/ggerganov/llama.cpp) |
| *.gguf models | Pre-trained LLMs | [HuggingFace](https://huggingface.co/models?library=gguf) |
| model.onnx | BERT embeddings | [Sentence Transformers](https://www.sbert.net) |
| vocab.txt | BERT tokenizer | With BERT model |

---

## Testing Strategy

### Unit Tests (`Application.AI.Tests`)
```
Tests/
??? Pooling/
?   ??? ModelInstancePoolTests.cs      # Pool behavior, health monitoring
?   ??? PooledInstanceTests.cs         # Automatic return, disposal
?
??? Processing/
?   ??? PersistentLlmProcessTests.cs   # Process spawning, timeouts
?   ??? LlmOutputPatternsTests.cs      # Pattern matching
?
??? Embeddings/
?   ??? SemanticEmbeddingServiceTests.cs # Embedding generation, normalization
?
??? Utilities/
    ??? DomainDetectorTests.cs         # Domain matching
    ??? EmbeddingPoolingTests.cs       # Pooling algorithms
```

### Integration Tests
```csharp
[Fact]
public async Task EndToEnd_RagQuery_ReturnsRelevantResponse()
{
    // Arrange: Full stack setup
    var embeddingService = new SemanticEmbeddingService(...);
    var repository = new VectorMemoryRepository(...);
    var memory = new DatabaseVectorMemory(embeddingService, repository, "test-collection");
    var pool = new ModelInstancePool(llmPath, modelPath, maxInstances: 1);
    await pool.InitializeAsync();
    var chatService = new AiChatServicePooled(memory, ..., pool);
    
    // Act: Query with RAG
    var response = await chatService.SendMessageAsync("How to win Munchkin?");
    
    // Assert: Response contains relevant info
    Assert.Contains("Level 10", response);
    Assert.DoesNotContain("<|eot_id|>", response); // Markers cleaned
}
```

---

## Performance Benchmarks

### Hardware: Intel i7-10700K (8-core), 32GB RAM, RTX 3070

| Component | Operation | Time | Notes |
|-----------|-----------|------|-------|
| **ModelInstancePool** | Initialize (3 instances) | 30-45s | Parallel loading |
| | Acquire (available) | <1ms | Lock only |
| | Acquire (all busy) | Varies | Waits for return |
| **PersistentLlmProcess** | Spawn process | 100-500ms | OS overhead |
| | Model load (cold) | 8-12s | Disk read |
| | Model load (warm) | 200-500ms | OS cache |
| | Query (200 tokens, GPU) | 5-10s | ~25 tok/s |
| | Query (200 tokens, CPU) | 20-45s | ~5 tok/s |
| **SemanticEmbeddingService** | Single embedding (GPU) | 10ms | DirectML |
| | Single embedding (CPU) | 50ms | Optimized |
| | Batch 10 (GPU) | 100ms | Parallel |
| | Batch 10 (CPU) | 500ms | Sequential |
| **AiChatServicePooled** | RAG query (end-to-end) | 6-12s | GPU, 3 contexts |
| | RAG query (end-to-end) | 25-50s | CPU, 3 contexts |

---

## Glossary

| Term | Definition |
|------|------------|
| **RAG** | Retrieval-Augmented Generation - combining search with LLM |
| **BERT** | Bidirectional Encoder Representations from Transformers |
| **ONNX** | Open Neural Network Exchange - cross-platform ML format |
| **GGUF** | GPT-Generated Unified Format - llama.cpp model format |
| **Embedding** | Vector representation of text for semantic similarity |
| **Pooling** | Combining multiple vectors into one (e.g., tokens ? sentence) |
| **L2 Normalization** | Scaling vector to unit length |
| **Cosine Similarity** | Measure of vector angle (0-1 scale) |
| **Token** | Smallest unit of text for LLM (word or subword) |
| **Context Window** | Maximum tokens LLM can process at once |
| **Temperature** | Controls randomness in LLM output (0=deterministic, 1=creative) |
| **Top-K** | Limit vocabulary choices to top K most likely tokens |
| **DirectML** | Microsoft's GPU acceleration library (Windows) |
| **SemaphoreSlim** | Lightweight semaphore for async locking |
| **ConcurrentBag** | Thread-safe unordered collection |

---

## Contact & Contributions

### For Questions
- **Architecture Decisions**: Review [AI_PROJECT_ARCHITECTURE_DECISIONS.md](AI_PROJECT_ARCHITECTURE_DECISIONS.md) first
- **Implementation Details**: Check [AI_COMPLETE_COMPONENT_REFERENCE.md](AI_COMPLETE_COMPONENT_REFERENCE.md)
- **Specific Components**: Use component-specific docs

### For Contributions
1. Read relevant documentation files
2. Follow existing patterns and conventions
3. Update documentation when making changes
4. Add tests for new functionality
5. Update this index if adding new docs

---

## Document Maintenance

### Version History
| Version | Date | Changes | Author |
|---------|------|---------|--------|
| 1.0 | 2024 | Initial comprehensive documentation | OfflineAI Team |

### Review Schedule
- **Architecture Decisions**: Review after major architectural changes
- **Component Docs**: Update when implementation changes
- **This Index**: Update when adding/removing documentation files

### Documentation Standards
- **Format**: Markdown (.md files)
- **Code Blocks**: Use language-specific syntax highlighting
- **Diagrams**: Use ASCII art for simple diagrams, Mermaid for complex
- **Examples**: Include both usage examples and common pitfalls
- **Cross-References**: Link between related documents

---

## Related Documentation

### Other Projects
- **[Services Project](../Services/README.md)**: Business logic, memory implementations
- **[Infrastructure.Data.Dapper](../Infrastructure.Data.Dapper/README.md)**: Database access
- **[Entities](../Entities/README.md)**: Domain models
- **[AiDashboard](../AiDashboard/README.md)**: Blazor web UI

### External Resources
- [llama.cpp GitHub](https://github.com/ggerganov/llama.cpp)
- [Sentence Transformers](https://www.sbert.net)
- [ONNX Runtime Docs](https://onnxruntime.ai)
- [Microsoft Semantic Kernel](https://learn.microsoft.com/semantic-kernel)
- [HuggingFace Model Hub](https://huggingface.co/models)

---

**Last Updated**: 2024  
**Maintained By**: OfflineAI Development Team  
**License**: MIT
