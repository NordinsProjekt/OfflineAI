namespace Services;

/// <summary>
/// Monitors streaming output and timing for the LLM process.
/// </summary>
internal class StreamingOutputMonitor
{
    private readonly int _timeoutMs;
    private DateTime _lastOutputTime;

    public object OutputLock { get; } = new();
    public string AssistantTag { get; } = "<|assistant|>";
    public bool AssistantStartFound { get; private set; }

    public StreamingOutputMonitor(int timeoutMs)
    {
        _timeoutMs = timeoutMs;
        _lastOutputTime = DateTime.UtcNow;
    }

    public void UpdateLastOutputTime()
    {
        _lastOutputTime = DateTime.UtcNow;
    }

    public void MarkAssistantFound()
    {
        AssistantStartFound = true;
    }

    public TimeSpan GetTimeSinceLastOutput()
    {
        lock (OutputLock)
        {
            return DateTime.UtcNow - _lastOutputTime;
        }
    }
}