using Entities;

namespace Services.Repositories;

/// <summary>
/// Interface for Question repository operations.
/// Manages the storage and retrieval of questions and their answers.
/// </summary>
public interface IQuestionRepository
{
    /// <summary>
    /// Initialize database schema for Questions table.
    /// </summary>
    Task InitializeDatabaseAsync();
    
    /// <summary>
    /// Save a question and its answer to the database.
    /// </summary>
    Task<Guid> SaveQuestionAsync(string question, string answer, Guid llmId);
    
    /// <summary>
    /// Get all questions from the database.
    /// </summary>
    Task<List<QuestionEntity>> GetAllQuestionsAsync();
    
    /// <summary>
    /// Get questions filtered by LLM ID.
    /// </summary>
    Task<List<QuestionEntity>> GetQuestionsByLlmAsync(Guid llmId);
    
    /// <summary>
    /// Get a specific question by ID.
    /// </summary>
    Task<QuestionEntity?> GetQuestionByIdAsync(Guid id);
    
    /// <summary>
    /// Get recent questions (ordered by creation date descending).
    /// </summary>
    Task<List<QuestionEntity>> GetRecentQuestionsAsync(int count = 10);
    
    /// <summary>
    /// Delete a question by ID.
    /// </summary>
    Task DeleteQuestionAsync(Guid id);
    
    /// <summary>
    /// Search questions by text content.
    /// </summary>
    Task<List<QuestionEntity>> SearchQuestionsAsync(string searchText);
}
