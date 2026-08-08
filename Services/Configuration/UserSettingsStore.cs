using System.Text.Json;

namespace Services.Configuration;

/// <summary>
/// Loads and saves the Settings page's values as JSON, defaulting to
/// <c>%AppData%\OfflineAI\settings.json</c> — same location and same best-effort philosophy as
/// <c>WorkspaceService</c>'s workspace list.
/// <para>
/// Saving is explicit (a button on the Settings page), not per keystroke: the live services apply
/// every edit immediately, so the file is only about surviving a restart. That matters most on the
/// unattended server setup, where a tuned agent configuration would otherwise have to be re-entered
/// by hand after every deploy.
/// </para>
/// <para>
/// A missing, empty, or corrupt file is not an error — <see cref="Load"/> returns <c>null</c> and
/// the app keeps its built-in defaults, exactly as it behaved before any settings were saved.
/// </para>
/// </summary>
public sealed class UserSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <param name="filePath">
    /// Full path to the JSON file. Defaults to <c>%AppData%\OfflineAI\settings.json</c>.
    /// </param>
    public UserSettingsStore(string? filePath = null)
    {
        FilePath = string.IsNullOrWhiteSpace(filePath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OfflineAI", "settings.json")
            : filePath;
    }

    /// <summary>Full path of the file this store reads and writes.</summary>
    public string FilePath { get; }

    /// <summary>True when a settings file exists at <see cref="FilePath"/>.</summary>
    public bool Exists
    {
        get
        {
            try
            {
                return File.Exists(FilePath);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Reads the persisted settings, or returns <c>null</c> when there is nothing usable to read
    /// (no file yet, unreadable, or corrupt JSON). Never throws — a broken settings file must not
    /// stop the app from starting.
    /// </summary>
    public UserSettings? Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return null;

            var json = File.ReadAllText(FilePath);
            return string.IsNullOrWhiteSpace(json)
                ? null
                : JsonSerializer.Deserialize<UserSettings>(json);
        }
        catch (Exception)
        {
            return null; // corrupt/unreadable — fall back to defaults
        }
    }

    /// <summary>
    /// Writes <paramref name="settings"/> to <see cref="FilePath"/>, creating the directory when
    /// needed. Throws on failure so the UI can tell the user the save did not happen — unlike
    /// loading, a silent failure here would look like the settings were kept when they were not.
    /// </summary>
    public void Save(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    /// <summary>
    /// Deletes the settings file so the next start uses the built-in/appsettings defaults again.
    /// Returns false when there was nothing to delete.
    /// </summary>
    public bool Delete()
    {
        if (!File.Exists(FilePath))
            return false;

        File.Delete(FilePath);
        return true;
    }

    /// <summary>Copies the live generation and agent services into a serializable snapshot.</summary>
    public static UserSettings Capture(GenerationSettingsService generation, AgentSettingsService agent)
    {
        ArgumentNullException.ThrowIfNull(generation);
        ArgumentNullException.ThrowIfNull(agent);

        return new UserSettings
        {
            Generation = new GenerationUserSettings
            {
                Temperature = generation.Temperature,
                MaxTokens = generation.MaxTokens,
                TopK = generation.TopK,
                TopP = generation.TopP,
                RepeatPenalty = generation.RepeatPenalty,
                PresencePenalty = generation.PresencePenalty,
                FrequencyPenalty = generation.FrequencyPenalty,
                TimeoutSeconds = generation.TimeoutSeconds,
                PauseTimeoutSeconds = generation.PauseTimeoutSeconds,
                RagMode = generation.RagMode,
                PerformanceMetrics = generation.PerformanceMetrics,
                DebugMode = generation.DebugMode,
                RagTopK = generation.RagTopK,
                RagMinRelevanceScore = generation.RagMinRelevanceScore,
                UseGpu = generation.UseGpu,
                GpuLayers = generation.GpuLayers
            },
            Agent = new AgentUserSettings
            {
                MaxIterations = agent.MaxIterations,
                VerificationTemperature = agent.VerificationTemperature,
                RequireApproval = agent.RequireApproval,
                StallLimit = agent.StallLimit
            }
        };
    }

    /// <summary>
    /// Applies a loaded snapshot to the live services. The services clamp out-of-range values
    /// themselves, so a hand-edited file can't push the app into an invalid state.
    /// </summary>
    public static void ApplyTo(UserSettings settings, GenerationSettingsService generation, AgentSettingsService agent)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(generation);
        ArgumentNullException.ThrowIfNull(agent);

        var g = settings.Generation;
        generation.Temperature = g.Temperature;
        generation.MaxTokens = g.MaxTokens;
        generation.TopK = g.TopK;
        generation.TopP = g.TopP;
        generation.RepeatPenalty = g.RepeatPenalty;
        generation.PresencePenalty = g.PresencePenalty;
        generation.FrequencyPenalty = g.FrequencyPenalty;
        generation.TimeoutSeconds = g.TimeoutSeconds;
        generation.PauseTimeoutSeconds = g.PauseTimeoutSeconds;
        generation.RagMode = g.RagMode;
        generation.PerformanceMetrics = g.PerformanceMetrics;
        generation.DebugMode = g.DebugMode;
        generation.RagTopK = g.RagTopK;
        generation.RagMinRelevanceScore = g.RagMinRelevanceScore;
        generation.UseGpu = g.UseGpu;
        generation.GpuLayers = g.GpuLayers;

        agent.MaxIterations = settings.Agent.MaxIterations;
        agent.VerificationTemperature = settings.Agent.VerificationTemperature;
        agent.RequireApproval = settings.Agent.RequireApproval;
        agent.StallLimit = settings.Agent.StallLimit;
    }
}
