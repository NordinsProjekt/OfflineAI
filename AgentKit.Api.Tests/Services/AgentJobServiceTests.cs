using AgentKit.Api.Models;
using AgentKit.Api.Services;
using Application.AI.Pooling;
using Entities;
using Microsoft.Extensions.Logging;
using Moq;
using Services.Configuration;
using Services.Repositories;

namespace AgentKit.Api.Tests.Services;

/// <summary>
/// Unit tests for <see cref="AgentJobService"/>. Deliberately does NOT exercise a job's actual
/// execution: that kicks off a real background run against the real <see cref="IModelInstancePool"/>
/// /LLM backend, which must not happen in automated tests (see CLAUDE.md / project constraint: no
/// real LLM calls). The goal-agent loop itself is already covered by
/// <c>Services.Tests/GoalAgent/GoalAgentServiceTests.cs</c> with a fake LLM delegate. This class
/// covers everything AROUND that: unknown-job handling, DB-backed status/summary mapping, the
/// local-vs-forward-to-peer routing decision (via a mocked <see cref="IClusterPeerClient"/>, so no
/// real network call happens either), and proxying for a job tracked as remote.
/// </summary>
public sealed class AgentJobServiceTests : IDisposable
{
    private readonly Mock<IModelInstancePool> _mockPool = new();
    private readonly Mock<IClusterPeerClient> _mockPeerClient = new();
    private readonly AppConfiguration _appConfig = new();
    private readonly List<string> _tempDirs = new();

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    private AgentJobService CreateSut(IAgentRunRepository? repository = null) =>
        new(_mockPool.Object, _appConfig, _mockPeerClient.Object, new Mock<ILogger<AgentJobService>>().Object, repository);

    private static ClusterPeerSettings MakePeer(string name) => new() { Name = name, BaseUrl = $"https://{name}:7016", ApiKey = "key" };

    // ── Constructor ─────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullModelPool_ThrowsArgumentNullException()
    {
        var act = () => new AgentJobService(null!, _appConfig, _mockPeerClient.Object, new Mock<ILogger<AgentJobService>>().Object);
        Assert.Throws<ArgumentNullException>(act);
    }

    [Fact]
    public void Constructor_NullAppConfig_ThrowsArgumentNullException()
    {
        var act = () => new AgentJobService(_mockPool.Object, null!, _mockPeerClient.Object, new Mock<ILogger<AgentJobService>>().Object);
        Assert.Throws<ArgumentNullException>(act);
    }

    [Fact]
    public void Constructor_NullClusterPeerClient_ThrowsArgumentNullException()
    {
        var act = () => new AgentJobService(_mockPool.Object, _appConfig, null!, new Mock<ILogger<AgentJobService>>().Object);
        Assert.Throws<ArgumentNullException>(act);
    }

