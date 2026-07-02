namespace Application.AI.Gemma4;

/// <summary>
/// Configuration for running Gemma 4 via a local <c>llama-cli</c> subprocess.
/// <para>
/// Mirrors the pattern used by <see cref="Application.AI.Processing.PersistentLlmProcess"/>
/// but exposes Gemma 4-specific defaults (temperature 1.0, top-p 0.95, top-k 64).
/// </para>
/// <para>
/// Minimal example:
/// <code>
/// new Gemma4CliOptions
/// {
///     LlamaCliPath = @"C:\llama.cpp\llama-cli.exe",
///     ModelPath    = @"C:\models\gemma-4-26B-A4B-it.gguf",
///     GpuLayers    = 99
/// }
/// </code>
/// </para>
/// </summary>
public sealed class Gemma4CliOptions
{
    /// <summary>
    /// Path to the <c>llama-cli</c> executable.
    /// Default: <c>llama-cli</c> (resolved from PATH).
    /// </summary>
    public string LlamaCliPath { get; init; } = "llama-cli";

    /// <summary>Path to the Gemma 4 GGUF model file.</summary>
    public string ModelPath { get; init; } = string.Empty;

    /// <summary>
    /// Number of model layers to offload to the GPU (<c>-ngl</c>).
    /// Use <c>0</c> for CPU-only, <c>99</c> to offload all layers.
    /// Default: 0
    /// </summary>
    public int GpuLayers { get; init; } = 0;

    /// <summary>
    /// KV-cache context size in tokens (<c>-c</c>).
    /// Gemma 4 26B A4B supports up to 256 K tokens.
    /// Default: 4096
    /// </summary>
    public int ContextSize { get; init; } = 4096;

    /// <summary>
    /// Maximum new tokens to generate per call (<c>-n</c>).
    /// Default: 2048
    /// </summary>
    public int MaxTokens { get; init; } = 2048;

    /// <summary>
    /// Sampling temperature. Gemma 4 documentation recommends <c>1.0</c>.
    /// Default: 1.0
    /// </summary>
    public double Temperature { get; init; } = 1.0;

    /// <summary>
    /// Top-P nucleus sampling. Gemma 4 documentation recommends <c>0.95</c>.
    /// Default: 0.95
    /// </summary>
    public double TopP { get; init; } = 0.95;

    /// <summary>
    /// Top-K sampling. Gemma 4 documentation recommends <c>64</c>.
    /// Default: 64
    /// </summary>
    public int TopK { get; init; } = 64;

    /// <summary>
    /// Hard wall-clock timeout for a single subprocess call, in milliseconds.
    /// Default: 120 000 ms (2 minutes)
    /// </summary>
    public int TimeoutMs { get; init; } = 120_000;

    /// <summary>
    /// If no new output is produced for this many milliseconds, treat generation as complete.
    /// Default: 10 000 ms (10 seconds)
    /// </summary>
    public int PauseTimeoutMs { get; init; } = 10_000;

    /// <summary>
    /// Maximum number of tool-call / tool-result round trips before forcing a final answer.
    /// Default: 5
    /// </summary>
    public int MaxToolCallIterations { get; init; } = 5;
}
