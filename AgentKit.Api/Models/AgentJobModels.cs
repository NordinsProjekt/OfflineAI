namespace AgentKit.Api.Models;

/// <summary>Request body for starting a new goal-agent job.</summary>
public class StartJobRequest
{
    /// <summary>Free-text description of the desired end result of the job's workspace.</summary>
    public string GoalDescription { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the work → verify iteration cap. Non-positive or omitted falls back
    /// to the server's configured default (<c>AppConfiguration.AgentTools.MaxGoalIterations</c>).
    /// </summary>
    public int? MaxIterations { get; set; }
}

/// <summary>Response returned immediately after a job is accepted; the run itself continues in the background.</summary>
public sealed record StartJobResponse(Guid JobId);

/// <summary>One requirement's state within a job, as shown to an API caller.</summary>
public sealed record AgentJobRequirementStatus(string Description, string Status, string? LastVerdict);

/// <summary>
/// Full status of a job: phase, iteration progress, requirements, and activity log. Returned by
/// both the live (in-memory) and persisted (database) status lookups, so a caller can't tell
/// which source served a given response.
/// </summary>
public sealed record AgentJobStatus(
    Guid JobId,
    string Phase,
    string GoalDescription,
    int CurrentIteration,
    int MaxIterations,
    IReadOnlyList<AgentJobRequirementStatus> Requirements,
    IReadOnlyList<string> ActivityLog);

/// <summary>One row of the recent-jobs list (<c>GET /api/jobs</c>).</summary>
public sealed record AgentJobSummary(
    Guid JobId,
    string GoalDescription,
    string Phase,
    int Iterations,
    int MaxIterations,
    DateTime StartedAt,
    DateTime? CompletedAt);
