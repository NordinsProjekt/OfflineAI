using Bunit;
using Entities;
using Microsoft.Extensions.DependencyInjection;
using Services.Repositories;
using AgentHistoryPage = AiDashboard.Components.Pages.AgentHistoryPage;

namespace Presentation.AiDashboard.Tests.Components;

/// <summary>
/// Tests for <see cref="AgentHistoryPage"/>: the read-only view over recorded goal-agent runs.
/// Uses a fake <see cref="IAgentRunRepository"/> (no database involved), and deliberately covers
/// the unregistered-repository case too — the page resolves the repository rather than injecting
/// it precisely because a machine without a configured database has none.
/// </summary>
public class AgentHistoryPageTests : TestContext
{
    private sealed class FakeAgentRunRepository : IAgentRunRepository
    {
        private readonly List<AgentRunEntity> _runs = new();
        private readonly List<AgentRunRequirementEntity> _requirements = new();
        private readonly List<AgentRunEventEntity> _events = new();

        /// <summary>When set, every read throws it.</summary>
        public Exception? ReadFailure { get; set; }

        public List<Guid> DeletedRunIds { get; } = new();

        public void Seed(AgentRunEntity run, params AgentRunRequirementEntity[] requirements)
        {
            _runs.Add(run);
            _requirements.AddRange(requirements);
        }

        public void SeedEvent(AgentRunEventEntity entry) => _events.Add(entry);

        private void FailIfConfigured()
        {
            if (ReadFailure is not null)
                throw ReadFailure;
        }

        public Task<List<AgentRunEntity>> GetRecentRunsAsync(int count = 25)
        {
            FailIfConfigured();
            return Task.FromResult(_runs.OrderByDescending(r => r.StartedAt).Take(count).ToList());
        }

        public Task<AgentRunEntity?> GetRunAsync(Guid runId) =>
            Task.FromResult(_runs.FirstOrDefault(r => r.Id == runId));

        public Task<List<AgentRunRequirementEntity>> GetRequirementsAsync(Guid runId)
        {
            FailIfConfigured();
            return Task.FromResult(_requirements.Where(r => r.RunId == runId).OrderBy(r => r.Ordinal).ToList());
        }

        public Task<List<AgentRunEventEntity>> GetEventsAsync(Guid runId)
        {
            FailIfConfigured();
            return Task.FromResult(_events.Where(e => e.RunId == runId).OrderBy(e => e.Sequence).ToList());
        }

        public Task DeleteRunAsync(Guid runId)
        {
            DeletedRunIds.Add(runId);
            _runs.RemoveAll(r => r.Id == runId);
            return Task.CompletedTask;
        }

        public Task InitializeDatabaseAsync() => Task.CompletedTask;
        public Task StartRunAsync(AgentRunEntity run) => Task.CompletedTask;
        public Task SaveRequirementsAsync(IReadOnlyList<AgentRunRequirementEntity> requirements) => Task.CompletedTask;
        public Task UpdateRequirementAsync(Guid requirementId, string status, string? lastVerdict) => Task.CompletedTask;
        public Task AddEventsAsync(IReadOnlyList<AgentRunEventEntity> events) => Task.CompletedTask;
        public Task CompleteRunAsync(Guid runId, string phase, int iterations, DateTime completedAt) => Task.CompletedTask;
    }

    private static AgentRunEntity Run(string goal, string phase, DateTime? startedAt = null) => new()
    {
        GoalDescription = goal,
        Phase = phase,
        ModelName = "gemma-4.gguf",
        WorkspacePath = @"C:\workspaces\recept",
        MaxIterations = 20,
        Iterations = 3,
        StartedAt = startedAt ?? new DateTime(2026, 7, 17, 10, 0, 0, DateTimeKind.Utc),
        CompletedAt = (startedAt ?? new DateTime(2026, 7, 17, 10, 0, 0, DateTimeKind.Utc)).AddMinutes(4)
    };

    [Fact]
    public void Renders_WithoutRepositoryRegistered_ShowsDatabaseHint()
    {
        var cut = RenderComponent<AgentHistoryPage>();

        Assert.Contains("needs a configured database", cut.Find(".oa-batch-empty").TextContent);
    }

    [Fact]
    public void Renders_WithNoRuns_ShowsEmptyMessage()
    {
        Services.AddSingleton<IAgentRunRepository>(new FakeAgentRunRepository());

        var cut = RenderComponent<AgentHistoryPage>();

        Assert.Contains("No runs recorded yet", cut.Find(".oa-batch-empty").TextContent);
    }

