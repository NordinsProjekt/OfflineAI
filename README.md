# OfflineAI - Local LLM RAG System

> **A hobby project exploring Retrieval-Augmented Generation (RAG) with local LLMs**  
> Built with .NET 9, Blazor, and llama.cpp - No cloud dependencies required

![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?style=flat&logo=dotnet)
![C#](https://img.shields.io/badge/C%23-13.0-239120?style=flat&logo=csharp)
![Blazor](https://img.shields.io/badge/Blazor-Server-512BD4?style=flat&logo=blazor)
![License](https://img.shields.io/badge/license-MIT-blue.svg)

---

## ?? Project Overview

**OfflineAI** is a comprehensive RAG (Retrieval-Augmented Generation) system that runs entirely offline using local LLM models. It demonstrates modern .NET architecture patterns while providing a practical solution for knowledge-based AI assistance without cloud dependencies.

### Why This Project?

This started as a learning journey to understand:
- How LLMs work at a lower level (beyond API calls)
- Vector embeddings and semantic search
- RAG architecture and its practical challenges
- Clean architecture in .NET with Blazor
- Real-time state management in web apps

**Project Stats:**
- **Started:** October 24, 2024
- **Total Commits:** 44+
- **Estimated Hours:** ~80-100 hours (based on commit history and complexity)
- **Lines of Code:** ~15,000+ (excluding generated files)

---

## ? Key Features

### ?? Local LLM Integration
- **llama.cpp** integration for running GGUF models locally
- Support for any GGUF-format model (Llama 3, Phi-3, Qwen, Mistral, etc.)
- Model pooling for efficient resource usage
- Real-time model switching without restart
- GPU acceleration support (CUDA, ROCm, Metal, Vulkan)

### ?? RAG (Retrieval-Augmented Generation)
- **Semantic search** using BERT embeddings (MPNet)
- **Database-backed vector store** (SQL Server)
- Domain-based filtering (multi-topic knowledge bases)
- Configurable relevance thresholds and chunk counts
- Database-level filtering for optimal performance

### ?? Document Processing
- **PDF support** with intelligent chunking
- **TXT file support** with hierarchy detection
- Smart document chunking with overlap
- Automatic metadata extraction
- Inbox/Archive workflow for knowledge ingestion

### ?? Modern Blazor Dashboard
- Real-time updates with SignalR
- Dark theme optimized for extended use
- Responsive design (mobile-friendly)
- Live model switching
- Generation parameter tuning
- Collection management

### ??? Data Architecture
- **Dapper** for high-performance database access
- Multiple vector store collections
- Dynamic table switching
- Efficient bulk operations
- Database-level domain filtering (84% faster queries)

---

## ??? Architecture

### Clean Architecture Layers

```
???????????????????????????????????????????
?         AiDashboard (Blazor)            ?  ? Presentation Layer
?  • Real-time UI                         ?
?  • State Management                     ?
?  • Interactive Components               ?
???????????????????????????????????????????
                    ?
???????????????????????????????????????????
?          AI (Application)                ?  ? Application Layer
?  • Chat Service                          ?
?  • Model Management                      ?
?  • Embedding Service                     ?
?  • Domain Detection                      ?
???????????????????????????????????????????
                    ?
???????????????????????????????????????????
?           Services (Core)                ?  ? Business Logic
?  • RAG Implementation                    ?
?  • Document Processing                   ?
?  • Vector Memory                         ?
?  • Configuration                         ?
???????????????????????????????????????????
                    ?
???????????????????????????????????????????
?   Infrastructure.Data.Dapper             ?  ? Data Access Layer
?  • Vector Repository                     ?
?  • Domain Repository                     ?
?  • Bulk Operations                       ?
???????????????????????????????????????????
                    ?
???????????????????????????????????????????
?           Entities                       ?  ? Domain Models
?  • MemoryFragmentEntity                  ?
?  • KnowledgeDomainEntity                 ?
???????????????????????????????????????????
```

### Projects in Solution

| Project | Purpose | Key Dependencies |
|---------|---------|------------------|
| **AiDashboard** | Blazor Server UI | ASP.NET Core 9.0, SignalR |
| **AI** | LLM integration & orchestration | Microsoft.SemanticKernel |
| **Services** | Business logic & RAG | UglyToad.PdfPig |
| **Infrastructure.Data.Dapper** | Data access | Dapper, Microsoft.Data.SqlClient |
| **Entities** | Domain models | None (pure POCO) |
| **Factories** | Object creation patterns | None |

---

## ?? Technologies Used

### Core Technologies
- **.NET 9** - Latest LTS framework
- **C# 13** - Modern language features
- **Blazor Server** - Real-time web UI
- **SQL Server** - Vector storage (LocalDB or full server)

### AI/ML Technologies
- **llama.cpp** - Local LLM runtime (GGUF models)
- **Microsoft.SemanticKernel** - AI orchestration
- **BERT Embeddings** - Semantic search (MPNet all-mpnet-base-v2)
- **ONNX Runtime** - ML model execution

### Data Technologies
- **Dapper** - Micro-ORM for performance
- **Microsoft.Data.SqlClient** - SQL Server provider
- **Vector embeddings** - Stored as VARBINARY in SQL

### Document Processing
- **UglyToad.PdfPig** - PDF text extraction
- **Custom chunking** - Semantic boundary detection

### Frontend Technologies
- **Blazor Server** - Component-based SPA
- **CSS3** - Custom dark theme
- **SignalR** - Real-time updates

---

## ?? Getting Started

### Prerequisites

1. **Development Environment**
   - Visual Studio 2022 (17.8+) or Visual Studio Code
   - .NET 9 SDK
   - SQL Server (LocalDB included with VS)

2. **LLM Runtime**
   - Download llama.cpp from: https://github.com/ggerganov/llama.cpp/releases
   - Extract `llama-cli.exe` (or `llama-server.exe`)

3. **LLM Model (GGUF format)**
   - Example: Phi-3.5 Mini (3.8 GB): https://huggingface.co/microsoft/Phi-3.5-mini-instruct-gguf
   - Or any other GGUF model from HuggingFace

4. **Embedding Model**
   - Download all-mpnet-base-v2 ONNX:
     - Model: https://huggingface.co/sentence-transformers/all-mpnet-base-v2
     - Vocab: `vocab.txt` from the same repo

### Quick Setup

1. **Clone the repository**
   ```bash
   git clone https://github.com/NordinsProjekt/OfflineAI.git
   cd OfflineAI
   ```

2. **Configure User Secrets** (AiDashboard project)
   ```bash
   cd AiDashboard
   dotnet user-secrets init
   ```

3. **Set configuration**
   ```bash
   dotnet user-secrets set "AppConfiguration:Llm:ExecutablePath" "d:/tinyllama/llama-cli.exe"
   dotnet user-secrets set "AppConfiguration:Llm:ModelPath" "d:/tinyllama/phi-3.5-mini-instruct.gguf"
   dotnet user-secrets set "AppConfiguration:Embedding:ModelPath" "d:/tinyllama/models/all-mpnet-base-v2/onnx/model.onnx"
   dotnet user-secrets set "AppConfiguration:Embedding:VocabPath" "d:/tinyllama/models/all-mpnet-base-v2/vocab.txt"
   dotnet user-secrets set "AppConfiguration:Folders:InboxFolder" "d:/tinyllama/inbox"
   dotnet user-secrets set "AppConfiguration:Folders:ArchiveFolder" "d:/tinyllama/archive"
   ```

4. **Create required folders**
   ```bash
   mkdir d:/tinyllama/inbox
   mkdir d:/tinyllama/archive
   ```

5. **Run the application**
   ```bash
   dotnet run --project AiDashboard
   ```

6. **Access the dashboard**
   - Open browser to: `https://localhost:5001`

### First Use

1. **Add knowledge documents**
   - Place PDF or TXT files in the inbox folder
   - Click "Reload Inbox" in the dashboard
   - Documents are processed and added to the vector database

2. **Create a domain** (optional)
   - Navigate to Domains Management
   - Add domains like "Gloomhaven", "Munchkin Panic", etc.
   - Domains help filter results by topic

3. **Start chatting**
   - Type a question in the chat input
   - Toggle RAG ON to use your knowledge base
   - Toggle RAG OFF for direct LLM responses

---

## ?? Key Concepts Explained

### RAG (Retrieval-Augmented Generation)

RAG enhances LLM responses by providing relevant context from your knowledge base:

```
User Question
     ?
1. Generate embedding (vector) for question
     ?
2. Search vector database for similar content
     ?
3. Retrieve top K most relevant chunks
     ?
4. Build prompt: [Context] + [Question]
     ?
5. Send to LLM for answer
     ?
6. LLM generates response based on provided context
```

**Benefits:**
- ? Accurate answers based on your documents
- ? Reduces hallucinations
- ? Works with local models
- ? Your data stays private

### Vector Embeddings

Text is converted to numerical vectors (arrays of floats) that capture semantic meaning:

```csharp
"How to win in Gloomhaven?" 
    ? (BERT embedding)
[0.023, -0.145, 0.891, ..., 0.234]  // 768 dimensions
```

Similar text has similar vectors, enabling semantic search:

```
Query: "winning strategies"
  ? Cosine Similarity
Chunk 1: "How to win" ? Score: 0.823 ? Relevant
Chunk 2: "Setup rules" ? Score: 0.321 ? Not relevant
```

### Domain Detection

Automatically identifies topics in questions to filter results:

```
Question: "How to attack in Gloomhaven?"
    ?
Detected domain: "gloomhaven"
    ?
Filter: Only search Gloomhaven knowledge
    ?
Result: 88 chunks (vs 551 total) ? 84% faster!
```

### Model Pooling

Keeps multiple LLM instances loaded for faster responses:

```
User A request ? Instance 1 (busy)
User B request ? Instance 2 (busy)  
User C request ? Instance 3 (busy)
User D request ? Wait for available instance
```

**Benefits:**
- ? Parallel request handling
- ? No model reload overhead
- ? Better resource utilization

---

## ??? Configuration Guide

### Generation Settings

Located in `appsettings.json` or User Secrets:

```json
{
  "AppConfiguration": {
    "Generation": {
      "MaxTokens": 512,           // Response length (tokens)
      "Temperature": 0.3,         // Creativity (0=focused, 2=creative)
      "TopK": 40,                 // Vocabulary limit
      "TopP": 0.95,               // Nucleus sampling
      "RepeatPenalty": 1.15,      // Discourage repetition
      "PresencePenalty": 0.0,     // New concept penalty
      "FrequencyPenalty": 0.0     // Pattern penalty
    }
  }
}
```

### RAG Settings

Configurable via UI or code:

```json
{
  "AppConfiguration": {
    "Generation": {
      "RagTopK": 3,                   // Chunks to retrieve (1-5)
      "RagMinRelevanceScore": 0.5     // Min similarity (0.3-0.8)
    }
  }
}
```

**Tuning Guide:**

| Use Case | TopK | MinRelevanceScore |
|----------|------|-------------------|
| Specific facts | 1-2 | 0.6-0.7 |
| General questions | 3 | 0.5 |
| Broad topics | 4-5 | 0.4-0.5 |
| Exploratory | 5 | 0.3 |

### Model Pool Settings

```json
{
  "AppConfiguration": {
    "Pool": {
      "MaxInstances": 3,        // Concurrent model instances
      "TimeoutMs": 60000        // Query timeout (60 seconds)
    }
  }
}
```

---

## ?? Performance Optimizations

### 1. Database-Level Domain Filtering

**Before:** Load all 551 chunks ? Filter in memory  
**After:** Filter at SQL level ? Load only 88 chunks  
**Result:** 84% less data transfer, 75% faster queries

```sql
-- Optimized query with domain filter
SELECT * FROM MemoryFragments 
WHERE CollectionName = @Collection
  AND (Category LIKE '%gloomhaven%')  -- Database-level filter
ORDER BY ChunkIndex
```

### 2. Cosine Similarity Extension Method

Reusable vector similarity calculation:

```csharp
double similarity = queryEmbedding
    .CosineSimilarityWithNormalization(documentEmbedding);
```

**Benefits:**
- ? Single implementation
- ? Consistent results
- ? Easy to optimize

### 3. Context Size Optimization

Limited to 2048 tokens to prevent memory issues:

```csharp
processInfo.Arguments += $" -c 2048";  // Context window
```

### 4. Bulk Database Operations

Process multiple embeddings efficiently:

```csharp
await _repository.BulkSaveAsync(entities);  // Batch insert
```

**vs** individual inserts (~100x slower)

---

## ?? Project Statistics

### Codebase Metrics (Approximate)

- **Total Lines of Code:** ~15,000+
- **Projects:** 6
- **Classes:** 100+
- **Commits:** 44
- **Development Time:** ~80-100 hours
- **Documentation:** 25+ markdown files

### Technology Breakdown

```
C#:              ~80%  (Business logic, services, data access)
Razor/HTML:      ~10%  (Blazor components)
CSS:             ~5%   (Styling)
SQL:             ~3%   (Database schema, queries)
JSON:            ~2%   (Configuration files)
```

### Feature Implementation Timeline

- **Week 1 (Oct 24-30):** Basic LLM integration, simple memory
- **Week 2 (Nov 3-11):** TXT file processing, semantic embeddings
- **Week 3 (Nov 12-14):** RAG with vector search, database persistence
- **Week 4 (Nov 14-15):** Blazor dashboard, real-time state management
- **Week 5 (Nov 16+):** PDF support, domain filtering, optimizations

---

## ?? What I Learned

### Technical Skills

1. **LLM Integration**
   - Understanding GGUF model format
   - Working with llama.cpp CLI
   - Prompt engineering for RAG
   - Model parameter tuning

2. **Vector Embeddings**
   - BERT embedding generation
   - Vector storage strategies
   - Cosine similarity calculations
   - Semantic search techniques

3. **RAG Architecture**
   - Document chunking strategies
   - Relevance scoring
   - Context window management
   - Query optimization

4. **Blazor & Real-Time UI**
   - Server-side Blazor patterns
   - State management with events
   - Component lifecycle
   - Dispose patterns for memory leaks

5. **Database Optimization**
   - Bulk operations with Dapper
   - VARBINARY storage for vectors
   - SQL-level filtering
   - Index strategies

6. **Clean Architecture**
   - Layer separation
   - Dependency injection
   - Service patterns
   - Repository pattern

### Architectural Patterns Used

- **Repository Pattern** - Data access abstraction
- **Factory Pattern** - Object creation (ProcessStartInfo)
- **Strategy Pattern** - Different memory implementations
- **Observer Pattern** - State change notifications
- **Singleton Pattern** - Service lifetimes
- **Builder Pattern** - Fluent configuration

### Design Decisions

1. **Dapper over EF Core**
   - ? Better performance for bulk operations
   - ? More control over SQL
   - ? Lightweight

2. **Blazor Server over WebAssembly**
   - ? Direct server access
   - ? Smaller client footprint
   - ? Real-time updates with SignalR

3. **SQL Server over NoSQL**
   - ? ACID compliance
   - ? Mature tooling
   - ? Familiar technology

4. **On-Demand Queries over In-Memory**
   - ? Scales to large knowledge bases
   - ? Lower memory usage
   - ? Database-level optimizations

---

## ?? Known Limitations

### Current Constraints

1. **Model Size**
   - Limited by available RAM
   - GPU acceleration helps but optional
   - Recommended: 8GB+ RAM for 7B models

2. **Query Speed**
   - First query slower (model loading)
   - Subsequent queries faster (pooling)
   - Typical: 5-30 seconds per response

3. **Database**
   - Currently SQL Server only
   - No distributed setup
   - Single-machine deployment

4. **Embedding Model**
   - Fixed to MPNet (768 dimensions)
   - No dynamic model switching
   - English-optimized

### Future Improvements

- [ ] PostgreSQL support (pgvector)
- [ ] Multi-language embedding models
- [ ] Streaming responses (token-by-token)
- [ ] Conversation history persistence
- [ ] Export/import knowledge bases
- [ ] Docker containerization
- [ ] API endpoint for external access

---

## ?? Documentation

Comprehensive documentation available in `/Docs`:

### Getting Started
- `KnowledgeDomainQuickStart.md` - Domain system overview
- `PDFProcessingGuide.md` - Document ingestion guide
- `RAGSettingsConfiguration.md` - RAG tuning guide

### Architecture
- `DashboardServiceRefactoring.md` - State management
- `DatabaseLevelDomainFiltering.md` - Query optimization
- `CosineSimilarityExtensionRefactor.md` - Vector math

### Implementation Details
- `DomainRefactoringComplete.md` - Domain system
- `VectorMemoryRemovalComplete.md` - Architecture cleanup
- `ChatTopBarReactiveUpdateFix.md` - Blazor patterns

### Guides
- `FileTypeProcessingGuide.md` - Document handling
- `LLMTimeoutDiagnosis.md` - Troubleshooting
- `GameManagementUIGuide.md` - Domain management

---

## ?? Contributing

This is a personal learning project, but feedback and suggestions are welcome!

### How to Contribute

1. **Report Issues** - Found a bug? Open an issue
2. **Suggest Features** - Have an idea? Start a discussion
3. **Share Knowledge** - Learned something? Add documentation
4. **Submit PRs** - Fixed something? PRs welcome!

### Development Setup

1. Fork the repository
2. Create a feature branch (`feature/amazing-feature`)
3. Make your changes
4. Run tests (if any)
5. Submit a pull request

---

## ?? License

This project is licensed under the MIT License - see the LICENSE file for details.

**TLDR:** You can use this code for anything, including commercial projects. Just keep the copyright notice.

---

## ?? Acknowledgments

### Technologies
- **llama.cpp** - Amazing local LLM runtime
- **Microsoft Semantic Kernel** - AI orchestration framework
- **Dapper** - Fast and simple data access
- **UglyToad PdfPig** - PDF processing in .NET

### Inspiration
- Simon Willison's RAG explorations
- LangChain documentation
- HuggingFace community
- r/LocalLLaMA subreddit

### Learning Resources
- Andrew Ng's Deep Learning Specialization
- Microsoft Learn (Blazor docs)
- Lex Fridman's AI podcasts
- GitHub Copilot (my coding companion)

---

## ?? Contact

**Markus Nordin**
- GitHub: [@NordinsProjekt](https://github.com/NordinsProjekt)
- Project: [OfflineAI](https://github.com/NordinsProjekt/OfflineAI)

---

## ?? Star History

If you found this project helpful for learning RAG or .NET architecture, please consider giving it a star! ?

---

## ?? Roadmap

### Phase 1: Core Functionality ?
- [x] LLM integration with llama.cpp
- [x] Vector embeddings with BERT
- [x] SQL Server vector store
- [x] PDF document processing
- [x] RAG implementation
- [x] Blazor dashboard

### Phase 2: Optimization ?
- [x] Domain filtering
- [x] Database-level queries
- [x] Model pooling
- [x] Configurable RAG parameters

### Phase 3: Enhancement ??
- [ ] Streaming responses
- [ ] Conversation persistence
- [ ] Multi-user support
- [ ] API endpoints

### Phase 4: Deployment ??
- [ ] Docker support
- [ ] Cloud deployment guide
- [ ] Performance benchmarks
- [ ] Unit tests

---

<div align="center">

**Built with ?? and curiosity about AI**

*Learning by building • One commit at a time*

[Report Bug](https://github.com/NordinsProjekt/OfflineAI/issues) • [Request Feature](https://github.com/NordinsProjekt/OfflineAI/issues) • [Documentation](./Docs/)

</div>
