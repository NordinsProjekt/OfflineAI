# AI Project - Pooling Components Documentation

## Overview
The **Pooling** namespace (`Application.AI.Pooling`) implements the Object Pool design pattern for managing LLM process instances. This is critical for performance in multi-user web scenarios.

---

## IModelInstancePool.cs

### Purpose
Defines the contract for LLM instance pool management, enabling dependency injection and testability.

### Key Responsibilities
1. **Instance Management**: Acquire and release LLM instances
2. **Lifecycle Control**: Initialize, reinitialize, and dispose instances
3. **Pool Monitoring**: Track available/total instances
4. **Configuration**: Timeout management

### Interface Members

```csharp
public interface IModelInstancePool : IDisposable
{
    // Read-only properties for monitoring
    int AvailableCount { get; }     // Instances ready to use
    int MaxInstances { get; }       // Pool capacity
    int TotalInstances { get; }     // Currently created instances
    
    // Configurable timeout
    int TimeoutMs { get; set; }     // Per-query timeout (1000-300000ms)
    
    // Lifecycle methods
    Task InitializeAsync(Action<int, int>? progressCallback = null);
    Task ReinitializeAsync(string llmPath, string modelPath, Action<int, int>? progressCallback = null);
    Task<PooledInstance> AcquireAsync(CancellationToken cancellationToken = default);
}
```

### Usage Example
```csharp
// Register pool in DI
services.AddSingleton<IModelInstancePool>(sp => 
    new ModelInstancePool(llmPath, modelPath, maxInstances: 3));

// Use in service
public class ChatService
{
    private readonly IModelInstancePool _pool;
    
    public async Task<string> QueryAsync(string question)
    {
        using var instance = await _pool.AcquireAsync();
        return await instance.Process.QueryAsync(systemPrompt, question);
    }
}
```

### Design Decisions
- **IDisposable**: Ensures proper cleanup of native resources (LLM processes)
- **Async Methods**: Non-blocking pool operations
- **CancellationToken**: Allows request cancellation
- **Progress Callbacks**: UI feedback during initialization

### Testing Strategy
- Mock `IModelInstancePool` for unit tests
- Test timeout enforcement
- Verify instance reuse and cleanup

---

## ModelInstancePool.cs

### Purpose
Concrete implementation of `IModelInstancePool` using `ConcurrentBag<T>` and `SemaphoreSlim` for thread-safe instance management.

### Architecture

```
??????????????????????????????????????????
?      ModelInstancePool                 ?
??????????????????????????????????????????
?  - _availableInstances: ConcurrentBag  ?
?  - _semaphore: SemaphoreSlim           ?
?  - _maxInstances: int (e.g., 3)        ?
??????????????????????????????????????????
?  + AcquireAsync() ? PooledInstance     ?
?  + ReturnInstance(instance)            ?
?  + InitializeAsync()                   ?
?  + ReinitializeAsync(newModel)         ?
??????????????????????????????????????????
         ?
         ? Contains 0-N instances
         ?
???????????????????????????????????????????
?   PersistentLlmProcess (instance)       ?
?   - IsHealthy: bool                     ?
?   - LastUsed: DateTime                  ?
?   - QueryAsync(prompt, question)        ?
???????????????????????????????????????????
```

### Key Features

#### 1. Thread-Safe Instance Management
```csharp
private readonly ConcurrentBag<PersistentLlmProcess> _availableInstances;
private readonly SemaphoreSlim _semaphore;

public async Task<PooledInstance> AcquireAsync(CancellationToken ct)
{
    await _semaphore.WaitAsync(ct); // Block if all instances busy
    
    // Try to get healthy instance
    while (_availableInstances.TryTake(out var candidate))
    {
        if (candidate.IsHealthy)
            return new PooledInstance(candidate, this);
        else
            CreateReplacement(candidate); // Replace unhealthy instance
    }
}
```

#### 2. Health Monitoring
- Tracks `IsHealthy` flag on each instance
- Automatically replaces unhealthy instances
- Disposes corrupted instances
- Maintains target pool size

#### 3. Dynamic Timeout Updates
```csharp
public int TimeoutMs 
{ 
    set
    {
        _timeoutMs = value;
        
        // Update all existing instances
        foreach (var instance in _availableInstances)
        {
            instance.TimeoutMs = value;
        }
    }
}
```

