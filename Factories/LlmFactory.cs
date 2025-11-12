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

    /// <summary>
    /// Creates a ProcessStartInfo configured for board game question-answering with optimized sampling parameters.
    /// </summary>
    /// <param name="cliPath">Path to the llama-cli executable</param>
    /// <param name="modelPath">Path to the GGUF model file</param>
    /// <param name="maxTokens">Maximum tokens to generate (default: 200)</param>
    /// <param name="temperature">Temperature for sampling (default: 0.4)</param>
    /// <returns>Fully configured ProcessStartInfo for board game scenarios</returns>
    public static ProcessStartInfo CreateForBoardGame(
        string cliPath, 
        string modelPath,
        int maxTokens = 200,
        float temperature = 0.4f)
    {
        return CreateForLlama(cliPath, modelPath)
            .SetBoardGameSampling(maxTokens: maxTokens, temperature: temperature);
    }
}