using Microsoft.AspNetCore.Mvc;
using OfflineAI.Api.Models;
using Services.Workspace;

namespace OfflineAI.Api.Controllers;

/// <summary>
/// Lists files present in the currently active workspace (see <see cref="WorkspaceController"/>).
/// Uploading and reading file content live in <see cref="FileContentController"/>; AI-powered
/// operations (RAG ingestion, image question-answering) live in <see cref="FilesRagController"/>.
/// </summary>
[ApiController]
[Route("api/Files")]
[Produces("application/json")]
public class FilesController : ControllerBase
{
    private readonly IWorkspaceService _workspaceService;

    public FilesController(IWorkspaceService workspaceService)
    {
        _workspaceService = workspaceService;
    }

    /// <summary>List files present in the active workspace.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<WorkspaceFileInfo>), StatusCodes.Status200OK)]
    public ActionResult<List<WorkspaceFileInfo>> ListFiles()
    {
        var workspacePath = _workspaceService.GetActiveWorkspace().Path;
        var files = Directory.Exists(workspacePath)
            ? Directory.GetFiles(workspacePath)
                .Select(path =>
                {
                    var info = new FileInfo(path);
                    return new WorkspaceFileInfo
                    {
                        Name = info.Name,
                        SizeBytes = info.Length,
                        LastModifiedUtc = info.LastWriteTimeUtc
                    };
                })
                .ToList()
            : new List<WorkspaceFileInfo>();

        return Ok(files);
    }
}
