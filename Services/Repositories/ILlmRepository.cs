using Entities;

namespace Services.Repositories;

/// <summary>
/// Interface for LLM repository operations.
/// Manages the list of available LLM models in the database.
/// </summary>
public interface ILlmRepository
{
    /// <summary>
    /// Initialize database schema for LLMs table.
    /// </summary>
    Task InitializeDatabaseAsync();
    
    /// <summary>
    /// Add a new LLM to the database if it doesn't already exist.
    /// Returns the ID of the existing or newly created LLM.
    /// </summary>
    Task<Guid> AddOrGetLlmAsync(string llmName);
    
    /// <summary>
    /// Get all LLMs from the database.
    /// </summary>
    Task<List<LlmEntity>> GetAllLlmsAsync();
    
    /// <summary>
    /// Get an LLM by its name.
    /// </summary>
    Task<LlmEntity?> GetLlmByNameAsync(string llmName);
    
    /// <summary>
    /// Get an LLM by its ID.
    /// </summary>
    Task<LlmEntity?> GetLlmByIdAsync(Guid id);
    
    /// <summary>
    /// Check if an LLM exists by name.
    /// </summary>
    Task<bool> LlmExistsAsync(string llmName);
    
    /// <summary>
    /// Delete an LLM by ID.
    /// </summary>
    Task DeleteLlmAsync(Guid id);
}
