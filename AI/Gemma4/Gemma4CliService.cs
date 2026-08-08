using System.Diagnostics;
using System.Globalization;
using System.Text;
using Factories;
using Factories.Extensions;
using Services.AgentTools;

namespace Application.AI.Gemma4;

/// <summary>
/// Subprocess-based Gemma 4 service. Each public method:
/// <list type="number">
///   <item>Builds a Gemma 4 chat-template prompt (<c>&lt;|turn&gt;</c> tokens).</item>
///   <item>Writes it to a temp file and spawns <c>llama-completion -f &lt;file&gt;</c>.</item>
///   <item>Streams stdout, applying pause-timeout logic identical to
///         <see cref="Application.AI.Processing.PersistentLlmProcess"/>.</item>
///   <item>Extracts the response by finding the last <c>&lt;|turn&gt;model</c> marker
///         and taking the text after the final <c>&lt;channel|&gt;</c>.</item>
/// </list>
/// <para>
/// <b>The <c>-sp</c> flag is load-bearing.</b> Gemma 4's turn tokens are real special tokens:
/// they are consumed at tokenization and do <em>not</em> echo back on stdout by default, so the
/// <c>&lt;|turn&gt;model</c> marker this class keys off would be invisible and extraction would
/// silently fall through to <see cref="Application.AI.Processing.LlmOutputPatterns.StripCliNoise"/>.
/// <c>-sp</c> turns special-token output on. Template and flag must change together.
/// </para>
/// <para>
/// Verified against <c>gemma-4-12b-it-Q4_K_S.gguf</c> (2026-07-17). See
/// <see href="https://ai.google.dev/gemma/docs/core/prompt-formatting-gemma4">Gemma 4 Prompt Formatting</see>.
/// Note that <c>ai.google.dev/gemma/docs/core/prompt-structure</c> is a live page documenting the
/// <em>older</em> <c>&lt;start_of_turn&gt;</c> format — it does not apply to Gemma 4.
/// </para>
/// </summary>
public sealed class Gemma4CliService : IGemma4CliService
{
    private readonly Gemma4CliOptions _options;

    // ── Gemma 4 chat-template tokens ──────────────────────────────────────────
    // Verified empirically; these are special tokens in the model's vocab, not literal text.
    private const string TurnStart = "<|turn>";
    private const string TurnEnd   = "<turn|>";
    private const string ModelTurn = "<|turn>model";

    /// <summary>Placed at the start of the system turn to enable chain-of-thought.</summary>
    private const string ThinkToken = "<|think|>";

    /// <summary>
    /// Opens the reasoning channel (<c>&lt;|channel&gt;thought</c>). Sometimes doubled. Present so
    /// an <em>unclosed</em> channel — opener emitted, generation ended before <see cref="ChannelEnd"/>
    /// — can be stripped instead of leaking the raw special token into the answer.
    /// </summary>
    private const string ChannelStart = "<|channel>";

    /// <summary>
    /// Closes the reasoning channel. Everything after the final occurrence is the answer.
    /// Sizes above E4B emit the channel even when thinking is off — with an empty block.
    /// </summary>
    private const string ChannelEnd = "<channel|>";

    // Native function-calling tokens.
    private const string ToolDefStart      = "<|tool>";
    private const string ToolDefEnd        = "<tool|>";
    private const string ToolCallStart     = "<|tool_call>";
    private const string ToolCallEnd       = "<tool_call|>";
    private const string ToolResponseStart = "<|tool_response>";
    private const string ToolResponseEnd   = "<tool_response|>";

    /// <summary>Delimits string values inside tool-call argument lists, so values may contain commas.</summary>
    private const string StringDelim = "<|\"|>";

    /// <summary>llama.cpp's own end-of-generation notice; not model output.</summary>
    private const string EndOfTextNotice = "[end of text]";

