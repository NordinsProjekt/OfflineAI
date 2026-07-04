using Services.AgentTools;

namespace Application.AI.Gemma4;

/// <summary>
/// Subprocess-based Gemma 4 service that runs <c>llama-cli</c> locally.
/// Each call spawns a fresh process, writes the prompt to a temp file (<c>-f</c>),
/// and captures stdout — mirroring the pattern of
/// <see cref="Application.AI.Processing.PersistentLlmProcess"/>.
/// <para>
/// Unlike <see cref="IGemma4AgentService"/> (which requires a running HTTP server),
/// this service works entirely offline with no network dependency.
/// </para>
/// </summary>
public interface IGemma4CliService
{
    /// <summary>
    /// File name of the Gemma 4 GGUF model currently configured for this service
    /// (derived from <see cref="Gemma4CliOptions.ModelPath"/>). Used to identify
    /// the LLM when persisting question/answer turns.
    /// </summary>
    string ModelName { get; }

    /// <summary>Simple text-only chat.</summary>
    Task<string> ChatAsync(string userMessage, CancellationToken cancellationToken = default);

    /// <summary>
    /// Multimodal chat using an image file already on disk.
    /// Passes the path directly to llama-cli via <c>--image &lt;path&gt;</c>.
    /// <para>
    /// Requires a multimodal-capable Gemma 4 GGUF (all sizes support images).
    /// </para>
    /// </summary>
    Task<string> ChatWithImageAsync(
        string userMessage,
        string imagePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Multimodal chat using raw image bytes.
    /// The bytes are written to a temp file, passed to llama-cli, then deleted.
    /// <para>
    /// Requires a multimodal-capable Gemma 4 GGUF (all sizes support images).
    /// </para>
    /// </summary>
    Task<string> ChatWithImageBytesAsync(
        string userMessage,
        ReadOnlyMemory<byte> imageData,
        string mimeType = "image/jpeg",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Chat with automatic tool calling.
    /// Tool definitions from <paramref name="toolRegistry"/> are injected into
    /// the user prompt as a JSON schema. The LLM replies with a JSON tool-call array;
    /// results are fed back in a <c>&lt;start_of_turn&gt;tool</c> turn and the model
    /// is re-queried — up to <see cref="Gemma4CliOptions.MaxToolCallIterations"/> times
    /// — until it returns a plain-text final answer.
    /// <para>
    /// Register the built-in file operations with
    /// <see cref="AgentToolRegistry"/> and expose them through
    /// <c>BuiltInFileTools</c> (or register any custom tools).
    /// </para>
    /// </summary>
    Task<string> ChatWithToolsAsync(
        string userMessage,
        IAgentToolRegistry toolRegistry,
        CancellationToken cancellationToken = default);
}
