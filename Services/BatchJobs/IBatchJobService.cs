namespace Services.BatchJobs;

/// <summary>
/// Queues natural-language batch tasks (e.g. "Read rules.txt and write 10 questions to QA.txt")
/// and processes them one at a time through <see cref="Services.AgentTools.IAgenticChatService"/>,
/// so each job can freely use the existing file-agent tools. Registered as a singleton so the
/// queue survives page navigation within the running app (but not an app restart).
/// </summary>
public interface IBatchJobService
{
    /// <summary>Raised whenever the job list or a job's status/result changes.</summary>
    event Action? OnChange;

    /// <summary>All jobs currently in the queue, in the order they were added.</summary>
    IReadOnlyList<BatchJob> Jobs { get; }

    /// <summary>True while <see cref="StartProcessingAsync"/> is actively working through the queue.</summary>
    bool IsProcessing { get; }

    /// <summary>Appends a new pending job to the end of the queue and returns it.</summary>
    BatchJob AddJob(string description);

    /// <summary>
    /// Removes a job by id. Only <see cref="BatchJobStatus.Pending"/> jobs can be removed (a
    /// running job can't be pulled out from under the processing loop, and completed jobs keep
    /// their result visible until explicitly cleared). Returns false if not found or not pending.
    /// </summary>
    bool RemoveJob(Guid id);

    /// <summary>Removes all jobs in a terminal state (<see cref="BatchJobStatus.Done"/> or <see cref="BatchJobStatus.Failed"/>).</summary>
    void ClearCompleted();

    /// <summary>
    /// Requests the processing loop stop after the current job finishes. Cannot interrupt a job
    /// mid-flight — <c>IAgenticChatService.SendWithToolsAsync</c>'s cancellation token is only
    /// checked between its own tool-call rounds, not inside a single in-flight LLM call.
    /// </summary>
    void RequestStop();

    /// <summary>
    /// Processes pending jobs in order, one at a time, until none remain or a stop is requested.
    /// Re-checks <see cref="Jobs"/> live on each iteration, so jobs added while processing is
    /// already running are picked up automatically. No-ops if already processing.
    /// </summary>
    /// <param name="sendToLlm">
    /// Delegate that sends a prompt to whichever LLM backend is currently active (e.g.
    /// <c>DashboardState.SendQuickAskActiveAsync</c>), forwarded to <c>IAgenticChatService.SendWithToolsAsync</c>.
    /// </param>
    /// <param name="onToolStatus">Optional live status callback, forwarded to <c>SendWithToolsAsync</c>.</param>
    Task StartProcessingAsync(Func<string, Task<string>> sendToLlm, Action<string>? onToolStatus = null);
}
