namespace Entities;

/// <summary>
/// Represents a game registered in the system for detection and filtering.
/// </summary>
public class GameEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// Unique game identifier (e.g., "munchkin-panic")
    /// </summary>
    public string GameId { get; set; } = string.Empty;
    
    /// <summary>
    /// Display name for the game (e.g., "Munchkin Panic")
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;
    
    /// <summary>
    /// When the game was first registered
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Last time the game or its variants were updated
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Source of the game registration (e.g., "manual", "txt-file", "pdf-file", "auto-discovered")
    /// </summary>
    public string Source { get; set; } = "manual";
}

/// <summary>
/// Represents a searchable variant or alias for a game.
/// Examples: "munchkin panic", "panic", "castle panic munchkin"
/// </summary>
public class GameVariantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// Foreign key to the game
    /// </summary>
    public Guid GameId { get; set; }
    
    /// <summary>
    /// The variant text to match against queries (lowercase)
    /// </summary>
    public string VariantText { get; set; } = string.Empty;
    
    /// <summary>
    /// When this variant was added
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
