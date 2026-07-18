# OfflineAI API Configuration

> **Related docs:** [RAG_CONTEXT_TEMPLATES.md](RAG_CONTEXT_TEMPLATES.md) (manual context templates) ·
> [DOMAIN_DISCOVERY.md](DOMAIN_DISCOVERY.md) (domain filter endpoints) ·
> [WORKSPACE_AND_FILES_GUIDE.md](WORKSPACE_AND_FILES_GUIDE.md) (workspaces, file upload, PDFs, pictures)

## Error: "No healthy instances available in pool"

This error occurs when the LLM model pool cannot be initialized. The most common cause is missing or incorrect configuration.

## Solution: Configure User Secrets

### Step 1: Open User Secrets
1. In Visual Studio, right-click the **OfflineAI.Api** project
2. Select **Manage User Secrets**
3. This will open the `secrets.json` file

### Step 2: Add Your Configuration
Replace the contents of `secrets.json` with:

```json
{
  "AppConfiguration": {
    "Llm": {
      "ExecutablePath": "C:\\path\\to\\your\\llama-cli.exe",
      "ModelPath": "C:\\path\\to\\your\\model.gguf",
      "ModelName": "mistral-7b-instruct-v0.2.q5_k_m",
      "ModelType": "Mistral",
      "UseGpu": false,
      "GpuLayers": 0,
      "ContextSize": 2048
    }
  }
}
```

### Step 3: Update the Paths
Replace the placeholder paths with your actual file paths:
- **ExecutablePath**: Path to your `llama-cli.exe` or similar LLM executable
- **ModelPath**: Path to your `.gguf` model file (e.g., Mistral, Llama, etc.)

### Step 4: Restart the API
1. Stop the API if it's running
2. Start it again
3. The console will show whether the configuration is valid

## Example Configuration

If you have the same setup as the AiDashboard project, you can copy the configuration from:
- The other User Secrets file you have open: `0b725f58-2de8-44d7-873c-73d5891fd43c\secrets.json`

## Verification

When the API starts correctly, you'll see:
```
? OfflineAI API is running
?? Swagger UI: https://localhost:7015/swagger
?? LLM Configured: True
```

When there are configuration errors, you'll see:
```
??  CONFIGURATION ERRORS DETECTED
? AppConfiguration:Llm:ExecutablePath is missing
? AppConfiguration:Llm:ModelPath is missing
```

## Testing

Once configured, test with:
```bash
curl -X 'POST' \
  'https://localhost:7015/api/Query' \
  -H 'accept: application/json' \
  -H 'Content-Type: application/json' \
  -d '{
  "question": "What is 10+9?",
  "enableRag": false,
  "maxTokens": 512,
  "temperature": 0.3
}'
```

## Quick Copy from AiDashboard

Since you have both User Secrets files open:
1. Copy the `AppConfiguration` section from the AiDashboard secrets
2. Paste it into the OfflineAI.Api secrets
3. Save and restart the API

# OfflineAI API - RAG Configuration Guide

## Overview

The OfflineAI API supports three query modes:

1. **Direct Query** - No additional context, just the LLM
2. **Manual Context RAG** - You provide pre-retrieved context
3. **Auto Vector Search RAG** - Automatically searches knowledge base using embeddings

## Quick Start: Discover Available Domains

Before using domain filters, discover what domains are available:

```bash
# Get all available domains
GET /api/Domains

# Get domains by category
GET /api/Domains/category/board-games

# Get all categories
GET /api/Domains/categories
```

**Example Response:**
```json
{
  "domains": [
    {
      "domainId": "chess",
      "displayName": "Chess",
      "category": "board-games",
      "createdAt": "2024-01-15T10:30:00Z",
      "source": "manual"
    },
    {
      "domainId": "monopoly",
      "displayName": "Monopoly",
      "category": "board-games",
      "createdAt": "2024-01-15T10:35:00Z",
      "source": "manual"
    }
  ],
  "count": 2,
  "categories": ["board-games", "card-games"],
  "usage": {
    "message": "Use domainId values in the domainFilter array when making RAG queries",
    "example": {
      "domainFilter": ["chess"]
    }
  }
}
```

