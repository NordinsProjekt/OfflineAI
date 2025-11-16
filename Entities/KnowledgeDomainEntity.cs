namespace Entities;

/// <summary>
/// Represents a knowledge domain registered in the system for detection and filtering.
/// Examples: board games, products, projects, academic subjects, etc.
/// </summary>
public class KnowledgeDomainEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// Unique domain identifier (e.g., "gloomhaven", "iphone-15", "project-alpha")
    /// </summary>
    public string DomainId { get; set; } = string.Empty;
    
    /// <summary>
    /// Display name for the domain (e.g., "Gloomhaven", "iPhone 15", "Project Alpha")
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;
    
    /// <summary>
    /// Category of the domain (e.g., "board-game", "product", "project", "academic", "general")
    /// Allows grouping related domains together.
    /// </summary>
    public string Category { get; set; } = "general";
    
    /// <summary>
    /// When the domain was first registered
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Last time the domain or its variants were updated
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Source of the domain registration (e.g., "manual", "txt-file", "pdf-file", "auto-discovered")
    /// </summary>
    public string Source { get; set; } = "manual";
}

/// <summary>
/// Represents a searchable variant or alias for a knowledge domain.
/// Examples: "gloomhaven", "gloom haven", "iphone 15", "iphone15"
/// </summary>
public class DomainVariantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// Foreign key to the knowledge domain
    /// </summary>
    public Guid DomainId { get; set; }
    
    /// <summary>
    /// The variant text to match against queries (lowercase)
    /// </summary>
    public string VariantText { get; set; } = string.Empty;
    
    /// <summary>
    /// When this variant was added
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
