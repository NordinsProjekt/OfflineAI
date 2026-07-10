using Microsoft.AspNetCore.Mvc;
using OfflineAI.Api.Models;
using Services.Workspace;

namespace OfflineAI.Api.Controllers;

/// <summary>
/// Manages the set of workspace directories the file agent is confined to. Uploaded files and
/// PDF ingestion (see <see cref="FilesController"/>) always operate on the currently active
/// workspace — switching it here re-confines every subsequent file operation.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class WorkspaceController : ControllerBase
{
    private readonly IWorkspaceService _workspaceService;
    private readonly ILogger<WorkspaceController> _logger;

    public WorkspaceController(IWorkspaceService workspaceService, ILogger<WorkspaceController> logger)
    {
        _workspaceService = workspaceService;
        _logger = logger;
    }

    /// <summary>List all known workspaces, indicating which one is active.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<WorkspaceResponse>), StatusCodes.Status200OK)]
    public ActionResult<List<WorkspaceResponse>> GetWorkspaces()
    {
        var active = _workspaceService.GetActiveWorkspace();
        var result = _workspaceService.GetWorkspaces()
            .Select(w => new WorkspaceResponse { Name = w.Name, Path = w.Path, IsActive = w.Name == active.Name })
            .ToList();

        return Ok(result);
    }

    /// <summary>Get the currently active workspace.</summary>
    [HttpGet("active")]
    [ProducesResponseType(typeof(WorkspaceResponse), StatusCodes.Status200OK)]
    public ActionResult<WorkspaceResponse> GetActiveWorkspace()
    {
        var active = _workspaceService.GetActiveWorkspace();
        return Ok(new WorkspaceResponse { Name = active.Name, Path = active.Path, IsActive = true });
    }

    /// <summary>Create a new workspace rooted at the given directory (does not activate it).</summary>
    [HttpPost]
    [ProducesResponseType(typeof(WorkspaceResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<WorkspaceResponse>> CreateWorkspace([FromBody] CreateWorkspaceRequest request)
    {
        try
        {
            var workspace = await _workspaceService.AddWorkspaceAsync(request.Name, request.Path);
            var result = new WorkspaceResponse { Name = workspace.Name, Path = workspace.Path, IsActive = false };
            return CreatedAtAction(nameof(GetWorkspaces), result);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Failed to create workspace");
            return BadRequest(new ErrorResponse { Error = "Failed to create workspace", StatusCode = 400, Details = ex.Message });
        }
    }

    /// <summary>Switch the active workspace. All subsequent file operations are confined to it.</summary>
    [HttpPost("active")]
    [ProducesResponseType(typeof(WorkspaceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<WorkspaceResponse>> SetActiveWorkspace([FromBody] SetActiveWorkspaceRequest request)
    {
        try
        {
            await _workspaceService.SetActiveWorkspaceAsync(request.Name);
            var active = _workspaceService.GetActiveWorkspace();
            return Ok(new WorkspaceResponse { Name = active.Name, Path = active.Path, IsActive = true });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to switch active workspace");
            return BadRequest(new ErrorResponse { Error = "Failed to switch workspace", StatusCode = 400, Details = ex.Message });
        }
    }

    /// <summary>Remove a workspace. If it was active, the first remaining workspace becomes active.</summary>
    [HttpDelete("{name}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveWorkspace(string name)
    {
        await _workspaceService.RemoveWorkspaceAsync(name);
        return NoContent();
    }
}
