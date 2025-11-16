using System.Text.RegularExpressions;
using Services.Repositories;

namespace Application.AI.Utilities;

/// <summary>
/// Detects game names from user queries to enable game-specific filtering.
/// Now database-backed for dynamic game management.
/// </summary>
public class GameDetector(IGameRepository gameRepository)
{
    private readonly IGameRepository _gameRepository = gameRepository ?? throw new ArgumentNullException(nameof(gameRepository));
    
    // Cache for performance (refreshed periodically)
    private Dictionary<string, List<string>> _variantsCache = new();
    private DateTime _lastCacheRefresh = DateTime.MinValue;
    private readonly TimeSpan _cacheLifetime = TimeSpan.FromMinutes(5);
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    /// <summary>
    /// Detects which game(s) are mentioned in a query.
    /// Returns normalized game names that can be used for filtering.
    /// </summary>
    public async Task<List<string>> DetectGamesAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<string>();

        await RefreshCacheIfNeededAsync();

        var detectedGames = new List<string>();
        var lowerQuery = query.ToLowerInvariant();

        foreach (var (gameId, variants) in _variantsCache)
        {
            foreach (var variant in variants)
            {
                if (lowerQuery.Contains(variant, StringComparison.OrdinalIgnoreCase))
                {
                    if (!detectedGames.Contains(gameId))
                    {
                        detectedGames.Add(gameId);
                    }
                    break;
                }
            }
        }

        return detectedGames;
    }

    /// <summary>
    /// Checks if a fragment category matches one of the detected games.
    /// </summary>
    public async Task<bool> MatchesGameAsync(string category, List<string> detectedGames)
    {
        if (detectedGames.Count == 0)
            return true;

        await RefreshCacheIfNeededAsync();

        var lowerCategory = category.ToLowerInvariant();

        foreach (var gameId in detectedGames)
        {
            var variants = _variantsCache.GetValueOrDefault(gameId, new List<string>());
            
            if (lowerCategory.Contains(gameId.Replace("-", " "), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            
            foreach (var variant in variants)
            {
                if (lowerCategory.Contains(variant, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Extracts the game name from a fragment category.
    /// </summary>
    public string ExtractGameNameFromCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return string.Empty;
        
        var parts = category.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries);
        
        if (parts.Length > 0)
        {
            return parts[0].Trim();
        }
        
        return category.Trim();
    }

    /// <summary>
    /// Registers a game from a category string.
    /// Auto-discovers new games from processed files.
    /// </summary>
    public async Task RegisterGameFromCategoryAsync(string category)
    {
        var gameName = ExtractGameNameFromCategory(category);
        
        if (string.IsNullOrWhiteSpace(gameName))
            return;
        
        var gameId = gameName.ToLowerInvariant().Replace(" ", "-");
        
        // Check if already exists
        if (await _gameRepository.GameExistsAsync(gameId))
            return;
        
        // Register new game
        await _gameRepository.RegisterGameAsync(
            gameId,
            gameName,
            new[] { gameName, gameName.ToLowerInvariant() },
            "auto-discovered");
        
        // Invalidate cache
        await InvalidateCacheAsync();
    }

    /// <summary>
    /// Gets a friendly display name for a game ID.
    /// </summary>
    public async Task<string> GetDisplayNameAsync(string gameId)
    {
        var game = await _gameRepository.GetGameByIdAsync(gameId);
        return game?.DisplayName ?? gameId;
    }

    /// <summary>
    /// Registers a new game with its variants.
    /// </summary>
    public async Task RegisterGameAsync(string gameId, string displayName, params string[] variants)
    {
        await _gameRepository.RegisterGameAsync(gameId, displayName, variants);
        await InvalidateCacheAsync();
    }

    /// <summary>
    /// Gets all registered games.
    /// </summary>
    public async Task<List<(string GameId, string DisplayName)>> GetAllGamesAsync()
    {
        var games = await _gameRepository.GetAllGamesAsync();
        return games.Select(g => (g.GameId, g.DisplayName)).ToList();
    }

    /// <summary>
    /// Deletes a game and all its variants.
    /// </summary>
    public async Task DeleteGameAsync(string gameId)
    {
        await _gameRepository.DeleteGameAsync(gameId);
        await InvalidateCacheAsync();
    }

    /// <summary>
    /// Adds a variant to an existing game.
    /// </summary>
    public async Task AddVariantAsync(string gameId, string variant)
    {
        await _gameRepository.AddVariantAsync(gameId, variant);
        await InvalidateCacheAsync();
    }

    /// <summary>
    /// Manually refresh the cache.
    /// </summary>
    public async Task RefreshCacheAsync()
    {
        await _cacheLock.WaitAsync();
        try
        {
            _variantsCache = await _gameRepository.GetAllVariantsAsync();
            _lastCacheRefresh = DateTime.UtcNow;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <summary>
    /// Invalidate the cache (forces refresh on next use).
    /// </summary>
    private async Task InvalidateCacheAsync()
    {
        await _cacheLock.WaitAsync();
        try
        {
            _lastCacheRefresh = DateTime.MinValue;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <summary>
    /// Refresh cache if it's expired.
    /// </summary>
    private async Task RefreshCacheIfNeededAsync()
    {
        if (DateTime.UtcNow - _lastCacheRefresh > _cacheLifetime)
        {
            await RefreshCacheAsync();
        }
    }

    /// <summary>
    /// Initialize the database and seed default games.
    /// Call this during application startup.
    /// </summary>
    public async Task InitializeAsync()
    {
        await _gameRepository.InitializeDatabaseAsync();
        await _gameRepository.SeedDefaultGamesAsync();
        await RefreshCacheAsync();
    }
}
