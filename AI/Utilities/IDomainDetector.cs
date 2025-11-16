namespace Application.AI.Utilities;

/// <summary>
/// Interface for detecting knowledge domains from user queries.
/// Enables domain-specific filtering and testability.
/// </summary>
public interface IDomainDetector
{
    /// <summary>
    /// Detects which domain(s) are mentioned in a query.
    /// Returns normalized domain IDs that can be used for filtering.
    /// </summary>
    Task<List<string>> DetectDomainsAsync(string query);

    /// <summary>
    /// Checks if a fragment category matches one of the detected domains.
    /// </summary>
    Task<bool> MatchesDomainAsync(string category, List<string> detectedDomains);

    /// <summary>
    /// Extracts the domain name from a fragment category.
    /// </summary>
    string ExtractDomainNameFromCategory(string category);

    /// <summary>
    /// Registers a domain from a category string.
    /// Auto-discovers new domains from processed files.
    /// </summary>
    Task RegisterDomainFromCategoryAsync(string category, string categoryType = "general");

    /// <summary>
    /// Gets a friendly display name for a domain ID.
    /// </summary>
    Task<string> GetDisplayNameAsync(string domainId);

    /// <summary>
    /// Registers a new domain with its variants.
    /// </summary>
    Task RegisterDomainAsync(string domainId, string displayName, string category = "general", params string[] variants);

    /// <summary>
    /// Gets all registered domains.
    /// </summary>
    Task<List<(string DomainId, string DisplayName, string Category)>> GetAllDomainsAsync();

    /// <summary>
    /// Gets domains filtered by category.
    /// </summary>
    Task<List<(string DomainId, string DisplayName)>> GetDomainsByCategoryAsync(string category);

    /// <summary>
    /// Gets all unique categories.
    /// </summary>
    Task<List<string>> GetCategoriesAsync();

    /// <summary>
    /// Deletes a domain and all its variants.
    /// </summary>
    Task DeleteDomainAsync(string domainId);

    /// <summary>
    /// Adds a variant to an existing domain.
    /// </summary>
    Task AddVariantAsync(string domainId, string variant);

    /// <summary>
    /// Updates a domain's category.
    /// </summary>
    Task UpdateCategoryAsync(string domainId, string category);

    /// <summary>
    /// Manually refresh the cache.
    /// </summary>
    Task RefreshCacheAsync();

    /// <summary>
    /// Initialize the database and seed default domains.
    /// Call this during application startup.
    /// </summary>
    Task InitializeAsync();
}
