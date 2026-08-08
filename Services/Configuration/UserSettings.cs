namespace Services.Configuration;

/// <summary>
/// Serializable snapshot of everything the Settings page can change at runtime, so a tuned setup
/// survives an app restart. Deliberately a flat DTO rather than the live services themselves:
/// the values are written by the UI, read back at startup, and must stay readable even when a
/// future version adds or removes a knob (missing members simply keep their default).
/// <para>
/// Persisted by <see cref="UserSettingsStore"/>, by default to
/// <c>%AppData%\OfflineAI\settings.json</c> — the same place
/// <c>WorkspaceService</c> keeps the workspace list.
/// </para>
/// </summary>
public sealed class UserSettings
{
    /// <summary>LLM sampling/timeout settings for the classic (pooled) backend.</summary>
    public GenerationUserSettings Generation { get; set; } = new();

    /// <summary>Agent Mode (goal agent) settings.</summary>
    public AgentUserSettings Agent { get; set; } = new();

    /// <summary>
    /// Gemma 4 CLI backend settings. Kept as a plain DTO because the options type itself lives in
    /// the AI project, which references this one — the dashboard maps between the two.
    /// </summary>
    public Gemma4UserSettings Gemma4 { get; set; } = new();
}

/// <summary>Mirror of the tunable parts of <see cref="GenerationSettingsService"/>.</summary>
public sealed class GenerationUserSettings
{
    public double Temperature { get; set; } = 0.3;
    public int MaxTokens { get; set; } = 128000;
    public int TopK { get; set; } = 40;
    public double TopP { get; set; } = 0.95;
    public double RepeatPenalty { get; set; } = 1.1;
    public double PresencePenalty { get; set; }
    public double FrequencyPenalty { get; set; }
    public int TimeoutSeconds { get; set; } = 300;
    public int PauseTimeoutSeconds { get; set; } = 10;
    public bool RagMode { get; set; } = true;
    public bool PerformanceMetrics { get; set; }
    public bool DebugMode { get; set; }
    public int RagTopK { get; set; } = 3;
    public double RagMinRelevanceScore { get; set; } = 0.5;
    public bool UseGpu { get; set; } = true;
    public int GpuLayers { get; set; } = 99;
}

/// <summary>Mirror of <see cref="AgentSettingsService"/>.</summary>
public sealed class AgentUserSettings
{
    public int MaxIterations { get; set; } = AgentSettingsService.DefaultMaxIterations;
    public double VerificationTemperature { get; set; } = AgentSettingsService.DefaultVerificationTemperature;
    public bool RequireApproval { get; set; } = true;
    public int StallLimit { get; set; } = AgentSettingsService.DefaultStallLimit;
}

/// <summary>
/// Mirror of the per-call knobs on the AI project's <c>Gemma4CliOptions</c>. Only values that
/// take effect on the next call are included: every Gemma 4 request spawns a fresh
/// <c>llama-completion</c> process, so sampling, context size, GPU layers and timeouts can all
/// change without a restart. Paths and the chat template cannot, and are not listed here.
/// </summary>
public sealed class Gemma4UserSettings
{
    public double Temperature { get; set; } = 1.0;
    public int MaxTokens { get; set; } = 2048;
    public double TopP { get; set; } = 0.95;
    public int TopK { get; set; } = 64;
    public int ContextSize { get; set; } = 32768;
    public int GpuLayers { get; set; }
    public int TimeoutMs { get; set; } = 120000;
    public int PauseTimeoutMs { get; set; } = 10000;
}
