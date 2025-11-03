using System.Collections.Concurrent;

namespace Services;

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
    private readonly string _llmPath;
    private readonly string _modelPath;
    private readonly int _maxInstances;
    private readonly int _timeoutMs;
    private bool _disposed;

    public int AvailableCount => _availableInstances.Count;
    public int MaxInstances => _maxInstances;

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
                    
                    _availableInstances.Add(instance);
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
                // Dispose unhealthy instance and try to create a new one
                candidate.Dispose();
                try
                {
                    instance = await PersistentLlmProcess.CreateAsync(_llmPath, _modelPath, _timeoutMs);
                    break;
                }
                catch
                {
                    // Failed to create replacement, try next candidate
                    continue;
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
    private void Return(PersistentLlmProcess instance)
    {
        if (_disposed)
        {
            instance.Dispose();
            return;
        }

        if (instance.IsHealthy)
        {
            _availableInstances.Add(instance);
        }
        else
        {
            instance.Dispose();
            
            // Try to create a replacement instance
            Task.Run(async () =>
            {
                try
                {
                    var replacement = await PersistentLlmProcess.CreateAsync(_llmPath, _modelPath, _timeoutMs);
                    _availableInstances.Add(replacement);
                }
                catch
                {
                    // Failed to create replacement, pool size will be reduced
                }
            });
        }

        _semaphore.Release();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Dispose all instances
        while (_availableInstances.TryTake(out var instance))
        {
            instance.Dispose();
        }

        _semaphore?.Dispose();
    }

    /// <summary>
    /// Wrapper that automatically returns the instance to the pool when disposed.
    /// Use with 'using' statement for automatic resource management.
    /// </summary>
    public class PooledInstance : IDisposable
    {
        public PersistentLlmProcess Process { get; }
        private readonly ModelInstancePool _pool;
        private bool _disposed;

        internal PooledInstance(PersistentLlmProcess process, ModelInstancePool pool)
        {
            Process = process ?? throw new ArgumentNullException(nameof(process));
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _pool.Return(Process);
                _disposed = true;
            }
        }
    }
}
