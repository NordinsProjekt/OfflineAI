using Dapper;
using Entities;
using Microsoft.Data.SqlClient;
using Services.Repositories;

namespace Infrastructure.Data.Dapper;

/// <summary>
/// Dapper-based repository for goal-agent run history.
/// Manages runs, their requirements, and their activity-log events in SQL Server.
/// </summary>
public class AgentRunRepository : IAgentRunRepository
{
    private readonly string _connectionString;

    private const string RunsTable = "AgentRuns";
    private const string RunsTableRef = "[AgentRuns]";
    private const string RequirementsTable = "AgentRunRequirements";
    private const string RequirementsTableRef = "[AgentRunRequirements]";
    private const string EventsTable = "AgentRunEvents";
    private const string EventsTableRef = "[AgentRunEvents]";

    public AgentRunRepository(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task InitializeDatabaseAsync()
    {
        // The child tables cascade from the run, so DeleteRunAsync only has to delete one row.
        var createTablesSql = $@"
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = '{RunsTable}')
            BEGIN
                CREATE TABLE {RunsTableRef} (
                    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
                    GoalDescription NVARCHAR(MAX) NOT NULL,
                    WorkspacePath NVARCHAR(1000) NULL,
                    ModelName NVARCHAR(500) NULL,
                    ConversationId UNIQUEIDENTIFIER NULL,
                    MaxIterations INT NOT NULL,
                    Iterations INT NOT NULL,
                    Phase NVARCHAR(100) NOT NULL,
                    StartedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                    CompletedAt DATETIME2 NULL
                );

                CREATE INDEX IX_{RunsTable}_StartedAt ON {RunsTableRef}(StartedAt);
                CREATE INDEX IX_{RunsTable}_ConversationId ON {RunsTableRef}(ConversationId);
            END

            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = '{RequirementsTable}')
            BEGIN
                CREATE TABLE {RequirementsTableRef} (
                    Id UNIQUEIDENTIFIER PRIMARY KEY,
                    RunId UNIQUEIDENTIFIER NOT NULL,
                    Ordinal INT NOT NULL,
                    Description NVARCHAR(MAX) NOT NULL,
                    Status NVARCHAR(100) NOT NULL,
                    LastVerdict NVARCHAR(MAX) NULL,
                    UpdatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                    FOREIGN KEY (RunId) REFERENCES {RunsTableRef}(Id) ON DELETE CASCADE
                );

                CREATE INDEX IX_{RequirementsTable}_RunId ON {RequirementsTableRef}(RunId);
            END

            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = '{EventsTable}')
            BEGIN
                CREATE TABLE {EventsTableRef} (
                    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
                    RunId UNIQUEIDENTIFIER NOT NULL,
                    -- Bracketed throughout: SEQUENCE is an ODBC reserved word.
                    [Sequence] INT NOT NULL,
                    EventType NVARCHAR(50) NOT NULL,
                    Iteration INT NULL,
                    Message NVARCHAR(MAX) NOT NULL,
                    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                    FOREIGN KEY (RunId) REFERENCES {RunsTableRef}(Id) ON DELETE CASCADE
                );

                CREATE INDEX IX_{EventsTable}_RunId_Sequence ON {EventsTableRef}(RunId, [Sequence]);
            END";

        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(createTablesSql);
    }

    public async Task StartRunAsync(AgentRunEntity run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (string.IsNullOrWhiteSpace(run.GoalDescription))
            throw new ArgumentException("Goal description cannot be empty", nameof(run));

        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(
            $@"INSERT INTO {RunsTableRef}
               (Id, GoalDescription, WorkspacePath, ModelName, ConversationId, MaxIterations, Iterations, Phase, StartedAt, CompletedAt)
               VALUES
               (@Id, @GoalDescription, @WorkspacePath, @ModelName, @ConversationId, @MaxIterations, @Iterations, @Phase, @StartedAt, @CompletedAt)",
            run);
    }

    public async Task SaveRequirementsAsync(IReadOnlyList<AgentRunRequirementEntity> requirements)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        if (requirements.Count == 0)
            return;

        using var connection = new SqlConnection(_connectionString);
        // Dapper turns an enumerable parameter into one round trip with a batched insert.
        await connection.ExecuteAsync(
            $@"INSERT INTO {RequirementsTableRef}
               (Id, RunId, Ordinal, Description, Status, LastVerdict, UpdatedAt)
               VALUES
               (@Id, @RunId, @Ordinal, @Description, @Status, @LastVerdict, @UpdatedAt)",
            requirements);
    }

    public async Task UpdateRequirementAsync(Guid requirementId, string status, string? lastVerdict)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(
            $@"UPDATE {RequirementsTableRef}
               SET Status = @Status, LastVerdict = @LastVerdict, UpdatedAt = @UpdatedAt
               WHERE Id = @Id",
            new { Id = requirementId, Status = status, LastVerdict = lastVerdict, UpdatedAt = DateTime.UtcNow });
    }

    public async Task AddEventsAsync(IReadOnlyList<AgentRunEventEntity> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0)
            return;

        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(
            $@"INSERT INTO {EventsTableRef}
               (Id, RunId, [Sequence], EventType, Iteration, Message, CreatedAt)
               VALUES
               (@Id, @RunId, @Sequence, @EventType, @Iteration, @Message, @CreatedAt)",
            events);
    }

    public async Task CompleteRunAsync(Guid runId, string phase, int iterations, DateTime completedAt)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(
            $@"UPDATE {RunsTableRef}
               SET Phase = @Phase, Iterations = @Iterations, CompletedAt = @CompletedAt
               WHERE Id = @Id",
            new { Id = runId, Phase = phase, Iterations = iterations, CompletedAt = completedAt });
    }

    public async Task<List<AgentRunEntity>> GetRecentRunsAsync(int count = 25)
    {
        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync<AgentRunEntity>(
            $"SELECT TOP(@Count) * FROM {RunsTableRef} ORDER BY StartedAt DESC",
            new { Count = count });

        return results.AsList();
    }

    public async Task<AgentRunEntity?> GetRunAsync(Guid runId)
    {
        using var connection = new SqlConnection(_connectionString);
        return await connection.QuerySingleOrDefaultAsync<AgentRunEntity>(
            $"SELECT * FROM {RunsTableRef} WHERE Id = @Id",
            new { Id = runId });
    }

    public async Task<List<AgentRunRequirementEntity>> GetRequirementsAsync(Guid runId)
    {
        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync<AgentRunRequirementEntity>(
            $"SELECT * FROM {RequirementsTableRef} WHERE RunId = @RunId ORDER BY Ordinal ASC",
            new { RunId = runId });

        return results.AsList();
    }

    public async Task<List<AgentRunEventEntity>> GetEventsAsync(Guid runId)
    {
        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync<AgentRunEventEntity>(
            $"SELECT * FROM {EventsTableRef} WHERE RunId = @RunId ORDER BY [Sequence] ASC",
            new { RunId = runId });

        return results.AsList();
    }

    public async Task DeleteRunAsync(Guid runId)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(
            $"DELETE FROM {RunsTableRef} WHERE Id = @Id",
            new { Id = runId });
    }
}
