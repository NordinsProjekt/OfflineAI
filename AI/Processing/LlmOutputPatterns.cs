namespace Application.AI.Processing;

/// <summary>
/// Contains patterns and markers for parsing LLM output from various model formats.
/// </summary>
public static class LlmOutputPatterns
{
    /// <summary>
    /// Patterns to detect where assistant responses start in the output stream.
    /// Order matters - more specific patterns should be checked first to avoid false positives.
    /// Each tuple contains (pattern to search for, marker length to skip).
    /// </summary>
    public static readonly (string Pattern, string Marker)[] AssistantPatterns =
    [
        ("<|start_header_id|>assistant<|end_header_id|>", "<|start_header_id|>assistant<|end_header_id|>"),  // Llama 3.2
        ("<|turn>model", "<|turn>model"),                    // Gemma 4 (requires -sp to appear in output)
        ("<start_of_turn>model", "<start_of_turn>model"),    // Gemma 1-3
        ("<|assistant|>", "<|assistant|>"),                  // TinyLlama, Phi, etc.
        ("<|im_start|>assistant", "<|im_start|>assistant"),  // ChatML format
        ("### Assistant:", "### Assistant:"),                // Some instruction-tuned models
        ("Assistant:", "Assistant:")                         // Mistral, some Llama (check last to avoid false positives)
    ];

    /// <summary>
    /// Markers that indicate the end of assistant output or start of next turn.
    /// Used to clean up responses by removing trailing tokens.
    /// </summary>
    public static readonly string[] EndMarkers =
    [
        "<|eot_id|>",      // Llama 3.2 end of turn
        "<|start_header_id|>", // Llama 3.2 next turn
        "<turn|>",         // Gemma 4 end of turn
        "<end_of_turn>",   // Gemma 1-3 end of turn
        "<|",              // Generic start of special token
        "<|end|>",         // TinyLlama, Phi
        "<|im_end|>",      // ChatML format
        "</s>",            // Llama EOS token
        "<|endoftext|>",   // GPT-style
        "<|user|>",        // Start of next user turn
        "User:",           // Start of next user turn
        "###"              // Some instruction formats
    ];

    /// <summary>
    /// Detects the per-turn performance summary that newer llama-cli builds print on stdout
    /// as soon as a response finishes in conversation mode, e.g.
    /// "[ Prompt: 2201.5 t/s | Generation: 65.7 t/s ]".
    /// Conversation mode applies the model's chat template internally, so none of the
    /// <see cref="AssistantPatterns"/> markers ever appear in the output stream, and the
    /// process stays alive waiting for the next user turn instead of exiting — this stats
    /// line is the only reliable end-of-generation signal in that mode.
    /// </summary>
    public static bool IsTurnStatsLine(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("[", StringComparison.Ordinal)
            && trimmed.Contains("Prompt:", StringComparison.Ordinal)
            && trimmed.Contains("Generation:", StringComparison.Ordinal)
            && trimmed.Contains("t/s", StringComparison.Ordinal);
    }

    /// <summary>
    /// The interactive-help command names newer llama-cli builds list in their startup banner
    /// ("  /exit or Ctrl+C     stop or exit", ...). Only these exact names are treated as
    /// noise — model responses legitimately contain other slash-command lines (the file-agent
    /// tools like /skapa or /lista), which must never be filtered out.
    /// </summary>
    private static readonly string[] CliHelpCommands = ["/exit", "/regen", "/clear", "/read", "/glob"];

    /// <summary>
    /// Labels of the "key : value" lines in the llama-cli startup banner
    /// ("build      : b9878-...", "model      : d:/...", ...).
    /// </summary>
    private static readonly string[] CliBannerLabels = ["build", "model", "ftype", "modalities"];

    /// <summary>
    /// Detects the startup-banner and interactive-UI lines newer llama-cli builds print on
    /// stdout before any generated text: "Loading model...", the block-character ASCII logo,
    /// the "build/model/ftype/modalities :" info lines, the "available commands:" help list,
    /// and the "&gt; " input-prompt echo. Used by <see cref="StripCliNoise"/> so a response
    /// captured without any <see cref="AssistantPatterns"/> marker doesn't hand the raw banner
    /// to the caller as if the model had said it.
    /// </summary>
    public static bool IsCliNoiseLine(string line)
    {
        var trimmed = line.Trim().TrimStart('\uFEFF'); // stray UTF-8 BOM from -f prompt files
        if (trimmed.Length == 0)
            return false;

        if (trimmed.StartsWith("Loading model", StringComparison.Ordinal))
            return true;

        // The ASCII-art logo consists solely of block characters and whitespace.
        if (trimmed.All(c => c is '▄' or '█' or '▀' or ' '))
            return true;

        if (CliBannerLabels.Any(label =>
                trimmed.StartsWith(label, StringComparison.Ordinal)
                && trimmed[label.Length..].TrimStart().StartsWith(':')))
            return true;

        if (trimmed.StartsWith("available commands:", StringComparison.Ordinal))
            return true;

        if (CliHelpCommands.Any(cmd =>
                trimmed.StartsWith(cmd + " ", StringComparison.Ordinal) || trimmed == cmd))
            return true;

        // The interactive input prompt (and its echo of what was fed on stdin).
        return trimmed == ">" || trimmed.StartsWith("> ", StringComparison.Ordinal);
    }

    /// <summary>
    /// Removes every <see cref="IsCliNoiseLine"/> line (and stray UTF-8 BOM characters) from
    /// raw llama-cli output, returning only the lines that could be genuine model text.
    /// Intended for the fallback path where no assistant marker was found in the output.
    /// </summary>
    public static string StripCliNoise(string rawOutput)
    {
        if (string.IsNullOrWhiteSpace(rawOutput))
            return string.Empty;

        var kept = rawOutput
            .Replace("\uFEFF", string.Empty)
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => !IsCliNoiseLine(l));

        return string.Join("\n", kept).Trim();
    }
}
