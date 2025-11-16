using Entities;

namespace Services.Repositories;

/// <summary>
/// Repository interface for managing game detection data.
/// Handles games and their variants for query filtering.
/// </summary>
public interface IGameRepository
{
    /// <summary>
    /// Initialize the game tables in the database
    /// </summary>
    Task InitializeDatabaseAsync();
    
    /// <summary>
    /// Register a new game with its variants
    /// </summary>
    Task<Guid> RegisterGameAsync(string gameId, string displayName, string[] variants, string source = "manual");
    
    /// <summary>
    /// Get all registered games
    /// </summary>
    Task<List<GameEntity>> GetAllGamesAsync();
    
    /// <summary>
    /// Get a game by its ID
    /// </summary>
    Task<GameEntity?> GetGameByIdAsync(string gameId);
    
    /// <summary>
    /// Get all variants for a specific game
    /// </summary>
    Task<List<GameVariantEntity>> GetGameVariantsAsync(Guid gameId);
    
    /// <summary>
    /// Get all variants for all games (for detection)
    /// </summary>
    Task<Dictionary<string, List<string>>> GetAllVariantsAsync();
    
    /// <summary>
    /// Check if a game exists
    /// </summary>
    Task<bool> GameExistsAsync(string gameId);
    
    /// <summary>
    /// Update game display name
    /// </summary>
    Task UpdateGameDisplayNameAsync(string gameId, string displayName);
    
    /// <summary>
    /// Add a variant to an existing game
    /// </summary>
    Task AddVariantAsync(string gameId, string variant);
    
    /// <summary>
    /// Remove a variant from a game
    /// </summary>
    Task RemoveVariantAsync(string gameId, string variant);
    
    /// <summary>
    /// Delete a game and all its variants
    /// </summary>
    Task DeleteGameAsync(string gameId);
    
    /// <summary>
    /// Search for games matching a query
    /// </summary>
    Task<List<string>> DetectGamesAsync(string query);
    
    /// <summary>
    /// Seed default games (for initial setup)
    /// </summary>
    Task SeedDefaultGamesAsync();
}
