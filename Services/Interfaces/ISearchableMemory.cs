namespace Services.Interfaces;

/// <summary>
/// Interface for memory implementations that support semantic vector search.
/// </summary>
public interface ISearchableMemory : ILlmMemory
{
    /// <summary>
    /// Search for relevant memory fragments based on semantic similarity.
    /// </summary>
    /// <param name="query">The search query</param>
    /// <param name="topK">Number of top results to return</param>
    /// <param name="minRelevanceScore">Minimum similarity score (0-1)</param>
    /// <param name="domainFilter">Optional list of domain IDs to filter by</param>
    /// <param name="maxCharsPerFragment">Optional maximum characters per fragment</param>
    /// <param name="includeMetadata">Include relevance scores and categories in output</param>
    /// <param name="language">Language for stop word filtering (e.g., "Swedish", "English"). Defaults to "English"</param>
    /// <returns>Formatted string containing relevant fragments, or null if none found</returns>
    Task<string?> SearchRelevantMemoryAsync(
        string query,
        int topK = 5,
        double minRelevanceScore = 0.5,
        List<string>? domainFilter = null,
        int? maxCharsPerFragment = null,
        bool includeMetadata = true,
        string language = "English");
}
