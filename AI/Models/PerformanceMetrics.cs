namespace Application.AI.Models;

/// <summary>
/// Performance metrics for LLM generation
/// </summary>
public class PerformanceMetrics
{
    /// <summary>
    /// Total time taken for the request in milliseconds
    /// </summary>
    public double TotalTimeMs { get; set; }
    
    /// <summary>
    /// Number of tokens in the prompt
    /// </summary>
    public int PromptTokens { get; set; }
    
    /// <summary>
    /// Number of tokens in the completion/response
    /// </summary>
    public int CompletionTokens { get; set; }
    
    /// <summary>
    /// Total tokens (prompt + completion)
    /// </summary>
    public int TotalTokens => PromptTokens + CompletionTokens;
    
    /// <summary>
    /// Tokens generated per second
    /// </summary>
    public double TokensPerSecond => TotalTimeMs > 0 ? (CompletionTokens / (TotalTimeMs / 1000.0)) : 0;
    
    /// <summary>
    /// Time to first token in milliseconds
    /// </summary>
    public double? TimeToFirstTokenMs { get; set; }
    
    /// <summary>
    /// Model name used for generation
    /// </summary>
    public string? ModelName { get; set; }
    
    /// <summary>
    /// Format metrics as a readable string
    /// </summary>
    public override string ToString()
    {
        var result = $"\n?? Performance Metrics:";
        if (!string.IsNullOrEmpty(ModelName))
            result += $"\n   Model: {ModelName}";
        
        result += $"\n   Total Time: {TotalTimeMs:F0}ms ({TotalTimeMs / 1000.0:F2}s)";
        
        if (TimeToFirstTokenMs.HasValue)
            result += $"\n   Time to First Token: {TimeToFirstTokenMs.Value:F0}ms";
        
        result += $"\n   Tokens: {CompletionTokens} generated";
        
        if (PromptTokens > 0)
            result += $", {PromptTokens} in prompt ({TotalTokens} total)";
        
        result += $"\n   Speed: {TokensPerSecond:F2} tokens/sec";
        
        return result;
    }
}
