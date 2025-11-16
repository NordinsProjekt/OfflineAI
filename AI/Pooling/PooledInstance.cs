using Application.AI.Processing;

namespace Application.AI.Pooling;

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
            _pool.ReturnInstance(Process);
            _disposed = true;
        }
    }
}
