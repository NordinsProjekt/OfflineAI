using AgentKit.Api.Models;

namespace AgentKit.Api.Services;

/// <summary>
/// Runs goal-agent jobs headlessly: each job gets its own workspace directory and its own
/// <c>GoalAgentService</c> instance, so many jobs can run concurrently without sharing state
/// (unlike the dashboard's single-active-run model). When this node is too busy for a new job and
/// peers are configured (<c>AppConfiguration.Cluster.Peers</c>), it forwards the job to an idle
/// peer instead — a job's id always identifies it to whichever node the caller originally talked
/// to, regardless of which node actually runs it. See <see cref="GetStatusAsync"/> for the
/// fallback once a job's process-local state is gone (server restart).
/// </summary>
public interface IAgentJobService
{
    /// <summary>
    /// Starts a new job — locally, or forwarded to an idle peer if this node is too busy and a
    /// peer has room — and returns its id immediately; the run itself continues after this call
    /// returns.
    /// </summary>
    Task<Guid> StartJobAsync(string goalDescription, int? maxIterations, CancellationToken cancellationToken);

    /// <summary>
    /// Status of a job: live from this process's memory if it's running here, proxied from the
    /// peer if it was forwarded, or read from this node's persisted history as a last resort.
    /// Null if the job is unknown by all three.
    /// </summary>
    Task<AgentJobStatus?> GetStatusAsync(Guid jobId, CancellationToken cancellationToken);

    /// <summary>
    /// The job's finished workspace as a zip — built from local disk if it ran here, or fetched
    /// from the peer if it was forwarded. Null if the job is unknown, still running, or its files
    /// are unavailable (caller is expected to have already checked <see cref="GetStatusAsync"/>
    /// for a terminal phase).
    /// </summary>
    Task<Stream?> GetResultZipAsync(Guid jobId, CancellationToken cancellationToken);

    /// <summary>
    /// Requests the job stop at its next step boundary — locally, or by asking the peer it was
    /// forwarded to. Returns false if the job is unknown to this process.
    /// </summary>
    Task<bool> RequestStopAsync(Guid jobId, CancellationToken cancellationToken);

    /// <summary>Recent jobs (this node's own persisted history), newest first.</summary>
    Task<IReadOnlyList<AgentJobSummary>> GetRecentJobsAsync(int count, CancellationToken cancellationToken);
}
