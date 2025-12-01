# AI Project - Processing Components Documentation

## Overview
The **Processing** namespace (`Application.AI.Processing`) handles direct interaction with LLM processes, including process spawning, output parsing, and lifecycle management.

---

## IPersistentLlmProcess.cs

### Purpose
Defines the contract for LLM process operations, enabling testability through mocking and abstraction from specific LLM implementations.

### Interface Definition
```csharp
public interface IPersistentLlmProcess : IDisposable
{
    // Health and diagnostics
    bool IsHealthy { get; }
    DateTime LastUsed { get; }
    int TimeoutMs { get; set; }
    
    // Core functionality
    Task<string> QueryAsync(
        string systemPrompt,
        string userQuestion,
        int maxTokens = 200,
        float temperature = 0.3f,
        int topK = 30,
        float topP = 0.85f,
        float repeatPenalty = 1.15f,
        float presencePenalty = 0.2f,
        float frequencyPenalty = 0.2f,
        bool useGpu = false,
        int gpuLayers = 0);
}
```

### Key Responsibilities
1. **Process Management**: Spawn and manage LLM process lifecycle
2. **Query Execution**: Send prompts and retrieve responses
3. **Health Monitoring**: Track process health and last use time
4. **Parameter Control**: Accept generation parameters (temperature, penalties, etc.)

### Design Decisions

#### Why Not Just Use Process Directly?
**Benefits of Interface**:
- **Testability**: Mock LLM for unit tests without spawning processes
- **Flexibility**: Swap implementations (llama.cpp ? GPT-4 API ? Ollama)
- **Abstraction**: Hide process management complexity from consumers

#### Why Include Generation Parameters?
**Rationale**: Each query might need different settings
```csharp
// Creative writing: High temperature
await process.QueryAsync(prompt, question, temperature: 0.9f);

// Factual Q&A: Low temperature
await process.QueryAsync(prompt, question, temperature: 0.1f);
```

### Usage Example
```csharp
public class MockLlmProcess : IPersistentLlmProcess
{
    public bool IsHealthy => true;
    public DateTime LastUsed => DateTime.UtcNow;
    public int TimeoutMs { get; set; } = 30000;
    
    public Task<string> QueryAsync(...)
    {
        return Task.FromResult("Mocked response for testing");
    }
    
    public void Dispose() { }
}

// Use in tests
[Fact]
public async Task ChatService_ReturnsResponse()
{
    var mockProcess = new MockLlmProcess();
    var service = new ChatService(mockProcess);
    
    var response = await service.QueryAsync("What is AI?");
    Assert.Equal("Mocked response for testing", response);
}
```

---

## PersistentLlmProcess.cs

### Purpose
Concrete implementation of `IPersistentLlmProcess` that spawns `llama-cli.exe` processes for each query.

### Architecture Decision: Process-Per-Query

```
???????????????????????????????????????????
?   PersistentLlmProcess (manager)        ?
?   - _llmPath, _modelPath                ?
?   - _requestLock (SemaphoreSlim)        ?
???????????????????????????????????????????
?   QueryAsync(prompt, question)          ?
?      ?                                   ?
?   1. Acquire request lock               ?
?   2. Spawn llama-cli.exe                ?
?   3. Stream output                      ?
?   4. Detect assistant response          ?
?   5. Clean and return                   ?
???????????????????????????????????????????
         ?
         ? spawns new process per query
         ?
???????????????????????????????????????????
?   llama-cli.exe (native process)        ?
?   - Loads model from disk/cache         ?
?   - Generates tokens                    ?
?   - Streams to stdout                   ?
???????????????????????????????????????????
```

### Why Not Interactive Mode?
| Aspect | Process-Per-Query | Interactive Mode |
|--------|-------------------|------------------|
| **Reliability** | Process crash = single query fails | Process crash = pool instance lost |
| **Complexity** | Simple: spawn, read, dispose | Complex: stdin/stdout protocol |
| **State Management** | Stateless, independent queries | Must manage conversation state |
| **Timeout Handling** | Kill process after timeout | Must send special commands |
| **Implementation** | ? Current approach | Future enhancement |

### Key Features

#### 1. Thread-Safe Query Execution
```csharp
private readonly SemaphoreSlim _requestLock = new(1, 1);

public async Task<string> QueryAsync(...)
{
    await _requestLock.WaitAsync(); // Only one query at a time
    try
    {
        return await ExecuteQueryAsync(...);
    }
    finally
    {
        _requestLock.Release();
    }
}
```

