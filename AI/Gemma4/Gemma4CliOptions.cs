namespace Application.AI.Gemma4;

/// <summary>
/// Configuration for running Gemma 4 via a local <c>llama-completion</c> subprocess.
/// <para>
/// Mirrors the pattern used by <see cref="Application.AI.Processing.PersistentLlmProcess"/>
/// but exposes Gemma 4-specific defaults (temperature 1.0, top-p 0.95, top-k 64).
/// </para>
/// <para>
/// Minimal example:
/// <code>
/// new Gemma4CliOptions
/// {
///     LlamaCliPath = @"C:\llama.cpp\llama-completion.exe",
///     ModelPath    = @"C:\models\gemma-4-12b-it-Q4_K_S.gguf",
///     GpuLayers    = 99
/// }
/// </code>
/// </para>
/// <para>
/// The per-call knobs (sampling, context size, GPU layers, timeouts) are settable after
/// construction, because every request spawns its own <c>llama-completion</c> process and reads
/// them fresh — so the dashboard's settings page can retune the running backend without a
/// restart. Everything that is baked into the process launch or the chat template
/// (<see cref="LlamaCliPath"/>, <see cref="ModelPath"/>, <see cref="Device"/>,
/// <see cref="SystemPrompt"/>, <see cref="EnableThinking"/>) stays init-only.
/// </para>
/// </summary>
public sealed class Gemma4CliOptions
{
    /// <summary>
    /// Path to the <c>llama-completion</c> executable.
    /// <para>
    /// Must be <c>llama-completion</c>, not <c>llama-cli</c>: newer llama.cpp builds split the old
    /// all-in-one binary in two, and <c>llama-cli</c> rejects the <c>-no-cnv</c> flag this service
    /// depends on — it prints that rejection to stdout (polluting the captured output) and then
    /// runs in conversation mode anyway.
    /// </para>
    /// Default: <c>llama-completion</c> (resolved from PATH).
    /// </summary>
    public string LlamaCliPath { get; init; } = "llama-completion";

    /// <summary>Path to the Gemma 4 GGUF model file.</summary>
    public string ModelPath { get; init; } = string.Empty;

    /// <summary>
    /// Number of model layers to offload to the GPU (<c>-ngl</c>).
    /// Use <c>0</c> for CPU-only, <c>99</c> to offload all layers.
    /// Default: 0
    /// </summary>
    public int GpuLayers { get; set; } = 0;

    /// <summary>
    /// Comma-separated devices to offload to (<c>--device</c>), e.g. <c>"CUDA0"</c>.
    /// Run <c>llama-completion --list-devices</c> on the target machine to see the names.
    /// <para>
    /// Set this on any machine with more than one GPU. When it is empty llama.cpp offloads
    /// across <em>every</em> visible device (split-mode defaults to <c>layer</c>), so on a box
    /// with a big card and a small one it will place layers on the small card too and fail to
    /// allocate — or succeed and run at the slow card's speed. Naming the single intended
    /// device avoids both.
    /// </para>
    /// Default: empty (let llama.cpp choose — correct only on single-GPU machines).
    /// </summary>
    public string Device { get; init; } = string.Empty;

    /// <summary>
    /// KV-cache context size in tokens (<c>-c</c>).
    /// Gemma 4 models can be configured up to 256K tokens architecturally, but the KV cache for
    /// a large context adds up fast on top of the model weights themselves — a 12B model at
    /// Q4 quantization already uses ~7GB just for weights, so a 256K context on a 10GB GPU
    /// generally won't fit and causes llama-cli to fail (empty output or a hard crash) rather
    /// than gracefully falling back to CPU/partial offload. Default: 32 768 — a much safer
    /// starting point for typical consumer GPUs; raise it only if you've confirmed it fits.
    /// </summary>
    public int ContextSize { get; set; } = 32_768;

    /// <summary>
    /// Maximum new tokens to generate per call (<c>-n</c>).
    /// Default: 2048
    /// </summary>
    public int MaxTokens { get; set; } = 2048;

    /// <summary>
    /// Sampling temperature. Gemma 4 documentation recommends <c>1.0</c>.
    /// Default: 1.0
    /// </summary>
    public double Temperature { get; set; } = 1.0;

    /// <summary>
    /// Top-P nucleus sampling. Gemma 4 documentation recommends <c>0.95</c>.
    /// Default: 0.95
    /// </summary>
    public double TopP { get; set; } = 0.95;

    /// <summary>
    /// Top-K sampling. Gemma 4 documentation recommends <c>64</c>.
    /// Default: 64
    /// </summary>
    public int TopK { get; set; } = 64;

    /// <summary>
    /// Hard wall-clock timeout for a single subprocess call, in milliseconds.
    /// Default: 120 000 ms (2 minutes)
    /// </summary>
    public int TimeoutMs { get; set; } = 120_000;

    /// <summary>
    /// If no new output is produced for this many milliseconds, treat generation as complete.
    /// Default: 10 000 ms (10 seconds)
    /// </summary>
    public int PauseTimeoutMs { get; set; } = 10_000;

    /// <summary>
    /// Maximum number of tool-call / tool-result round trips before forcing a final answer.
    /// Default: 5
    /// </summary>
    public int MaxToolCallIterations { get; init; } = 5;

    /// <summary>
    /// System instruction placed in the native <c>&lt;|turn&gt;system</c> turn. Empty means no
    /// system turn is emitted (unless tools or thinking require one).
    /// <para>
    /// Gemma 4 has a real system role; Gemma 3 did not. Do not fold system prompts into the user
    /// turn — that was a Gemma 3 workaround.
    /// </para>
    /// Default: empty.
    /// </summary>
    public string SystemPrompt { get; init; } = string.Empty;

    /// <summary>
    /// Emits <c>&lt;|think|&gt;</c> at the start of the system turn, enabling chain-of-thought.
    /// <para>
    /// Off by default, and deliberately so: thinking is expensive. On a 12B it can consume the
    /// entire <see cref="MaxTokens"/> budget reasoning about a trivial question and never reach
    /// the answer — raise <see cref="MaxTokens"/> substantially when turning this on. The
    /// reasoning also tends to come out in English regardless of the system prompt's language.
    /// </para>
    /// <para>
    /// Note that this does not silence the reasoning channel: every size above E4B emits
    /// <c>&lt;|channel&gt;thought … &lt;channel|&gt;</c> regardless, with an empty block when
    /// thinking is off. <c>Gemma4CliService</c> strips it either way.
    /// </para>
    /// Default: <c>false</c>.
    /// </summary>
    public bool EnableThinking { get; init; }
}
