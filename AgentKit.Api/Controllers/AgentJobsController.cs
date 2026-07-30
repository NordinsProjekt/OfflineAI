using System.IO.Compression;
using AgentKit.Api.Models;
using AgentKit.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgentKit.Api.Controllers;

/// <summary>
/// Headless goal-agent jobs: describe a desired workspace end result, poll progress, and download
/// the resulting files once the job finishes. Mirrors what Agent Mode does in the dashboard, but
/// as a start/poll/download HTTP flow instead of a live Blazor page, since a job can run for
/// minutes and involves many LLM round trips — not a fit for a single blocking request.
/// </summary>
[ApiController]
[Route("api/jobs")]
[Produces("application/json")]
public class AgentJobsController : ControllerBase
{
    private static readonly HashSet<string> TerminalPhases = new(StringComparer.OrdinalIgnoreCase)
    {
        "Completed", "MaxIterationsReached", "Stopped", "Failed"
    };

    private readonly IAgentJobService _jobService;
    private readonly ILogger<AgentJobsController> _logger;

    public AgentJobsController(IAgentJobService jobService, ILogger<AgentJobsController> logger)
    {
        _jobService = jobService;
        _logger = logger;
    }

    /// <summary>Starts a new goal-agent job. The run continues in the background — poll <c>GET /api/jobs/{id}</c> for progress.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(StartJobResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public ActionResult<StartJobResponse> StartJob([FromBody] StartJobRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.GoalDescription))
        {
            return BadRequest(new ErrorResponse
            {
                Error = "goalDescription is required",
                StatusCode = StatusCodes.Status400BadRequest
            });
        }

        var jobId = _jobService.StartJob(request.GoalDescription.Trim(), request.MaxIterations);
        _logger.LogInformation("Started agent job {JobId}", jobId);

        return AcceptedAtAction(nameof(GetStatus), new { id = jobId }, new StartJobResponse(jobId));
    }

    /// <summary>
    /// Job status: phase, iteration progress, requirements, and activity log. Served from process
    /// memory while the job is live; falls back to persisted history for a job whose process
    /// restarted (or that ran in an earlier process).
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AgentJobStatus), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AgentJobStatus>> GetStatus(Guid id)
    {
        var status = _jobService.GetStatus(id) ?? await _jobService.GetPersistedStatusAsync(id);
        if (status is null)
        {
            return NotFound(new ErrorResponse
            {
                Error = $"Job {id} not found.",
                StatusCode = StatusCodes.Status404NotFound
            });
        }

        return Ok(status);
    }

    /// <summary>
    /// Downloads the job's finished workspace as a zip — the deliverable, whether it's one file
    /// or a hundred. Only available once the job has reached a terminal phase.
    /// </summary>
    [HttpGet("{id:guid}/result")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public ActionResult GetResult(Guid id)
    {
        var status = _jobService.GetStatus(id);
        if (status is null)
        {
            return NotFound(new ErrorResponse
            {
                Error = $"Job {id} not found or not running in this process. If the server restarted mid-run, its files are still on disk but not downloadable through this endpoint yet.",
                StatusCode = StatusCodes.Status404NotFound
            });
        }

        if (!TerminalPhases.Contains(status.Phase))
        {
            return Conflict(new ErrorResponse
            {
                Error = $"Job {id} is still running (phase: {status.Phase}). Try again once it finishes.",
                StatusCode = StatusCodes.Status409Conflict
            });
        }

        var workspacePath = _jobService.GetWorkspacePath(id);
        if (workspacePath is null || !Directory.Exists(workspacePath))
        {
            return NotFound(new ErrorResponse
            {
                Error = $"Job {id}'s workspace directory is missing.",
                StatusCode = StatusCodes.Status404NotFound
            });
        }

        // Built in memory rather than to a temp file — jobs are expected to produce at most a
        // handful of MB of text/code, well within what's reasonable to hold in memory for the
        // length of one response.
        var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in Directory.GetFiles(workspacePath, "*", SearchOption.AllDirectories))
            {
                var entryName = Path.GetRelativePath(workspacePath, file);
                zip.CreateEntryFromFile(file, entryName);
            }
        }
        buffer.Position = 0;

        return File(buffer, "application/zip", $"{id}.zip");
    }

    /// <summary>Requests the job stop at its next step boundary. No-ops (returns 404) for a job unknown to this process.</summary>
    [HttpPost("{id:guid}/stop")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public ActionResult StopJob(Guid id)
    {
        if (!_jobService.RequestStop(id))
        {
            return NotFound(new ErrorResponse
            {
                Error = $"Job {id} not found or not running in this process.",
                StatusCode = StatusCodes.Status404NotFound
            });
        }

        return Ok();
    }

    /// <summary>Recent jobs (persisted history), newest first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<AgentJobSummary>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AgentJobSummary>>> GetRecentJobs([FromQuery] int count = 25)
    {
        var jobs = await _jobService.GetRecentJobsAsync(count);
        return Ok(jobs);
    }
}
