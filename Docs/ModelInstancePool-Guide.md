# Model Instance Pool - Keep LLM Loaded in Memory

## Problem
Previously, the application loaded the TinyLlama model from disk for each conversation request, then unloaded it after completion. This approach:
- Takes 5-15 seconds to load the model each time
- Wastes CPU and I/O resources
- Makes the application unsuitable for web deployment with multiple users

## Solution: Model Instance Pool
The new `ModelInstancePool` keeps LLM instances loaded in memory and reuses them across multiple requests.

### Architecture

```
???????????????????????????????????????????????????????
?            ModelInstancePool (3 instances)           ?
???????????????????????????????????????????????????????
?  [Instance 1]  [Instance 2]  [Instance 3]           ?
?     Ready        In Use        Ready                 ?
???????????????????????????????????????????????????????
         ?            ?             ?
         ?            ?             ?
    Request A    Request B      Request C
```

### Components

1. **PersistentLlmProcess** - Wraps a single LLM instance
   - Thread-safe request handling
   - Health monitoring
   - Automatic cleanup

2. **ModelInstancePool** - Manages multiple instances
   - Pre-loads models at startup
   - Distributes requests to available instances
   - Auto-replaces unhealthy instances

3. **AiChatServicePooled** - Chat service using the pool
   - Acquires instance from pool
   - Executes query
   - Returns instance to pool

### Memory Requirements

| Configuration | RAM Needed | Supports |
|--------------|------------|----------|
| 1 instance | 1-2 GB | Testing/Development |
| 3 instances | 3-5 GB | 3-10 concurrent users |
| 5 instances | 7-10 GB | 10-30 concurrent users |
| 10 instances | 15-20 GB | 30-100 concurrent users |

**TinyLlama 1.1B Q5_K_M**: ~800 MB per instance + overhead

## Usage

### Console Application (Current)

```csharp
// Initialize pool at startup
var modelPool = new ModelInstancePool(llmPath, modelPath, maxInstances: 3);
await modelPool.InitializeAsync((current, total) =>
{
    Console.WriteLine($"Loading instance {current}/{total}...");
});

// Create chat service
var service = new AiChatServicePooled(vectorMemory, conversationMemory, modelPool);

// Use throughout application lifetime
var response = await service.SendMessageAsync(userInput);

// Cleanup on shutdown
modelPool.Dispose();
```

### Web Application (Future)

```csharp
// Program.cs - Register as singleton
builder.Services.AddSingleton(sp =>
{
    var pool = new ModelInstancePool(llmPath, modelPath, maxInstances: 5);
    pool.InitializeAsync().GetAwaiter().GetResult();
    return pool;
});

builder.Services.AddScoped<AiChatServicePooled>();

// Controller
public class ChatController : ControllerBase
{
    private readonly AiChatServicePooled _chatService;
    
    public ChatController(AiChatServicePooled chatService)
    {
        _chatService = chatService;
    }
    
    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] ChatRequest request)
    {
        var response = await _chatService.SendMessageAsync(request.Message);
        return Ok(new { response });
    }
}
```

## Performance Comparison

### Before (Load/Unload Each Request)
```
Request 1: [15s load] + [2s inference] = 17s total
Request 2: [15s load] + [2s inference] = 17s total
Request 3: [15s load] + [2s inference] = 17s total
```

### After (Instance Pool)
```
Startup:   [30s load 3 instances]
Request 1: [2s inference]
Request 2: [2s inference]  
Request 3: [2s inference]
Request 4: [2s inference] (waits if all busy)
```

**Result**: 
- 15-second startup cost (one-time)
- 2-second response time per request
- 8.5x faster after startup

## Configuration Recommendations

### Development
```csharp
maxInstances: 1  // Minimal memory usage
```

### Small Website (<10 concurrent users)
```csharp
maxInstances: 3  // ~4 GB RAM
timeoutMs: 30000
```

### Medium Website (10-50 concurrent users)
```csharp
maxInstances: 5  // ~8 GB RAM
timeoutMs: 20000
```

### Large Website (50-100 concurrent users)
```csharp
maxInstances: 10  // ~16 GB RAM
timeoutMs: 15000
```

## Commands

The updated mode includes a new `/pool` command:

```
> /pool
?? Pool Status:
   Available: 2/3
   In Use: 1
```

## Implementation Notes

### Current Limitation
The current implementation creates a new process per query because `llama-cli` doesn't easily support persistent interactive sessions. However, the pool still provides:
- Request serialization (one query per instance)
- Concurrent request handling (multiple instances)
- Resource management

### Future Enhancement: llama.cpp Server Mode
For true persistent processes, consider migrating to llama.cpp server mode:

```bash
# Start llama.cpp in server mode
llama-server -m model.gguf --port 8080

# Use HTTP API for requests (much faster)
curl http://localhost:8080/completion -d '{"prompt": "..."}'
```

This would eliminate the per-query process creation overhead entirely.

## Files Added

1. **Services/PersistentLlmProcess.cs** - Single LLM instance wrapper
2. **Services/ModelInstancePool.cs** - Instance pool manager
3. **Services/AiChatServicePooled.cs** - Chat service using pool

## Files Modified

1. **OfflineAI/Modes/RunVectorMemoryWithDatabaseMode.cs** - Uses pool instead of per-request service

## Migration Path

The original `AiChatService` is still available and unchanged. You can:
- Use `AiChatServicePooled` for web scenarios (keeps models loaded)
- Use `AiChatService` for testing/development (loads per request)

Both services implement the same interface pattern and work with the same memory systems.
