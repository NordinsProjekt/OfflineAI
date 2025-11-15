using System;
using System.Threading.Tasks;

namespace Application.AI.Management
{
    public interface IModelManager
    {
        /// <summary>
        /// Switch the running model to the specified GGUF file path. Implementations should take care
        /// of reinitializing the model pool or instructing the runtime to load the new model.
        /// </summary>
        /// <param name="modelFullPath">Full path to the GGUF model file.</param>
        /// <param name="progressCallback">Optional progress callback (instanceNumber, totalInstances).</param>
        Task SwitchModelAsync(string modelFullPath, Action<int,int>? progressCallback = null);

        /// <summary>
        /// Returns the currently active model file path (or null if unknown).
        /// </summary>
        string? GetActiveModelPath();
    }
}
