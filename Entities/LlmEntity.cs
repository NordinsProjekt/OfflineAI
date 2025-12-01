namespace Entities;

/// <summary>
/// Database entity for storing Large Language Model (LLM) information.
/// Maps to the LLMs table in MSSQL.
/// </summary>
public class LlmEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// Name of the LLM model file (e.g., "phi-3.5-mini-instruct.gguf")
    /// </summary>
    public string LlmName { get; set; } = string.Empty;
    
    /// <summary>
    /// Timestamp when this LLM was added to the database
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