## API Endpoints

### Discovery Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/Health` | GET | Health check |
| `/api/Health/models` | GET | Available LLM models |
| `/api/Domains` | GET | **All available domain filters** |
| `/api/Domains/category/{category}` | GET | Domains by category |
| `/api/Domains/categories` | GET | All categories |

### Query Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/Query` | POST | Execute LLM query (with optional RAG) |
| `/api/Query/validate` | POST | Validate request parameters |
| `/api/Query/image` | POST | Ask a question about an uploaded picture (Gemma 4 multimodal) |

### Workspace & File Endpoints

Workspaces, file upload/listing, PDF text extraction, PDF-to-RAG ingestion, and image
question-answering are covered in detail in
[WORKSPACE_AND_FILES_GUIDE.md](WORKSPACE_AND_FILES_GUIDE.md). Quick reference:

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/Workspace` | GET / POST | List / create workspaces |
| `/api/Workspace/active` | GET / POST | Get / switch the active workspace |
| `/api/Workspace/{name}` | DELETE | Remove a workspace |
| `/api/Files` | GET | List files in the active workspace |
| `/api/Files/upload` | POST | Upload a file (image/PDF/text) into the active workspace |
| `/api/Files/{filename}/text` | GET | Extract PDF/text content from a workspace file |
| `/api/Files/{filename}/ingest` | POST | Ingest a workspace PDF into the RAG knowledge base |
| `/api/Files/{filename}/ask-image` | POST | Ask a question about a workspace image |

## Query Modes

### 1. Direct Query (No RAG)

Use this when you don't need additional context, just the LLM's knowledge.

```json
{
  "question": "What is 10+9?",
  "enableRag": false
}
```

### 2. Manual Context RAG

Provide your own context. Useful when you've already retrieved relevant information or want precise control over what context is used.

**Example: Game Rules**
```json
{
  "question": "How do I win in Monopoly?",
  "context": "In Monopoly, the objective is to bankrupt all other players. Players move around the board by rolling two dice. When landing on an unowned property, a player may buy it. If another player owns the property, rent must be paid. The game ends when all but one player has gone bankrupt.",
  "enableRag": true
}
```

**Context Format Best Practices:**
- Keep context focused and relevant
- Use clear, concise language
- Separate different topics with line breaks
- Typical length: 200-1000 characters
- Maximum recommended: 2000 characters

### 3. Auto Vector Search RAG

Let the API automatically search the knowledge base using semantic similarity.

**Step 1: Discover Available Domains**
```bash
GET /api/Domains
```

**Step 2: Use Domain Filters in Your Query**

**Basic Auto RAG:**
```json
{
  "question": "How does the knight move in chess?",
  "enableRag": true,
  "topK": 3,
  "minRelevanceScore": 0.5
}
```

**With Domain Filtering:**
```json
{
  "question": "What happens when I land on Free Parking?",
  "enableRag": true,
  "domainFilter": ["monopoly"],
  "topK": 5,
  "minRelevanceScore": 0.6
}
```

**Multiple Domains:**
```json
{
  "question": "Can pieces move backwards?",
  "enableRag": true,
  "domainFilter": ["chess", "checkers"],
  "topK": 4,
  "minRelevanceScore": 0.5
}
```

## Workflow: Using Domain Filters

### Recommended Workflow

1. **Discover domains**: `GET /api/Domains`
2. **Choose relevant domains**: Look at `domainId` values
3. **Make RAG query**: Use `domainFilter` with chosen IDs

### Example Workflow

```bash
# Step 1: Discover what domains exist
GET /api/Domains

# Response shows:
# - chess (board-games)
# - monopoly (board-games)
# - poker (card-games)

