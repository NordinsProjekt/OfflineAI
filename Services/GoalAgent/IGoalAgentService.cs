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

    /// <summary>
    /// True while the run is paused waiting for <see cref="ApproveRequirements"/> — the run is
    /// still "running", it is just not doing any work until the requirement list is accepted.
    /// </summary>
    bool IsAwaitingApproval { get; }

    /// <summary>
    /// True when <see cref="ContinueAsync"/> would do something: a finished run that did not end
    /// all-green and still has requirements to work on. False while a run is active, after a
    /// completed ("all green") run, and before the first run.
    /// </summary>
    bool CanContinue { get; }

    /// <summary>
    /// The active run's cancellation token — linked to whatever token the caller passed, and
    /// additionally cancelled by <see cref="RequestStop"/>. <see cref="CancellationToken.None"/>
    /// when no run is active.
    /// <para>
    /// Build the <c>sendToLlm</c> delegate around this rather than around a token the caller owns:
    /// it is the only way a stop can abort an LLM call that is already generating, and because it
    /// is read from the service on every call it keeps working even for a UI that was rebuilt
    /// mid-run (navigating away from the page and back).
    /// </para>
    /// </summary>
    CancellationToken RunToken { get; }

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
    /// <param name="requireApproval">
    /// When true the run pauses in <see cref="GoalAgentPhase.AwaitingApproval"/> once the
    /// requirements have been derived, and does no file work until <see cref="ApproveRequirements"/>
    /// is called (or the run is stopped). Leave false for unattended/headless callers, which have
    /// nobody to answer the prompt.
    /// </param>
    /// <param name="stallLimit">
    /// Optional. Overrides, for this run, how many consecutive identical failures across every
    /// remaining requirement end the run early in <see cref="GoalAgentPhase.Stalled"/>. 0 disables
    /// the check; null falls back to the value the service was constructed with.
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
        Guid? runId = null,
        bool requireApproval = false,
        int? stallLimit = null);

    /// <summary>
    /// Resumes a finished run that did not end all-green: keeps the goal, the requirement list and
    /// every requirement's current verdict, and runs the work → verify loop for another
    /// <paramref name="maxIterations"/> iterations. No-ops unless <see cref="CanContinue"/> is true.
    /// <para>
    /// Requirement generation is deliberately skipped — the requirements have already been derived
    /// (and possibly approved by hand), and re-deriving them from the same goal description would
    /// throw that away and could produce a different list, making the earlier verdicts meaningless.
    /// </para>
    /// <para>
    /// The continuation is recorded as its own run in the history (a run row is closed when it
    /// ends), marked as a continuation in its activity log. The workspace transcript is appended
    /// to rather than overwritten, so the full story stays in one file.
    /// </para>
    /// </summary>
    /// <param name="maxIterations">
    /// Iteration cap for the continuation only. Non-positive or null falls back to the cap the
    /// service was constructed with — it is not inherited from the run being continued.
    /// </param>
    /// <param name="stallLimit">
    /// Stall limit for the continuation only; see <see cref="RunAsync"/>. The repeat counters
    /// themselves start fresh, so continuing a stalled run always gets a real attempt before it
    /// can give up again.
    /// </param>
    Task ContinueAsync(
        Func<string, Task<string>> sendToLlm,
        Action<string>? onToolStatus = null,
        CancellationToken cancellationToken = default,
        string? modelName = null,
        Guid? conversationId = null,
        Func<string, Task<string>>? verifySendToLlm = null,
        int? maxIterations = null,
        int? stallLimit = null);

    /// <summary>
    /// Releases a run paused in <see cref="GoalAgentPhase.AwaitingApproval"/> so the work loop can
    /// start. No-ops when no run is waiting for approval.
    /// </summary>
    /// <param name="requirements">
    /// Optional replacement requirement list (edited, reordered, added to, or trimmed by the user).
    /// When null the generated requirements are used as-is. Blank entries are dropped; if nothing
    /// usable remains, the generated list is kept rather than starting a run with no requirements
    /// at all — a run with an empty suite would report "all green" without doing anything.
    /// </param>
    void ApproveRequirements(IReadOnlyList<string>? requirements = null);

    /// <summary>
    /// Requests the run stop at the next step boundary (between work items or verifications), and
    /// releases a run that is waiting for requirement approval. Does not by itself interrupt an
    /// in-flight LLM call — cancel the <c>cancellationToken</c> passed to
    /// <see cref="RunAsync"/> for that.
    /// </summary>
    void RequestStop();

    /// <summary>
    /// Clears the requirements, log, and phase back to <see cref="GoalAgentPhase.Idle"/> so a
    /// fresh run can be presented. Ignored while a run is active.
    /// </summary>
    void Reset();
}
