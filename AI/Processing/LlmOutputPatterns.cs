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
        "<|",              // Generic start of special token
        "<|end|>",         // TinyLlama, Phi
        "<|im_end|>",      // ChatML format
        "</s>",            // Llama EOS token
        "<|endoftext|>",   // GPT-style
        "<|user|>",        // Start of next user turn
        "User:",           // Start of next user turn
        "###"              // Some instruction formats
    ];
}
