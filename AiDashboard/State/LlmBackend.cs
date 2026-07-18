namespace AiDashboard.State;

/// <summary>
/// Identifies which LLM backend the dashboard should use.
/// </summary>
public enum LlmBackend
{
    /// <summary>Classic pooled subprocess (PersistentLlmProcess) with optional RAG.</summary>
    Classic,

    /// <summary>Local Gemma 4 subprocess via <c>llama-cli</c> (offline, no RAG).</summary>
    Gemma4Cli
}
