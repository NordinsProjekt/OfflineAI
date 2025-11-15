using System;
using System.Threading.Tasks;
using Application.AI.Pooling;

namespace Application.AI.Management
{
    public class ModelManager : IModelManager, IDisposable
    {
        private readonly ModelInstancePool _pool;
        private readonly string _llmExecutablePath;
        private string? _activeModelPath;
        private bool _disposed;

        public ModelManager(ModelInstancePool pool, string llmExecutablePath)
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _llmExecutablePath = llmExecutablePath ?? throw new ArgumentNullException(nameof(llmExecutablePath));
        }

        public string? GetActiveModelPath() => _activeModelPath;

        public async Task SwitchModelAsync(string modelFullPath, Action<int,int>? progressCallback = null)
        {
            if (string.IsNullOrWhiteSpace(modelFullPath))
                throw new ArgumentNullException(nameof(modelFullPath));

            // Reinitialize pool with new model. This disposes existing instances and creates new ones.
            await _pool.ReinitializeAsync(_llmExecutablePath, modelFullPath, progressCallback);
            _activeModelPath = modelFullPath;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _pool?.Dispose();
        }
    }
}
