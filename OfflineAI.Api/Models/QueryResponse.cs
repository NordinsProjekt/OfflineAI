namespace OfflineAI.Api.Models;

/// <summary>
/// Response model for LLM query.
/// </summary>
public class QueryResponse
{
    /// <summary>
    /// The generated answer from the LLM.
    /// </summary>
    public required string Answer { get; set; }
    
    /// <summary>
    /// Model used for generating the response.
    /// </summary>
    public required string Model { get; set; }
    
    /// <summary>
    /// Whether RAG was used for this query.
    /// </summary>
    public bool UsedRag { get; set; }
    
    /// <summary>
    /// Number of documents retrieved from knowledge base (if RAG was used).
    /// </summary>
    public int DocumentsRetrieved { get; set; }
    
    /// <summary>
    /// Time taken to generate the response (in milliseconds).
    /// </summary>
    public long ResponseTimeMs { get; set; }
    
    /// <summary>
    /// Number of tokens in the prompt.
    /// </summary>
    public int PromptTokens { get; set; }
    
    /// <summary>
    /// Number of tokens in the completion.
    /// </summary>
    public int CompletionTokens { get; set; }
    
    /// <summary>
    /// Total tokens used (prompt + completion).
    /// </summary>
    public int TotalTokens => PromptTokens + CompletionTokens;
    
    /// <summary>
    /// Tokens generated per second.
    /// </summary>
    public double TokensPerSecond { get; set; }
    
    /// <summary>
    /// Indicates if the response was successful.
    /// </summary>
    public bool Success { get; set; } = true;
    
    /// <summary>
    /// Optional warning messages (e.g., timeout approaching, low relevance scores).
    /// </summary>
    public List<string> Warnings { get; set; } = new();
}
