namespace OfflineAI.Api.Models;

/// <summary>
/// Request model for LLM query with optional RAG context.
/// </summary>
public class QueryRequest
{
    /// <summary>
    /// The user's question or prompt.
    /// </summary>
    public required string Question { get; set; }
    
    /// <summary>
    /// Optional: Model name to use for inference. If not specified, uses default model.
    /// </summary>
    public string? Model { get; set; }
    
    /// <summary>
    /// Optional: Additional context to include in the query.
    /// If not provided, RAG will search the knowledge base.
    /// </summary>
    public string? Context { get; set; }
    
    /// <summary>
    /// Enable or disable RAG (Retrieval-Augmented Generation).
    /// Default: true
    /// </summary>
    public bool EnableRag { get; set; } = true;
    
    /// <summary>
    /// Maximum number of tokens to generate.
    /// Default: 512
    /// </summary>
    public int MaxTokens { get; set; } = 512;
    
    /// <summary>
    /// Temperature for response generation (0.0 - 2.0).
    /// Lower = more focused, Higher = more creative.
    /// Default: 0.3
    /// </summary>
    public float Temperature { get; set; } = 0.3f;
    
    /// <summary>
    /// Number of relevant documents to retrieve for RAG (if EnableRag is true).
    /// Default: 3
    /// </summary>
    public int TopK { get; set; } = 3;
    
    /// <summary>
    /// Minimum relevance score for RAG documents (0.0 - 1.0).
    /// Default: 0.5
    /// </summary>
    public float MinRelevanceScore { get; set; } = 0.5f;
}
