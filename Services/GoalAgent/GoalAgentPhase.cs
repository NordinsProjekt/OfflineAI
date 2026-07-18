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

    /// <summary>The agent is doing file work to satisfy unmet requirements.</summary>
    Working,

    /// <summary>The agent is checking every requirement against the workspace files.</summary>
    Verifying,

    /// <summary>Every requirement passed verification — the run is "all green".</summary>
    Completed,

    /// <summary>The iteration cap was reached with at least one requirement still not passed.</summary>
    MaxIterationsReached,

    /// <summary>The user requested a stop and the run halted between steps.</summary>
    Stopped,

    /// <summary>The run aborted on an unexpected error (e.g. the LLM backend threw).</summary>
    Failed
}