#### 2. Dynamic Timeout with Pause Detection
```csharp
// Overall timeout: 45 seconds
var totalTime = (DateTime.UtcNow - start).TotalMilliseconds;
if (totalTime > _timeoutMs) { Kill(); }

// Pause detection: No output for 5-10 seconds = done
var timeSinceOutput = (DateTime.UtcNow - lastOutput).TotalMilliseconds;
if (assistantStarted && timeSinceOutput > pauseTimeoutMs)
{
    Console.WriteLine("[Generation complete - pause detected]");
    break;
}
```

**Benefits**:
- Allows natural completion without killing mid-sentence
- Adaptive pause timeout based on overall timeout (3s-10s)
- Prevents truncation of slow-generating models

#### 3. Multi-Format Output Parsing
Uses `LlmOutputPatterns` to detect where assistant response starts:

```csharp
// Try multiple patterns in order (specific ? general)
foreach (var (pattern, marker) in LlmOutputPatterns.AssistantPatterns)
{
    if (fullText.Contains(pattern))
    {
        assistantStarted = true;
        output.Clear();
        output.Append(fullText.Substring(startIndex));
        break;
    }
}
```

**Supported Formats**:
1. **Llama 3.2**: `<|start_header_id|>assistant<|end_header_id|>`
2. **TinyLlama/Phi**: `<|assistant|>`
3. **ChatML**: `<|im_start|>assistant`
4. **Mistral**: `Assistant:`

#### 4. Automatic Response Cleaning
```csharp
private static string CleanResponse(string response)
{
    // Remove EOS tokens
    foreach (var marker in LlmOutputPatterns.EndMarkers)
    {
        var endIndex = response.IndexOf(marker);
        if (endIndex >= 0)
            response = response.Substring(0, endIndex);
    }
    
    // Remove incomplete sentences ending with '>'
    if (response.EndsWith(">") && !response.EndsWith(">>"))
    {
        var lastStop = Math.Max(
            response.LastIndexOf('.'),
            Math.Max(response.LastIndexOf('!'), response.LastIndexOf('?'))
        );
        if (lastStop > 0)
            response = response.Substring(0, lastStop + 1);
    }
    
    return response.Trim();
}
```

#### 5. GPU/CPU Configuration
```csharp
// CPU-only: Disable GPU layers
if (!useGpu)
{
    processInfo.Arguments += " -ngl 0";
}
// GPU: Offload N layers to GPU
else if (gpuLayers > 0)
{
    processInfo.Arguments += $" -ngl {gpuLayers}";
}
```

**GPU Layer Recommendations**:
| Model | Total Layers | CPU | Hybrid (50%) | Full GPU |
|-------|--------------|-----|--------------|----------|
| TinyLlama 1.1B | 22 | 0 | 11 | 22 |
| Phi-2 2.7B | 32 | 0 | 16 | 32 |
| Llama 3.2 3B | 26 | 0 | 13 | 26 |
| Mistral 7B | 32 | 0 | 16 | 32 |
| Llama 3.1 8B | 32 | 0 | 16 | 32 |

### Process Creation Example
```csharp
public static async Task<PersistentLlmProcess> CreateAsync(
    string llmPath, 
    string modelPath, 
    int timeoutMs = 30000)
{
    // Validate paths
    if (!File.Exists(llmPath))
        throw new FileNotFoundException($"LLM not found: {llmPath}");
    
    if (!File.Exists(modelPath))
        throw new FileNotFoundException($"Model not found: {modelPath}");
    
    return new PersistentLlmProcess(llmPath, modelPath, timeoutMs);
}
```

### Query Execution Workflow

```
1. Acquire Request Lock
   ?
2. Build Full Prompt
   systemPrompt + "\n\nUser: " + question + "\nAssistant:"
   ?
3. Create Process
   llama-cli.exe -m model.gguf -p "prompt" -n 200 --temp 0.3 ...
   ?
4. Start Process & Attach Handlers
   process.OutputDataReceived += (s, e) => { ... }
   ?
5. Stream Output & Detect Format
   Wait for "<|assistant|>" or "Assistant:" pattern
   ?
6. Collect Response
   Accumulate text after assistant marker
   ?
7. Timeout or Pause Detection
   - Overall timeout (45s): Kill process
   - Pause detected (5s): Natural completion
   ?
8. Clean Response
   Remove EOS tokens, incomplete sentences
   ?
9. Return Result & Release Lock
```

