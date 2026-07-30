using AgentKit.Api.Models;

namespace AgentKit.Api.Services;

/// <summary>
/// Runs goal-agent jobs headlessly: each job gets its own workspace directory and its own
/// <c>GoalAgentService</c> instance, so many jobs can run concurrently without sharing state
/// (unlike the dashboard's single-active-run model). A job's lifetime spans the process — see
/// <see cref="GetPersistedStatusAsync"/> for the fallback once a job's process-local state is gone
/// (server restart).
/// </summary>
public interface IAgentJobService
{
    /// <summary>
    /// Starts a new job in the background and returns its id immediately; the run itself
    /// continues after this call returns (fire-and-forget, owned by this singleton service so it
    /// outlives the HTTP request that started it).
    /// </summary>
    Guid StartJob(string goalDescription, int? maxIterations);

    /// <summary>Live status of a job whose <c>GoalAgentService</c> instance is still tracked in this process, or null if unknown.</summary>
    AgentJobStatus? GetStatus(Guid jobId);

    /// <summary>
    /// Status of a job read from the database instead of process memory — the fallback for a job
    /// started by a prior process (e.g. before a restart). Returns null when no run history is
    /// configured, or no such run exists.
    /// </summary>
    Task<AgentJobStatus?> GetPersistedStatusAsync(Guid jobId);

    /// <summary>The job's workspace directory, or null if the job is unknown to this process.</summary>
    string? GetWorkspacePath(Guid jobId);

    /// <summary>Requests the job stop at its next step boundary. Returns false if the job is unknown to this process.</summary>
    bool RequestStop(Guid jobId);

    /// <summary>Recent jobs (persisted history), newest first.</summary>
    Task<IReadOnlyList<AgentJobSummary>> GetRecentJobsAsync(int count);
}
