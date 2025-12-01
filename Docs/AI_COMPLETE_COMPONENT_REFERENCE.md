# AI Project - Complete Component Reference

## Table of Contents
1. [Chat Components](#chat-components)
2. [Embedding Components](#embedding-components)
3. [Management Components](#management-components)
4. [Utility Components](#utility-components)
5. [Extension Components](#extension-components)
6. [Model Components](#model-components)

---

# Chat Components

## AiChatServicePooled.cs

### Purpose
Orchestrates RAG-enhanced chat using pooled LLM instances. Acts as the main entry point for question-answering with context retrieval.

### Architecture
```
User Question
    ?
AiChatServicePooled.SendMessageAsync()
    ?
1. Extract keywords
2. Detect domains (DomainDetector)
3. Search memory (DatabaseVectorMemory)
4. Build system prompt with context
5. Acquire LLM instance (ModelInstancePool)
6. Generate response (PersistentLlmProcess)
7. Clean and return response
```

### Key Responsibilities
1. **RAG Orchestration**: Coordinate memory search and LLM query
2. **Context Management**: Truncate context to fit LLM window
3. **Domain Filtering**: Apply domain-based filtering for relevance
4. **Error Handling**: Handle missing context, timeouts, failures
5. **Performance Tracking**: Record metrics (tokens/sec, timing)

### Constructor Dependencies
```csharp
public AiChatServicePooled(
    ILlmMemory memory,                    // Vector memory for RAG
    ILlmMemory conversationMemory,        // In-memory conversation history
    IModelInstancePool modelPool,         // Pool of LLM instances
    GenerationSettings generationSettings,// Temperature, maxTokens, etc.
    LlmSettings? llmSettings = null,      // GPU settings
    bool debugMode = false,               // Verbose logging
    bool enableRag = true,                // RAG vs. direct mode
    bool showPerformanceMetrics = false,  // Display tokens/sec
    IDomainDetector? domainDetector = null) // Domain detection
```

### Usage Example
```csharp
// Setup (in DI container)
services.AddSingleton<ILlmMemory>(sp => 
    new DatabaseVectorMemory(embeddingService, repository, "game-rules"));
services.AddSingleton<IModelInstancePool>(sp => 
    new ModelInstancePool(llmPath, modelPath, maxInstances: 3));

var chatService = sp.GetRequiredService<AiChatServicePooled>();

// Query
var response = await chatService.SendMessageAsync("How do I win in Munchkin?");
// Output: "To win in Munchkin, you need to reach Level 10..."
```

### RAG Mode vs. Direct Mode

#### RAG Mode (enableRag = true)
```csharp
User Question: "How to win Munchkin?"
    ?
SearchRelevantMemoryAsync()
    ?
Context: "[Winning Conditions]\nReach Level 10 by killing monsters..."
    ?
System Prompt: "Answer using only the information below.\n\nInformation:\n[context]"
    ?
LLM Response: "To win in Munchkin, you need to reach Level 10..."
```

#### Direct Mode (enableRag = false)
```csharp
User Question: "What is 2+2?"
    ?
System Prompt: "Answer the question directly in one paragraph."
    ?
LLM Response: "2+2 equals 4."
```

### Context Building Process
```csharp
private async Task<string?> BuildSystemPromptAsync(string question)
{
    const string basePrompt = "Answer using only the information below.\n";
    
    // Step 1: Detect domains
    var domains = await DetectAndFilterDomainsAsync(question);
    // e.g., ["board-game-munchkin"]
    
    // Step 2: Retrieve relevant context
    var context = await RetrieveRelevantContextAsync(question, domains);
    // e.g., "[Winning Conditions]\nReach Level 10..."
    
    // Step 3: Clean EOS/EOF markers
    context = CleanAndValidateContext(context);
    
    // Step 4: Truncate if needed
    if (context.Length > MaxContextChars)
        context = TruncateContextIfNeeded(context); // Max 1500 chars
    
    // Step 5: Build final prompt
    return basePrompt + "\nInformation:\n" + context;
}
```

### Error Handling Strategies

#### Missing Context
```csharp
if (relevantMemory == null)
{
    return "I don't have any relevant information in my knowledge base...";
}
```

#### Domain-Specific Missing Context
```csharp
if (detectedDomains.Count > 0 && relevantMemory == null)
{
    return "I don't have information about {domainName} loaded...";
}
```

#### Timeout
```csharp
catch (TimeoutException tex)
{
    return "[ERROR] TinyLlama timed out after waiting for response...";
}
```

#### Empty Response
```csharp
if (string.IsNullOrWhiteSpace(response))
{
    return "[WARNING] Model returned empty response. Try a more specific question.";
}
```

### Performance Metrics
```csharp
public PerformanceMetrics? LastMetrics { get; private set; }

public class PerformanceMetrics
{
    public double TotalTimeMs { get; set; }
    public int CompletionTokens { get; set; }
    public int PromptTokens { get; set; }
    public string? ModelName { get; set; }
    
    public override string ToString()
    {
        var tokensPerSec = CompletionTokens / (TotalTimeMs / 1000.0);
        return $"[Performance] {TotalTimeMs:F0}ms | " +
               $"{CompletionTokens} tokens | " +
               $"{tokensPerSec:F1} tok/s";
    }
}
```

### Configuration
```json
"Generation": {
  "MaxTokens": 200,
  "Temperature": 0.3,
  "TopK": 30,
  "TopP": 0.85,
  "RepeatPenalty": 1.15,
  "RagTopK": 3,              // Number of context fragments
  "RagMinRelevanceScore": 0.5 // Similarity threshold
}
```

---

# Embedding Components

## SemanticEmbeddingService.cs

### Purpose
Generates semantic embeddings using ONNX Runtime and BERT-based models for vector search.

### Supported Models
| Model | Dimensions | Speed (CPU) | Quality | Use Case |
|-------|------------|-------------|---------|----------|
| all-MiniLM-L6-v2 | 384 | Fast (30ms) | Good | Small knowledge bases |
| all-mpnet-base-v2 | 768 | Medium (50ms) | Best | Production (English) |
| paraphrase-multilingual | 768 | Medium (60ms) | Good | Multi-language |

### Architecture
```
Text Input
    ?
TextNormalizer.Normalize() - Clean special chars
    ?
BertTokenizer.Encode() - Convert to token IDs
    ?
Create Tensors (input_ids, attention_mask, token_type_ids)
    ?
ONNX Inference (BERT model)
    ?
Mean Pooling - Average token embeddings
    ?
L2 Normalization - Unit vector
    ?
ReadOnlyMemory<float> embedding (768 dims)
```

### Key Features

#### 1. Model Auto-Detection
```csharp
// Detect BERT vs. MPNet based on input requirements
var inputMetadata = _session.InputMetadata;
_requiresTokenTypeIds = inputMetadata.ContainsKey("token_type_ids");

// BERT-style: needs 3 inputs
if (_requiresTokenTypeIds)
{
    inputs.Add(NamedOnnxValue.CreateFromTensor("input_ids", ...));
    inputs.Add(NamedOnnxValue.CreateFromTensor("attention_mask", ...));
    inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", ...));
}
// MPNet-style: needs 2 inputs
else
{
    inputs.Add(NamedOnnxValue.CreateFromTensor("input_ids", ...));
    inputs.Add(NamedOnnxValue.CreateFromTensor("attention_mask", ...));
}
```

#### 2. GPU Acceleration (Windows)
```csharp
try
{
    // Try DirectML for GPU acceleration
    sessionOptions.AppendExecutionProvider_DML(0); // Device 0
    gpuEnabled = true;
}
catch
{
    // Fallback to CPU
    gpuEnabled = false;
}
```

#### 3. Memory Optimization (CPU)
```csharp
if (!_isGpuEnabled)
{
    // Strict memory limits for < 2GB usage
    sessionOptions.EnableCpuMemArena = false;    // Disable arena
    sessionOptions.IntraOpNumThreads = 1;        // Single-threaded
    sessionOptions.ExecutionMode = ORT_SEQUENTIAL;
    
    // Aggressive GC after each embedding
    GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
}
```

#### 4. Tokenization Strategies
```csharp
// Auto-detect tokenizer format
if (vocabPath.EndsWith(".json"))
{
    // Multilingual models (tokenizer.json)
    _tokenizer = TiktokenTokenizer.CreateForModel("gpt-4");
}
else
{
    // BERT models (vocab.txt)
    _tokenizer = BertTokenizer.Create(vocabPath);
}
```

### Usage Example
```csharp
// Setup
var service = new SemanticEmbeddingService(
    modelPath: "models/all-mpnet-base-v2.onnx",
    vocabPath: "models/vocab.txt",
    embeddingDimension: 768,
    debugMode: false);

// Generate single embedding
var embedding = await service.GenerateEmbeddingAsync("How to win Munchkin?");
// Result: ReadOnlyMemory<float> of 768 dimensions

// Generate batch
var texts = new[] { "text1", "text2", "text3" };
var embeddings = await service.GenerateEmbeddingsAsync(texts);
// Result: IList<ReadOnlyMemory<float>>
```

### Performance Characteristics
| Hardware | Speed/Embedding | Memory | Batch (10) |
|----------|-----------------|--------|------------|
| CPU 4-core | 50ms | 1.5GB | 500ms |
| CPU 8-core | 30ms | 1.5GB | 300ms |
| GPU DirectML | 10ms | 2.0GB | 100ms |
| GPU CUDA | 5ms | 2.5GB | 50ms |

### Text Normalization Pipeline
```csharp
// In SemanticEmbeddingService.GenerateBertEmbedding()

// Step 1: Pre-truncate very large texts
if (text.Length > 10000)
{
    text = text.Substring(0, 5000);
}

// Step 2: Normalize with limits
text = TextNormalizer.NormalizeWithLimits(text, maxLength: 5000);

// Step 3: Tokenize
var tokens = _tokenizer.EncodeToTokens(text);

// Step 4: Truncate to max sequence length
tokens = tokens.Take(_maxSequenceLength - 2); // -2 for [CLS] and [SEP]

// Step 5: Add special tokens
inputIds = [101, ...tokens..., 102]; // [CLS], tokens, [SEP]
```

### Common Issues and Solutions

#### Issue: Out of Memory
**Symptom**: `OutOfMemoryException` during embedding generation
**Solutions**:
1. Disable GPU: CPU uses less memory
2. Reduce batch size: Process one at a time
3. Enable aggressive GC: Force cleanup after each embedding
4. Truncate text: Limit to 5000 chars before normalization

#### Issue: Slow Performance
**Symptom**: >100ms per embedding on decent hardware
**Solutions**:
1. Enable GPU acceleration (DirectML or CUDA)
2. Use smaller model (MiniLM 384-dim vs MPNet 768-dim)
3. Batch requests: 10 at a time is optimal
4. Pre-compute embeddings: Cache in database

#### Issue: Poor Quality Results
**Symptom**: Irrelevant search results despite semantic similarity
**Solutions**:
1. Use larger model (MPNet instead of MiniLM)
2. Add hybrid search (exact string matching)
3. Use language-specific models for non-English
4. Fine-tune embeddings on domain data

---

# Management Components

## IModelManager.cs & ModelManager.cs

### Purpose
Handles model switching (hot-swapping) without restarting the application.

### Key Responsibilities
1. **Model Discovery**: Scan folder for available GGUF models
2. **Hot-Swapping**: Switch models without stopping service
3. **Pool Coordination**: Reinitialize pool with new model
4. **Progress Tracking**: Report loading progress to UI

### Interface
```csharp
public interface IModelManager
{
    Task SwitchModelAsync(
        string modelFullPath, 
        Action<int, int>? progressCallback = null);
}
```

### Implementation
```csharp
public async Task SwitchModelAsync(string modelFullPath, Action<int, int>? progressCallback)
{
    // Validate model exists
    if (!File.Exists(modelFullPath))
        throw new FileNotFoundException($"Model not found: {modelFullPath}");
    
    // Reinitialize pool with new model
    await _pool.ReinitializeAsync(_llmPath, modelFullPath, progressCallback);
    
    Console.WriteLine($"[?] Switched to model: {Path.GetFileName(modelFullPath)}");
}
```

### Usage in Dashboard
```csharp
// In DashboardState.ModelService
public async Task SwitchModelAsync(string modelPath)
{
    Loading = true;
    try
    {
        await SwitchModelHandler(modelPath, (current, total) =>
        {
            StatusMessage = $"Loading {current}/{total}...";
            NotifyStateChanged();
        });
        
        CurrentModel = Path.GetFileName(modelPath);
    }
    finally
    {
        Loading = false;
    }
}
```

## ModelManagementService.cs

### Purpose
Discovers and lists available GGUF models in a folder.

### Key Methods
```csharp
public class ModelManagementService
{
    public List<string> DiscoverModels(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            return new List<string>();
        
        return Directory.GetFiles(folderPath, "*.gguf")
            .OrderBy(f => Path.GetFileName(f))
            .ToList();
    }
    
    public string GetDisplayName(string modelPath)
    {
        var fileName = Path.GetFileNameWithoutExtension(modelPath);
        
        // Extract meaningful info
        // "llama-3.2-3b-instruct-q5_k_m.gguf" ? "Llama 3.2 3B (Q5_K_M)"
        return FormatModelName(fileName);
    }
}
```

### Model Naming Conventions
| File Name | Display Name | Size |
|-----------|--------------|------|
| tinyllama-1.1b-chat-q5_k_m.gguf | TinyLlama 1.1B (Q5_K_M) | 637 MB |
| phi-2-q5_k_m.gguf | Phi-2 2.7B (Q5_K_M) | 1.7 GB |
| mistral-7b-instruct-v0.2-q5_k_m.gguf | Mistral 7B (Q5_K_M) | 4.1 GB |
| llama-3.2-3b-instruct-q5_k_m.gguf | Llama 3.2 3B (Q5_K_M) | 2.0 GB |

---

# Utility Components

## IDomainDetector.cs & DomainDetector.cs

### Purpose
Detects knowledge domains from user queries to enable filtered RAG searches.

### Architecture
```
User Query: "How to win in Munchkin?"
    ?
ExtractKeywords(query) - ["win", "munchkin"]
    ?
LoadAllDomains() - Get all registered domains
    ?
MatchKeywords() - Check if keywords match domain
    ?
Return Domain IDs - ["board-game-munchkin"]
```

### Domain Entity
```csharp
public class KnowledgeDomainEntity
{
    public Guid Id { get; set; }
    public string DomainId { get; set; } = string.Empty;  // "board-game-munchkin"
    public string DisplayName { get; set; } = string.Empty; // "Munchkin"
    public string Category { get; set; } = string.Empty;   // "board-games"
    public string Keywords { get; set; } = string.Empty;   // "munchkin,dungeon,level up"
}
```

### Key Methods
```csharp
public interface IDomainDetector
{
    Task InitializeAsync();
    Task<List<string>> DetectDomainsAsync(string query);
    Task<string> GetDisplayNameAsync(string domainId);
    Task<List<(string DomainId, string DisplayName, string Category)>> GetAllDomainsAsync();
    Task<List<string>> GetCategoriesAsync();
}
```

### Usage Example
```csharp
// Setup
var detector = new DomainDetector(knowledgeDomainRepository);
await detector.InitializeAsync();

// Detect domains from query
var domains = await detector.DetectDomainsAsync("How to win in Munchkin?");
// Result: ["board-game-munchkin"]

// Use in RAG search
var context = await memory.SearchRelevantMemoryAsync(
    query, 
    topK: 3, 
    domainFilter: domains); // Filter to Munchkin rules only
```

### Keyword Matching Algorithm
```csharp
private async Task<List<string>> DetectDomainsAsync(string query)
{
    var queryLower = query.ToLowerInvariant();
    var keywords = ExtractKeywords(queryLower); // ["win", "munchkin"]
    
    var matched = new List<string>();
    
    foreach (var domain in _domains)
    {
        var domainKeywords = domain.Keywords.Split(',');
        
        // Check if ANY query keyword matches ANY domain keyword
        foreach (var queryWord in keywords)
        {
            foreach (var domainWord in domainKeywords)
            {
                if (domainWord.Contains(queryWord) || queryWord.Contains(domainWord))
                {
                    matched.Add(domain.DomainId);
                    break;
                }
            }
        }
    }
    
    return matched.Distinct().ToList();
}
```

## EmbeddingPooling.cs

### Purpose
Implements attention-masked mean pooling for BERT embeddings.

### Why Pooling?
BERT outputs **token-level embeddings** (e.g., 256 tokens × 768 dims). We need a **single sentence embedding** (768 dims).

### Pooling Strategies
| Strategy | Method | Use Case |
|----------|--------|----------|
| Mean Pooling | Average all tokens | Balanced representation |
| Max Pooling | Take max of each dim | Capture strongest features |
| CLS Token | Use [CLS] token only | BERT-style classification |
| **Attention-Masked Mean** | Average non-[PAD] tokens | **Best for sentence similarity** |

### Implementation
```csharp
public static float[] PoolAndNormalize(
    float[] tokenEmbeddings,  // 256 tokens × 768 dims = 196,608 floats
    long[] attentionMask,     // [1,1,1,...,0,0,0] (1=real, 0=padding)
    int embeddingDimension)   // 768
{
    var pooled = new float[embeddingDimension];
    int validTokenCount = 0;
    
    // Sum embeddings of non-padded tokens
    for (int token = 0; token < attentionMask.Length; token++)
    {
        if (attentionMask[token] == 1)
        {
            for (int dim = 0; dim < embeddingDimension; dim++)
            {
                pooled[dim] += tokenEmbeddings[token * embeddingDimension + dim];
            }
            validTokenCount++;
        }
    }
    
    // Average (mean pooling)
    for (int dim = 0; dim < embeddingDimension; dim++)
    {
        pooled[dim] /= validTokenCount;
    }
    
    // L2 normalization (unit vector)
    var norm = Math.Sqrt(pooled.Sum(x => x * x));
    for (int dim = 0; dim < embeddingDimension; dim++)
    {
        pooled[dim] /= (float)norm;
    }
    
    return pooled;
}
```

### Why L2 Normalization?
- **Cosine similarity**: Dot product of unit vectors = cosine of angle
- **Range [0, 1]**: Easier to interpret (1 = identical, 0 = orthogonal)
- **Invariant to length**: Focus on direction, not magnitude

## TextNormalizer.cs

### Purpose
Cleans and normalizes text before tokenization to prevent BERT errors.

### Normalization Steps
```csharp
public static string NormalizeWithLimits(string text, int maxLength = 5000, string fallbackText = "[empty]")
{
    if (string.IsNullOrWhiteSpace(text))
        return fallbackText;
    
    // Step 1: Truncate if too long
    if (text.Length > maxLength)
        text = text.Substring(0, maxLength);
    
    // Step 2: Replace problematic characters
    text = text.Replace('\0', ' ');              // Null bytes
    text = text.Replace('\ufffd', ' ');          // Replacement character
    text = text.Replace('\u0000', ' ');          // Null character
    
    // Step 3: Normalize whitespace
    text = Regex.Replace(text, @"\s+", " ");    // Multiple spaces ? single
    text = text.Trim();
    
    // Step 4: Handle special Unicode ranges
    text = RemoveControlCharacters(text);
    
    return string.IsNullOrWhiteSpace(text) ? fallbackText : text;
}
```

### Why Needed?
**Problem**: Some PDFs contain invisible control characters that crash BERT tokenizers
**Solution**: Clean text before tokenization
```csharp
// Before normalization
string text = "Hello\0World\ufffd";  // Contains null byte and replacement char

// After normalization
string cleaned = TextNormalizer.Normalize(text);
// Result: "Hello World"
```

---

# Extension Components

## EmbeddingExtensions.cs

### Purpose
Extension methods for `ReadOnlyMemory<float>` to calculate similarity scores.

### Key Methods
```csharp
public static class EmbeddingExtensions
{
    // Cosine similarity (no normalization check)
    public static double CosineSimilarity(
        this ReadOnlyMemory<float> vector1, 
        ReadOnlyMemory<float> vector2)
    {
        var span1 = vector1.Span;
        var span2 = vector2.Span;
        
        double dotProduct = 0;
        for (int i = 0; i < span1.Length; i++)
            dotProduct += span1[i] * span2[i];
        
        return dotProduct; // Assumes unit vectors
    }
    
    // Cosine similarity with normalization
    public static double CosineSimilarityWithNormalization(
        this ReadOnlyMemory<float> vector1, 
        ReadOnlyMemory<float> vector2)
    {
        var span1 = vector1.Span;
        var span2 = vector2.Span;
        
        double dotProduct = 0;
        double norm1 = 0;
        double norm2 = 0;
        
        for (int i = 0; i < span1.Length; i++)
        {
            dotProduct += span1[i] * span2[i];
            norm1 += span1[i] * span1[i];
            norm2 += span2[i] * span2[i];
        }
        
        return dotProduct / (Math.Sqrt(norm1) * Math.Sqrt(norm2));
    }
}
```

### Usage
```csharp
var queryEmbedding = await embeddingService.GenerateEmbeddingAsync("query");
var docEmbedding = entity.GetEmbeddingAsMemory();

// If embeddings are already normalized (from SemanticEmbeddingService)
var similarity = queryEmbedding.CosineSimilarity(docEmbedding);

// If unsure about normalization
var similarity = queryEmbedding.CosineSimilarityWithNormalization(docEmbedding);
```

## StringExtensions.cs

### Purpose
String manipulation helpers for text processing.

### Key Methods
```csharp
public static class StringExtensions
{
    // Truncate at word boundary
    public static string TruncateAtWordBoundary(this string text, int maxLength)
    {
        if (text.Length <= maxLength)
            return text;
        
        var truncated = text.Substring(0, maxLength);
        var lastSpace = truncated.LastIndexOf(' ');
        
        return lastSpace > maxLength - 50 
            ? truncated.Substring(0, lastSpace) + "..."
            : truncated + "...";
    }
    
    // Remove diacritics (å ? a, ö ? o)
    public static string RemoveDiacritics(this string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
```

---

# Model Components

## PerformanceMetrics.cs

### Purpose
Data class for tracking LLM generation performance.

### Properties
```csharp
public class PerformanceMetrics
{
    public double TotalTimeMs { get; set; }        // Total generation time
    public int CompletionTokens { get; set; }      // Tokens generated
    public int PromptTokens { get; set; }          // Tokens in prompt
    public string? ModelName { get; set; }         // Model identifier
    
    public double TokensPerSecond => 
        CompletionTokens / (TotalTimeMs / 1000.0);
    
    public override string ToString()
    {
        return $"[Performance] {TotalTimeMs:F0}ms | " +
               $"{CompletionTokens} tokens | " +
               $"{TokensPerSecond:F1} tok/s | " +
               $"Model: {ModelName ?? "Unknown"}";
    }
}
```

### Usage
```csharp
// In AiChatServicePooled
var stopwatch = Stopwatch.StartNew();
var response = await llm.QueryAsync(systemPrompt, question);
stopwatch.Stop();

LastMetrics = new PerformanceMetrics
{
    TotalTimeMs = stopwatch.Elapsed.TotalMilliseconds,
    CompletionTokens = EstimateTokenCount(response),
    PromptTokens = EstimateTokenCount(systemPrompt + question),
    ModelName = "TinyLlama 1.1B"
};

// Display if enabled
if (showPerformanceMetrics)
{
    Console.WriteLine(LastMetrics.ToString());
    // Output: [Performance] 8234ms | 156 tokens | 18.9 tok/s | Model: TinyLlama 1.1B
}
```

## SimpleMemory.cs

### Purpose
Basic in-memory storage implementing `ILlmMemory` for conversation history.

### Implementation
```csharp
public class SimpleMemory : ILlmMemory
{
    private List<IMemoryFragment> _memory = new List<IMemoryFragment>();
    
    public void ImportMemory(IMemoryFragment section)
    {
        _memory.Add(section);
    }
    
    public override string ToString()
    {
        var sb = new StringBuilder();
        foreach (var fragment in _memory)
        {
            sb.AppendLine(fragment.ToString());
        }
        return sb.ToString();
    }
}
```

### Usage
```csharp
// Store conversation history
var conversationMemory = new SimpleMemory();
conversationMemory.ImportMemory(new MemoryFragment("User", "Hello"));
conversationMemory.ImportMemory(new MemoryFragment("AI", "Hi! How can I help?"));

// Convert to string for context
string conversationContext = conversationMemory.ToString();
// Output:
// User: Hello
// AI: Hi! How can I help?
```

---

## Document Version
- **Files**: All files in `AI\Chat`, `AI\Embeddings`, `AI\Management`, `AI\Utilities`, `AI\Extensions`, `AI\Models`
- **Purpose**: Complete reference for all AI project components
- **Last Updated**: 2024
- **Maintained By**: OfflineAI Development Team
