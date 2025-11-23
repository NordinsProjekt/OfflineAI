using System.Text.RegularExpressions;
using Services.Repositories;

namespace Application.AI.Utilities;

/// <summary>
/// Detects knowledge domains from user queries to enable domain-specific filtering.
/// Now database-backed for dynamic domain management.
/// </summary>
public class DomainDetector(IKnowledgeDomainRepository domainRepository) : IDomainDetector
{
    private readonly IKnowledgeDomainRepository _domainRepository = domainRepository ?? throw new ArgumentNullException(nameof(domainRepository));
    
    // Cache for performance (refreshed periodically)
    private Dictionary<string, List<string>> _variantsCache = new();
    private DateTime _lastCacheRefresh = DateTime.MinValue;
    private readonly TimeSpan _cacheLifetime = TimeSpan.FromMinutes(5);
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    /// <summary>
    /// Detects which domain(s) are mentioned in a query.
    /// Returns normalized domain IDs that can be used for filtering.
    /// </summary>
    public async Task<List<string>> DetectDomainsAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<string>();

        await RefreshCacheIfNeededAsync();

        var detectedDomains = new List<string>();
        var lowerQuery = query.ToLowerInvariant();

        foreach (var (domainId, variants) in _variantsCache)
        {
            foreach (var variant in variants)
            {
                if (lowerQuery.Contains(variant, StringComparison.OrdinalIgnoreCase))
                {
                    if (!detectedDomains.Contains(domainId))
                    {
                        detectedDomains.Add(domainId);
                    }
                    break;
                }
            }
        }

        return detectedDomains;
    }

    /// <summary>
    /// Checks if a fragment category matches one of the detected domains.
    /// </summary>
    public async Task<bool> MatchesDomainAsync(string category, List<string> detectedDomains)
    {
        if (detectedDomains.Count == 0)
            return true; // No domain filter, include all

        await RefreshCacheIfNeededAsync();

        var lowerCategory = category.ToLowerInvariant();

        foreach (var domainId in detectedDomains)
        {
            var variants = _variantsCache.GetValueOrDefault(domainId, new List<string>());
            
            // Check domain ID match (with spaces instead of hyphens)
            if (lowerCategory.Contains(domainId.Replace("-", " "), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            
            // Check variant matches
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
    /// Extracts the domain name from a fragment category.
    /// </summary>
    public string ExtractDomainNameFromCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return string.Empty;
        
        // Category format: "Domain Name - Section" or just "Domain Name"
        var parts = category.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries);
        
        if (parts.Length > 0)
        {
            return parts[0].Trim();
        }
        
        return category.Trim();
    }

    /// <summary>
    /// Registers a domain from a category string.
    /// Auto-discovers new domains from processed files.
    /// </summary>
    public async Task RegisterDomainFromCategoryAsync(string category, string categoryType = "general")
    {
        // Clean markdown headers (##) from category before processing
        var cleanCategory = category?.Replace("##", "").Trim();
        
        var domainName = ExtractDomainNameFromCategory(cleanCategory ?? string.Empty);
        
        if (string.IsNullOrWhiteSpace(domainName))
            return;
        
        var domainId = domainName.ToLowerInvariant().Replace(" ", "-");
        
        // Check if already exists
        if (await _domainRepository.DomainExistsAsync(domainId))
            return;
        
        // Register new domain
        await _domainRepository.RegisterDomainAsync(
            domainId,
            domainName,
            new[] { domainName, domainName.ToLowerInvariant() },
            category: categoryType,
            source: "auto-discovered");
        
        // Invalidate cache
        await InvalidateCacheAsync();
    }

    /// <summary>
    /// Gets a friendly display name for a domain ID.
    /// </summary>
    public async Task<string> GetDisplayNameAsync(string domainId)
    {
        var domain = await _domainRepository.GetDomainByIdAsync(domainId);
        return domain?.DisplayName ?? domainId;
    }

    /// <summary>
    /// Registers a new domain with its variants.
    /// </summary>
    public async Task RegisterDomainAsync(
        string domainId, 
        string displayName, 
        string category = "general",
        params string[] variants)
    {
        await _domainRepository.RegisterDomainAsync(domainId, displayName, variants, category);
        await InvalidateCacheAsync();
    }

    /// <summary>
    /// Gets all registered domains.
    /// </summary>
    public async Task<List<(string DomainId, string DisplayName, string Category)>> GetAllDomainsAsync()
    {
        var domains = await _domainRepository.GetAllDomainsAsync();
        return domains.Select(d => (d.DomainId, d.DisplayName, d.Category)).ToList();
    }

    /// <summary>
    /// Gets domains filtered by category.
    /// </summary>
    public async Task<List<(string DomainId, string DisplayName)>> GetDomainsByCategoryAsync(string category)
    {
        var domains = await _domainRepository.GetDomainsByCategoryAsync(category);
        return domains.Select(d => (d.DomainId, d.DisplayName)).ToList();
    }

    /// <summary>
    /// Gets all unique categories.
    /// </summary>
    public async Task<List<string>> GetCategoriesAsync()
    {
        return await _domainRepository.GetCategoriesAsync();
    }

    /// <summary>
    /// Deletes a domain and all its variants.
    /// </summary>
    public async Task DeleteDomainAsync(string domainId)
    {
        await _domainRepository.DeleteDomainAsync(domainId);
        await InvalidateCacheAsync();
    }

    /// <summary>
    /// Adds a variant to an existing domain.
    /// </summary>
    public async Task AddVariantAsync(string domainId, string variant)
    {
        await _domainRepository.AddVariantAsync(domainId, variant);
        await InvalidateCacheAsync();
    }

    /// <summary>
    /// Updates a domain's category.
    /// </summary>
    public async Task UpdateCategoryAsync(string domainId, string category)
    {
        await _domainRepository.UpdateDomainCategoryAsync(domainId, category);
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
            _variantsCache = await _domainRepository.GetAllVariantsAsync();
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
    /// Initialize the database and seed default domains.
    /// Call this during application startup.
    /// </summary>
    public async Task InitializeAsync()
    {
        await _domainRepository.InitializeDatabaseAsync();
        await _domainRepository.SeedDefaultDomainsAsync();
        await RefreshCacheAsync();
    }
}
