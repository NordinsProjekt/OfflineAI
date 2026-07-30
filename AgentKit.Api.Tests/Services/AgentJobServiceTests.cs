using AgentKit.Api.Services;
using Application.AI.Pooling;
using Entities;
using Microsoft.Extensions.Logging;
using Moq;
using Services.Configuration;
using Services.Repositories;

namespace AgentKit.Api.Tests.Services;

/// <summary>
/// Unit tests for <see cref="AgentJobService"/>. Deliberately does NOT exercise
/// <see cref="AgentJobService.StartJob"/>'s success path: that kicks off a real background run
/// against the real <see cref="IModelInstancePool"/>/LLM backend, which must not happen in
/// automated tests (see CLAUDE.md / project constraint: no real LLM calls). The goal-agent loop
/// itself is already covered by <c>Services.Tests/GoalAgent/GoalAgentServiceTests.cs</c> with a
/// fake LLM delegate — this class only tests the job-service plumbing around it: unknown-job
/// handling, and the DB-backed status/summary mapping, none of which touch the LLM.
/// </summary>
public class AgentJobServiceTests
{
    private readonly Mock<IModelInstancePool> _mockPool = new();
    private readonly AppConfiguration _appConfig = new();

    private AgentJobService CreateSut(IAgentRunRepository? repository = null) =>
        new(_mockPool.Object, _appConfig, new Mock<ILogger<AgentJobService>>().Object, repository);

    // ── Constructor ─────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullModelPool_ThrowsArgumentNullException()
    {
        var act = () => new AgentJobService(null!, _appConfig, new Mock<ILogger<AgentJobService>>().Object);
        Assert.Throws<ArgumentNullException>(act);
    }

    [Fact]
    public void Constructor_NullAppConfig_ThrowsArgumentNullException()
    {
        var act = () => new AgentJobService(_mockPool.Object, null!, new Mock<ILogger<AgentJobService>>().Object);
        Assert.Throws<ArgumentNullException>(act);
    }

    // ── StartJob input validation (fires before anything touches the LLM) ──

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void StartJob_EmptyGoalDescription_ThrowsWithoutStartingAnything(string goalDescription)
    {
        var sut = CreateSut();
        Action act = () => sut.StartJob(goalDescription, null);
        Assert.Throws<ArgumentException>(act);
    }

    // ── Unknown-job behavior (no job was ever started, so this never touches the LLM) ──

    [Fact]
    public void GetStatus_UnknownJob_ReturnsNull()
    {
        var sut = CreateSut();
        Assert.Null(sut.GetStatus(Guid.NewGuid()));
    }

    [Fact]
    public void GetWorkspacePath_UnknownJob_ReturnsNull()
    {
        var sut = CreateSut();
        Assert.Null(sut.GetWorkspacePath(Guid.NewGuid()));
    }

    [Fact]
    public void RequestStop_UnknownJob_ReturnsFalse()
    {
        var sut = CreateSut();
        Assert.False(sut.RequestStop(Guid.NewGuid()));
    }

    // ── No repository configured ─────────────────────────────────────────

