using AgentKit.Api.Models;
using AgentKit.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgentKit.Api.Controllers;

/// <summary>
/// Headless goal-agent jobs: describe a desired workspace end result, poll progress, and download
/// the resulting files once the job finishes. Mirrors what Agent Mode does in the dashboard, but
/// as a start/poll/download HTTP flow instead of a live Blazor page, since a job can run for
/// minutes and involves many LLM round trips — not a fit for a single blocking request. A job may
/// run on this node or, when it's too busy, on a configured peer — the caller never needs to know
/// which; every action here is keyed by the id returned from <see cref="StartJob"/>.
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
    public async Task<ActionResult<StartJobResponse>> StartJob([FromBody] StartJobRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.GoalDescription))
        {
            return BadRequest(new ErrorResponse
            {
                Error = "goalDescription is required",
                StatusCode = StatusCodes.Status400BadRequest
            });
        }

        var jobId = await _jobService.StartJobAsync(request.GoalDescription.Trim(), request.MaxIterations, cancellationToken);
        _logger.LogInformation("Started agent job {JobId}", jobId);

        return AcceptedAtAction(nameof(GetStatus), new { id = jobId }, new StartJobResponse(jobId));
    }

    /// <summary>
    /// Job status: phase, iteration progress, requirements, and activity log. Served live
    /// (locally or proxied from the peer running it) while the job is active; falls back to
    /// persisted history for a job whose process restarted.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AgentJobStatus), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AgentJobStatus>> GetStatus(Guid id, CancellationToken cancellationToken)
    {
        var status = await _jobService.GetStatusAsync(id, cancellationToken);
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
    public async Task<ActionResult> GetResult(Guid id, CancellationToken cancellationToken)
    {
        var status = await _jobService.GetStatusAsync(id, cancellationToken);
        if (status is null)
        {
            return NotFound(new ErrorResponse
            {
                Error = $"Job {id} not found.",
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

        var zip = await _jobService.GetResultZipAsync(id, cancellationToken);
        if (zip is null)
        {
            return NotFound(new ErrorResponse
            {
                Error = $"Job {id}'s result is unavailable (its workspace is missing, or the peer that ran it is unreachable).",
                StatusCode = StatusCodes.Status404NotFound
            });
        }

        return File(zip, "application/zip", $"{id}.zip");
    }

    /// <summary>Requests the job stop at its next step boundary. No-ops (returns 404) for a job unknown to this process.</summary>
    [HttpPost("{id:guid}/stop")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult> StopJob(Guid id, CancellationToken cancellationToken)
    {
        if (!await _jobService.RequestStopAsync(id, cancellationToken))
        {
            return NotFound(new ErrorResponse
            {
                Error = $"Job {id} not found or not running in this process.",
                StatusCode = StatusCodes.Status404NotFound
            });
        }

        return Ok();
    }

    /// <summary>Recent jobs (this node's own persisted history), newest first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<AgentJobSummary>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AgentJobSummary>>> GetRecentJobs([FromQuery] int count = 25, CancellationToken cancellationToken = default)
    {
        var jobs = await _jobService.GetRecentJobsAsync(count, cancellationToken);
        return Ok(jobs);
    }
}
