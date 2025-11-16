using Entities;

namespace Services.Repositories;

/// <summary>
/// Repository interface for managing knowledge domains.
/// Handles domains and their variants for query filtering and categorization.
/// </summary>
public interface IKnowledgeDomainRepository
{
    /// <summary>
    /// Initialize the domain tables in the database
    /// </summary>
    Task InitializeDatabaseAsync();
    
    /// <summary>
    /// Register a new domain with its variants
    /// </summary>
    Task<Guid> RegisterDomainAsync(
        string domainId, 
        string displayName, 
        string[] variants, 
        string category = "general",
        string source = "manual");
    
    /// <summary>
    /// Get all registered domains
    /// </summary>
    Task<List<KnowledgeDomainEntity>> GetAllDomainsAsync();
    
    /// <summary>
    /// Get domains filtered by category
    /// </summary>
    Task<List<KnowledgeDomainEntity>> GetDomainsByCategoryAsync(string category);
    
    /// <summary>
    /// Get a domain by its ID
    /// </summary>
    Task<KnowledgeDomainEntity?> GetDomainByIdAsync(string domainId);
    
    /// <summary>
    /// Get all variants for a specific domain
    /// </summary>
    Task<List<DomainVariantEntity>> GetDomainVariantsAsync(Guid domainId);
    
    /// <summary>
    /// Get all variants for all domains (for detection)
    /// Returns dictionary of domainId ? list of variant strings
    /// </summary>
    Task<Dictionary<string, List<string>>> GetAllVariantsAsync();
    
    /// <summary>
    /// Check if a domain exists
    /// </summary>
    Task<bool> DomainExistsAsync(string domainId);
    
    /// <summary>
    /// Update domain display name
    /// </summary>
    Task UpdateDomainDisplayNameAsync(string domainId, string displayName);
    
    /// <summary>
    /// Update domain category
    /// </summary>
    Task UpdateDomainCategoryAsync(string domainId, string category);
    
    /// <summary>
    /// Add a variant to an existing domain
    /// </summary>
    Task AddVariantAsync(string domainId, string variant);
    
    /// <summary>
    /// Remove a variant from a domain
    /// </summary>
    Task RemoveVariantAsync(string domainId, string variant);
    
    /// <summary>
    /// Delete a domain and all its variants
    /// </summary>
    Task DeleteDomainAsync(string domainId);
    
    /// <summary>
    /// Search for domains matching a query
    /// </summary>
    Task<List<string>> DetectDomainsAsync(string query);
    
    /// <summary>
    /// Seed default domains (for initial setup)
    /// </summary>
    Task SeedDefaultDomainsAsync();
    
    /// <summary>
    /// Get all unique categories
    /// </summary>
    Task<List<string>> GetCategoriesAsync();
}