#### 4. Model Hot-Swapping
```csharp
public async Task ReinitializeAsync(string newModel, ...)
{
    // 1. Dispose all old instances
    while (_availableInstances.TryTake(out var instance))
    {
        instance.Dispose();
    }
    
    // 2. Reset semaphore
    ResetSemaphore();
    
    // 3. Load new model instances
    await InitializeAsync();
}
```

### Configuration Guidelines

| Concurrent Users | Recommended Pool Size | RAM Required | CPU Cores |
|------------------|----------------------|--------------|-----------|
| 1-5 | 2 | 2.5 GB | 4+ |
| 5-10 | 3 | 3.6 GB | 4+ |
| 10-25 | 5 | 6.0 GB | 8+ |
| 25-50 | 8 | 9.6 GB | 8+ |
| 50-100 | 10 | 12 GB | 16+ |

### Initialization Workflow
```csharp
// Step 1: Create pool (lazy, no instances yet)
var pool = new ModelInstancePool(llmPath, modelPath, maxInstances: 3);

// Step 2: Pre-warm pool (loads all instances)
await pool.InitializeAsync((current, total) => 
{
    Console.WriteLine($"Loading instance {current}/{total}...");
});

// Step 3: Pool ready for concurrent requests
using var instance1 = await pool.AcquireAsync(); // Returns immediately
using var instance2 = await pool.AcquireAsync(); // Returns immediately
using var instance3 = await pool.AcquireAsync(); // Returns immediately
using var instance4 = await pool.AcquireAsync(); // Blocks until one returns
```

### Error Handling
- **Initialization Failure**: Throws if zero instances successfully created
- **Acquire Timeout**: Respects `CancellationToken` for timeout control
- **Unhealthy Instance**: Automatically replaced on next acquire
- **Disposal**: Cleans up all instances and releases semaphore

### Performance Characteristics
| Operation | Time | Notes |
|-----------|------|-------|
| Acquire (instance available) | <1ms | Lock acquisition only |
| Acquire (all busy) | Varies | Waits for return |
| Return | <1ms | Add to bag + release semaphore |
| Initialize (3 instances) | 30-90s | Parallel loading |
| Reinitialize | 35-95s | Dispose + initialize |

### Best Practices
1. **Always use `using` statement** with `AcquireAsync()` to ensure return
2. **Pre-warm pool at startup** to avoid cold starts
3. **Monitor `AvailableCount`** to detect bottlenecks
4. **Tune `MaxInstances`** based on server RAM and user load
5. **Set appropriate `TimeoutMs`** (30-60s for CPU, 15-30s for GPU)

### Common Issues and Solutions

#### Issue: All instances busy
**Symptom**: `AcquireAsync()` blocks for long periods
**Solution**: Increase `MaxInstances` or reduce `TimeoutMs`

#### Issue: Out of memory
**Symptom**: System becomes unresponsive, high swap usage
**Solution**: Decrease `MaxInstances` or use smaller model

#### Issue: Instances becoming unhealthy
**Symptom**: Frequent "Replacing unhealthy instance" messages
**Solution**: Check for LLM crashes, increase timeout, verify model compatibility

---

## PooledInstance.cs

### Purpose
RAII (Resource Acquisition Is Initialization) wrapper that automatically returns an LLM instance to the pool when disposed.

### Design Pattern
**Disposable Wrapper Pattern** - Ensures resources are returned even if exception occurs.

### Implementation
```csharp
public class PooledInstance : IDisposable
{
    public IPersistentLlmProcess Process { get; }
    private readonly IModelInstancePool _pool;
    private bool _disposed;
    
    public void Dispose()
    {
        if (!_disposed)
        {
            if (Process is PersistentLlmProcess persistentProcess 
                && _pool is ModelInstancePool concretePool)
            {
                concretePool.ReturnInstance(persistentProcess);
            }
            _disposed = true;
        }
    }
}
```

