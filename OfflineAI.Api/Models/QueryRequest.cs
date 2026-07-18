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
    /// Optional: Pre-retrieved context to include in the query.
    /// 
    /// Two RAG modes supported:
    /// 1. Manual Context: Provide context directly (ignores vector search)
    /// 2. Auto Vector Search: Leave empty to automatically search the knowledge base
    /// 
    /// Example manual context for game rules:
    /// "In Monopoly, players roll two dice to move around the board. 
    ///  When you land on an unowned property, you may buy it.
    ///  If another player owns the property, you must pay rent."
    /// </summary>
    public string? Context { get; set; }
    
    /// <summary>
    /// Enable or disable RAG (Retrieval-Augmented Generation).
    /// 
    /// When true and Context is null: Performs vector search on knowledge base
    /// When true and Context is provided: Uses the provided context
    /// When false: Direct LLM query without additional context
    /// 
    /// Default: true
    /// </summary>
    public bool EnableRag { get; set; } = true;
    
    /// <summary>
    /// Optional: Domain filters for vector search (only used when Context is null and EnableRag is true).
    /// 
    /// Examples:
    /// - ["monopoly"] - Only search Monopoly game rules
    /// - ["chess", "checkers"] - Search both Chess and Checkers rules
    /// - null or empty - Search all domains
    /// 
    /// Domain IDs are lowercase, hyphen-separated (e.g., "board-games", "card-games")
    /// </summary>
    public List<string>? DomainFilter { get; set; }
    
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
    /// Number of relevant documents to retrieve for RAG (if EnableRag is true and Context is null).
    /// Default: 3
    /// </summary>
    public int TopK { get; set; } = 3;
    
    /// <summary>
    /// Minimum relevance score for RAG documents (0.0 - 1.0).
    /// Higher values = stricter matching, Lower values = more results.
    /// Default: 0.5
    /// </summary>
    public double MinRelevanceScore { get; set; } = 0.5;
}
