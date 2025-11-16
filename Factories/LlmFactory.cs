using System.Diagnostics;
using Factories.Extensions;

namespace Factories;

public static class LlmFactory
{
    /// <summary>
    /// Creates a ProcessStartInfo with default values for LLM execution.
    /// </summary>
    public static ProcessStartInfo Create()
    {
        return new ProcessStartInfo()
            .SetDefaultValues();
    }

    /// <summary>
    /// Creates a fully configured ProcessStartInfo for Llama CLI execution.
    /// </summary>
    /// <param name="cliPath">Path to the llama-cli executable</param>
    /// <param name="modelPath">Path to the GGUF model file</param>
    /// <returns>Pre-configured ProcessStartInfo ready for additional configuration</returns>
    public static ProcessStartInfo CreateForLlama(string cliPath, string modelPath)
    {
        return Create()
            .SetLlmCli(cliPath)
            .SetModel(modelPath);
    }
}