    [Fact]
    public void Renders_WithRuns_ListsNewestFirstWithPhaseBadge()
    {
        var repository = new FakeAgentRunRepository();
        repository.Seed(Run("Ett gammalt mål", "Failed", new DateTime(2026, 7, 16, 9, 0, 0, DateTimeKind.Utc)));
        repository.Seed(Run("Skapa ett pannkaksrecept", "Completed"));
        Services.AddSingleton<IAgentRunRepository>(repository);

        var cut = RenderComponent<AgentHistoryPage>();

        var rows = cut.FindAll(".oa-history-row");
        Assert.Equal(2, rows.Count);
        Assert.Contains("Skapa ett pannkaksrecept", rows[0].TextContent);
        Assert.Contains("All green", rows[0].TextContent);
        Assert.Contains("Ett gammalt mål", rows[1].TextContent);
        Assert.Contains("Failed", rows[1].TextContent);
    }

    [Fact]
    public void ClickRun_ExpandsDetailWithRequirementsAndActivity()
    {
        var repository = new FakeAgentRunRepository();
        var run = Run("Skapa ett pannkaksrecept", "MaxIterationsReached");
        repository.Seed(run,
            new AgentRunRequirementEntity
            {
                RunId = run.Id, Ordinal = 1, Description = "Filen recept.txt finns i arbetsytan.", Status = "Passed"
            },
            new AgentRunRequirementEntity
            {
                RunId = run.Id, Ordinal = 2, Description = "recept.txt innehåller en ingredienslista.",
                Status = "Failed", LastVerdict = "ingredienslistan saknas"
            });
        repository.SeedEvent(new AgentRunEventEntity
        {
            RunId = run.Id, Sequence = 1, EventType = AgentRunEventTypes.Tool, Message = "🔧 /skapa recept.txt"
        });
        Services.AddSingleton<IAgentRunRepository>(repository);
        var cut = RenderComponent<AgentHistoryPage>();

        cut.Find(".oa-history-summary").Click();

        var detail = cut.Find(".oa-history-detail");
        Assert.Contains("1 of 2 passed", detail.TextContent);
        Assert.Contains("Filen recept.txt finns i arbetsytan.", detail.TextContent);
        Assert.Contains("ingredienslistan saknas", cut.Find(".oa-goal-verdict").TextContent);
        Assert.Contains("🔧 /skapa recept.txt", cut.Find(".oa-goal-log").TextContent);
        Assert.Contains("gemma-4.gguf", detail.TextContent);
        Assert.Contains("3 of 20", detail.TextContent);
    }

    [Fact]
    public void ClickSelectedRunAgain_CollapsesDetail()
    {
        var repository = new FakeAgentRunRepository();
        repository.Seed(Run("Skapa ett pannkaksrecept", "Completed"));
        Services.AddSingleton<IAgentRunRepository>(repository);
        var cut = RenderComponent<AgentHistoryPage>();

        cut.Find(".oa-history-summary").Click();
        Assert.NotEmpty(cut.FindAll(".oa-history-detail"));

        cut.Find(".oa-history-summary").Click();

        Assert.Empty(cut.FindAll(".oa-history-detail"));
    }

    [Fact]
    public void UnfinishedRun_IsShownAsSuchRatherThanAsADuration()
    {
        var repository = new FakeAgentRunRepository();
        var run = Run("Skapa ett pannkaksrecept", "Working");
        run.CompletedAt = null;
        repository.Seed(run);
        Services.AddSingleton<IAgentRunRepository>(repository);

        var cut = RenderComponent<AgentHistoryPage>();

        Assert.Contains("unfinished", cut.Find(".oa-history-meta").TextContent);
    }

    [Fact]
    public void DeleteRun_RemovesItFromTheList()
    {
        var repository = new FakeAgentRunRepository();
        var run = Run("Skapa ett pannkaksrecept", "Completed");
        repository.Seed(run);
        Services.AddSingleton<IAgentRunRepository>(repository);
        var cut = RenderComponent<AgentHistoryPage>();
        cut.Find(".oa-history-summary").Click();

        cut.Find(".oa-batch-stop-btn").Click();

        Assert.Equal(new[] { run.Id }, repository.DeletedRunIds);
        Assert.Contains("No runs recorded yet", cut.Find(".oa-batch-empty").TextContent);
    }

    [Fact]
    public void RepositoryThrows_ShowsErrorInsteadOfCrashing()
    {
        var repository = new FakeAgentRunRepository { ReadFailure = new InvalidOperationException("db down") };
        Services.AddSingleton<IAgentRunRepository>(repository);

        var cut = RenderComponent<AgentHistoryPage>();

        Assert.Contains("Could not load run history: db down", cut.Find(".oa-batch-empty").TextContent);
    }
}