    /// <summary>
    /// UTF-8 <b>without</b> a byte-order mark.
    /// <para>
    /// <see cref="Encoding.UTF8"/> emits a BOM, which lands at the head of the <c>-f</c> prompt
    /// file — directly in front of the first <c>&lt;|turn&gt;</c> token. Plain chat survives that,
    /// but native tool calling does not: with a BOM present the model stops emitting
    /// <c>&lt;|tool_call&gt;</c> and <em>invents a plausible answer instead</em> (verified — it
    /// reported a fabricated temperature rather than calling the weather tool). A silent
    /// hallucination is the worst possible failure mode, so never hand a BOM to llama.cpp.
    /// </para>
    /// </summary>
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public Gemma4CliService(Gemma4CliOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        if (string.IsNullOrWhiteSpace(options.ModelPath))
            throw new ArgumentException("ModelPath must not be empty.", nameof(options));
    }

    /// <inheritdoc/>
    public string ModelName => Path.GetFileName(_options.ModelPath);

    // ── Public API ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<string> ChatAsync(string userMessage, CancellationToken cancellationToken = default)
        => ChatAsync(userMessage, temperatureOverride: null, cancellationToken);

    /// <inheritdoc/>
    public Task<string> ChatAsync(
        string userMessage,
        double? temperatureOverride,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userMessage);

