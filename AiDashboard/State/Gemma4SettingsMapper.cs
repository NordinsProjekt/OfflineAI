using Application.AI.Gemma4;
using Services.Configuration;

namespace AiDashboard.State;

/// <summary>
/// Maps between the AI project's live <see cref="Gemma4CliOptions"/> and the persistable
/// <see cref="Gemma4UserSettings"/> DTO. The two live in different projects (Services cannot see
/// the AI project, which references it), so the translation belongs here in the host — and in one
/// place, so both directions stay in sync.
/// <para>
/// Only the per-call knobs are mapped. Every Gemma 4 request spawns a fresh
/// <c>llama-completion</c> process and re-reads them, so changing these retunes the running
/// backend; paths and the chat template do not work that way and are deliberately absent.
/// </para>
/// </summary>
public static class Gemma4SettingsMapper
{
    /// <summary>Copies persisted settings onto the live options instance.</summary>
    public static void ApplyTo(Gemma4UserSettings source, Gemma4CliOptions target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        target.Temperature = source.Temperature;
        target.MaxTokens = source.MaxTokens;
        target.TopP = source.TopP;
        target.TopK = source.TopK;
        target.ContextSize = source.ContextSize;
        target.GpuLayers = source.GpuLayers;
        target.TimeoutMs = source.TimeoutMs;
        target.PauseTimeoutMs = source.PauseTimeoutMs;
    }

    /// <summary>Snapshots the live options instance into a persistable DTO.</summary>
    public static Gemma4UserSettings Capture(Gemma4CliOptions source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new Gemma4UserSettings
        {
            Temperature = source.Temperature,
            MaxTokens = source.MaxTokens,
            TopP = source.TopP,
            TopK = source.TopK,
            ContextSize = source.ContextSize,
            GpuLayers = source.GpuLayers,
            TimeoutMs = source.TimeoutMs,
            PauseTimeoutMs = source.PauseTimeoutMs
        };
    }
}
