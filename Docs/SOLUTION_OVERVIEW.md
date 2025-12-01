# OfflineAI Solution Overview

## Project Description
OfflineAI is an intelligent Retrieval-Augmented Generation (RAG) system that enables running AI-powered question-answering applications entirely offline using local Large Language Models (LLMs). The system combines semantic search with vector embeddings to provide context-aware responses from a knowledge base, making it ideal for scenarios requiring privacy, data sovereignty, or operation without internet connectivity.

## Tech Stack

### Core Technologies
- **.NET 9.0** - Primary framework for all projects
- **C# 13.0** - Latest language features for improved performance and code quality
- **Blazor Server** - Interactive web UI framework for the dashboard application

### AI/ML Components
- **llama.cpp** - Local LLM inference engine (via `llama-cli.exe`)
- **ONNX Runtime** - For running embedding models
  - **Microsoft.ML.OnnxRuntime** (v1.20.1)
  - **Microsoft.ML.OnnxRuntime.DirectML** (v1.20.1) - GPU acceleration support
- **Microsoft Semantic Kernel** (v1.66.0) - AI orchestration framework
- **BERTTokenizers** (v1.2.0) - Text tokenization for embeddings
- **Microsoft.ML.Tokenizers** (v2.0.0) - Modern tokenization library

### Database & Storage
- **Microsoft SQL Server (LocalDB)** - Vector storage and metadata
- **Dapper** (v2.1.66) - High-performance micro-ORM
- **Microsoft.Data.SqlClient** (v6.1.2) - SQL Server connectivity

### Document Processing
- **UglyToad.PdfPig** (v1.7.0-custom-5) - PDF parsing and text extraction

### Testing
- **xUnit** - Unit testing framework
- **Moq** - Mocking framework for tests

## Solution Architecture

### Project Structure

```
OfflineAI/
??? AiDashboard/              # Blazor Server web application
?   ??? Components/           # Blazor components, pages, layouts
?   ??? Services/             # Dashboard-specific services
?   ??? State/                # Application state management
?   ??? Program.cs            # Application entry point
?
??? AI/                       # Core AI/ML functionality
?   ??? Chat/                 # Chat services with LLM interaction
?   ??? Embeddings/           # Semantic embedding generation
?   ??? Management/           # Model lifecycle management
?   ??? Models/               # AI model interfaces and implementations
?   ??? Pooling/              # LLM instance pooling for performance
?   ??? Processing/           # LLM process management
?   ??? Utilities/            # AI helper utilities
?
??? Services/                 # Business logic and services
?   ??? Configuration/        # Application configuration models
?   ??? Interfaces/           # Service interfaces
?   ??? Management/           # Service orchestration
?   ??? Memory/               # Vector memory and document processing
?   ??? Repositories/         # Data access interfaces
?   ??? UI/                   # UI helper services
?   ??? Utilities/            # General utilities
?
??? Infrastructure.Data.Dapper/  # Data access layer
?   ??? [Repositories]        # Dapper implementations
?
??? Entities/                 # Domain entities and data models
?   ??? MemoryFragmentEntity.cs
?   ??? LlmEntity.cs
?   ??? QuestionEntity.cs
?   ??? BotPersonalityEntity.cs
?   ??? KnowledgeDomainEntity.cs
?
??? Factories/                # Object creation patterns
?
??? Services.Tests/           # Service layer tests
??? Application.AI.Tests/     # AI functionality tests
??? Presentation.AiDashboard.Tests/  # Dashboard tests
```

## How the System Works

### 1. Document Ingestion & Processing

**Workflow:**
```
PDF/Text Files ? Inbox Folder ? MultiFormatFileWatcher
    ?
Document Chunking (max 1500 chars per chunk)
    ?
Embedding Generation (768-dim vectors via all-mpnet-base-v2)
    ?
Storage in SQL Database (MemoryFragmentEntity)
```

