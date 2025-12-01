namespace Application.AI.Processing;

/// <summary>
/// Interface for persistent LLM process operations.
/// Enables testability and mocking of LLM interactions.
/// </summary>
public interface IPersistentLlmProcess : IDisposable
{
    bool IsHealthy { get; }
    DateTime LastUsed { get; }
    int TimeoutMs { get; set; }
    
    Task<string> QueryAsync(
        string systemPrompt,
        string userQuestion,
        int maxTokens = 200,
        float temperature = 0.3f,
        int topK = 30,
        float topP = 0.85f,
        float repeatPenalty = 1.15f,
        float presencePenalty = 0.2f,
        float frequencyPenalty = 0.2f,
        bool useGpu = false,
        int gpuLayers = 0);
}