# Step 2: Query with specific domain
POST /api/Query
{
  "question": "How do I castle?",
  "enableRag": true,
  "domainFilter": ["chess"],
  "topK": 3,
  "minRelevanceScore": 0.6
}
```

### Filtering by Category

```bash
# Get all board game domains
GET /api/Domains/category/board-games

# Use multiple board game domains
POST /api/Query
{
  "question": "What happens when I roll doubles?",
  "enableRag": true,
  "domainFilter": ["monopoly", "backgammon"],
  "topK": 5
}
```

## Configuration

### Required for All Modes

Set in User Secrets (Right-click project ? Manage User Secrets):

```json
{
  "AppConfiguration": {
    "Llm": {
      "ExecutablePath": "d:\\tinyllama\\llama-cli.exe",
      "ModelPath": "d:\\tinyllama\\mistral-7b-instruct-v0.2.Q5_K_M.gguf"
    }
  }
}
```

### Additional for Auto Vector Search RAG

Auto vector search requires:
1. **Embedding Service** - Converts text to vectors
2. **Vector Database** - Stores document embeddings
3. **Knowledge Base** - Pre-indexed documents
4. **Domain Repository** - Domain metadata (optional but recommended)

**User Secrets Configuration:**
```json
{
  "AppConfiguration": {
    "Llm": {
      "ExecutablePath": "d:\\tinyllama\\llama-cli.exe",
      "ModelPath": "d:\\tinyllama\\mistral-7b-instruct-v0.2.Q5_K_M.gguf"
    },
    "Embedding": {
      "ModelPath": "d:\\tinyllama\\models\\paraphrase-multilingual-mpnet-base-v2\\model.onnx",
      "VocabPath": "d:\\tinyllama\\models\\paraphrase-multilingual-mpnet-base-v2\\tokenizer.json",
      "Dimension": 768
    },
    "Database": {
      "ConnectionString": "Server=(localdb)\\mssqllocaldb;Database=VectorMemoryDB;Integrated Security=true;TrustServerCertificate=true;",
      "ActiveTableName": "MemoryFragments"
    }
  }
}
```

## Domain Management

### Understanding Domains

**What is a domain?**
- A logical grouping of related knowledge (e.g., "chess", "monopoly")
- Used to filter vector search results
- Improves relevance by limiting search scope

**Domain Structure:**
```json
{
  "domainId": "chess",        // Unique identifier (lowercase, hyphenated)
  "displayName": "Chess",     // Human-readable name
  "category": "board-games",  // Grouping category
  "source": "manual"          // How it was created
}
```

### Common Domain Categories

| Category | Description | Example Domains |
|----------|-------------|-----------------|
| `board-games` | Board game rules | chess, monopoly, risk |
| `card-games` | Card game rules | poker, uno, bridge |
| `video-games` | Video game guides | minecraft, terraria |
| `support-docs` | Customer support | product-faq, troubleshooting |
| `technical` | Technical docs | api-docs, user-manuals |

### When Domain Repository is Not Available

If you see:
```json
{
  "error": "Domain repository not configured",
  "availableModes": [
    "Direct Query (enableRag: false)",
    "Manual Context RAG (provide context field)"
  ]
}
```

**You can still use:**
- ? Direct queries (`enableRag: false`)
- ? Manual context RAG (provide `context` field)

**Not available:**
- ? Auto vector search
- ? Domain filtering
- ? Domain discovery endpoints

## Parameters Reference

### Core Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `question` | string | *required* | The user's question or prompt |
| `enableRag` | boolean | `true` | Enable/disable RAG mode |
| `context` | string | `null` | Manual context (skips vector search if provided) |
| `domainFilter` | string[] | `null` | Filter vector search to specific domains |

### Generation Parameters

| Parameter | Type | Default | Range | Description |
|-----------|------|---------|-------|-------------|
| `maxTokens` | int | `512` | 1-4096 | Maximum tokens to generate |
| `temperature` | float | `0.3` | 0.0-2.0 | Creativity (lower=focused, higher=creative) |

### RAG Parameters

| Parameter | Type | Default | Range | Description |
|-----------|------|---------|-------|-------------|
| `topK` | int | `3` | 1-20 | Number of documents to retrieve |
| `minRelevanceScore` | double | `0.5` | 0.0-1.0 | Minimum similarity score |

**Relevance Score Guidelines:**
- `0.3-0.4` - Very broad, may include loosely related content
- `0.5-0.6` - **Recommended** - Good balance
- `0.7-0.8` - Strict, only highly relevant content
- `0.9+` - Very strict, exact matches only

## Response Format

```json
{
  "answer": "The knight moves in an L-shape...",
  "model": "mistral-7b-instruct-v0.2.q5_k_m",
  "usedRag": true,
  "documentsRetrieved": 3,
  "responseTimeMs": 1250,
  "promptTokens": 245,
  "completionTokens": 87,
  "totalTokens": 332,
  "tokensPerSecond": 69.6,
  "success": true,
  "warnings": []
}
```

## Troubleshooting

### "Domain repository not configured"

**Cause:** Auto vector search infrastructure not set up

**Solutions:**
1. Use manual context RAG (provide `context` in request)
2. Set up embedding service + database (see Configuration)
3. Use direct query mode (`enableRag: false`)

### "No domains registered yet"

**Cause:** Database doesn't have any domain metadata

**Solution:** Add knowledge with domain tags using the AiDashboard application

### "No relevant documents found in knowledge base"

**Causes:**
- `minRelevanceScore` is too high
- Knowledge base doesn't contain relevant documents
- Domain filter is too restrictive

**Solutions:**
- Lower `minRelevanceScore` (e.g., from 0.7 to 0.5)
- Increase `topK` to retrieve more documents
- Remove or broaden `domainFilter`
- Verify documents are in the database

### How to find available domains

**Always start with:**
```bash
GET /api/Domains
```

This tells you exactly which `domainId` values you can use in `domainFilter`.

## Testing

Use the provided `.http` file (`OfflineAI.Api.http`) with examples for:
- ? Domain discovery
- ? Direct queries
- ? Manual context RAG
- ? Auto vector search RAG
- ? Domain filtering
- ? Parameter validation

## Example Use Cases

### Discover and Use Game Rule Domains

**Step 1: Find available game domains**
```bash
GET /api/Domains/category/board-games
```

**Response:**
```json
{
  "category": "board-games",
  "domains": [
    { "domainId": "chess", "displayName": "Chess" },
    { "domainId": "monopoly", "displayName": "Monopoly" }
  ],
  "count": 2
}
```

**Step 2: Query with domain filter**
```json
{
  "question": "How do I move the knight?",
  "enableRag": true,
  "domainFilter": ["chess"],
  "topK": 3,
  "minRelevanceScore": 0.6
}
```

### Multi-Domain Search

```json
{
  "question": "Can I jump over other pieces?",
  "enableRag": true,
  "domainFilter": ["chess", "checkers", "chinese-checkers"],
  "topK": 5,
  "minRelevanceScore": 0.5
}
```

### Category-Based Filtering

```bash
# Get all card game domains
GET /api/Domains/category/card-games

# Query across all card games
POST /api/Query
{
  "question": "What beats a full house?",
  "enableRag": true,
  "domainFilter": ["poker", "texas-holdem"],
  "topK": 3
}
```

## Best Practices

1. **Discover First** - Always call `/api/Domains` before using domain filters
2. **Start Broad** - Begin with no domain filter, then narrow down if needed
3. **Use Categories** - Group related domains using category endpoints
4. **Test Relevance** - Adjust `minRelevanceScore` based on result quality
5. **Monitor Warnings** - Check the `warnings` array in responses
6. **Cache Domain List** - Domains don't change frequently, cache the list

## Performance Tips

- **Domain Filtering**: Reduces search space, improves speed and relevance
- **Category-Based Queries**: Use `/api/Domains/category/{category}` to get related domains
- **Relevance Threshold**: Higher `minRelevanceScore` = fewer documents = faster
- **TopK**: Lower values (2-3) are faster than higher values (8-10)