    [Fact]
    public async Task GetPersistedStatusAsync_NoRepositoryConfigured_ReturnsNull()
    {
        var sut = CreateSut(repository: null);
        Assert.Null(await sut.GetPersistedStatusAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetRecentJobsAsync_NoRepositoryConfigured_ReturnsEmpty()
    {
        var sut = CreateSut(repository: null);
        var jobs = await sut.GetRecentJobsAsync(25);
        Assert.Empty(jobs);
    }

    // ── Persisted status / recent-jobs mapping (fake repository, no LLM involved) ──

    [Fact]
    public async Task GetPersistedStatusAsync_ExistingRun_MapsEntityFieldsToStatus()
    {
        var runId = Guid.NewGuid();
        var repository = new FakeAgentRunRepository();
        repository.Runs[runId] = new AgentRunEntity
        {
            Id = runId,
            GoalDescription = "Skapa ett pannkaksrecept",
            Phase = "Completed",
            Iterations = 3,
            MaxIterations = 20
        };
        repository.Requirements[runId] = new List<AgentRunRequirementEntity>
        {
            new() { RunId = runId, Ordinal = 2, Description = "krav B", Status = "Passed" },
            new() { RunId = runId, Ordinal = 1, Description = "krav A", Status = "Failed", LastVerdict = "saknas" }
        };
        repository.Events[runId] = new List<AgentRunEventEntity>
        {
            new() { RunId = runId, Message = "först" },
            new() { RunId = runId, Message = "sedan" }
        };
        var sut = CreateSut(repository);

        var status = await sut.GetPersistedStatusAsync(runId);

        Assert.NotNull(status);
        Assert.Equal("Skapa ett pannkaksrecept", status!.GoalDescription);
        Assert.Equal("Completed", status.Phase);
        Assert.Equal(3, status.CurrentIteration);
        Assert.Equal(20, status.MaxIterations);
        // Ordered by Ordinal, not insertion order.
        Assert.Equal(new[] { "krav A", "krav B" }, status.Requirements.Select(r => r.Description));
        Assert.Equal("saknas", status.Requirements[0].LastVerdict);
        Assert.Equal(new[] { "först", "sedan" }, status.ActivityLog);
    }

    [Fact]
    public async Task GetPersistedStatusAsync_UnknownRun_ReturnsNull()
    {
        var sut = CreateSut(new FakeAgentRunRepository());
        Assert.Null(await sut.GetPersistedStatusAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetRecentJobsAsync_MapsEntitiesToSummaries()
    {
        var repository = new FakeAgentRunRepository();
        var runId = Guid.NewGuid();
        repository.Runs[runId] = new AgentRunEntity
        {
            Id = runId,
            GoalDescription = "Mål",
            Phase = "Completed",
            Iterations = 2,
            MaxIterations = 20,
            StartedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CompletedAt = new DateTime(2026, 1, 1, 0, 5, 0, DateTimeKind.Utc)
        };
        var sut = CreateSut(repository);

        var summaries = await sut.GetRecentJobsAsync(25);

        var summary = Assert.Single(summaries);
        Assert.Equal(runId, summary.JobId);
        Assert.Equal("Mål", summary.GoalDescription);
        Assert.Equal("Completed", summary.Phase);
        Assert.Equal(2, summary.Iterations);
        Assert.Equal(20, summary.MaxIterations);
    }

    private sealed class FakeAgentRunRepository : IAgentRunRepository
    {
        public Dictionary<Guid, AgentRunEntity> Runs { get; } = new();
        public Dictionary<Guid, List<AgentRunRequirementEntity>> Requirements { get; } = new();
        public Dictionary<Guid, List<AgentRunEventEntity>> Events { get; } = new();

        public Task InitializeDatabaseAsync() => Task.CompletedTask;
        public Task StartRunAsync(AgentRunEntity run) => Task.CompletedTask;
        public Task SaveRequirementsAsync(IReadOnlyList<AgentRunRequirementEntity> requirements) => Task.CompletedTask;
        public Task UpdateRequirementAsync(Guid requirementId, string status, string? lastVerdict) => Task.CompletedTask;
        public Task AddEventsAsync(IReadOnlyList<AgentRunEventEntity> events) => Task.CompletedTask;
        public Task CompleteRunAsync(Guid runId, string phase, int iterations, DateTime completedAt) => Task.CompletedTask;

        public Task<List<AgentRunEntity>> GetRecentRunsAsync(int count = 25) => Task.FromResult(Runs.Values.ToList());

        public Task<AgentRunEntity?> GetRunAsync(Guid runId) =>
            Task.FromResult(Runs.TryGetValue(runId, out var r) ? r : null);

        public Task<List<AgentRunRequirementEntity>> GetRequirementsAsync(Guid runId) =>
            Task.FromResult(Requirements.TryGetValue(runId, out var list) ? list : new List<AgentRunRequirementEntity>());

        public Task<List<AgentRunEventEntity>> GetEventsAsync(Guid runId) =>
            Task.FromResult(Events.TryGetValue(runId, out var list) ? list : new List<AgentRunEventEntity>());

        public Task DeleteRunAsync(Guid runId) => Task.CompletedTask;
    }
}