**Key Components:**
- `MultiFormatFileWatcher` - Monitors inbox folder for new documents
- `PdfFragmentProcessor` - Extracts text from PDFs
- `DocumentChunker` - Splits documents into manageable fragments
- `SemanticEmbeddingService` - Generates vector embeddings using ONNX models
- `VectorMemoryPersistenceService` - Persists fragments and embeddings to database

### 2. Vector Memory System

**Storage Strategy:**
- **Collections** - Logical groupings of related documents (e.g., "game-rules-mpnet", "recycling-guide")
- **Fragments** - Individual chunks of text with metadata
- **Multiple Embeddings per Fragment:**
  - **Category Embedding** - Vector for the fragment's category/title
  - **Content Embedding** - Vector for the main content
  - **Combined Embedding** - Fallback/legacy full-text embedding

**Database Schema:**
```sql
MemoryFragmentEntity:
- Id (GUID)
- CollectionName (string)
- Category (string)
- Content (string)
- ContentLength (int)
- Embedding (binary) - Combined embedding
- CategoryEmbedding (binary) - Weighted search
- ContentEmbedding (binary) - Weighted search
- EmbeddingDimension (int)
- CreatedAt, UpdatedAt (datetime)
- SourceFile, ChunkIndex (metadata)
```

### 3. Retrieval-Augmented Generation (RAG)

**Query Flow:**
```
User Question ? Domain Detection (optional)
    ?
Keyword Extraction (remove stop words)
    ?
Query Embedding Generation
    ?
Hybrid Search (Vector + Fuzzy + Exact Match)
    ?
Top-K Relevant Fragments (typically 3-5)
    ?
Context Assembly (?1500 chars)
    ?
LLM Prompt Construction
    ?
Local LLM Inference
    ?
Response to User
```

**Search Strategy (`DatabaseVectorMemory`):**
1. **Vector Similarity** - Weighted cosine similarity (40% category, 30% content, 30% combined)
2. **Exact String Matching** - Boosts results containing query terms
3. **Fuzzy Matching** - Levenshtein distance for typo tolerance
4. **Phrase Preservation** - Recognizes multi-word concepts

### 4. LLM Instance Pooling

**Purpose:** Maintain warm LLM instances to reduce latency

**Architecture:**
```
ModelInstancePool (manages N instances)
    ??? PooledInstance #1 (PersistentLlmProcess)
    ??? PooledInstance #2 (PersistentLlmProcess)
    ??? PooledInstance #N (PersistentLlmProcess)
```

**Benefits:**
- **Fast Response Times** - No model loading overhead
- **Concurrent Requests** - Multiple users can query simultaneously
- **Resource Management** - Configurable instance limits

**Configuration:**
- `Pool.MaxInstances` - Maximum number of LLM instances (default: 2-3)
- `Pool.TimeoutMs` - Query timeout in milliseconds (default: 45000)

### 5. Blazor Dashboard

**Features:**
- **Interactive Chat Interface** - Ask questions and receive AI-powered answers
- **RAG Mode Toggle** - Switch between RAG-enhanced and direct LLM modes
- **Collection Management** - Load, switch, and manage knowledge collections
- **Model Switching** - Hot-swap between different GGUF models
- **Performance Metrics** - Monitor tokens/sec, response times
- **Domain Filtering** - Focus queries on specific knowledge domains
- **Bot Personalities** - Customize AI response style and tone

**State Management:**
- `DashboardState` - Centralized state for the application
- `DashboardChatService` - Orchestrates RAG queries and LLM interactions

### 6. Configuration System

**Hierarchy:**
1. `appsettings.json` - Base configuration (checked into source control)
2. **User Secrets** - Sensitive/machine-specific settings (NOT in source control)
3. **Environment Variables** - Override for deployment scenarios

