namespace Services.Configuration;

/// <summary>
/// Live, user-tunable settings for Agent Mode (the goal agent), with change notification —
/// the agent counterpart to <see cref="GenerationSettingsService"/>.
/// <para>
/// Registered as a singleton so the settings page and the Agent Mode page edit the same values:
/// before this existed, the iteration cap was a page-local field that reset to the configured
/// default on every navigation, and the verification temperature was a hard-coded constant in
/// the page's markup with no way to tune it against a different model.
/// </para>
/// </summary>
public sealed class AgentSettingsService
{
    /// <summary>Fallback iteration cap when no configured default is supplied (matches
    /// <c>AgentToolsSettings.MaxGoalIterations</c>).</summary>
    public const int DefaultMaxIterations = 20;

    /// <summary>
    /// Fallback verification temperature. Verification wants deterministic verdicts, not
    /// creativity: Gemma's recommended 1.0 gives intermittent empty/garbled replies, which in a
    /// real run burned a third of all reviews.
    /// </summary>
    public const double DefaultVerificationTemperature = 0.3;

    /// <summary>Highest iteration cap the UI accepts — a run is long and expensive, so an
    /// accidental extra digit should not become an all-night run.</summary>
    public const int MaxAllowedIterations = 100;

    private readonly int _configuredMaxIterations;

    /// <summary>Raised whenever any setting changes.</summary>
    public event Action? OnChange;

    /// <param name="configuredMaxIterations">
    /// Default iteration cap, typically <c>AppConfiguration.AgentTools.MaxGoalIterations</c>.
    /// Non-positive values fall back to <see cref="DefaultMaxIterations"/>.
    /// </param>
    public AgentSettingsService(int configuredMaxIterations = DefaultMaxIterations)
    {
        _configuredMaxIterations = configuredMaxIterations > 0
            ? Math.Min(configuredMaxIterations, MaxAllowedIterations)
            : DefaultMaxIterations;
        _maxIterations = _configuredMaxIterations;
    }

    private int _maxIterations;

    /// <summary>
    /// Cap on work → verify iterations per run. The loop exits as soon as every requirement
    /// passes, so this only bounds the pathological case — a higher value lets a weaker model
    /// keep retrying instead of hitting the cap while requirements are still being worked on.
    /// </summary>
    public int MaxIterations
    {
        get => _maxIterations;
        set
        {
            var clamped = Math.Clamp(value, 1, MaxAllowedIterations);
            if (_maxIterations == clamped) return;
            _maxIterations = clamped;
            NotifyStateChanged();
        }
    }

    private double _verificationTemperature = DefaultVerificationTemperature;

    /// <summary>
    /// Sampling temperature used for the goal agent's verification steps only, so reviews can be
    /// deterministic while the work steps keep the model's recommended sampling. Only applies to
    /// the Gemma 4 CLI backend; the classic backend runs verification at its configured
    /// generation temperature.
    /// </summary>
    public double VerificationTemperature
    {
        get => _verificationTemperature;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 2.0);
            if (Math.Abs(_verificationTemperature - clamped) < 0.0001) return;
            _verificationTemperature = clamped;
            NotifyStateChanged();
        }
    }

    private bool _requireApproval = true;

    /// <summary>
    /// When true, a run started from the dashboard pauses after deriving its requirements and does
    /// no file work until the user approves the list (which they may edit first).
    /// <para>
    /// On by default: the requirements are the contract for everything that follows, and a
    /// misread goal is the cheapest failure to catch here — after twenty iterations it has cost a
    /// full run. Turn it off for runs that are meant to be started and left alone; headless
    /// callers (the job API) never ask for approval regardless.
    /// </para>
    /// </summary>
    public bool RequireApproval
    {
        get => _requireApproval;
        set
        {
            if (_requireApproval == value) return;
            _requireApproval = value;
            NotifyStateChanged();
        }
    }

    /// <summary>Default number of identical failures in a row that counts as a stalled run.</summary>
    public const int DefaultStallLimit = 3;

    /// <summary>Upper bound accepted by the UI — beyond this the check would never fire in practice.</summary>
    public const int MaxAllowedStallLimit = 20;

    private int _stallLimit = DefaultStallLimit;

    /// <summary>
    /// How many verifications in a row may fail for the exact same reason, across every remaining
    /// requirement, before the run gives up early instead of spending the rest of its iteration
    /// budget repeating itself. 0 turns the check off and always uses the full budget.
    /// </summary>
    public int StallLimit
    {
        get => _stallLimit;
        set
        {
            var clamped = Math.Clamp(value, 0, MaxAllowedStallLimit);
            if (_stallLimit == clamped) return;
            _stallLimit = clamped;
            NotifyStateChanged();
        }
    }

    /// <summary>
    /// Restores the iteration cap to the value the service was configured with (appsettings),
    /// the verification temperature to <see cref="DefaultVerificationTemperature"/>, the approval
    /// gate to on, and the stall limit to <see cref="DefaultStallLimit"/>.
    /// </summary>
    public void ResetToDefaults()
    {
        MaxIterations = _configuredMaxIterations;
        VerificationTemperature = DefaultVerificationTemperature;
        RequireApproval = true;
        StallLimit = DefaultStallLimit;
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}
