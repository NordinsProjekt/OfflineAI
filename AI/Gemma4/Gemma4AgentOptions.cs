namespace Application.AI.Gemma4;

/// <summary>
/// Configuration for connecting to a Gemma 4 llama.cpp server
/// (or any OpenAI-compatible server such as vLLM, Ollama, SGLang).
/// <para>
/// Start the server with:
/// <code>llama-server -hf unsloth/gemma-4-26B-A4B-it-qat-GGUF:UD-Q4_K_XL</code>
/// </para>
/// </summary>
public sealed class Gemma4AgentOptions
{
    /// <summary>
    /// Base URL of the OpenAI-compatible server.
    /// Default: <c>http://localhost:8080</c>
    /// </summary>
    public string ServerBaseUrl { get; init; } = "http://localhost:8080";

    /// <summary>
    /// Model identifier sent in the API request.
    /// Use the model name shown by the server, or any non-empty string for llama.cpp.
    /// Default: <c>gemma-4</c>
    /// </summary>
    public string ModelId { get; init; } = "gemma-4";

    /// <summary>
    /// API key. Local servers do not require a real key; any non-empty value works.
    /// Default: <c>none</c>
    /// </summary>
    public string ApiKey { get; init; } = "none";

    /// <summary>
    /// Maximum number of tokens to generate per response.
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
}