**Key Settings:**
```json
{
  "AppConfiguration": {
    "Llm": {
      "ExecutablePath": "path/to/llama-cli.exe",
      "ModelPath": "path/to/model.gguf",
      "UseGpu": false,
      "GpuLayers": 0,
      "ContextSize": 2048
    },
    "Embedding": {
      "ModelPath": "path/to/model.onnx",
      "VocabPath": "path/to/vocab.txt",
      "Dimension": 768
    },
    "Pool": {
      "MaxInstances": 2,
      "TimeoutMs": 45000
    },
    "Generation": {
      "MaxTokens": 200,
      "Temperature": 0.3,
      "TopK": 30,
      "TopP": 0.85,
      "RepeatPenalty": 1.15
    },
    "Debug": {
      "EnableRagMode": true,
      "CollectionName": "game-rules-mpnet"
    }
  },
  "DatabaseConfig": {
    "ConnectionString": "Server=(localdb)\\mssqllocaldb;Database=VectorMemoryDB;...",
    "ActiveTableName": "MemoryFragments"
  }
}
```

## Data Flow

### Typical RAG Query Execution

```
???????????????
?   User UI   ? (Blazor Component)
???????????????
       ? Question
       ?
????????????????????????
? DashboardChatService ?
????????????????????????
           ? 1. Detect Domain (optional)
           ?
????????????????????????
?   DomainDetector     ? (Keyword/embedding matching)
????????????????????????
           ? Domain IDs
           ?
????????????????????????
? DatabaseVectorMemory ? (SearchRelevantMemoryAsync)
????????????????????????
           ? 2. Generate query embedding
           ? 3. Hybrid search (vector + fuzzy + exact)
           ? 4. Retrieve top-K fragments
           ?
????????????????????????
? VectorMemoryRepository? (Dapper ? SQL)
????????????????????????
           ? MemoryFragmentEntities
           ?
????????????????????????
? AiChatServicePooled  ? (Construct prompt)
????????????????????????
           ? System Prompt + Context + Question
           ?
????????????????????????
?  ModelInstancePool   ? (Acquire instance)
????????????????????????
           ? PooledInstance
           ?
????????????????????????
? PersistentLlmProcess ? (llama-cli.exe)
????????????????????????
           ? Generated Response
           ?
????????????????????????
?  User UI (Response)  ?
????????????????????????
```

## Key Design Patterns

### 1. Repository Pattern
- **Interfaces:** `IVectorMemoryRepository`, `ILlmRepository`, `IQuestionRepository`, etc.
- **Implementations:** Dapper-based in `Infrastructure.Data.Dapper` project
- **Benefits:** Decouples data access from business logic, enables testing with mocks

### 2. Dependency Injection
- All services registered in `Program.cs` startup
- Scoped and Singleton lifetimes managed appropriately
- Constructor injection for testability

### 3. Strategy Pattern
- `ISearchableMemory` interface with multiple implementations:
  - `DatabaseVectorMemory` - Production vector search
  - `StringJoinMemory` - Simple in-memory (for conversation history)

### 4. Object Pool Pattern
- `ModelInstancePool` - Reuses expensive LLM instances
- `PooledInstance` - Wrapper with automatic return-to-pool

### 5. Observer Pattern
- `MultiFormatFileWatcher` - Monitors file system changes
- Event-driven document processing pipeline

## Performance Considerations

### Optimization Strategies

1. **Database Indexing**
   - Indexes on `CollectionName`, `Category`, `ContentLength`, `CreatedAt`
   - Efficient filtering at SQL level for domain-based queries

2. **Embedding Caching**
   - Embeddings stored as binary data in database
   - No regeneration needed for repeated queries

3. **Hybrid Search**
   - Combines vector similarity with string matching
   - Fuzzy matching (Levenshtein distance) for typo tolerance
   - Boosts relevance scores for exact/partial matches

4. **Context Truncation**
   - Limits retrieved context to avoid LLM context window overflow
   - Sentence-boundary truncation for readability
   - Configurable `maxCharsPerFragment` and `topK` parameters

