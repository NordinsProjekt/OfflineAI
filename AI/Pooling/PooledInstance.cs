using Application.AI.Processing;

namespace Application.AI.Pooling;

/// <summary>
/// Wrapper that automatically returns the instance to the pool when disposed.
/// Use with 'using' statement for automatic resource management.
/// </summary>
public class PooledInstance(IPersistentLlmProcess process, IModelInstancePool pool) : IDisposable
{
    public IPersistentLlmProcess Process { get; } = process ?? throw new ArgumentNullException(nameof(process));
    private readonly IModelInstancePool _pool = pool ?? throw new ArgumentNullException(nameof(pool));
    private bool _disposed;

    public void Dispose()
    {
        if (!_disposed)
        {
            if (Process is PersistentLlmProcess persistentProcess && _pool is ModelInstancePool concretePool)
            {
                concretePool.ReturnInstance(persistentProcess);
            }
            _disposed = true;
        }
    }
}
