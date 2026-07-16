using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Factories;
using Factories.Extensions;
using Services.AgentTools;

namespace Application.AI.Gemma4;

/// <summary>
/// Subprocess-based Gemma 4 service. Each public method:
/// <list type="number">
///   <item>Builds a Gemma 4 chat-template prompt.</item>
///   <item>Writes it to a temp file and spawns <c>llama-cli -f &lt;file&gt;</c>.</item>
///   <item>Streams stdout, applying pause-timeout logic identical to
///         <see cref="Application.AI.Processing.PersistentLlmProcess"/>.</item>
///   <item>Extracts the response by finding the last
///         <c>&lt;start_of_turn&gt;model</c> marker in the collected output.</item>
/// </list>
/// </summary>
public sealed class Gemma4CliService : IGemma4CliService
{
    private readonly Gemma4CliOptions _options;

    // Gemma 4 chat-template tokens
    private const string TurnStart = "<start_of_turn>";
    private const string TurnEnd   = "<end_of_turn>";
    private const string ModelTurn = "<start_of_turn>model";

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
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userMessage);

        var prompt = BuildGemma4Prompt([(Role.User, userMessage)]);
        return RunAsync(prompt, imagePath: null, cancellationToken);
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

        var prompt = BuildGemma4Prompt([(Role.User, userMessage)]);
        return RunAsync(prompt, imagePath, cancellationToken);
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

        var tools = toolRegistry.GetTools();
        var toolsJson = SerializeToolDefinitions(tools);
        var firstUserMessage = BuildToolUserMessage(userMessage, toolsJson);

        var turns = new List<(string Role, string Content)>
        {
            (Role.User, firstUserMessage)
        };

        for (var i = 0; i < _options.MaxToolCallIterations; i++)
        {
            var prompt = BuildGemma4Prompt(turns);
            var response = await RunAsync(prompt, imagePath: null, cancellationToken);

            var toolCalls = TryParseToolCalls(response);
            if (toolCalls is null || toolCalls.Count == 0)
                return response; // plain-text answer — done

            // Execute each requested tool
            var results = new List<ToolCallResult>();
            foreach (var call in toolCalls)
            {
                var result = await toolRegistry.InvokeAsync(call.Name, call.Arguments);
                results.Add(new ToolCallResult(call.Id, result));
            }

            // Append the model turn (JSON tool-call) and the tool-result turn
            turns.Add((Role.Model, response));
            turns.Add((Role.Tool,  SerializeToolResults(results)));
        }

        // Exhausted iterations — request a final plain-text answer
        var finalPrompt = BuildGemma4Prompt(turns);
        return await RunAsync(finalPrompt, imagePath: null, cancellationToken);
    }

    // ── Core execution ───────────────────────────────────────────────────────

    private async Task<string> RunAsync(
        string prompt,
        string? imagePath,
        CancellationToken cancellationToken)
    {
        var tempPromptFile = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            await File.WriteAllTextAsync(tempPromptFile, prompt, Encoding.UTF8, cancellationToken);

            var psi = LlmFactory.CreateForLlama(_options.LlamaCliPath, _options.ModelPath);

            // Prompt file (avoids shell-quoting issues with special characters)
            psi.Arguments += $" -f \"{tempPromptFile}\"";

            // Force plain text-completion mode. Newer llama-cli builds default to interactive
            // conversation mode for chat-template models (Gemma included): the process prints
            // a startup banner and "> " prompt and waits on stdin instead of completing the -f
            // prompt and exiting. This class hand-builds the Gemma chat template itself and
            // extracts the reply from the echoed <start_of_turn>model marker, which only works
            // in completion mode. Same fix as PersistentLlmProcess.QueryAsync.
            psi.Arguments += " -no-cnv";

            // Context, tokens, sampling. The floating-point flags MUST be formatted with the
            // invariant culture: under a locale that uses a decimal comma (e.g. sv-SE) the default
            // interpolation emits "--temp 0,30", and llama.cpp's C++ float parser stops at the
            // comma and reads it as 0 — silently forcing temp/top-p to zero.
            psi.Arguments += $" -c {_options.ContextSize}";
            psi.Arguments += $" -n {_options.MaxTokens}";
            psi.Arguments += string.Create(CultureInfo.InvariantCulture, $" --temp {_options.Temperature:F2}");
            psi.Arguments += string.Create(CultureInfo.InvariantCulture, $" --top-p {_options.TopP:F2}");
            psi.Arguments += $" --top-k {_options.TopK}";

            // GPU offloading
            psi.Arguments += $" -ngl {_options.GpuLayers}";

            // Multimodal image (all Gemma 4 GGUF sizes support vision)
            if (imagePath != null)
                psi.Arguments += $" --image \"{imagePath}\"";

            return await ExecuteAndExtractAsync(psi.Build(), cancellationToken);
        }
        finally
        {
            DeleteTemp(tempPromptFile);
        }
    }

    /// <summary>
    /// Streams stdout from the process, tracks the pause-timeout, then extracts
    /// Gemma 4's response from the collected output.
    /// </summary>
    private async Task<string> ExecuteAndExtractAsync(Process process, CancellationToken cancellationToken)
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

        // stderr carries llama-cli's model-loading/prompt-eval progress while stdout stays
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

        // Poll with pause-timeout and hard timeout, matching PersistentLlmProcess
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

        return ExtractGemma4Response(fullOutput.ToString());
    }

    // ── Gemma 4 prompt builder ────────────────────────────────────────────────

    /// <summary>
    /// Formats a conversation history using Gemma 4's chat template.
    /// Appends a bare <c>&lt;start_of_turn&gt;model\n</c> to prime generation.
    /// </summary>
    private static string BuildGemma4Prompt(IEnumerable<(string Role, string Content)> turns)
    {
        var sb = new StringBuilder();
        foreach (var (role, content) in turns)
        {
            sb.Append(TurnStart).Append(role).Append('\n');
            sb.Append(content);
            sb.Append(TurnEnd).Append('\n');
        }
        // Prime the model to start generating
        sb.Append(ModelTurn).Append('\n');
        return sb.ToString();
    }

    // ── Response extraction ───────────────────────────────────────────────────

    /// <summary>
    /// Finds the <em>last</em> <c>&lt;start_of_turn&gt;model</c> in the raw output
    /// (the echo of the full prompt + generated continuation) and returns everything
    /// after it, stripped of the trailing <c>&lt;end_of_turn&gt;</c>.
    /// Using <c>LastIndexOf</c> correctly handles multi-turn tool-call conversations
    /// where earlier model turns are part of the echoed prompt.
    /// </summary>
    private static string ExtractGemma4Response(string rawOutput)
    {
        var idx = rawOutput.LastIndexOf(ModelTurn, StringComparison.Ordinal);
        if (idx < 0)
        {
            // Fallback when the marker never appeared in the output: return what's left after
            // stripping llama-cli's own startup banner/UI lines, so the CLI's banner is never
            // handed to the caller as if the model had said it.
            return Application.AI.Processing.LlmOutputPatterns.StripCliNoise(rawOutput);
        }

        var afterMarker = rawOutput[(idx + ModelTurn.Length)..].TrimStart('\r', '\n');

        var endIdx = afterMarker.IndexOf(TurnEnd, StringComparison.Ordinal);
        if (endIdx >= 0)
            afterMarker = afterMarker[..endIdx];

        return CleanModelArtifacts(afterMarker);
    }

    /// <summary>
    /// Strips the non-content artifacts this Gemma 4 GGUF prints in completion mode: the
    /// reasoning-channel delimiters (<c>&lt;|channel&gt;</c>/<c>&lt;channel|&gt;</c> and the bare
    /// "thought" channel-name line) and llama.cpp's own <c>[end of text]</c> end-of-generation
    /// notice. These would otherwise lead the reply and, for the chat UI, be shown verbatim.
    /// The verdict/requirement/tool parsers are line-based and tolerate the noise, but a clean
    /// reply is nicer and keeps the reviewer's "starts with RESULTAT:" contract intact.
    /// </summary>
    private static string CleanModelArtifacts(string text)
    {
        var cleaned = text
            .Replace("<|channel>", string.Empty, StringComparison.Ordinal)
            .Replace("<channel|>", string.Empty, StringComparison.Ordinal)
            .Replace("[end of text]", string.Empty, StringComparison.Ordinal);

        // Drop a leading, standalone "thought" channel-name line if one is left behind.
        var lines = cleaned.Split('\n').ToList();
        while (lines.Count > 0 &&
               (lines[0].Trim().Length == 0 ||
                string.Equals(lines[0].Trim(), "thought", StringComparison.OrdinalIgnoreCase)))
        {
            lines.RemoveAt(0);
        }

        return string.Join("\n", lines).Trim();
    }

    // ── Tool calling helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Builds the initial user message that includes tool definitions and
    /// instructions for the JSON tool-call response format.
    /// </summary>
    private static string BuildToolUserMessage(string userMessage, string toolsJson)
    {
        const string example =
            "[{\"id\":\"0\",\"type\":\"function\",\"function\":{\"name\":\"tool_name\",\"arguments\":{\"param\":\"value\"}}}]";

        return
            "You have access to the following tools. When you need to use one, respond " +
            "with ONLY a JSON array in this exact format (no other text on that turn):\n" +
            example + "\n\n" +
            $"Available tools:\n{toolsJson}\n\n" +
            userMessage;
    }

    /// <summary>
    /// Serializes <see cref="AgentTool"/> definitions to the JSON Schema format
    /// that Gemma 4's native function-calling understands.
    /// </summary>
    private static string SerializeToolDefinitions(IReadOnlyList<AgentTool> tools)
    {
        var defs = tools.Select(t => new
        {
            name        = t.Name,
            description = t.Description,
            parameters  = new
            {
                type       = "object",
                properties = t.Parameters.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new { type = kvp.Value.Type, description = kvp.Value.Description }),
                required   = t.Parameters
                              .Where(kvp => kvp.Value.Required)
                              .Select(kvp => kvp.Key)
                              .ToArray()
            }
        });

        return JsonSerializer.Serialize(defs);
    }

    /// <summary>
    /// Tries to parse a Gemma 4 tool-call JSON array from the model response.
    /// Returns <c>null</c> when the response is a plain-text answer, not a tool call.
    /// </summary>
    private static List<ToolCallInfo>? TryParseToolCalls(string response)
    {
        var trimmed = response.Trim();
        if (!trimmed.StartsWith('['))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return null;

            var calls = new List<ToolCallInfo>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (!element.TryGetProperty("function", out var func)) continue;
                if (!func.TryGetProperty("name", out var nameEl)) continue;

                var id   = element.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "0" : "0";
                var name = nameEl.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name)) continue;

                var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (func.TryGetProperty("arguments", out var argsEl))
                {
                    foreach (var prop in argsEl.EnumerateObject())
                        args[prop.Name] = prop.Value.ToString();
                }

                calls.Add(new ToolCallInfo(id, name, args));
            }

            return calls.Count > 0 ? calls : null;
        }
        catch (JsonException)
        {
            return null; // response was not valid JSON — treat as plain-text answer
        }
    }

    private static string SerializeToolResults(IReadOnlyList<ToolCallResult> results)
    {
        var docs = results.Select(r => new { id = r.Id, result = r.Result });
        return JsonSerializer.Serialize(docs);
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

    // ── Private record types ──────────────────────────────────────────────────

    private sealed record ToolCallInfo(
        string Id,
        string Name,
        IReadOnlyDictionary<string, string> Arguments);

    private sealed record ToolCallResult(string Id, string Result);

    // Role constants matching Gemma 4's chat template
    private static class Role
    {
        public const string User  = "user";
        public const string Model = "model";
        public const string Tool  = "tool";
    }
}
