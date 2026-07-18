namespace Services.QuickAsk;

/// <summary>
/// Predefined MaxTokens values for the QuickAsk token-limit selector.
/// The backing int value is the actual token count passed to the LLM.
/// </summary>
public enum MaxTokensPreset
{
    Tokens2K   = 2048,
    Tokens4K   = 4096,
    Tokens128K = 128000,
    Tokens256K = 256000
}

public static class MaxTokensPresetExtensions
{
    /// <summary>Returns the raw token count for this preset.</summary>
    public static int ToInt(this MaxTokensPreset preset) => (int)preset;

    /// <summary>Returns a short user-friendly label (e.g. "128K tokens").</summary>
    public static string ToLabel(this MaxTokensPreset preset) => preset switch
    {
        MaxTokensPreset.Tokens2K   => "2K tokens",
        MaxTokensPreset.Tokens4K   => "4K tokens",
        MaxTokensPreset.Tokens128K => "128K tokens",
        MaxTokensPreset.Tokens256K => "256K tokens",
        _                          => preset.ToString()
    };
}
