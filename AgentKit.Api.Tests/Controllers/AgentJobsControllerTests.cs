using System.IO.Compression;
using AgentKit.Api.Controllers;
using AgentKit.Api.Models;
using AgentKit.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace AgentKit.Api.Tests.Controllers;

/// <summary>
/// Unit tests for <see cref="AgentJobsController"/>: request validation, status lookup
/// (live-then-persisted fallback), result download gating by phase, and error mapping.
/// The job service is mocked throughout — no real agent run or LLM call happens here.
/// </summary>
public sealed class AgentJobsControllerTests : IDisposable
{
    private readonly Mock<IAgentJobService> _mockJobService;
    private readonly AgentJobsController _controller;
    private readonly List<string> _tempDirs = new();

    public AgentJobsControllerTests()
    {
        _mockJobService = new Mock<IAgentJobService>();
        _controller = new AgentJobsController(_mockJobService.Object, new Mock<ILogger<AgentJobsController>>().Object);
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    private string CreateTempWorkspace(params (string Name, string Content)[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "AgentJobsControllerTests_" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        foreach (var (name, content) in files)
            File.WriteAllText(Path.Combine(dir, name), content);
        return dir;
    }

    private static AgentJobStatus MakeStatus(string phase, Guid? jobId = null) => new(
        jobId ?? Guid.NewGuid(),
        phase,
        "Skapa ett pannkaksrecept",
        1,
        20,
        new List<AgentJobRequirementStatus>(),
        new List<string>());

    // ── StartJob ────────────────────────────────────────────────────────

    [Fact]
    public void StartJob_ValidRequest_ReturnsAcceptedWithJobId()
    {
        var jobId = Guid.NewGuid();
        _mockJobService.Setup(s => s.StartJob("Skapa ett pannkaksrecept", null)).Returns(jobId);

        var result = _controller.StartJob(new StartJobRequest { GoalDescription = "Skapa ett pannkaksrecept" });

        var accepted = Assert.IsType<AcceptedAtActionResult>(result.Result);
        var response = Assert.IsType<StartJobResponse>(accepted.Value);
        Assert.Equal(jobId, response.JobId);
    }

    [Fact]
    public void StartJob_PassesMaxIterationsThrough()
    {
        _mockJobService.Setup(s => s.StartJob(It.IsAny<string>(), 5)).Returns(Guid.NewGuid());

        _controller.StartJob(new StartJobRequest { GoalDescription = "Mål", MaxIterations = 5 });

        _mockJobService.Verify(s => s.StartJob("Mål", 5), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void StartJob_EmptyGoalDescription_ReturnsBadRequest(string? goalDescription)
    {
        var result = _controller.StartJob(new StartJobRequest { GoalDescription = goalDescription! });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var error = Assert.IsType<ErrorResponse>(badRequest.Value);
        Assert.Equal(400, error.StatusCode);
    }

    // ── GetStatus ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetStatus_LiveJob_ReturnsLiveStatusWithoutTouchingPersistedFallback()
    {
        var jobId = Guid.NewGuid();
        var status = MakeStatus("Working", jobId);
        _mockJobService.Setup(s => s.GetStatus(jobId)).Returns(status);

        var result = await _controller.GetStatus(jobId);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(status, ok.Value);
        _mockJobService.Verify(s => s.GetPersistedStatusAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task GetStatus_NotLiveButPersisted_FallsBackToPersistedStatus()
    {
        var jobId = Guid.NewGuid();
        var persisted = MakeStatus("Completed", jobId);
        _mockJobService.Setup(s => s.GetStatus(jobId)).Returns((AgentJobStatus?)null);
        _mockJobService.Setup(s => s.GetPersistedStatusAsync(jobId)).ReturnsAsync(persisted);

        var result = await _controller.GetStatus(jobId);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(persisted, ok.Value);
    }

    [Fact]
    public async Task GetStatus_UnknownEverywhere_ReturnsNotFound()
    {
        var jobId = Guid.NewGuid();
        _mockJobService.Setup(s => s.GetStatus(jobId)).Returns((AgentJobStatus?)null);
        _mockJobService.Setup(s => s.GetPersistedStatusAsync(jobId)).ReturnsAsync((AgentJobStatus?)null);

        var result = await _controller.GetStatus(jobId);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Equal(404, Assert.IsType<ErrorResponse>(notFound.Value).StatusCode);
    }

    // ── GetResult ───────────────────────────────────────────────────────

    [Fact]
    public void GetResult_UnknownJob_ReturnsNotFound()
    {
        var jobId = Guid.NewGuid();
        _mockJobService.Setup(s => s.GetStatus(jobId)).Returns((AgentJobStatus?)null);

        var result = _controller.GetResult(jobId);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void GetResult_StillRunning_ReturnsConflict()
    {
        var jobId = Guid.NewGuid();
        _mockJobService.Setup(s => s.GetStatus(jobId)).Returns(MakeStatus("Working", jobId));

        var result = _controller.GetResult(jobId);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(409, Assert.IsType<ErrorResponse>(conflict.Value).StatusCode);
    }

    [Fact]
    public void GetResult_TerminalPhaseButWorkspaceGone_ReturnsNotFound()
    {
        var jobId = Guid.NewGuid();
        _mockJobService.Setup(s => s.GetStatus(jobId)).Returns(MakeStatus("Completed", jobId));
        _mockJobService.Setup(s => s.GetWorkspacePath(jobId)).Returns((string?)null);

        var result = _controller.GetResult(jobId);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void GetResult_CompletedWithFiles_ReturnsZipContainingThem()
    {
        var jobId = Guid.NewGuid();
        var workspace = CreateTempWorkspace(("recept.txt", "Pannkaksrecept\n1. Vispa\n"), ("notes.txt", "klart"));
        _mockJobService.Setup(s => s.GetStatus(jobId)).Returns(MakeStatus("Completed", jobId));
        _mockJobService.Setup(s => s.GetWorkspacePath(jobId)).Returns(workspace);

        var result = _controller.GetResult(jobId);

        var fileResult = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("application/zip", fileResult.ContentType);

        using var zip = new ZipArchive(fileResult.FileStream, ZipArchiveMode.Read);
        Assert.Equal(2, zip.Entries.Count);
        Assert.Contains(zip.Entries, e => e.Name == "recept.txt");
        Assert.Contains(zip.Entries, e => e.Name == "notes.txt");
    }

    // ── StopJob ─────────────────────────────────────────────────────────

    [Fact]
    public void StopJob_KnownJob_ReturnsOk()
    {
        var jobId = Guid.NewGuid();
        _mockJobService.Setup(s => s.RequestStop(jobId)).Returns(true);

        var result = _controller.StopJob(jobId);

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public void StopJob_UnknownJob_ReturnsNotFound()
    {
        var jobId = Guid.NewGuid();
        _mockJobService.Setup(s => s.RequestStop(jobId)).Returns(false);

        var result = _controller.StopJob(jobId);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // ── GetRecentJobs ───────────────────────────────────────────────────

    [Fact]
    public async Task GetRecentJobs_ReturnsJobsFromService()
    {
        var jobs = new List<AgentJobSummary>
        {
            new(Guid.NewGuid(), "Mål 1", "Completed", 3, 20, DateTime.UtcNow, DateTime.UtcNow)
        };
        _mockJobService.Setup(s => s.GetRecentJobsAsync(25)).ReturnsAsync(jobs);

        var result = await _controller.GetRecentJobs();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(jobs, ok.Value);
    }
}
