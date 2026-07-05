namespace Services.BatchJobs;

/// <summary>
/// Lifecycle state of a <see cref="BatchJob"/> as it moves through the queue.
/// </summary>
public enum BatchJobStatus
{
    /// <summary>Queued, not yet started.</summary>
    Pending,

    /// <summary>Currently being processed by <see cref="IBatchJobService.StartProcessingAsync"/>.</summary>
    Running,

    /// <summary>Completed successfully; <see cref="BatchJob.Result"/> holds the LLM's final answer.</summary>
    Done,

    /// <summary>Completed with an error; <see cref="BatchJob.Result"/> holds the error message.</summary>
    Failed
}

/// <summary>
/// A single natural-language task in the batch queue (e.g. "Read rules.txt and write 10
/// questions to QA.txt"), processed via <see cref="Services.AgentTools.IAgenticChatService"/> so
/// it can use the existing file-agent tools (/läs, /skapa, /fyll, /läs-pdf, /redigera, /lista).
/// </summary>
public class BatchJob
{
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>The free-text instruction sent to the LLM as the job's prompt.</summary>
    public string Description { get; }

    public BatchJobStatus Status { get; internal set; } = BatchJobStatus.Pending;

    /// <summary>
    /// The LLM's final answer text once <see cref="Status"/> is <see cref="BatchJobStatus.Done"/>,
    /// or the error message once <see cref="BatchJobStatus.Failed"/>. Null while Pending/Running.
    /// </summary>
    public string? Result { get; internal set; }

    public DateTime CreatedAt { get; } = DateTime.Now;

    public DateTime? CompletedAt { get; internal set; }

    public BatchJob(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Job description cannot be empty.", nameof(description));

        Description = description;
    }
}
