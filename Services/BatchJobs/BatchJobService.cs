using Services.AgentTools;

namespace Services.BatchJobs;

/// <inheritdoc/>
public sealed class BatchJobService : IBatchJobService
{
    private readonly IAgenticChatService _agenticChat;
    private readonly List<BatchJob> _jobs = new();
    private readonly object _lock = new();
    private volatile bool _isProcessing;
    private volatile bool _stopRequested;

    public event Action? OnChange;

    public BatchJobService(IAgenticChatService agenticChat)
    {
        _agenticChat = agenticChat ?? throw new ArgumentNullException(nameof(agenticChat));
    }

    /// <inheritdoc/>
    public IReadOnlyList<BatchJob> Jobs
    {
        get
        {
            lock (_lock)
            {
                return _jobs.ToList();
            }
        }
    }

    /// <inheritdoc/>
    public bool IsProcessing => _isProcessing;

    /// <inheritdoc/>
    public BatchJob AddJob(string description)
    {
        var job = new BatchJob(description);
        lock (_lock)
        {
            _jobs.Add(job);
        }
        NotifyChange();
        return job;
    }

    /// <inheritdoc/>
    public bool RemoveJob(Guid id)
    {
        lock (_lock)
        {
            var job = _jobs.FirstOrDefault(j => j.Id == id);
            if (job is null || job.Status != BatchJobStatus.Pending)
                return false;

            _jobs.Remove(job);
        }

        NotifyChange();
        return true;
    }

    /// <inheritdoc/>
    public void ClearCompleted()
    {
        lock (_lock)
        {
            _jobs.RemoveAll(j => j.Status is BatchJobStatus.Done or BatchJobStatus.Failed);
        }
        NotifyChange();
    }

    /// <inheritdoc/>
    public void RequestStop() => _stopRequested = true;

    /// <inheritdoc/>
    public async Task StartProcessingAsync(Func<string, Task<string>> sendToLlm, Action<string>? onToolStatus = null)
    {
        ArgumentNullException.ThrowIfNull(sendToLlm);

        if (_isProcessing)
            return;

        _isProcessing = true;
        _stopRequested = false;
        NotifyChange();

        try
        {
            while (!_stopRequested)
            {
                BatchJob? next;
                lock (_lock)
                {
                    next = _jobs.FirstOrDefault(j => j.Status == BatchJobStatus.Pending);
                }

                if (next is null)
                    break;

                next.Status = BatchJobStatus.Running;
                NotifyChange();

                try
                {
                    var result = await _agenticChat.SendWithToolsAsync(
                        next.Description,
                        sendToLlm,
                        onToolStatus: onToolStatus);

                    next.Status = BatchJobStatus.Done;
                    next.Result = result.FinalResponse;
                }
                catch (Exception ex)
                {
                    next.Status = BatchJobStatus.Failed;
                    next.Result = ex.Message;
                }
                finally
                {
                    next.CompletedAt = DateTime.Now;
                    NotifyChange();
                }
            }
        }
        finally
        {
            _isProcessing = false;
            NotifyChange();
        }
    }

    private void NotifyChange() => OnChange?.Invoke();
}
