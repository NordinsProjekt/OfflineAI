namespace Services.GoalAgent;

/// <summary>
/// TDD-style goal agent for the active workspace: the user describes the desired end result
/// in free text, the LLM breaks that down into concrete, checkable requirements ("tests"),
/// the agent does file work through <see cref="AgentKit.ToolLoop.IAgenticChatService"/> to
/// satisfy them, verifies each requirement against the workspace files, and repeats the
/// work → verify cycle until every requirement passes or the iteration cap is reached.
/// <para>
/// Registered as a singleton so a run's requirements, statuses, and activity log survive page
/// navigation within the running app (but not an app restart) — same lifetime pattern as
/// <c>IBatchJobService</c>. Only one run can be active at a time.
/// </para>
/// </summary>
public interface IGoalAgentService
{
    /// <summary>Raised whenever the phase, a requirement's status, or the activity log changes.</summary>
    event Action? OnChange;

    /// <summary>Current phase of the run (Idle when nothing has been started).</summary>
    GoalAgentPhase Phase { get; }

    /// <summary>True while <see cref="RunAsync"/> is executing.</summary>
    bool IsRunning { get; }

    /// <summary>The goal description of the current/last run, or null before the first run.</summary>
    string? GoalDescription { get; }

    /// <summary>Requirements generated for the current/last run, in the order the LLM listed them.</summary>
    IReadOnlyList<GoalRequirement> Requirements { get; }

    /// <summary>1-based work/verify iteration currently executing (0 before the loop starts).</summary>
    int CurrentIteration { get; }

    /// <summary>Maximum number of work → verify iterations before the run gives up.</summary>
    int MaxIterations { get; }

    /// <summary>Human-readable progress lines for the current/last run, oldest first.</summary>
    IReadOnlyList<string> ActivityLog { get; }

    /// <summary>
    /// Executes a full goal-agent run: generates requirements from
    /// <paramref name="goalDescription"/>, then loops work → verify until all requirements
    /// pass, the iteration cap is hit, or a stop is requested. No-ops if a run is already
    /// active. Never throws for LLM/tool failures — the run ends in
    /// <see cref="GoalAgentPhase.Failed"/> with the error in <see cref="ActivityLog"/> instead,
    /// so fire-and-forget callers (the dashboard page) can't produce an unobserved exception.
    /// </summary>
    /// <param name="goalDescription">Free-text description of the desired workspace end result.</param>
    /// <param name="sendToLlm">
    /// Delegate that sends a prompt to whichever LLM backend is currently active (e.g.
    /// <c>DashboardState.SendQuickAskActiveAsync</c>). Used directly for requirement
    /// generation and forwarded to <c>IAgenticChatService.SendWithToolsAsync</c> for the
    /// work and verification steps.
    /// </param>
    /// <param name="onToolStatus">Optional live status callback, forwarded to <c>SendWithToolsAsync</c>.</param>
    /// <param name="cancellationToken">Cancellation token, forwarded to <c>SendWithToolsAsync</c>.</param>
    /// <param name="modelName">
    /// Optional. Name of the LLM behind <paramref name="sendToLlm"/>, recorded with the run's
    /// history so runs can be compared across models. The agent has no way to know this itself —
    /// the backend choice lives in the caller.
    /// </param>
    /// <param name="conversationId">
    /// Optional. The conversation the run's LLM turns are being saved under in the Questions
    /// table, recorded with the run's history so its raw prompts and replies stay findable.
    /// Callers should start a fresh conversation per run, or the run's turns will be
    /// indistinguishable from ordinary chat.
    /// </param>
    /// <param name="verifySendToLlm">
    /// Optional. Separate LLM delegate used for the verification steps only, so a caller can
    /// run reviews with different sampling than the creative work steps (verification wants
    /// deterministic verdicts — empty/garbled replies cluster at high temperature). When null,
    /// <paramref name="sendToLlm"/> is used for everything.
    /// </param>
    /// <param name="maxIterations">
    /// Optional. Overrides the work → verify iteration cap for this run only. Non-positive or
    /// null falls back to the cap the service was constructed with (typically
    /// <c>AppConfiguration.AgentTools.MaxGoalIterations</c>).
    /// </param>
    /// <param name="runId">
    /// Optional. Id to use for the persisted run row instead of a randomly generated one — lets
    /// a caller that already handed out an id for this run before calling <see cref="RunAsync"/>
    /// (e.g. a job id an API returned to its caller before the run started) look the same row up
    /// later by that id. Ignored when the service was constructed without a run repository.
    /// </param>
    /// <remarks>
    /// When the service was constructed with a file agent, the complete raw transcript of the
    /// run (every prompt, every LLM reply including internal tool-call rounds, executed tool
    /// commands, and verdicts) is written to <c>GoalAgentService.TranscriptFileName</c>
    /// ("agentlogg.txt") in the active workspace, overwriting the previous run's log.
    /// When it was constructed with an <c>IAgentRunRepository</c>, the run, its requirements, and
    /// its activity log are additionally recorded as history that survives an app restart.
    /// </remarks>
    Task RunAsync(
        string goalDescription,
        Func<string, Task<string>> sendToLlm,
        Action<string>? onToolStatus = null,
        CancellationToken cancellationToken = default,
        string? modelName = null,
        Guid? conversationId = null,
        Func<string, Task<string>>? verifySendToLlm = null,
        int? maxIterations = null,
        Guid? runId = null);

    /// <summary>
    /// Requests the run stop at the next step boundary (between work items or verifications).
    /// Cannot interrupt a single in-flight LLM call.
    /// </summary>
    void RequestStop();

    /// <summary>
    /// Clears the requirements, log, and phase back to <see cref="GoalAgentPhase.Idle"/> so a
    /// fresh run can be presented. Ignored while a run is active.
    /// </summary>
    void Reset();
}
