using Services.AgentTools;

namespace Application.AI.Gemma4;

/// <summary>
/// Subprocess-based Gemma 4 service that runs <c>llama-completion</c> locally.
/// Each call spawns a fresh process, writes the prompt to a temp file (<c>-f</c>),
/// and captures stdout — mirroring the pattern of
/// <see cref="Application.AI.Processing.PersistentLlmProcess"/>.
/// <para>
/// Unlike <see cref="IGemma4AgentService"/> (which requires a running HTTP server),
/// this service works entirely offline with no network dependency.
/// </para>
/// <para>
/// Prompts use Gemma 4's <c>&lt;|turn&gt;</c> chat template — <b>not</b> Gemma 3's
/// <c>&lt;start_of_turn&gt;</c>. See <c>Gemma4CliService</c>'s remarks for why the template and the
/// <c>-sp</c> flag must always change together.
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
    /// Chat with automatic tool calling, using Gemma 4's native function-calling tokens.
    /// <para>
    /// Tool definitions from <paramref name="toolRegistry"/> are rendered as
    /// <c>&lt;|tool&gt;name{param:type}: description&lt;tool|&gt;</c> into the system turn. The
    /// model answers with <c>&lt;|tool_call&gt;call:name{args}&lt;tool_call|&gt;</c> and then stops
    /// at <c>&lt;|tool_response&gt;</c> — that token is in the model's end-of-generation set, so it
    /// hands control back without any prompt-level protocol. The result is appended to complete the
    /// response block and generation resumes from that point, up to
    /// <see cref="Gemma4CliOptions.MaxToolCallIterations"/> round trips, until the model produces a
    /// plain-text answer.
    /// </para>
    /// <para>
    /// A tool's <see cref="AgentTool.Description"/> is not documentation: it is the only signal the
    /// model has when choosing between tools, and it is emitted verbatim into the prompt.
    /// </para>
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
