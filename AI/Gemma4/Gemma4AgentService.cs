using System.ClientModel;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI;

#pragma warning disable SKEXP0001 // FunctionChoiceBehavior is experimental in some SK builds

namespace Application.AI.Gemma4;

/// <summary>
/// Gemma 4 agent that communicates with a local llama.cpp server running in
/// OpenAI-compatible server mode. Supports plain text, multimodal (image, audio),
/// and automatic tool calling via Semantic Kernel.
/// <para>
/// Start the server before using this service:
/// <code>llama-server -hf unsloth/gemma-4-26B-A4B-it-qat-GGUF:UD-Q4_K_XL</code>
/// </para>
/// </summary>
public sealed class Gemma4AgentService : IGemma4AgentService
{
    private readonly Gemma4AgentOptions _options;
    private readonly OpenAIClient _openAIClient;

    /// <param name="options">Server connection and generation settings.</param>
    public Gemma4AgentService(Gemma4AgentOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _openAIClient = new OpenAIClient(
            new ApiKeyCredential(options.ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(options.ServerBaseUrl) });
    }

    /// <inheritdoc/>
    public Kernel CreateKernel()
    {
        var builder = Kernel.CreateBuilder();
        builder.AddOpenAIChatCompletion(_options.ModelId, _openAIClient);
        return builder.Build();
    }

    /// <inheritdoc/>
    public async Task<string> ChatAsync(string userMessage, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userMessage);

        var kernel = CreateKernel();
        var chat = kernel.GetRequiredService<IChatCompletionService>();

        var history = new ChatHistory();
        history.AddUserMessage(userMessage);

        var results = await chat.GetChatMessageContentsAsync(
            history, BuildSettings(), cancellationToken: cancellationToken);
        return results[^1].Content ?? string.Empty;
    }

    /// <inheritdoc/>
    public async Task<string> ChatWithImageUrlAsync(
        string userMessage,
        string imageUrl,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userMessage);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageUrl);

        var kernel = CreateKernel();
        var chat = kernel.GetRequiredService<IChatCompletionService>();

        var history = new ChatHistory();
        // Gemma 4 best practice: image before text.
        history.Add(new ChatMessageContent(AuthorRole.User,
            new ChatMessageContentItemCollection
            {
                new ImageContent(new Uri(imageUrl)),
                new TextContent(userMessage)
            }));

        var results = await chat.GetChatMessageContentsAsync(
            history, BuildSettings(), cancellationToken: cancellationToken);
        return results[^1].Content ?? string.Empty;
    }

    /// <inheritdoc/>
    public async Task<string> ChatWithImageBytesAsync(
        string userMessage,
        ReadOnlyMemory<byte> imageData,
        string mimeType = "image/jpeg",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userMessage);
        if (imageData.IsEmpty)
            throw new ArgumentException("Image data must not be empty.", nameof(imageData));

        var kernel = CreateKernel();
        var chat = kernel.GetRequiredService<IChatCompletionService>();

        var history = new ChatHistory();
        // Gemma 4 best practice: image before text.
        history.Add(new ChatMessageContent(AuthorRole.User,
            new ChatMessageContentItemCollection
            {
                new ImageContent(imageData, mimeType),
                new TextContent(userMessage)
            }));

        var results = await chat.GetChatMessageContentsAsync(
            history, BuildSettings(), cancellationToken: cancellationToken);
        return results[^1].Content ?? string.Empty;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Audio is only supported by Gemma 4 E2B, E4B, and 12B models.
    /// The 26B A4B MoE and 31B Dense models do not have an audio encoder.
    /// Maximum audio length is 30 seconds.
    /// </remarks>
    public async Task<string> ChatWithAudioAsync(
        string userMessage,
        ReadOnlyMemory<byte> audioData,
        string mimeType = "audio/wav",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userMessage);
        if (audioData.IsEmpty)
            throw new ArgumentException("Audio data must not be empty.", nameof(audioData));

        var kernel = CreateKernel();
        var chat = kernel.GetRequiredService<IChatCompletionService>();

        var history = new ChatHistory();
        // Gemma 4 best practice: text before audio.
        history.Add(new ChatMessageContent(AuthorRole.User,
            new ChatMessageContentItemCollection
            {
                new TextContent(userMessage),
                new AudioContent(audioData, mimeType)
            }));

        var results = await chat.GetChatMessageContentsAsync(
            history, BuildSettings(), cancellationToken: cancellationToken);
        return results[^1].Content ?? string.Empty;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The <paramref name="kernel"/> must have been created with <see cref="CreateKernel"/>
    /// so that the Gemma 4 chat service is registered. Add your plugins before calling:
    /// <code>
    /// var kernel = service.CreateKernel();
    /// kernel.Plugins.AddFromObject(new BuiltInFileTools(fileAgent));
    /// var answer = await service.ChatWithToolsAsync("...", kernel);
    /// </code>
    /// SK automatically invokes kernel functions when Gemma 4 returns a tool-call
    /// response, feeding results back until a final text answer is produced.
    /// </remarks>
    public async Task<string> ChatWithToolsAsync(
        string userMessage,
        Kernel kernel,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userMessage);
        ArgumentNullException.ThrowIfNull(kernel);

        var chat = kernel.GetRequiredService<IChatCompletionService>();

        var history = new ChatHistory();
        history.AddUserMessage(userMessage);

        var settings = BuildSettings(autoInvokeTools: true);
        var results = await chat.GetChatMessageContentsAsync(
            history, settings, kernel, cancellationToken);
        return results[^1].Content ?? string.Empty;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private OpenAIPromptExecutionSettings BuildSettings(bool autoInvokeTools = false) =>
        new()
        {
            MaxTokens = _options.MaxTokens,
            Temperature = _options.Temperature,
            TopP = _options.TopP,
            FunctionChoiceBehavior = autoInvokeTools
                ? FunctionChoiceBehavior.Auto()
                : null
        };
}

#pragma warning restore SKEXP0001