### Usage Pattern
```csharp
// ? CORRECT: Automatic return via using statement
public async Task<string> QueryAsync(string question)
{
    using var pooled = await _pool.AcquireAsync();
    return await pooled.Process.QueryAsync(systemPrompt, question);
    
    // Instance automatically returned here
}

// ? INCORRECT: Forgetting to dispose leaks instances
public async Task<string> QueryAsync(string question)
{
    var pooled = await _pool.AcquireAsync();
    return await pooled.Process.QueryAsync(systemPrompt, question);
    
    // Instance NOT returned! Pool depleted after N calls!
}
```

### Safety Features
1. **Double-Dispose Protection**: `_disposed` flag prevents multiple returns
2. **Type Safety**: Only returns instances of expected types
3. **Exception Safety**: Using statement guarantees return even if query throws

### Why Not Just Return the Process?
**Problem**: Developers might forget to manually return instances
```csharp
// Fragile - easy to forget
var process = await pool.AcquireAsync();
try 
{
    await process.QueryAsync(...);
}
finally 
{
    pool.Return(process); // Easily forgotten!
}
```

**Solution**: Using statement + IDisposable = automatic cleanup
```csharp
// Robust - C# guarantees Dispose() is called
using var pooled = await pool.AcquireAsync();
await pooled.Process.QueryAsync(...);
// Dispose() called automatically, instance returned
```

### Testing Strategy
```csharp
[Fact]
public async Task PooledInstance_AutomaticallyReturnsToPool()
{
    // Arrange
    var pool = new ModelInstancePool(llmPath, modelPath, maxInstances: 1);
    await pool.InitializeAsync();
    
    // Act & Assert
    Assert.Equal(1, pool.AvailableCount);
    
    using (var pooled = await pool.AcquireAsync())
    {
        Assert.Equal(0, pool.AvailableCount); // Instance acquired
    }
    
    Assert.Equal(1, pool.AvailableCount); // Instance returned
}
```

---

## Integration with Other Components

### Dependency Graph
```
AiDashboard.Services.DashboardChatService
    ? uses
IModelInstancePool (interface)
    ? implemented by
ModelInstancePool (concrete)
    ? contains
PersistentLlmProcess (instance)
    ? wraps
llama-cli.exe (native process)
```

### Configuration Flow
```
appsettings.json
    ?
AppConfiguration.Pool.MaxInstances
    ?
ModelInstancePool constructor
    ?
Pool initialization (N instances)
    ?
Ready for concurrent requests
```

---

## Monitoring and Diagnostics

### Console Output (Verbose Mode)
```
[*] Pool initialized: 3/3 instances
[*] Pool timeout updated to 60000ms (60s)
[*] Reinitializing pool with new model...
[*] Disposed 3 old instances
[+] Pool reinitialized: 3/3 instances
[!] Returning unhealthy instance, will create replacement
[+] Replacement instance created and added to pool
[+] Pool disposed
```

### Metrics to Monitor
1. **AvailableCount**: Should stay >0 under normal load
2. **TotalInstances**: Should equal MaxInstances
3. **Acquire Duration**: Time waiting for instance (should be <100ms)
4. **Unhealthy Count**: Frequency of instance replacement (should be rare)

---

## Performance Optimization Tips

### 1. Pre-warm Pool at Startup
```csharp
// SLOW: Lazy initialization on first request
var pool = new ModelInstancePool(...);
// First 3 users wait 30 seconds each

// FAST: Pre-warm during app startup
var pool = new ModelInstancePool(...);
await pool.InitializeAsync();
// All users get immediate responses
```

### 2. Tune Pool Size for Hardware
```csharp
// Calculate based on available RAM
var availableGB = 16; // Total system RAM
var osReserved = 4;   // OS + other apps
var modelSize = 1.2;  // TinyLlama Q5_K_M
var maxInstances = (int)((availableGB - osReserved) / modelSize);
```

### 3. Use Cancellation Tokens
```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
using var pooled = await _pool.AcquireAsync(cts.Token);
// User won't wait forever if pool exhausted
```

---

## Document Version
- **File**: `AI\Pooling\*`
- **Purpose**: LLM instance pooling for concurrent request handling
- **Key Classes**: `IModelInstancePool`, `ModelInstancePool`, `PooledInstance`
- **Dependencies**: `Application.AI.Processing.PersistentLlmProcess`
- **Consumed By**: `AiDashboard.Services.DashboardChatService`
- **Last Updated**: 2024
