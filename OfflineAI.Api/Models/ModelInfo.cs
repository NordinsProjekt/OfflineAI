namespace OfflineAI.Api.Models;

/// <summary>
/// Model information response.
/// </summary>
public class ModelInfo
{
    /// <summary>
    /// Model name/identifier.
    /// </summary>
    public required string Name { get; set; }
    
    /// <summary>
    /// Model display name.
    /// </summary>
    public required string DisplayName { get; set; }
    
    /// <summary>
    /// Model description.
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// Whether this is the default model.
    /// </summary>
    public bool IsDefault { get; set; }
    
    /// <summary>
    /// Maximum context length (tokens).
    /// </summary>
    public int MaxContextLength { get; set; }
    
    /// <summary>
    /// Whether the model is currently available.
    /// </summary>
    public bool IsAvailable { get; set; }
}
