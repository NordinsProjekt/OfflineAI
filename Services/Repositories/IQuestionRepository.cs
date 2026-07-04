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
    /// <param name="question">The user's question/message.</param>
    /// <param name="answer">The LLM's answer.</param>
    /// <param name="llmId">The LLM that generated the answer.</param>
    /// <param name="conversationId">
    /// Optional identifier grouping this turn with other turns from the same
    /// multi-turn conversation/session. Leave null for a standalone turn.
    /// </param>
    Task<Guid> SaveQuestionAsync(string question, string answer, Guid llmId, Guid? conversationId = null);

    /// <summary>
    /// Get all questions from the database.
    /// </summary>
    Task<List<QuestionEntity>> GetAllQuestionsAsync();

    /// <summary>
    /// Get all questions belonging to a single conversation/session, ordered chronologically,
    /// so the full multi-turn conversation can be reconstructed as one unit.
    /// </summary>
    Task<List<QuestionEntity>> GetQuestionsByConversationAsync(Guid conversationId);
    
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
