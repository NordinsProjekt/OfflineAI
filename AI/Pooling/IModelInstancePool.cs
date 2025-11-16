namespace Application.AI.Pooling;

/// <summary>
/// Interface for managing a pool of LLM process instances.
/// Enables testability and dependency injection.
/// </summary>
public interface IModelInstancePool : IDisposable
{
    /// <summary>
    /// Number of available instances in the pool.
    /// </summary>
    int AvailableCount { get; }

    /// <summary>
    /// Maximum number of instances allowed in the pool.
    /// </summary>
    int MaxInstances { get; }

    /// <summary>
    /// Total number of instances created.
    /// </summary>
    int TotalInstances { get; }

    /// <summary>
    /// Timeout in milliseconds for LLM operations.
    /// </summary>
    int TimeoutMs { get; set; }

    /// <summary>
    /// Pre-warm the pool by loading all instances.
    /// Call this at application startup to avoid cold-start delays.
    /// </summary>
    Task InitializeAsync(Action<int, int>? progressCallback = null);

    /// <summary>
    /// Reinitialize the pool with a new model.
    /// Disposes all existing instances and creates new ones with the specified model.
    /// </summary>
    Task ReinitializeAsync(string llmPath, string modelPath, Action<int, int>? progressCallback = null);

    /// <summary>
    /// Acquire an instance from the pool. Blocks if all instances are busy.
    /// Always use with 'using' statement to ensure instance is returned to pool.
    /// </summary>
    Task<PooledInstance> AcquireAsync(CancellationToken cancellationToken = default);
}