    // ── StartJobAsync input validation (fires before anything touches the LLM or a peer) ──

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task StartJobAsync_EmptyGoalDescription_ThrowsWithoutStartingAnything(string goalDescription)
    {
        var sut = CreateSut();
        await Assert.ThrowsAsync<ArgumentException>(() => sut.StartJobAsync(goalDescription, null, CancellationToken.None));
        _mockPeerClient.Verify(c => c.GetAvailableCapacityAsync(It.IsAny<ClusterPeerSettings>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── StartJobAsync routing decision ──────────────────────────────────

    [Fact]
    public async Task StartJobAsync_NoPeersConfigured_NeverTouchesPeerClient()
    {
        _mockPool.Setup(p => p.AvailableCount).Returns(0);
        var sut = CreateSut();

        await sut.StartJobAsync("Mål", null, CancellationToken.None);

        _mockPeerClient.Verify(c => c.GetAvailableCapacityAsync(It.IsAny<ClusterPeerSettings>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockPeerClient.Verify(c => c.ForwardJobAsync(It.IsAny<ClusterPeerSettings>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartJobAsync_LocalHasRoom_NeverChecksPeersEvenIfConfigured()
    {
        _appConfig.Cluster.Peers.Add(MakePeer("peer1"));
        _mockPool.Setup(p => p.AvailableCount).Returns(2);
        var sut = CreateSut();

        await sut.StartJobAsync("Mål", null, CancellationToken.None);

        _mockPeerClient.Verify(c => c.GetAvailableCapacityAsync(It.IsAny<ClusterPeerSettings>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartJobAsync_LocalSaturatedAndPeerHasCapacity_ForwardsToThatPeer()
    {
        var peer = MakePeer("peer1");
        _appConfig.Cluster.Peers.Add(peer);
        _mockPool.Setup(p => p.AvailableCount).Returns(0);
        _mockPeerClient.Setup(c => c.GetAvailableCapacityAsync(peer, It.IsAny<CancellationToken>())).ReturnsAsync(3);
        var forwardedPeerJobId = Guid.NewGuid();
        _mockPeerClient
            .Setup(c => c.ForwardJobAsync(peer, "Mål", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(forwardedPeerJobId);
        var sut = CreateSut();

        var jobId = await sut.StartJobAsync("Mål", null, CancellationToken.None);

        _mockPeerClient.Verify(c => c.ForwardJobAsync(peer, "Mål", null, It.IsAny<CancellationToken>()), Times.Once);

        // The job is now tracked as remote — GetStatusAsync must proxy to that same peer/id.
        var remoteStatus = new AgentJobStatus(forwardedPeerJobId, "Working", "Mål", 1, 20, new List<AgentJobRequirementStatus>(), new List<string>());
        _mockPeerClient.Setup(c => c.GetRemoteStatusAsync(peer, forwardedPeerJobId, It.IsAny<CancellationToken>())).ReturnsAsync(remoteStatus);

        var status = await sut.GetStatusAsync(jobId, CancellationToken.None);
        Assert.Same(remoteStatus, status);
    }

    [Fact]
    public async Task StartJobAsync_FirstPeerHasNoCapacity_TriesSecondPeer()
    {
        var peer1 = MakePeer("peer1");
        var peer2 = MakePeer("peer2");
        _appConfig.Cluster.Peers.Add(peer1);
        _appConfig.Cluster.Peers.Add(peer2);
        _mockPool.Setup(p => p.AvailableCount).Returns(0);
        _mockPeerClient.Setup(c => c.GetAvailableCapacityAsync(peer1, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        _mockPeerClient.Setup(c => c.GetAvailableCapacityAsync(peer2, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _mockPeerClient
            .Setup(c => c.ForwardJobAsync(peer2, It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        var sut = CreateSut();

        await sut.StartJobAsync("Mål", null, CancellationToken.None);

        _mockPeerClient.Verify(c => c.ForwardJobAsync(peer1, It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockPeerClient.Verify(c => c.ForwardJobAsync(peer2, It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartJobAsync_EveryPeerUnreachableOrFull_FallsBackToLocalWithoutThrowing()
    {
        var peer1 = MakePeer("peer1");
        var peer2 = MakePeer("peer2");
        _appConfig.Cluster.Peers.Add(peer1);
        _appConfig.Cluster.Peers.Add(peer2);
        _mockPool.Setup(p => p.AvailableCount).Returns(0);
        _mockPeerClient.Setup(c => c.GetAvailableCapacityAsync(peer1, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        _mockPeerClient.Setup(c => c.GetAvailableCapacityAsync(peer2, It.IsAny<CancellationToken>())).ReturnsAsync((int?)null); // unreachable
        var sut = CreateSut();

        // Must complete without throwing and without ever forwarding — falls back to a local
        // (queued) run, exactly like Phase 1's only behavior.
        var jobId = await sut.StartJobAsync("Mål", null, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, jobId);
        _mockPeerClient.Verify(c => c.ForwardJobAsync(It.IsAny<ClusterPeerSettings>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Unknown-job behavior (no job was ever started, so this never touches the LLM or a peer) ──

    [Fact]
    public async Task GetStatusAsync_UnknownJobAndNoRepository_ReturnsNull()
    {
        var sut = CreateSut();
        Assert.Null(await sut.GetStatusAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task GetResultZipAsync_UnknownJob_ReturnsNull()
    {
        var sut = CreateSut();
        Assert.Null(await sut.GetResultZipAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task RequestStopAsync_UnknownJob_ReturnsFalse()
    {
        var sut = CreateSut();
        Assert.False(await sut.RequestStopAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task GetRecentJobsAsync_NoRepositoryConfigured_ReturnsEmpty()
    {
        var sut = CreateSut(repository: null);
        var jobs = await sut.GetRecentJobsAsync(25, CancellationToken.None);
        Assert.Empty(jobs);
    }

    // ── Persisted status / recent-jobs mapping (fake repository, no LLM or peer involved) ──

    [Fact]
    public async Task GetStatusAsync_UnknownToProcessButPersisted_FallsBackToDatabase()
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

        var status = await sut.GetStatusAsync(runId, CancellationToken.None);

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

        var summaries = await sut.GetRecentJobsAsync(25, CancellationToken.None);

        var summary = Assert.Single(summaries);
        Assert.Equal(runId, summary.JobId);
        Assert.Equal("Mål", summary.GoalDescription);
        Assert.Equal("Completed", summary.Phase);
        Assert.Equal(2, summary.Iterations);
        Assert.Equal(20, summary.MaxIterations);
    }

    // ── Remote job proxying (job tracked here as forwarded to a peer) ──

    [Fact]
    public async Task GetResultZipAsync_RemoteJob_ProxiesToThePeerItWasForwardedTo()
    {
        var peer = MakePeer("peer1");
        _appConfig.Cluster.Peers.Add(peer);
        _mockPool.Setup(p => p.AvailableCount).Returns(0);
        _mockPeerClient.Setup(c => c.GetAvailableCapacityAsync(peer, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var peerJobId = Guid.NewGuid();
        _mockPeerClient.Setup(c => c.ForwardJobAsync(peer, It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>())).ReturnsAsync(peerJobId);
        var sut = CreateSut();
        var jobId = await sut.StartJobAsync("Mål", null, CancellationToken.None);

        using var expectedZip = new MemoryStream();
        _mockPeerClient.Setup(c => c.GetRemoteResultZipAsync(peer, peerJobId, It.IsAny<CancellationToken>())).ReturnsAsync(expectedZip);

        var zip = await sut.GetResultZipAsync(jobId, CancellationToken.None);

        Assert.Same(expectedZip, zip);
    }

    [Fact]
    public async Task RequestStopAsync_RemoteJob_ProxiesToThePeerItWasForwardedTo()
    {
        var peer = MakePeer("peer1");
        _appConfig.Cluster.Peers.Add(peer);
        _mockPool.Setup(p => p.AvailableCount).Returns(0);
        _mockPeerClient.Setup(c => c.GetAvailableCapacityAsync(peer, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var peerJobId = Guid.NewGuid();
        _mockPeerClient.Setup(c => c.ForwardJobAsync(peer, It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>())).ReturnsAsync(peerJobId);
        var sut = CreateSut();
        var jobId = await sut.StartJobAsync("Mål", null, CancellationToken.None);

        _mockPeerClient.Setup(c => c.RequestRemoteStopAsync(peer, peerJobId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var stopped = await sut.RequestStopAsync(jobId, CancellationToken.None);

        Assert.True(stopped);
        _mockPeerClient.Verify(c => c.RequestRemoteStopAsync(peer, peerJobId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Local job result zip (file I/O only — no LLM/peer involved) ──

    [Fact]
    public async Task GetResultZipAsync_LocalJobWithFiles_ReturnsZipContainingThem()
    {
        var jobsRoot = Path.Combine(Path.GetTempPath(), "AgentJobServiceTests_" + Guid.NewGuid());
        _tempDirs.Add(jobsRoot);
        _appConfig.Jobs.RootFolder = jobsRoot;
        // Local path is taken because no peers are configured, regardless of pool capacity.
        var sut = CreateSut();

        var jobId = await sut.StartJobAsync("Mål", null, CancellationToken.None);

        // StartJobAsync already created the job's workspace directory via FileAgentService's
        // constructor; write a file into it directly rather than waiting on the (mocked-pool,
        // LLM-touching) background run to produce one.
        var workspaceDir = Path.Combine(jobsRoot, jobId.ToString());
        await File.WriteAllTextAsync(Path.Combine(workspaceDir, "recept.txt"), "Pannkaksrecept");

        using var zip = await sut.GetResultZipAsync(jobId, CancellationToken.None);

        Assert.NotNull(zip);
        using var archive = new System.IO.Compression.ZipArchive(zip!, System.IO.Compression.ZipArchiveMode.Read);
        Assert.Contains(archive.Entries, e => e.Name == "recept.txt");
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
