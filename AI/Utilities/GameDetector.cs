using System.Text.RegularExpressions;

namespace Application.AI.Utilities;

/// <summary>
/// Detects game names from user queries to enable game-specific filtering.
/// </summary>
public static class GameDetector
{
    // Known game names and their variants
    private static readonly Dictionary<string, string[]> GameVariants = new()
    {
        ["munchkin-panic"] = new[] 
        { 
            "munchkin panic", 
            "panic", 
            "castle panic munchkin",
            "munchkin castle panic"
        },
        ["munchkin-treasure-hunt"] = new[] 
        { 
            "munchkin treasure hunt", 
            "treasure hunt",
            "munchkin quest",
            "treasure hunting"
        },
        ["munchkin"] = new[] 
        { 
            "munchkin deluxe",
            "munchkin game",
            "base munchkin"
        }
    };

    /// <summary>
    /// Detects which game(s) are mentioned in a query.
    /// Returns normalized game names that can be used for filtering.
    /// </summary>
    /// <param name="query">User's question</param>
    /// <returns>List of detected game identifiers, or empty if no specific game detected</returns>
    public static List<string> DetectGames(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<string>();

        var detectedGames = new List<string>();
        var lowerQuery = query.ToLowerInvariant();

        foreach (var (gameId, variants) in GameVariants)
        {
            foreach (var variant in variants)
            {
                if (lowerQuery.Contains(variant, StringComparison.OrdinalIgnoreCase))
                {
                    if (!detectedGames.Contains(gameId))
                    {
                        detectedGames.Add(gameId);
                    }
                    break; // Found this game, check next one
                }
            }
        }

        return detectedGames;
    }

    /// <summary>
    /// Checks if a fragment category matches one of the detected games.
    /// </summary>
    /// <param name="category">Fragment category (e.g., "Munchkin Panic - Section 1")</param>
    /// <param name="detectedGames">List of detected game IDs</param>
    /// <returns>True if category matches any detected game, false otherwise</returns>
    public static bool MatchesGame(string category, List<string> detectedGames)
    {
        if (detectedGames.Count == 0)
            return true; // No specific game mentioned, include all

        var lowerCategory = category.ToLowerInvariant();

        foreach (var gameId in detectedGames)
        {
            var variants = GameVariants.GetValueOrDefault(gameId, Array.Empty<string>());
            
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
    /// Gets a friendly display name for a game ID.
    /// </summary>
    public static string GetDisplayName(string gameId)
    {
        return gameId switch
        {
            "munchkin-panic" => "Munchkin Panic",
            "munchkin-treasure-hunt" => "Munchkin Treasure Hunt",
            "munchkin" => "Munchkin",
            _ => gameId
        };
    }

    /// <summary>
    /// Adds a new game and its variants to the detector.
    /// Useful for dynamically discovered games.
    /// </summary>
    public static void RegisterGame(string gameId, params string[] variants)
    {
        if (!GameVariants.ContainsKey(gameId))
        {
            GameVariants[gameId] = variants;
        }
    }
}
