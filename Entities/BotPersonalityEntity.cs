namespace Entities;

/// <summary>
/// Represents a bot personality configuration with system prompt and settings.
/// Used to create different bot behaviors (rules bot, support bot, etc.)
/// </summary>
public class BotPersonalityEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// Unique identifier for the personality (e.g., "rules-bot", "support-bot")
    /// </summary>
    public string PersonalityId { get; set; } = string.Empty;
    
    /// <summary>
    /// Display name shown in the UI
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;
    
    /// <summary>
    /// Description of what this bot does
    /// </summary>
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// System prompt that defines the bot's behavior and personality
    /// </summary>
    public string SystemPrompt { get; set; } = string.Empty;
    
    /// <summary>
    /// Optional collection name to use by default
    /// </summary>
    public string? DefaultCollection { get; set; }
    
    /// <summary>
    /// Temperature setting for this personality (0.0 - 2.0)
    /// </summary>
    public float? Temperature { get; set; }
    
    /// <summary>
    /// Whether RAG mode should be enabled by default
    /// </summary>
    public bool EnableRag { get; set; } = true;
    
    /// <summary>
    /// Icon name or emoji for visual identification
    /// </summary>
    public string? Icon { get; set; }
    
    /// <summary>
    /// Category for grouping personalities
    /// </summary>
    public string Category { get; set; } = "general";
    
    /// <summary>
    /// Whether this personality is active and available
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// Creation timestamp
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Last update timestamp
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
