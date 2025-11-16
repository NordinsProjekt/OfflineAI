using Application.AI.Processing;
using System.Collections.Concurrent;

namespace Application.AI.Pooling;

/// <summary>
/// Manages a pool of pre-loaded LLM process instances to handle concurrent requests
/// without repeatedly loading/unloading the model from memory.
/// 
/// Best Practices:
/// - For <10 concurrent users: Use 2-3 instances
/// - For 10-50 concurrent users: Use 3-5 instances  
/// - For 50-100 concurrent users: Use 8-10 instances
/// 
/// Memory usage: ~1-1.5 GB per instance for TinyLlama 1.1B Q5_K_M
/// </summary>
public class ModelInstancePool : IDisposable
{
    private readonly ConcurrentBag<PersistentLlmProcess> _availableInstances = new();
    private readonly SemaphoreSlim _semaphore;
    private string _llmPath;
    private string _modelPath;
    private readonly int _maxInstances;
    private int _timeoutMs;
    private bool _disposed;
    private int _totalInstancesCreated = 0;
    private readonly object _lock = new object();

    public int AvailableCount => _availableInstances.Count;
    public int MaxInstances => _maxInstances;
    public int TotalInstances => _totalInstancesCreated;
    public int TimeoutMs 
    { 
        get => _timeoutMs;
        set
        {
            if (value < 1000 || value > 300000)
                throw new ArgumentOutOfRangeException(nameof(value), "Timeout must be between 1 and 300 seconds (1000-300000ms)");
            
            _timeoutMs = value;
            
            // Update timeout on all existing instances in the pool
            lock (_lock)
            {
                foreach (var instance in _availableInstances)
                {
                    instance.TimeoutMs = value;
                }
            }
            
            Console.WriteLine($"[*] Pool timeout updated to {value}ms ({value/1000}s)");
        }
    }

    public ModelInstancePool(
        string llmPath, 
        string modelPath, 
        int maxInstances = 3,
        int timeoutMs = 30000)
    {
        if (maxInstances < 1)
            throw new ArgumentException("Must have at least 1 instance", nameof(maxInstances));

        _llmPath = llmPath ?? throw new ArgumentNullException(nameof(llmPath));
        _modelPath = modelPath ?? throw new ArgumentNullException(nameof(modelPath));
        _maxInstances = maxInstances;
        _timeoutMs = timeoutMs;
        _semaphore = new SemaphoreSlim(maxInstances, maxInstances);
    }

