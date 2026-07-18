using Microsoft.SemanticKernel;

namespace Application.AI.Gemma4;

/// <summary>
/// Multimodal + tool-calling agent service for Gemma 4.
/// <para>
/// Requires a running OpenAI-compatible server, for example:
/// <code>llama-server -hf unsloth/gemma-4-26B-A4B-it-qat-GGUF:UD-Q4_K_XL</code>
/// </para>
/// <para>
/// Typical tool-calling workflow:
/// <code>
/// var kernel = service.CreateKernel();
/// kernel.Plugins.AddFromObject(new BuiltInFileTools(fileAgent));
/// string answer = await service.ChatWithToolsAsync("Create notes.txt and write tips to it.", kernel);
/// </code>
/// Semantic Kernel handles the full tool-call loop: it forwards the LLM's tool-call
/// requests to the registered kernel functions and feeds the results back until the
/// model returns a final text answer.
/// </para>
/// </summary>
public interface IGemma4AgentService
{
    /// <summary>Simple text-only chat.</summary>
    Task<string> ChatAsync(string userMessage, CancellationToken cancellationToken = default);

    /// <summary>
    /// Multimodal chat — image supplied as a URL.
    /// <para>
    /// Gemma 4 best practice: image is placed <em>before</em> the text in the message.
    /// </para>
    /// </summary>
    Task<string> ChatWithImageUrlAsync(
        string userMessage,
        string imageUrl,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Multimodal chat — image supplied as raw bytes (base64-encoded before sending).
    /// <para>
    /// Gemma 4 best practice: image is placed <em>before</em> the text in the message.
    /// </para>
    /// </summary>
    Task<string> ChatWithImageBytesAsync(
        string userMessage,
        ReadOnlyMemory<byte> imageData,
        string mimeType = "image/jpeg",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Multimodal chat — audio supplied as raw bytes.
    /// <para>
    /// Audio is supported by Gemma 4 E2B, E4B, and 12B only (not 26B A4B or 31B).
    /// Maximum audio length: 30 seconds.
    /// Gemma 4 best practice: text is placed <em>before</em> the audio in the message.
    /// </para>
    /// </summary>
    Task<string> ChatWithAudioAsync(
        string userMessage,
        ReadOnlyMemory<byte> audioData,
        string mimeType = "audio/wav",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Chat with automatic tool calling.
    /// <para>
    /// The <paramref name="kernel"/> must already contain the chat completion service
    /// (obtain one via <see cref="CreateKernel"/>) and any plugins/tools the LLM may call.
    /// </para>
    /// <para>
    /// Semantic Kernel handles the full invocation loop: when the model returns a tool-call
    /// response, SK invokes the matching kernel function and sends the result back to the
    /// model — repeating until a final text answer is produced.
    /// </para>
    /// </summary>
    Task<string> ChatWithToolsAsync(
        string userMessage,
        Kernel kernel,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new <see cref="Kernel"/> pre-configured with this service's Gemma 4
    /// chat backend. Add plugins to the returned kernel, then pass it to
    /// <see cref="ChatWithToolsAsync"/>.
    /// </summary>
    Kernel CreateKernel();
}
