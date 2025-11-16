# OfflineAI - Local LLM RAG System

> **A personal project exploring Retrieval-Augmented Generation (RAG) with local LLMs**  
> Built with .NET 9, Blazor, and llama.cpp - Fully offline, multilingual support (Swedish/English)

![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?style=flat&logo=dotnet)
![C#](https://img.shields.io/badge/C%23-13.0-239120?style=flat&logo=csharp)
![Blazor](https://img.shields.io/badge/Blazor-Server-512BD4?style=flat&logo=blazor)
![License](https://img.shields.io/badge/license-MIT-blue.svg)

---

## ?? Project Overview

**OfflineAI** is a RAG (Retrieval-Augmented Generation) system that runs entirely offline using local LLM models. It demonstrates modern .NET architecture while providing practical knowledge-based AI assistance with **multilingual support for Swedish and English**.

---

## ? Key Features

### ?? Local LLM Integration
- **llama.cpp** integration for running GGUF models locally
- Support for any GGUF-format model (Llama 3, Phi-3, Qwen, Mistral, etc.)
- **Multilingual models** - Swedish and English support
- Model pooling for efficient resource usage
- Real-time model switching without restart
- GPU acceleration support (CUDA, ROCm, Metal, Vulkan)

### ?? RAG (Retrieval-Augmented Generation)
- **Semantic search** using BERT embeddings
- **Database-backed vector store** (SQL Server)
- Domain-based filtering (multi-topic knowledge bases)
- Configurable relevance thresholds
- Database-level filtering for optimal performance

### ?? Document Processing
- **PDF support** with intelligent chunking
- **TXT file support** with hierarchy detection
- Smart document chunking with overlap
- Automatic metadata extraction
- Inbox/Archive workflow

### ?? Modern Blazor Dashboard
- Real-time updates with SignalR
- Dark theme optimized for extended use
- Responsive design
- Live model switching
- Generation parameter tuning
- Collection and domain management

---

## ??? Architecture

### Clean Architecture Layers

```
???????????????????????????????????????????
?         AiDashboard (Blazor)            ?  ? Presentation Layer
?  • Real-time UI                         ?
?  • State Management                     ?
???????????????????????????????????????????
                    ?
???????????????????????????????????????????
?          AI (Application)                ?  ? Application Layer
?  • Chat Service                          ?
?  • Model Management                      ?
?  • Embedding Service                     ?
???????????????????????????????????????????
                    ?
???????????????????????????????????????????
?           Services (Core)                ?  ? Business Logic
?  • RAG Implementation                    ?
?  • Document Processing                   ?
?  • Vector Memory                         ?
???????????????????????????????????????????
                    ?
???????????????????????????????????????????
?   Infrastructure.Data.Dapper             ?  ? Data Access Layer
?  • Vector Repository                     ?
?  • Domain Repository                     ?
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

## ??? Technologies Used

### Core Stack
- **.NET 9** / **C# 13**
- **Blazor Server** - Real-time web UI
- **SQL Server** - Vector storage

### AI/ML
- **llama.cpp** - Local LLM runtime (GGUF models)
- **Microsoft.SemanticKernel** - AI orchestration
- **BERT Embeddings** - Semantic search (MPNet all-mpnet-base-v2)
- **ONNX Runtime** - ML model execution

### Data & Processing
- **Dapper** - Micro-ORM for performance
- **UglyToad.PdfPig** - PDF text extraction
- **SignalR** - Real-time updates

---

## ?? Getting Started

### Prerequisites

1. **Development Environment**
   - Visual Studio 2022 (17.8+) or Visual Studio Code
   - .NET 9 SDK
   - SQL Server (LocalDB or full server)

2. **LLM Runtime**
   - Download llama.cpp from: https://github.com/ggerganov/llama.cpp/releases
   - Extract `llama-cli.exe` (or `llama-server.exe`)

3. **LLM Model (GGUF format)**
   - **For Swedish/English support:** Use multilingual models
   - Example: Phi-3.5 Mini: https://huggingface.co/microsoft/Phi-3.5-mini-instruct-gguf
   - Or any GGUF model from HuggingFace

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
   dotnet user-secrets set "AppConfiguration:Llm:ExecutablePath" "d:/tinyllama/llama-cli.exe"
   dotnet user-secrets set "AppConfiguration:Llm:ModelPath" "d:/tinyllama/phi-3.5-mini-instruct.gguf"
   dotnet user-secrets set "AppConfiguration:Embedding:ModelPath" "d:/tinyllama/models/all-mpnet-base-v2/onnx/model.onnx"
   dotnet user-secrets set "AppConfiguration:Embedding:VocabPath" "d:/tinyllama/models/all-mpnet-base-v2/vocab.txt"
   dotnet user-secrets set "AppConfiguration:Folders:InboxFolder" "d:/tinyllama/inbox"
   dotnet user-secrets set "AppConfiguration:Folders:ArchiveFolder" "d:/tinyllama/archive"
   ```

3. **Create required folders**
   ```bash
   mkdir d:/tinyllama/inbox
   mkdir d:/tinyllama/archive
   ```

4. **Run the application**
   ```bash
   dotnet run --project AiDashboard
   ```

5. **Access the dashboard**
   - Open browser to: `https://localhost:5001`

### First Use

1. **Add knowledge documents** (PDF or TXT files in Swedish/English)
   - Place files in the inbox folder
   - Click "Reload Inbox" in the dashboard

2. **Create domains** (optional)
   - Navigate to Domains Management
   - Add topics to organize your knowledge base

3. **Start chatting**
   - Ask questions in Swedish or English
   - Toggle RAG ON to use your knowledge base
   - Toggle RAG OFF for direct LLM responses

---

## ?? Key Concepts

### RAG (Retrieval-Augmented Generation)

RAG enhances LLM responses by providing relevant context from your knowledge base:

```
User Question (Swedish/English)
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
6. LLM generates response in same language
```

### Multilingual Support

The system uses multilingual models that understand both Swedish and English:
- Ask questions in either language
- Process documents in both languages
- Get responses in the language of your question
- No separate configuration needed

---

## ?? Configuration

### Generation Settings

Located in `appsettings.json` or User Secrets:

```json
{
  "AppConfiguration": {
    "Generation": {
      "MaxTokens": 512,
      "Temperature": 0.3,
      "TopK": 40,
      "TopP": 0.95,
      "RepeatPenalty": 1.15
    }
  }
}
```

### RAG Settings

```json
{
  "AppConfiguration": {
    "Generation": {
      "RagTopK": 3,
      "RagMinRelevanceScore": 0.5
    }
  }
}
```

---

## ?? Performance Optimizations

### Database-Level Domain Filtering
- **84% less data transfer** with SQL-level filtering
- **75% faster queries** compared to in-memory filtering

### Model Pooling
- Parallel request handling
- No model reload overhead
- Better resource utilization

### Context Size Optimization
- Limited to 2048 tokens for optimal performance
- Prevents memory issues with large documents

---

## ?? Documentation

- **Comprehensive documentation** available in `/Docs`
- Covers setup, architecture, guides, and more

---

## ?? Contact

**Markus Nordin**
- GitHub: [@NordinsProjekt](https://github.com/NordinsProjekt)
- Project: [OfflineAI](https://github.com/NordinsProjekt/OfflineAI)

---

<div align="center">

**Built with ?? and curiosity about AI**

*Learning by building • One commit at a time*

[Report Bug](https://github.com/NordinsProjekt/OfflineAI/issues) • [Request Feature](https://github.com/NordinsProjekt/OfflineAI/issues)

</div>