    /// <summary>
    /// Pre-warm the pool by loading all instances.
    /// Call this at application startup to avoid cold-start delays.
    /// </summary>
    public async Task InitializeAsync(Action<int, int>? progressCallback = null)
    {
        var tasks = new List<Task>();
        
        for (int i = 0; i < _maxInstances; i++)
        {
            var instanceNumber = i + 1;
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    progressCallback?.Invoke(instanceNumber, _maxInstances);
                    
                    var instance = await PersistentLlmProcess.CreateAsync(
                        _llmPath, 
                        _modelPath, 
                        _timeoutMs);
                    
                    lock (_lock)
                    {
                        _availableInstances.Add(instance);
                        _totalInstancesCreated++;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to initialize instance {instanceNumber}: {ex.Message}");
                }
            }));
        }

        await Task.WhenAll(tasks);

        if (_availableInstances.Count == 0)
        {
            throw new InvalidOperationException("Failed to initialize any LLM instances");
        }

        Console.WriteLine($"? Pool initialized: {_availableInstances.Count}/{_maxInstances} instances");
    }

    /// <summary>
    /// Reinitialize the pool with a new model.
    /// Disposes all existing instances and creates new ones with the specified model.
    /// </summary>
    public async Task ReinitializeAsync(
        string llmPath,
        string modelPath,
        Action<int, int>? progressCallback = null)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ModelInstancePool));

        Console.WriteLine($"[*] Reinitializing pool with new model...");
        Console.WriteLine($"    Current state: {_availableInstances.Count}/{_totalInstancesCreated} instances");

        // Update paths
        _llmPath = llmPath ?? throw new ArgumentNullException(nameof(llmPath));
        _modelPath = modelPath ?? throw new ArgumentNullException(nameof(modelPath));

        // Dispose all existing instances
        int disposedCount = 0;
        lock (_lock)
        {
            while (_availableInstances.TryTake(out var instance))
            {
                instance.Dispose();
                disposedCount++;
                _totalInstancesCreated--;
            }
        }

        Console.WriteLine($"[*] Disposed {disposedCount} old instances");

        // Reset the semaphore to ensure correct state
        // We need to drain any available permits and reset to max
        while (_semaphore.CurrentCount > 0)
        {
            await _semaphore.WaitAsync();
        }
        for (int i = 0; i < _maxInstances; i++)
        {
            _semaphore.Release();
        }

        // Reinitialize with new model
        await InitializeAsync(progressCallback);
        
        Console.WriteLine($"? Pool reinitialized: {_availableInstances.Count}/{_maxInstances} instances");
    }

    /// <summary>
    /// Acquire an instance from the pool. Blocks if all instances are busy.
    /// Always use with 'using' statement to ensure instance is returned to pool.
    /// </summary>
    public async Task<PooledInstance> AcquireAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ModelInstancePool));

        await _semaphore.WaitAsync(cancellationToken);
        
        PersistentLlmProcess? instance = null;
        
        // Try to get a healthy instance
        while (_availableInstances.TryTake(out var candidate))
        {
            if (candidate.IsHealthy)
            {
                instance = candidate;
                break;
            }
            else
            {
                // Dispose unhealthy instance
                Console.WriteLine($"[!] Disposing unhealthy instance");
                candidate.Dispose();
                
                lock (_lock)
                {
                    _totalInstancesCreated--;
                }
                
                // Try to create a replacement synchronously
                try
                {
                    Console.WriteLine($"[*] Creating replacement instance...");
                    instance = await PersistentLlmProcess.CreateAsync(_llmPath, _modelPath, _timeoutMs);
                    
                    lock (_lock)
                    {
                        _totalInstancesCreated++;
                    }
                    
                    Console.WriteLine($"[+] Replacement instance created");
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[!] Failed to create replacement: {ex.Message}");
                    // Continue trying other candidates
                }
            }
        }

        if (instance == null)
        {
            _semaphore.Release();
            throw new InvalidOperationException("No healthy instances available in pool");
        }

        return new PooledInstance(instance, this);
    }

    /// <summary>
    /// Return an instance to the pool (called automatically by PooledInstance.Dispose).
    /// </summary>
    internal void ReturnInstance(PersistentLlmProcess instance)
    {
        if (_disposed)
        {
            instance.Dispose();
            lock (_lock)
            {
                _totalInstancesCreated--;
            }
            return;
        }

        if (instance.IsHealthy)
        {
            _availableInstances.Add(instance);
            _semaphore.Release();
        }
        else
        {
            // Instance is unhealthy, dispose it
            Console.WriteLine($"[!] Returning unhealthy instance, will create replacement");
            instance.Dispose();
            
            lock (_lock)
            {
                _totalInstancesCreated--;
            }
            
            // Try to create a replacement synchronously to maintain pool size
            Task.Run(async () =>
            {
                try
                {
                    var replacement = await PersistentLlmProcess.CreateAsync(_llmPath, _modelPath, _timeoutMs);
                    
                    lock (_lock)
                    {
                        if (!_disposed && _totalInstancesCreated < _maxInstances)
                        {
                            _availableInstances.Add(replacement);
                            _totalInstancesCreated++;
                            Console.WriteLine($"[+] Replacement instance created and added to pool");
                        }
                        else
                        {
                            replacement.Dispose();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[!] Failed to create replacement instance: {ex.Message}");
                }
            }).ContinueWith(_ => _semaphore.Release()); // Release semaphore after replacement attempt
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        Console.WriteLine($"[*] Disposing pool with {_availableInstances.Count} instances");

        // Dispose all instances
        lock (_lock)
        {
            while (_availableInstances.TryTake(out var instance))
            {
                instance.Dispose();
                _totalInstancesCreated--;
            }
        }

        _semaphore?.Dispose();
        
        Console.WriteLine($"[+] Pool disposed");
    }
}