### Error Handling

```csharp
try
{
    return await ExecuteQueryAsync(...);
}
catch (FileNotFoundException ex)
{
    IsHealthy = false;
    throw new InvalidOperationException(
        "LLM executable or model not found", ex);
}
catch (TimeoutException ex)
{
    IsHealthy = false;
    throw new InvalidOperationException(
        "Query timeout exceeded", ex);
}
catch (Exception ex)
{
    IsHealthy = false;
    throw new InvalidOperationException(
        $"Failed to query LLM: {ex.Message}", ex);
}
```

### Performance Characteristics

| Operation | Time (CPU) | Time (GPU) | Notes |
|-----------|------------|------------|-------|
| Process Spawn | 100-500ms | 100-500ms | OS-level overhead |
| Model Load (cold) | 8-12s | 5-8s | First load from disk |
| Model Load (warm) | 200-500ms | 100-200ms | OS file cache hit |
| Token Generation | 3-5 tok/s | 20-50 tok/s | Depends on model size |
| 200 Token Response | 40-60s | 5-10s | Typical RAG response |

### Configuration Examples

#### Conservative (Safety-First)
```csharp
var process = await PersistentLlmProcess.CreateAsync(
    llmPath, modelPath, timeoutMs: 120000); // 2 minutes

var response = await process.QueryAsync(
    systemPrompt, question,
    maxTokens: 150,       // Shorter responses
    temperature: 0.1f,    // Very focused
    repeatPenalty: 1.3f); // Strong repetition control
```

#### Balanced (Default)
```csharp
var process = await PersistentLlmProcess.CreateAsync(
    llmPath, modelPath, timeoutMs: 45000); // 45 seconds

var response = await process.QueryAsync(
    systemPrompt, question,
    maxTokens: 200,
    temperature: 0.3f,
    topK: 30,
    topP: 0.85f,
    repeatPenalty: 1.15f);
```

#### Creative (Experimentation)
```csharp
var response = await process.QueryAsync(
    systemPrompt, question,
    maxTokens: 500,      // Longer responses
    temperature: 0.8f,   // More creative
    topK: 50,            // More options
    topP: 0.95f,         // Nucleus sampling
    repeatPenalty: 1.1f); // Light repetition control
```

---

## LlmOutputPatterns.cs

### Purpose
Centralized repository of patterns for detecting LLM output markers across different model architectures.

### Pattern Categories

#### 1. Assistant Start Patterns
Detect where the assistant's response begins:

```csharp
public static readonly (string Pattern, string Marker)[] AssistantPatterns =
[
    // Llama 3.2 (most specific)
    ("<|start_header_id|>assistant<|end_header_id|>", 
     "<|start_header_id|>assistant<|end_header_id|>"),
    
    // TinyLlama, Phi-2
    ("<|assistant|>", "<|assistant|>"),
    
    // ChatML format
    ("<|im_start|>assistant", "<|im_start|>assistant"),
    
    // Instruction-tuned models
    ("### Assistant:", "### Assistant:"),
    
    // Mistral, some Llama (check last!)
    ("Assistant:", "Assistant:")
];
```

**Why Order Matters**:
- "Assistant:" appears in `"<|start_header_id|>assistant"`
- Checking generic patterns first causes false positives
- Always check **specific ? general**

#### 2. End Markers
Detect where to stop collecting output:

```csharp
public static readonly string[] EndMarkers =
[
    "<|eot_id|>",         // Llama 3.2
    "<|start_header_id|>",// Next turn start
    "<|",                 // Generic special token
    "<|end|>",            // TinyLlama, Phi
    "<|im_end|>",         // ChatML
    "</s>",               // Llama EOS
    "<|endoftext|>",      // GPT-style
    "<|user|>",           // Next user turn
    "User:",              // Next user turn
    "###"                 // Instruction format
];
```

### Usage in Process Management
```csharp
// In PersistentLlmProcess.ExecuteProcessAsync()
process.OutputDataReceived += (sender, e) =>
{
    if (!assistantStarted)
    {
        // Try each pattern
        foreach (var (pattern, marker) in LlmOutputPatterns.AssistantPatterns)
        {
            if (fullText.Contains(pattern))
            {
                assistantStarted = true;
                var startIndex = fullText.IndexOf(pattern) + marker.Length;
                output.Append(fullText.Substring(startIndex).TrimStart());
                break;
            }
        }
    }
    else
    {
        output.Append(e.Data);
    }
};

// In CleanResponse()
foreach (var marker in LlmOutputPatterns.EndMarkers)
{
    var endIndex = response.IndexOf(marker);
    if (endIndex >= 0)
        response = response.Substring(0, endIndex);
}
```

