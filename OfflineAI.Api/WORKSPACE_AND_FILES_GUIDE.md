# OfflineAI API — Workspaces, Files, PDFs & Pictures

## Overview

Beyond plain LLM queries and RAG (see [README.md](README.md)), the API can:

1. **Manage workspaces** — named directories the file agent is confined to.
2. **Upload and list files** (images, PDFs, text) inside the active workspace.
3. **Extract text from PDFs** already in the workspace.
4. **Ingest a PDF into the RAG knowledge base** (chunk + embed + store).
5. **Ask questions about pictures** — either a one-shot upload or an image already in the workspace — using the Gemma 4 multimodal backend.

All of this is additive and optional: if the relevant configuration is missing, the affected
endpoints return `503 Service Unavailable` with a message explaining what to configure, while
plain queries and manual-context RAG keep working.

## Configuration

| Feature | Required config | Notes |
|---|---|---|
| Workspaces + file upload/listing/text | none | Always available. Defaults to `Documents\OfflineAI\AgentFiles` unless `AppConfiguration:Folders:AgentFilesFolder` is set. |
| PDF ingestion into RAG (`POST /api/files/{filename}/ingest`) | `AppConfiguration:Embedding` + `AppConfiguration:Database` | Same settings that enable auto vector search RAG — see [README.md](README.md#additional-for-auto-vector-search-rag). |
| Picture/image queries (`POST /api/query/image`, `POST /api/files/{filename}/ask-image`) | `AppConfiguration:Gemma4Cli:ModelPath` (+ `LlamaCliPath`, falls back to `Llm:ExecutablePath`) | Requires a **multimodal** Gemma 4 GGUF model. |

Example User Secrets enabling all of it:

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
    },
    "Folders": {
      "AgentFilesFolder": "d:\\tinyllama\\AgentFiles"
    },
    "Gemma4Cli": {
      "LlamaCliPath": "d:\\tinyllama\\llama-cli.exe",
      "ModelPath": "d:\\tinyllama\\gemma-4-4b-it-qat.gguf"
    }
  }
}
```

On startup the console log reports what got registered, e.g.:

```
[+] Workspace + file agent services registered
[+] Vector memory + knowledge domain repositories registered
[+] Persistence service registered (PDF ingestion into RAG available)
[+] Gemma 4 CLI service registered (model: gemma-4-4b-it-qat.gguf, image queries available)
```

## Workspaces

A **workspace** is a named directory. Exactly one is *active* at a time, and every file
operation described below (upload, list, text extraction, ingestion, image questions) always
operates on the currently active workspace. Switching the active workspace re-confines all
subsequent file operations to the new directory — the API can never read or write outside of it.

A "Standard" workspace is seeded automatically the first time the API runs.

### Endpoints

| Endpoint | Method | Description |
|---|---|---|
| `/api/Workspace` | GET | List all workspaces, with `isActive` flagged |
| `/api/Workspace/active` | GET | Get the currently active workspace |
| `/api/Workspace` | POST | Create a new workspace (does not activate it) |
| `/api/Workspace/active` | POST | Switch the active workspace |
| `/api/Workspace/{name}` | DELETE | Remove a workspace (activates the first remaining one if it was active) |

### Examples

**List workspaces:**
```http
GET /api/Workspace
```
```json
[
  { "name": "Standard", "path": "C:\\Users\\me\\Documents\\OfflineAI\\AgentFiles", "isActive": true },
  { "name": "Project X", "path": "D:\\Projects\\X\\docs", "isActive": false }
]
```

**Create a workspace:**
```http
POST /api/Workspace
Content-Type: application/json

{ "name": "Project X", "path": "D:\\Projects\\X\\docs" }
```

**Switch the active workspace:**
```http
POST /api/Workspace/active
Content-Type: application/json

{ "name": "Project X" }
```

**Remove a workspace:**
```http
DELETE /api/Workspace/Project%20X
```

## Files (pictures, PDFs, text)

All endpoints below operate on the **active workspace** (see above).

### Endpoints

| Endpoint | Method | Description |
|---|---|---|
| `/api/Files` | GET | List files in the active workspace (name, size, last modified) |
| `/api/Files/upload` | POST | Upload a file (multipart) into the active workspace, overwriting any same-named file |
| `/api/Files/{filename}/text` | GET | Extract text — PDF pages are parsed, other files are read as plain text |
| `/api/Files/{filename}/ingest` | POST | Chunk + embed a workspace PDF and store it in the RAG knowledge base |
| `/api/Files/{filename}/ask-image` | POST | Ask a question about an image already in the workspace (Gemma 4 multimodal) |

### Upload a file

```http
POST /api/Files/upload
Content-Type: multipart/form-data; boundary=----X

------X
Content-Disposition: form-data; name="file"; filename="rules.pdf"
Content-Type: application/pdf