        var prompt = BuildPrompt(BuildTurns(userMessage, toolDefinitions: null));
        return RunAndExtractAsync(prompt, imagePath: null, cancellationToken, temperatureOverride);
    }

    /// <inheritdoc/>
    public Task<string> ChatWithImageAsync(
        string userMessage,
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userMessage);
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);

        if (!File.Exists(imagePath))
            throw new FileNotFoundException("Image file not found.", imagePath);

        var prompt = BuildPrompt(BuildTurns(userMessage, toolDefinitions: null));
        return RunAndExtractAsync(prompt, imagePath, cancellationToken);
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

        var ext = MimeTypeToExtension(mimeType);
        var tempImage = Path.ChangeExtension(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()), ext);
        try
        {
            await File.WriteAllBytesAsync(tempImage, imageData.ToArray(), cancellationToken);
            return await ChatWithImageAsync(userMessage, tempImage, cancellationToken);
        }
        finally
        {
            DeleteTemp(tempImage);
        }
    }

    /// <inheritdoc/>
    public async Task<string> ChatWithToolsAsync(
        string userMessage,
        IAgentToolRegistry toolRegistry,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userMessage);
        ArgumentNullException.ThrowIfNull(toolRegistry);

        var toolDefinitions = SerializeToolDefinitions(toolRegistry.GetTools());

        // Gemma 4's tool loop is a single growing completion, not a rebuilt turn list: the model
        // emits <|tool_call>…<tool_call|><|tool_response> and stops there (<|tool_response> is in
        // the model's EOG set), we append the result payload, and generation resumes from that
        // exact point. So the prompt prefix is fixed and only the model region grows.
        var promptPrefix = BuildPrompt(BuildTurns(userMessage, toolDefinitions));
        var modelRegion  = string.Empty;

        for (var i = 0; i < _options.MaxToolCallIterations; i++)
        {
            var raw = await RunAsync(promptPrefix + modelRegion, imagePath: null, cancellationToken);

            modelRegion = ExtractModelRegion(raw);
            if (modelRegion is null)
                return Application.AI.Processing.LlmOutputPatterns.StripCliNoise(raw);

            var call = TryParseLastToolCall(modelRegion);
            if (call is null)
                return ExtractAnswer(modelRegion);   // plain-text answer — done

            var result = await toolRegistry.InvokeAsync(call.Name, call.Arguments);
            modelRegion = AppendToolResponse(modelRegion, call.Name, result);
        }

        // Iteration cap reached — ask for a final plain-text answer instead of returning nothing.
        var finalRaw = await RunAsync(promptPrefix + modelRegion, imagePath: null, cancellationToken);
        var finalRegion = ExtractModelRegion(finalRaw);

        return finalRegion is null
            ? Application.AI.Processing.LlmOutputPatterns.StripCliNoise(finalRaw)
            : ExtractAnswer(finalRegion);
    }

    // ── Prompt construction ──────────────────────────────────────────────────

    /// <summary>
    /// Builds the turn list for a single-shot exchange. A system turn is emitted when there is a
    /// system prompt, tool definitions, or thinking to enable — Gemma 4 has a native system role
    /// (Gemma 3 did not, which is why older code folded the system prompt into the user turn).
    /// </summary>
    private List<(string Role, string Content)> BuildTurns(string userMessage, string? toolDefinitions)
    {
        var turns = new List<(string Role, string Content)>();

        var system = BuildSystemContent(toolDefinitions);
        if (system.Length > 0)
            turns.Add((Role.System, system));

        turns.Add((Role.User, userMessage));
        return turns;
    }

    private string BuildSystemContent(string? toolDefinitions)
    {
        var sb = new StringBuilder();

        // Thinking is opt-in and must lead the system turn.
        if (_options.EnableThinking)
            sb.Append(ThinkToken);

        if (!string.IsNullOrWhiteSpace(_options.SystemPrompt))
            sb.Append(_options.SystemPrompt);

        if (!string.IsNullOrWhiteSpace(toolDefinitions))
        {
            if (sb.Length > 0)
                sb.Append('\n');
            sb.Append(toolDefinitions);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Formats a conversation using Gemma 4's chat template and appends a bare
    /// <c>&lt;|turn&gt;model\n</c> to prime generation.
    /// <para>
    /// Deliberately does <b>not</b> prepend <c>&lt;bos&gt;</c>: llama.cpp adds it automatically
    /// for <c>-f</c> prompt files, and adding it here would double it.
    /// </para>
    /// </summary>
    private static string BuildPrompt(IEnumerable<(string Role, string Content)> turns)
    {
        var sb = new StringBuilder();
        foreach (var (role, content) in turns)
        {
            sb.Append(TurnStart).Append(role).Append('\n');
            sb.Append(content);
            sb.Append(TurnEnd).Append('\n');
        }
        sb.Append(ModelTurn).Append('\n');
        return sb.ToString();
    }

    // ── Core execution ───────────────────────────────────────────────────────

    private async Task<string> RunAndExtractAsync(
        string prompt,
        string? imagePath,
        CancellationToken cancellationToken,
        double? temperatureOverride = null)
    {
        var raw = await RunAsync(prompt, imagePath, cancellationToken, temperatureOverride);
        var region = ExtractModelRegion(raw);

        return region is null
            ? Application.AI.Processing.LlmOutputPatterns.StripCliNoise(raw)
            : ExtractAnswer(region);
    }

    private async Task<string> RunAsync(
        string prompt,
        string? imagePath,
        CancellationToken cancellationToken,
        double? temperatureOverride = null)
    {
        var tempPromptFile = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            await File.WriteAllTextAsync(tempPromptFile, prompt, Utf8NoBom, cancellationToken);

            var psi = LlmFactory.CreateForLlama(_options.LlamaCliPath, _options.ModelPath);

            // Prompt file (avoids shell-quoting issues with special characters)
            psi.Arguments += $" -f \"{tempPromptFile}\"";

            // Force plain text-completion mode. Newer builds default to interactive conversation
            // mode for chat-template models: the process prints a banner and "> " prompt and waits
            // on stdin instead of completing the -f prompt and exiting. This class hand-builds the
            // chat template and extracts the reply from the echoed marker, which only works in
            // completion mode. Requires llama-completion — llama-cli rejects -no-cnv, prints that
            // warning to stdout, and runs in conversation mode anyway.
            psi.Arguments += " -no-cnv";

            // Print special tokens. Without this, Gemma 4's <|turn> tokens are consumed at
            // tokenization and never echo, so ExtractModelRegion finds no marker and every call
            // degrades to the StripCliNoise fallback. See the class remarks.
            psi.Arguments += " -sp";

            // Context, tokens, sampling. The floating-point flags MUST be formatted with the
            // invariant culture: under a locale that uses a decimal comma (e.g. sv-SE) the default
            // interpolation emits "--temp 0,30", and llama.cpp's C++ float parser stops at the
            // comma and reads it as 0 — silently forcing temp/top-p to zero.
            psi.Arguments += $" -c {_options.ContextSize}";
            psi.Arguments += $" -n {_options.MaxTokens}";
            psi.Arguments += string.Create(CultureInfo.InvariantCulture, $" --temp {temperatureOverride ?? _options.Temperature:F2}");
            psi.Arguments += string.Create(CultureInfo.InvariantCulture, $" --top-p {_options.TopP:F2}");
            psi.Arguments += $" --top-k {_options.TopK}";

            // GPU offloading. --device must come with -ngl: it restricts *which* devices are
            // offload targets but doesn't offload anything by itself.
            psi.Arguments += $" -ngl {_options.GpuLayers}";
            if (!string.IsNullOrWhiteSpace(_options.Device))
                psi.Arguments += $" --device {_options.Device}";

            // Multimodal image
            if (imagePath != null)
                psi.Arguments += $" --image \"{imagePath}\"";

            return await ExecuteAsync(psi.Build(), cancellationToken);
        }
        finally
        {
            DeleteTemp(tempPromptFile);
        }
    }

    /// <summary>
    /// Streams stdout from the process, tracking the pause-timeout, and returns the raw output.
    /// </summary>
    private async Task<string> ExecuteAsync(Process process, CancellationToken cancellationToken)
    {
        var fullOutput   = new StringBuilder();
        var lastOutput   = DateTime.UtcNow;
        var processStart = DateTime.UtcNow;
        var outputLock   = new object();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (outputLock)
            {
                lastOutput = DateTime.UtcNow;
                fullOutput.AppendLine(e.Data);
            }
        };

        // stderr carries llama.cpp's model-loading/prompt-eval progress while stdout stays
        // silent — treat it as liveness so the pause timeout doesn't kill a cold process
        // mid-load (which would return only the startup banner). The content itself is
        // informational noise (ggml_cuda_init, load_backend, etc.) and is not captured.
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (outputLock)
            {
                lastOutput = DateTime.UtcNow;
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        while (!process.HasExited && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(500, CancellationToken.None);

            var now = DateTime.UtcNow;
            if ((now - lastOutput).TotalMilliseconds > _options.PauseTimeoutMs)
                break;
            if ((now - processStart).TotalMilliseconds > _options.TimeoutMs)
                break;
        }

        if (!process.HasExited)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already exited */ }
        }

        await process.WaitForExitAsync();
        process.Dispose();

        // Small delay so the last OutputDataReceived events can flush
        await Task.Delay(200, CancellationToken.None);

        // The loop above exits on cancellation and the process has just been killed, so whatever
        // was captured is a truncated generation — never a real answer. Throwing (after the
        // cleanup, so no process is left behind) is what lets a caller distinguish "the user
        // stopped this" from "the model replied", instead of feeding half a reply into a parser.
        cancellationToken.ThrowIfCancellationRequested();

        lock (outputLock)
        {
            return fullOutput.ToString();
        }
    }

    // ── Response extraction ───────────────────────────────────────────────────

    /// <summary>
    /// Returns everything the model generated after the <em>last</em> <c>&lt;|turn&gt;model</c>
    /// marker in the raw output, with llama.cpp's <c>[end of text]</c> notice removed.
    /// Returns <c>null</c> when the marker never appeared — which means something went wrong
    /// (wrong binary, missing <c>-sp</c>, process killed during load), not that the model was quiet.
    /// <para>
    /// <c>LastIndexOf</c> is required: during a tool loop the prompt echo already contains the
    /// model turn plus every earlier generation, and <c>IndexOf</c> would re-read the first one
    /// on every iteration — the classic symptom being an agent that repeats itself forever.
    /// </para>
    /// </summary>
    internal static string? ExtractModelRegion(string rawOutput)
    {
        var idx = rawOutput.LastIndexOf(ModelTurn, StringComparison.Ordinal);
        if (idx < 0)
            return null;

        return rawOutput[(idx + ModelTurn.Length)..]
            .Replace(EndOfTextNotice, string.Empty, StringComparison.Ordinal)
            .TrimStart('\r', '\n');
    }

    /// <summary>
    /// Reduces a model region to the user-facing answer: drops the reasoning channel (everything
    /// up to and including the final <c>&lt;channel|&gt;</c>) and cuts at the closing
    /// <c>&lt;turn|&gt;</c>.
    /// <para>
    /// Every size above E4B emits <c>&lt;|channel&gt;thought … &lt;channel|&gt;</c> even when
    /// thinking is disabled — with an empty block — so this is the normal path, not a special case.
    /// The opening marker is sometimes doubled (<c>&lt;|channel&gt;&lt;|channel&gt;thought</c>),
    /// which is why the closed case keys off the closing token only.
    /// </para>
    /// <para>
    /// When the channel is <em>unclosed</em> — Gemma opened <c>&lt;|channel&gt;thought</c> but
    /// generation ended (empty/thinking-only reply, token budget, killed process) before the
    /// closing <c>&lt;channel|&gt;</c> — there is no answer, only unterminated reasoning. The
    /// same applies when the model closes its channel, answers, and then <em>reopens</em> a
    /// thought channel it never closes. In both cases the text is cut at the opener rather than
    /// leaking the raw <c>&lt;|channel&gt;</c> special token downstream, where e.g. the
    /// goal-agent verdict parser would choke on it and log a bogus "kunde inte tolkas" verdict
    /// instead of an honest empty reply.
    /// </para>
    /// <para>
    /// Native tool tokens get the same treatment (also observed leaking into goal-agent
    /// verdicts): in a finished tool loop the user-facing answer is whatever follows the last
    /// serviced <c>&lt;tool_response|&gt;</c>, and a <em>dangling</em> <c>&lt;|tool_call&gt;</c>
    /// — the model calling a tool in a flow with nothing wired to service it — is not an answer
    /// at all, so the text is cut where it starts.
    /// </para>
    /// </summary>
    internal static string ExtractAnswer(string modelRegion)
    {
        var text = modelRegion;

        var channelIdx = text.LastIndexOf(ChannelEnd, StringComparison.Ordinal);
        if (channelIdx >= 0)
            text = text[(channelIdx + ChannelEnd.Length)..];

        var responseIdx = text.LastIndexOf(ToolResponseEnd, StringComparison.Ordinal);
        if (responseIdx >= 0)
            text = text[(responseIdx + ToolResponseEnd.Length)..];

        // Anything from a dangling tool call, a stray tool-response opener, or a (re)opened
        // reasoning channel onward is not part of the answer — cut at whichever comes first.
        foreach (var marker in new[] { ToolCallStart, ToolResponseStart, ChannelStart })
        {
            var markerIdx = text.IndexOf(marker, StringComparison.Ordinal);
            if (markerIdx >= 0)
                text = text[..markerIdx];
        }

        var endIdx = text.IndexOf(TurnEnd, StringComparison.Ordinal);
        if (endIdx >= 0)
            text = text[..endIdx];

        return text.Trim();
    }

    // ── Tool calling ──────────────────────────────────────────────────────────

    /// <summary>
    /// Renders tool definitions in Gemma 4's native form, one per line:
    /// <c>&lt;|tool&gt;name{param:type,param2:type}: description&lt;tool|&gt;</c>.
    /// <para>
    /// The description is not documentation — it is the only signal the model has when choosing
    /// between tools, so it is emitted verbatim into the system turn.
    /// </para>
    /// </summary>
    private static string SerializeToolDefinitions(IReadOnlyList<AgentTool> tools)
    {
        var sb = new StringBuilder();

        foreach (var tool in tools)
        {
            sb.Append(ToolDefStart).Append(tool.Name).Append('{');
            sb.AppendJoin(',', tool.Parameters.Select(p => $"{p.Key}:{p.Value.Type}"));
            sb.Append('}');

            if (!string.IsNullOrWhiteSpace(tool.Description))
                sb.Append(": ").Append(tool.Description.ReplaceLineEndings(" "));

            sb.Append(ToolDefEnd).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>
    /// Parses the last <c>&lt;|tool_call&gt;call:name{args}&lt;tool_call|&gt;</c> in the model
    /// region. Returns <c>null</c> when the region holds a plain-text answer instead — which is
    /// the loop's termination signal, not an error.
    /// <para>
    /// A call is only pending if the region ends at <c>&lt;|tool_response&gt;</c>: the model stops
    /// there because that token is in its EOG set. A tool call followed by a response has already
    /// been serviced on an earlier iteration.
    /// </para>
    /// </summary>
    private static ToolCallInfo? TryParseLastToolCall(string modelRegion)
    {
        if (!modelRegion.TrimEnd().EndsWith(ToolResponseStart, StringComparison.Ordinal))
            return null;

        var callIdx = modelRegion.LastIndexOf(ToolCallStart, StringComparison.Ordinal);
        if (callIdx < 0)
            return null;

        var body = modelRegion[(callIdx + ToolCallStart.Length)..];

        var endIdx = body.IndexOf(ToolCallEnd, StringComparison.Ordinal);
        if (endIdx < 0)
            return null;

        body = body[..endIdx].Trim();

        const string callPrefix = "call:";
        if (body.StartsWith(callPrefix, StringComparison.Ordinal))
            body = body[callPrefix.Length..];

        var braceIdx = body.IndexOf('{');
        if (braceIdx < 0)
        {
            var bareName = body.Trim();
            return bareName.Length == 0
                ? null
                : new ToolCallInfo(bareName, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }

        var name = body[..braceIdx].Trim();
        if (name.Length == 0)
            return null;

        var args = body[(braceIdx + 1)..].TrimEnd();
        if (args.EndsWith('}'))
            args = args[..^1];

        return new ToolCallInfo(name, ParseToolArguments(args));
    }

    /// <summary>
    /// Parses a tool-call argument list. Values may be delimited with <c>&lt;|"|&gt;</c> (the
    /// documented form, which lets a value contain commas), with plain double quotes (what the
    /// model actually emits in practice), or be bare — as numbers are. All three are accepted
    /// because the model mixes them.
    /// </summary>
    private static Dictionary<string, string> ParseToolArguments(string args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var i = 0;

        while (i < args.Length)
        {
            var colonIdx = args.IndexOf(':', i);
            if (colonIdx < 0)
                break;

            var key = args[i..colonIdx].Trim().Trim(',').Trim();
            i = colonIdx + 1;

            while (i < args.Length && args[i] == ' ')
                i++;

            string value;
            if (i + StringDelim.Length <= args.Length &&
                string.CompareOrdinal(args, i, StringDelim, 0, StringDelim.Length) == 0)
            {
                var start = i + StringDelim.Length;
                var end   = args.IndexOf(StringDelim, start, StringComparison.Ordinal);
                (value, i) = end < 0 ? (args[start..], args.Length) : (args[start..end], end + StringDelim.Length);
            }
            else if (i < args.Length && args[i] == '"')
            {
                var start = i + 1;
                var end   = args.IndexOf('"', start);
                (value, i) = end < 0 ? (args[start..], args.Length) : (args[start..end], end + 1);
            }
            else
            {
                var end = args.IndexOf(',', i);
                (value, i) = end < 0 ? (args[i..].Trim(), args.Length) : (args[i..end].Trim(), end);
            }

            if (key.Length > 0)
                result[key] = value;

            var nextComma = args.IndexOf(',', i);
            if (nextComma < 0)
                break;
            i = nextComma + 1;
        }

        return result;
    }

    /// <summary>
    /// Completes the pending <c>&lt;|tool_response&gt;</c> the model already opened, so generation
    /// resumes from exactly that point on the next call.
    /// </summary>
    private static string AppendToolResponse(string modelRegion, string toolName, string result)
    {
        var trimmed = modelRegion.TrimEnd();

        // The model emits the opening <|tool_response> itself and stops; only add one if it didn't.
        if (!trimmed.EndsWith(ToolResponseStart, StringComparison.Ordinal))
            trimmed += ToolResponseStart;

        // Strip any delimiter the tool result happens to contain, so it can't close the value early.
        var safeResult = (result ?? string.Empty).Replace(StringDelim, string.Empty, StringComparison.Ordinal);

        return trimmed
             + "response:" + toolName + "{result:" + StringDelim + safeResult + StringDelim + "}"
             + ToolResponseEnd;
    }

    // ── Utility ───────────────────────────────────────────────────────────────

    private static string MimeTypeToExtension(string mimeType) => mimeType switch
    {
        "image/png"  => ".png",
        "image/gif"  => ".gif",
        "image/webp" => ".webp",
        _            => ".jpg"
    };

    private static void DeleteTemp(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore cleanup errors */ }
    }

    // ── Private types ─────────────────────────────────────────────────────────

    private sealed record ToolCallInfo(
        string Name,
        IReadOnlyDictionary<string, string> Arguments);

    /// <summary>
    /// Role names in Gemma 4's chat template. Gemma 4 added the native system role — Gemma 3 had
    /// only user and model, which is why older code folded system prompts into the first user turn.
    /// The model role is spelled out in <see cref="ModelTurn"/>, which is also the extraction marker.
    /// </summary>
    private static class Role
    {
        public const string System = "system";
        public const string User   = "user";
    }
}