### Adding New Model Support

**Step 1**: Test the model and identify its format
```bash
llama-cli.exe -m model.gguf -p "User: Hello\nAssistant:" -n 50
```

**Step 2**: Look for patterns in raw output
```
Loading model...
<|im_start|>system
You are a helpful assistant.
<|im_start|>user
Hello
<|im_start|>assistant
Hello! How can I help you today?<|im_end|>
```

**Step 3**: Add patterns to arrays
```csharp
// Add to AssistantPatterns (in appropriate position)
("<|im_start|>assistant", "<|im_start|>assistant"),

// Add to EndMarkers
"<|im_end|>",
```

**Step 4**: Test with your model
```csharp
var process = await PersistentLlmProcess.CreateAsync(llmPath, newModelPath);
var response = await process.QueryAsync("System prompt", "User question");
Assert.DoesNotContain("<|im_start|>", response);
Assert.DoesNotContain("<|im_end|>", response);
```

### Model Format Reference

| Model | Format | Start Pattern | End Pattern |
|-------|--------|---------------|-------------|
| TinyLlama 1.1B | Custom | `<|assistant|>` | `<|end|>` |
| Phi-2 2.7B | Custom | `<|assistant|>` | `<|end|>` |
| Llama 3.2 3B | Llama 3 | `<|start_header_id|>assistant` | `<|eot_id|>` |
| Llama 3.1 8B | Llama 3 | `<|start_header_id|>assistant` | `<|eot_id|>` |
| Mistral 7B | Mistral | `Assistant:` | `</s>` |
| Qwen 2.5 | ChatML | `<|im_start|>assistant` | `<|im_end|>` |
| Gemma 2 | Custom | `model\n` | `<end_of_turn>` |

---

## Integration with Pool

### Dependency Flow
```
ModelInstancePool
    ? creates
PersistentLlmProcess
    ? uses
LlmOutputPatterns
    ? parses
llama-cli.exe output
```

### Error Propagation
```
llama-cli.exe crashes
    ?
PersistentLlmProcess catches exception
    ?
Sets IsHealthy = false
    ?
ModelInstancePool detects unhealthy
    ?
Creates replacement instance
    ?
Pool remains operational
```

---

## Best Practices

### 1. Always Set Appropriate Timeout
```csharp
// BAD: Default timeout might be too short/long
var process = await PersistentLlmProcess.CreateAsync(llmPath, modelPath);

// GOOD: Set based on hardware
var timeout = hasGpu ? 30000 : 60000;
var process = await PersistentLlmProcess.CreateAsync(llmPath, modelPath, timeout);
```

### 2. Monitor Health Status
```csharp
public async Task<string> QueryAsync(string question)
{
    if (!_process.IsHealthy)
    {
        // Recreate or use pool's replacement mechanism
        _process = await PersistentLlmProcess.CreateAsync(...);
    }
    
    return await _process.QueryAsync(systemPrompt, question);
}
```

### 3. Use Context-Size Limits
```csharp
// Prevent memory issues with large prompts
processInfo.Arguments += $" -c 2048"; // 2048 token context
```

### 4. Test with Multiple Models
```csharp
[Theory]
[InlineData("tinyllama-1.1b.gguf")]
[InlineData("mistral-7b.gguf")]
[InlineData("llama-3.2-3b.gguf")]
public async Task Process_WorksWithMultipleModels(string modelFile)
{
    var process = await PersistentLlmProcess.CreateAsync(llmPath, modelFile);
    var response = await process.QueryAsync("You are helpful", "What is AI?");
    Assert.NotEmpty(response);
}
```

---

## Document Version
- **Files**: `IPersistentLlmProcess.cs`, `PersistentLlmProcess.cs`, `LlmOutputPatterns.cs`
- **Purpose**: LLM process spawning, output parsing, health monitoring
- **Key Patterns**: Process-per-query, multi-format detection, pause-based completion
- **Consumed By**: `ModelInstancePool`
- **Dependencies**: `Factories.LlmFactory`, `LlmOutputPatterns`
- **Last Updated**: 2024
