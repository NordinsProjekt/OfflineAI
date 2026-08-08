namespace Services.GoalAgent;

/// <summary>
/// Overall phase of a goal-agent run as it moves through the generate-requirements →
/// work → verify → repeat cycle.
/// </summary>
public enum GoalAgentPhase
{
    /// <summary>No run started (or state was reset).</summary>
    Idle,

    /// <summary>Asking the LLM to break the goal description down into checkable requirements.</summary>
    GeneratingRequirements,

    /// <summary>
    /// The requirements are derived and the run is paused until the user approves them (possibly
    /// after editing the list). Only reached when the run was started with approval required —
    /// a misread goal is the cheapest failure to catch here and the most expensive to catch after
    /// twenty iterations of work.
    /// </summary>
    AwaitingApproval,

    /// <summary>The agent is doing file work to satisfy unmet requirements.</summary>
    Working,

    /// <summary>The agent is checking every requirement against the workspace files.</summary>
    Verifying,

    /// <summary>Every requirement passed verification — the run is "all green".</summary>
    Completed,

    /// <summary>The iteration cap was reached with at least one requirement still not passed.</summary>
    MaxIterationsReached,

    /// <summary>
    /// The run stopped early because it had stopped making progress: every remaining requirement
    /// failed for the exact same reason several verifications in a row. Continuing would only
    /// repeat the same work, so the iteration budget is left unspent instead.
    /// </summary>
    Stalled,

    /// <summary>The user requested a stop and the run halted between steps.</summary>
    Stopped,

    /// <summary>The run aborted on an unexpected error (e.g. the LLM backend threw).</summary>
    Failed
}