5. **LLM Pooling**
   - Eliminates model loading overhead (can take 5-30 seconds)
   - Enables concurrent query handling
   - Configurable pool size and timeout

## Security & Privacy

### Data Sovereignty
- **100% Offline** - No external API calls, no internet required
- **Local Storage** - All data resides in local SQL database
- **Private Models** - LLMs and embeddings run entirely on-premises

### Sensitive Configuration
- **User Secrets** - Keeps file paths and connection strings out of source control
- **Windows Authentication** - SQL LocalDB uses integrated security

## Testing Strategy

### Test Projects
- **Services.Tests** - Unit tests for business logic services
- **Application.AI.Tests** - Tests for AI components (embeddings, LLM interaction)
- **Presentation.AiDashboard.Tests** - Dashboard component tests

### Testability Features
- Repository interfaces enable mocking
- Dependency injection facilitates unit testing
- Separation of concerns between layers

## Deployment Considerations

### Prerequisites
1. **.NET 9 Runtime** (or SDK for development)
2. **SQL Server LocalDB** (included with Visual Studio) or full SQL Server
3. **llama.cpp executable** (`llama-cli.exe`)
4. **GGUF model files** (e.g., TinyLlama, Mistral, Llama3)
5. **ONNX embedding models** (e.g., all-mpnet-base-v2)

### Typical Deployment Workflow
1. Clone repository
2. Configure User Secrets with file paths
3. Run database initialization (automatic on first launch)
4. Place knowledge documents in configured inbox folder
5. Start the Blazor dashboard application
6. Browse to `https://localhost` (default port)

### Resource Requirements
- **CPU:** Multi-core recommended (LLM inference is CPU-intensive without GPU)
- **RAM:** 8-16 GB minimum (depends on model size)
- **GPU:** Optional (DirectML supported for Windows, CUDA for Linux)
- **Disk:** 5-50 GB for models and vector database

## Extensibility Points

### Adding New Document Types
1. Implement new processor inheriting from `PdfFragmentProcessor` pattern
2. Register in `MultiFormatFileWatcher`
3. Handle new file extensions in monitoring logic

### Custom Embedding Models
1. Replace ONNX model in `SemanticEmbeddingService`
2. Adjust `EmbeddingDimension` configuration
3. Re-generate embeddings for existing collections

### Alternative LLM Backends
1. Implement `IPersistentLlmProcess` interface
2. Update `ModelInstancePool` to use new implementation
3. Adjust prompt templates if model has different formatting requirements

### Multi-Language Support
- Update `ExtractKeywords()` stop word lists for new languages
- Adjust `DomainDetector` keyword matching rules
- Use language-specific embedding models

## Known Limitations

1. **Context Window** - Limited by LLM model (2048-8192 tokens typical)
2. **Swedish Embeddings** - English models (MPNet) are suboptimal for Swedish text
3. **GPU Support** - DirectML (Windows) and CUDA (Linux) have different performance profiles
4. **Concurrent Queries** - Limited by `Pool.MaxInstances` configuration
5. **Document Size** - Very large documents (>1MB) may require chunking optimization

## Future Roadmap

### Potential Enhancements
- Multi-modal support (images, tables)
- Fine-tuning embeddings for specific domains
- Distributed deployment (multi-server setup)
- Advanced prompt engineering tools
- Chat history persistence
- Export/import of collections
- API layer for programmatic access
- Real-time collaboration features

## Getting Started

### For Developers
1. Clone the repository
2. Open `OfflineAI.sln` in Visual Studio 2022+
3. Configure User Secrets (see documentation)
4. Build and run `AiDashboard` project
5. Access at `https://localhost:5001`

### For Users
1. Install prerequisites (.NET 9 Runtime, SQL LocalDB)
2. Download and configure LLM models
3. Place documents in inbox folder
4. Launch dashboard application
5. Start asking questions!

---

**Document Version:** 1.0  
**Last Updated:** 2024  
**Maintained By:** OfflineAI Development Team
