using Microsoft.AspNetCore.Mvc;
using OfflineAI.Api.Models;
using Services.FileAgent;

namespace OfflineAI.Api.Controllers;

/// <summary>
/// Uploads files into, and reads text content from, the currently active workspace (see
/// <see cref="WorkspaceController"/>). Listing files lives in <see cref="FilesController"/>;
/// AI-powered operations (RAG ingestion, image question-answering) live in
/// <see cref="FilesRagController"/>.
/// </summary>
[ApiController]
[Route("api/Files")]
[Produces("application/json")]
public class FileContentController : ControllerBase
{
    private const long MaxUploadSizeBytes = 100 * 1024 * 1024; // 100 MB

    private readonly IFileAgentService _fileAgentService;

    public FileContentController(IFileAgentService fileAgentService)
    {
        _fileAgentService = fileAgentService;
    }

    /// <summary>
    /// Upload a file (image, PDF, or text) into the active workspace. Overwrites any existing
    /// file with the same name.
    /// </summary>
    [HttpPost("upload")]
    [ProducesResponseType(typeof(UploadFileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [RequestSizeLimit(MaxUploadSizeBytes)]
    public async Task<ActionResult<UploadFileResponse>> UploadFile(IFormFile file)
    {
        if (file.Length == 0)
        {
            return BadRequest(new ErrorResponse { Error = "File is empty", StatusCode = 400 });
        }

        var filename = Path.GetFileName(file.FileName);
        await using var stream = file.OpenReadStream();
        var result = await _fileAgentService.SaveUploadedFileAsync(filename, stream);

        if (!result.IsSuccess)
        {
            return BadRequest(new ErrorResponse { Error = "Failed to save file", StatusCode = 400, Details = result.Message });
        }

        return Ok(new UploadFileResponse { Filename = filename, Message = result.Message });
    }

    /// <summary>
    /// Extract the text content of a workspace file. PDFs are parsed page-by-page; other files
    /// are read as plain text.
    /// </summary>
    [HttpGet("{filename}/text")]
    [ProducesResponseType(typeof(FileTextResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FileTextResponse>> GetFileText(string filename)
    {
        var safeFilename = Path.GetFileName(filename);
        var isPdf = string.Equals(Path.GetExtension(safeFilename), ".pdf", StringComparison.OrdinalIgnoreCase);

        var result = isPdf
            ? await _fileAgentService.ReadPdfFileAsync(safeFilename)
            : await _fileAgentService.ReadFileRawAsync(safeFilename);

        if (!result.IsSuccess || result.InjectedContext == null)
        {
            return NotFound(new ErrorResponse { Error = "Failed to read file", StatusCode = 404, Details = result.Message });
        }

        return Ok(new FileTextResponse { Filename = safeFilename, Text = result.InjectedContext });
    }
}
