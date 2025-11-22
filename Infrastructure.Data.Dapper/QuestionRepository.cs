using Dapper;
using Entities;
using Microsoft.Data.SqlClient;
using Services.Repositories;

namespace Infrastructure.Data.Dapper;

/// <summary>
/// Dapper-based repository for Question data.
/// Manages questions, answers, and their associated LLMs in SQL Server.
/// </summary>
public class QuestionRepository : IQuestionRepository
{
    private readonly string _connectionString;
    private const string QuestionsTable = "Questions";

    public QuestionRepository(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task InitializeDatabaseAsync()
    {
        var createTableSql = $@"
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = '{QuestionsTable}')
            BEGIN
                CREATE TABLE [{QuestionsTable}] (
                    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
                    Question NVARCHAR(MAX) NOT NULL,
                    Answer NVARCHAR(MAX) NOT NULL,
                    LlmId UNIQUEIDENTIFIER NOT NULL,
                    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                    FOREIGN KEY (LlmId) REFERENCES [LLMs](Id) ON DELETE CASCADE
                );

                CREATE INDEX IX_{QuestionsTable}_LlmId ON [{QuestionsTable}](LlmId);
                CREATE INDEX IX_{QuestionsTable}_CreatedAt ON [{QuestionsTable}](CreatedAt);
            END";

        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(createTableSql);
    }

    public async Task<Guid> SaveQuestionAsync(string question, string answer, Guid llmId)
    {
        if (string.IsNullOrWhiteSpace(question))
            throw new ArgumentException("Question cannot be empty", nameof(question));
        if (string.IsNullOrWhiteSpace(answer))
            throw new ArgumentException("Answer cannot be empty", nameof(answer));
        if (llmId == Guid.Empty)
            throw new ArgumentException("LLM ID cannot be empty", nameof(llmId));

        var entity = new QuestionEntity
        {
            Id = Guid.NewGuid(),
            Question = question,
            Answer = answer,
            LlmId = llmId,
            CreatedAt = DateTime.UtcNow
        };

        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(
            $@"INSERT INTO [{QuestionsTable}] (Id, Question, Answer, LlmId, CreatedAt)
               VALUES (@Id, @Question, @Answer, @LlmId, @CreatedAt)",
            entity);

        return entity.Id;
    }

    public async Task<List<QuestionEntity>> GetAllQuestionsAsync()
    {
        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync<QuestionEntity>(
            $"SELECT * FROM [{QuestionsTable}] ORDER BY CreatedAt DESC");

        return results.AsList();
    }

    public async Task<List<QuestionEntity>> GetQuestionsByLlmAsync(Guid llmId)
    {
        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync<QuestionEntity>(
            $"SELECT * FROM [{QuestionsTable}] WHERE LlmId = @LlmId ORDER BY CreatedAt DESC",
            new { LlmId = llmId });

        return results.AsList();
    }

    public async Task<QuestionEntity?> GetQuestionByIdAsync(Guid id)
    {
        using var connection = new SqlConnection(_connectionString);
        return await connection.QuerySingleOrDefaultAsync<QuestionEntity>(
            $"SELECT * FROM [{QuestionsTable}] WHERE Id = @Id",
            new { Id = id });
    }

    public async Task<List<QuestionEntity>> GetRecentQuestionsAsync(int count = 10)
    {
        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync<QuestionEntity>(
            $"SELECT TOP(@Count) * FROM [{QuestionsTable}] ORDER BY CreatedAt DESC",
            new { Count = count });

        return results.AsList();
    }

    public async Task DeleteQuestionAsync(Guid id)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(
            $"DELETE FROM [{QuestionsTable}] WHERE Id = @Id",
            new { Id = id });
    }

    public async Task<List<QuestionEntity>> SearchQuestionsAsync(string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
            return new List<QuestionEntity>();

        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync<QuestionEntity>(
            $@"SELECT * FROM [{QuestionsTable}] 
               WHERE Question LIKE @SearchText OR Answer LIKE @SearchText 
               ORDER BY CreatedAt DESC",
            new { SearchText = $"%{searchText}%" });

        return results.AsList();
    }
}
