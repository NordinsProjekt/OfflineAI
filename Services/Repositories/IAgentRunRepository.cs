using Entities;

namespace Services.Repositories;

/// <summary>
/// Interface for goal-agent run history: the run-level record of what Agent Mode was asked to do
/// and how it went, which the question/answer tables cannot express.
/// <para>
/// Every write is called from a live run, so implementations must be cheap and must not be
/// depended on for correctness — <c>Services.GoalAgent.GoalAgentService</c> treats persistence as
/// best-effort and keeps running if it fails.
/// </para>
/// </summary>
public interface IAgentRunRepository
{
    /// <summary>
    /// Initialize the database schema for the AgentRuns, AgentRunRequirements, and
    /// AgentRunEvents tables.
    /// </summary>
    Task InitializeDatabaseAsync();

    /// <summary>
    /// Insert the run row at the start of a run, with <see cref="AgentRunEntity.CompletedAt"/>
    /// still null. A row that never gets completed marks a run that died with the process.
    /// </summary>
    Task StartRunAsync(AgentRunEntity run);

    /// <summary>
    /// Insert the requirement rows for a run, once the LLM has derived them from the goal.
    /// </summary>
    Task SaveRequirementsAsync(IReadOnlyList<AgentRunRequirementEntity> requirements);

    /// <summary>
    /// Update a single requirement's verdict after it has been verified. No-ops for an unknown id.
    /// </summary>
    /// <param name="requirementId">The requirement's id (the run's in-memory requirement id).</param>
    /// <param name="status">Name of the <c>RequirementStatus</c> value.</param>
    /// <param name="lastVerdict">Failure motivation, or null when it passed.</param>
    Task UpdateRequirementAsync(Guid requirementId, string status, string? lastVerdict);

    /// <summary>
    /// Append a batch of activity-log events. Called at step boundaries rather than per event, so
    /// a long run doesn't pay a round trip per log line.
    /// </summary>
    Task AddEventsAsync(IReadOnlyList<AgentRunEventEntity> events);

    /// <summary>
    /// Record the run's terminal phase, how many iterations it used, and when it finished.
    /// </summary>
    Task CompleteRunAsync(Guid runId, string phase, int iterations, DateTime completedAt);

    /// <summary>Get the most recent runs, newest first, for the history list.</summary>
    Task<List<AgentRunEntity>> GetRecentRunsAsync(int count = 25);

    /// <summary>Get a single run, or null if it does not exist.</summary>
    Task<AgentRunEntity?> GetRunAsync(Guid runId);

    /// <summary>Get a run's requirements, in the order the LLM listed them.</summary>
    Task<List<AgentRunRequirementEntity>> GetRequirementsAsync(Guid runId);

    /// <summary>Get a run's activity log, oldest first.</summary>
    Task<List<AgentRunEventEntity>> GetEventsAsync(Guid runId);

    /// <summary>Delete a run and everything belonging to it.</summary>
    Task DeleteRunAsync(Guid runId);
}