<binary data>
------X--
```
```json
{ "filename": "rules.pdf", "message": "Saved rules.pdf" }
```

### List files

```http
GET /api/Files
```
```json
[
  { "name": "rules.pdf", "sizeBytes": 245678, "lastModifiedUtc": "2026-07-01T10:00:00Z" },
  { "name": "board.png", "sizeBytes": 88210, "lastModifiedUtc": "2026-07-02T09:15:00Z" }
]
```

### Extract text from a PDF (or read a text file)

```http
GET /api/Files/rules.pdf/text
```
```json
{ "filename": "rules.pdf", "text": "--- Page 1 ---\nObjective: bankrupt all other players...\n\n--- Page 2 ---\n..." }
```

### Ingest a PDF into the RAG knowledge base

Requires `Embedding` + `Database` to be configured (see [Configuration](#configuration)). The PDF
must already be in the active workspace (upload it first via `POST /api/Files/upload`).

```http
POST /api/Files/rules.pdf/ingest
Content-Type: application/json

{ "collectionName": "monopoly", "replaceExisting": false }
```
```json
{ "filename": "rules.pdf", "collectionName": "monopoly", "fragmentsCreated": 14 }
```

`collectionName` defaults to the file name (without extension) when omitted. Once ingested, the
fragments are retrievable through normal auto vector search RAG queries (`POST /api/Query` with
`enableRag: true`, optionally filtered via `domainFilter`) — see [README.md](README.md).

If ingestion isn't configured, the endpoint returns:
```json
{
  "error": "RAG ingestion not configured",
  "statusCode": 503,
  "details": "Embedding service and/or database are not configured for this API instance.",
  "suggestions": ["Configure AppConfiguration:Embedding and AppConfiguration:Database"]
}
```

### Ask a question about an uploaded image

Requires `Gemma4Cli:ModelPath` to be configured. Upload the image first via
`POST /api/Files/upload`, then:

```http
POST /api/Files/board.png/ask-image
Content-Type: application/json

{ "question": "What color are the pieces on this board?" }
```
```json
{ "answer": "The pieces are red and black.", "model": "gemma-4-4b-it-qat.gguf", "usedRag": false }
```

## Picture (image) queries

For a **one-shot** question about a picture you don't need to keep around, skip the workspace
entirely and post the image directly:

```http
POST /api/Query/image
Content-Type: multipart/form-data; boundary=----X

------X
Content-Disposition: form-data; name="question"

What is shown in this picture?
------X
Content-Disposition: form-data; name="image"; filename="photo.jpg"
Content-Type: image/jpeg

<binary data>
------X--
```
```json
{
  "answer": "A wooden chessboard set up for the start of a game.",
  "model": "gemma-4-4b-it-qat.gguf",
  "usedRag": false,
  "responseTimeMs": 4210
}
```

Use `POST /api/Files/{filename}/ask-image` instead (see above) when you want to upload an image
once and ask multiple questions about it.

If Gemma 4 isn't configured, both image endpoints return:
```json
{
  "error": "Image queries not configured",
  "statusCode": 503,
  "details": "AppConfiguration:Gemma4Cli:ModelPath is not set for this API instance.",
  "suggestions": ["Configure AppConfiguration:Gemma4Cli:ModelPath and LlamaCliPath"]
}
```

## End-to-end example: PDF into RAG, then query it

```http
### 1. Upload the rulebook into the active workspace
POST /api/Files/upload
Content-Type: multipart/form-data; boundary=----X
(file: monopoly-rules.pdf)

### 2. Ingest it into the knowledge base
POST /api/Files/monopoly-rules.pdf/ingest
Content-Type: application/json
{ "collectionName": "monopoly" }

### 3. Ask a question — auto vector search RAG now has content to retrieve
POST /api/Query
Content-Type: application/json
{
  "question": "What happens if I land on Free Parking?",
  "enableRag": true,
  "domainFilter": ["monopoly"],
  "topK": 3,
  "minRelevanceScore": 0.5
}
```

## End-to-end example: dedicated workspace per project

```http
### 1. Create and activate a project-specific workspace
POST /api/Workspace
{ "name": "Rulebooks", "path": "D:\\Games\\Rulebooks" }

POST /api/Workspace/active
{ "name": "Rulebooks" }

### 2. Upload files into it
POST /api/Files/upload   (file: chess-diagram.png)
POST /api/Files/upload   (file: chess-rules.pdf)

### 3. Use them
POST /api/Files/chess-diagram.png/ask-image
{ "question": "Whose move is it in this position?" }

GET /api/Files/chess-rules.pdf/text
```

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `503` on `/api/Files/{filename}/ingest` | Embedding/Database not configured | Set `AppConfiguration:Embedding` + `AppConfiguration:Database` (see [Configuration](#configuration)) |
| `503` on `/api/Query/image` or `/ask-image` | `Gemma4Cli:ModelPath` not configured, or model isn't multimodal | Set `AppConfiguration:Gemma4Cli:ModelPath` to a multimodal Gemma 4 GGUF |
| `404` on `/api/Files/{filename}/...` | File isn't in the *active* workspace | `GET /api/Workspace/active` to confirm which workspace is active, `GET /api/Files` to see what's actually there |
| `400 "Only PDF files can be ingested"` | Tried to ingest a non-`.pdf` file | Only PDFs are supported for ingestion; use `/text` to read other file types |
