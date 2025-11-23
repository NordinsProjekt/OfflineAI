using Entities;

namespace Services.Repositories;

/// <summary>
/// Repository interface for managing bot personalities.
/// </summary>
public interface IBotPersonalityRepository
{
    /// <summary>
    /// Initialize the database schema for bot personalities.
    /// </summary>
    Task InitializeDatabaseAsync();
    
    /// <summary>
    /// Get all active bot personalities.
    /// </summary>
    Task<List<BotPersonalityEntity>> GetAllActiveAsync();
    
    /// <summary>
    /// Get all bot personalities including inactive ones.
    /// </summary>
    Task<List<BotPersonalityEntity>> GetAllAsync();
    
    /// <summary>
    /// Get a bot personality by its ID.
    /// </summary>
    Task<BotPersonalityEntity?> GetByPersonalityIdAsync(string personalityId);
    
    /// <summary>
    /// Get bot personalities by category.
    /// </summary>
    Task<List<BotPersonalityEntity>> GetByCategoryAsync(string category);
    
    /// <summary>
    /// Get all unique categories.
    /// </summary>
    Task<List<string>> GetCategoriesAsync();
    
    /// <summary>
    /// Save or update a bot personality.
    /// </summary>
    Task<Guid> SaveAsync(BotPersonalityEntity entity);
    
    /// <summary>
    /// Delete a bot personality.
    /// </summary>
    Task DeleteAsync(string personalityId);
    
    /// <summary>
    /// Check if a personality ID exists.
    /// </summary>
    Task<bool> ExistsAsync(string personalityId);
    
    /// <summary>
    /// Seed default bot personalities if none exist.
    /// </summary>
    Task SeedDefaultPersonalitiesAsync();
}
