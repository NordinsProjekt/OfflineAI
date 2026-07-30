using System.IO.Compression;
using AgentKit.Api.Controllers;
using AgentKit.Api.Models;
using AgentKit.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace AgentKit.Api.Tests.Controllers;

/// <summary>
/// Unit tests for <see cref="AgentJobsController"/>: request validation, status lookup, result
/// download gating by phase, and error mapping. The job service is mocked throughout — no real
/// agent run, LLM call, or peer network call happens here. Zip-building itself (local vs. proxied
/// from a peer) is <see cref="AgentJobService"/>'s responsibility and is tested there; this class
/// only checks the controller passes whatever stream the service returns straight through.
/// </summary>
public class AgentJobsControllerTests
{
    private readonly Mock<IAgentJobService> _mockJobService;
    private readonly AgentJobsController _controller;

    public AgentJobsControllerTests()
    {
        _mockJobService = new Mock<IAgentJobService>();
        _controller = new AgentJobsController(_mockJobService.Object, new Mock<ILogger<AgentJobsController>>().Object);
    }

    private static AgentJobStatus MakeStatus(string phase, Guid? jobId = null) => new(
        jobId ?? Guid.NewGuid(),
        phase,
        "Skapa ett pannkaksrecept",
        1,
        20,
        new List<AgentJobRequirementStatus>(),
        new List<string>());

    private static MemoryStream MakeZipStream(string entryName = "recept.txt")
    {
        var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry(entryName);
            using var writer = new StreamWriter(entry.Open());
            writer.Write("klart");
        }
        buffer.Position = 0;
        return buffer;
    }

    // ── StartJob ────────────────────────────────────────────────────────

    [Fact]
    public async Task StartJob_ValidRequest_ReturnsAcceptedWithJobId()
    {
        var jobId = Guid.NewGuid();
        _mockJobService
            .Setup(s => s.StartJobAsync("Skapa ett pannkaksrecept", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobId);

        var result = await _controller.StartJob(new StartJobRequest { GoalDescription = "Skapa ett pannkaksrecept" }, CancellationToken.None);

        var accepted = Assert.IsType<AcceptedAtActionResult>(result.Result);
        var response = Assert.IsType<StartJobResponse>(accepted.Value);
        Assert.Equal(jobId, response.JobId);
    }

    [Fact]
    public async Task StartJob_PassesMaxIterationsThrough()
    {
        _mockJobService
            .Setup(s => s.StartJobAsync(It.IsAny<string>(), 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        await _controller.StartJob(new StartJobRequest { GoalDescription = "Mål", MaxIterations = 5 }, CancellationToken.None);

        _mockJobService.Verify(s => s.StartJobAsync("Mål", 5, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task StartJob_EmptyGoalDescription_ReturnsBadRequest(string? goalDescription)
    {
        var result = await _controller.StartJob(new StartJobRequest { GoalDescription = goalDescription! }, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var error = Assert.IsType<ErrorResponse>(badRequest.Value);
        Assert.Equal(400, error.StatusCode);
        _mockJobService.Verify(s => s.StartJobAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── GetStatus ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetStatus_KnownJob_ReturnsOk()
    {
        var jobId = Guid.NewGuid();
        var status = MakeStatus("Working", jobId);
        _mockJobService.Setup(s => s.GetStatusAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync(status);

        var result = await _controller.GetStatus(jobId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(status, ok.Value);
    }

    [Fact]
    public async Task GetStatus_UnknownJob_ReturnsNotFound()
    {
        var jobId = Guid.NewGuid();
        _mockJobService.Setup(s => s.GetStatusAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync((AgentJobStatus?)null);

        var result = await _controller.GetStatus(jobId, CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Equal(404, Assert.IsType<ErrorResponse>(notFound.Value).StatusCode);
    }

    // ── GetResult ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetResult_UnknownJob_ReturnsNotFound()
    {
        var jobId = Guid.NewGuid();
        _mockJobService.Setup(s => s.GetStatusAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync((AgentJobStatus?)null);

        var result = await _controller.GetResult(jobId, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetResult_StillRunning_ReturnsConflict()
    {
        var jobId = Guid.NewGuid();
        _mockJobService.Setup(s => s.GetStatusAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync(MakeStatus("Working", jobId));

        var result = await _controller.GetResult(jobId, CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(409, Assert.IsType<ErrorResponse>(conflict.Value).StatusCode);
        _mockJobService.Verify(s => s.GetResultZipAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetResult_TerminalPhaseButZipUnavailable_ReturnsNotFound()
    {
        var jobId = Guid.NewGuid();
        _mockJobService.Setup(s => s.GetStatusAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync(MakeStatus("Completed", jobId));
        _mockJobService.Setup(s => s.GetResultZipAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync((Stream?)null);

        var result = await _controller.GetResult(jobId, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetResult_TerminalPhaseWithZip_ReturnsItAsFileDownload()
    {
        var jobId = Guid.NewGuid();
        var zip = MakeZipStream();
        _mockJobService.Setup(s => s.GetStatusAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync(MakeStatus("Completed", jobId));
        _mockJobService.Setup(s => s.GetResultZipAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync(zip);

        var result = await _controller.GetResult(jobId, CancellationToken.None);

        var fileResult = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("application/zip", fileResult.ContentType);
        Assert.Same(zip, fileResult.FileStream);
    }

    // ── StopJob ─────────────────────────────────────────────────────────

    [Fact]
    public async Task StopJob_KnownJob_ReturnsOk()
    {
        var jobId = Guid.NewGuid();
        _mockJobService.Setup(s => s.RequestStopAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await _controller.StopJob(jobId, CancellationToken.None);

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task StopJob_UnknownJob_ReturnsNotFound()
    {
        var jobId = Guid.NewGuid();
        _mockJobService.Setup(s => s.RequestStopAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await _controller.StopJob(jobId, CancellationToken.None);

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
        _mockJobService.Setup(s => s.GetRecentJobsAsync(25, It.IsAny<CancellationToken>())).ReturnsAsync(jobs);

        var result = await _controller.GetRecentJobs(cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(jobs, ok.Value);
    }
}
