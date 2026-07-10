using Microsoft.AspNetCore.Mvc;
using Application.AI.Gemma4;
using OfflineAI.Api.Models;
using Services.Memory;
using Services.Workspace;

namespace OfflineAI.Api.Controllers;

/// <summary>
/// AI-powered operations on files already in the active workspace (see
/// <see cref="WorkspaceController"/>): PDF-to-RAG ingestion and image question-answering (via the
/// Gemma 4 multimodal CLI). Core file operations (list/upload/read text) live in
/// <see cref="FilesController"/>.
/// </summary>
[ApiController]
[Route("api/Files")]
[Produces("application/json")]
public class FilesRagController : ControllerBase
{
    private readonly IWorkspaceService _workspaceService;
    private readonly VectorMemoryPersistenceService? _persistenceService;
    private readonly IGemma4CliService? _gemma4CliService;
    private readonly ILogger<FilesRagController> _logger;

    public FilesRagController(
        IWorkspaceService workspaceService,
        ILogger<FilesRagController> logger,
        VectorMemoryPersistenceService? persistenceService = null,
        IGemma4CliService? gemma4CliService = null)
    {
        _workspaceService = workspaceService;
        _logger = logger;
        _persistenceService = persistenceService;
        _gemma4CliService = gemma4CliService;
    }

    /// <summary>
    /// Ingest a PDF already in the active workspace into the RAG knowledge base: extracts text,
    /// splits it into semantic chunks, generates embeddings, and stores the fragments so future
    /// RAG queries (see <c>QueryController</c>) can retrieve them.
    /// </summary>
    [HttpPost("{filename}/ingest")]
    [ProducesResponseType(typeof(IngestPdfResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<IngestPdfResponse>> IngestPdf(string filename, [FromBody] IngestPdfRequest? request = null)
    {
        if (_persistenceService == null)
        {
            return StatusCode(503, new ErrorResponse
            {
                Error = "RAG ingestion not configured",
                StatusCode = 503,
                Details = "Embedding service and/or database are not configured for this API instance.",
                Suggestions = new List<string> { "Configure AppConfiguration:Embedding and AppConfiguration:Database" }
            });
        }

        var safeFilename = Path.GetFileName(filename);
        if (!string.Equals(Path.GetExtension(safeFilename), ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new ErrorResponse { Error = "Only PDF files can be ingested", StatusCode = 400 });
        }

        var workspacePath = _workspaceService.GetActiveWorkspace().Path;
        var fullPath = Path.Combine(workspacePath, safeFilename);
        if (!System.IO.File.Exists(fullPath))
        {
            return NotFound(new ErrorResponse { Error = "File not found in active workspace", StatusCode = 404, Details = safeFilename });
        }

        var collectionName = request?.CollectionName ?? Path.GetFileNameWithoutExtension(safeFilename);

        try
        {
            var processor = new PdfFragmentProcessor();
            var fragments = await processor.ProcessPdfFileAsync(fullPath, collectionName);

            await _persistenceService.SaveFragmentsAsync(
                fragments,
                collectionName,
                sourceFile: safeFilename,
                replaceExisting: request?.ReplaceExisting ?? false);

            return Ok(new IngestPdfResponse
            {
                Filename = safeFilename,
                CollectionName = collectionName,
                FragmentsCreated = fragments.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ingest PDF {Filename}", safeFilename);
            return StatusCode(500, new ErrorResponse { Error = "Failed to ingest PDF", StatusCode = 500, Details = "An unexpected error occurred while ingesting the file. See the server logs for details." });
        }
    }

    /// <summary>
    /// Ask a question about an image already uploaded to the active workspace, using the Gemma 4
    /// multimodal CLI backend.
    /// </summary>
    [HttpPost("{filename}/ask-image")]
    [ProducesResponseType(typeof(QueryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<QueryResponse>> AskAboutImage(
        string filename,
        [FromBody] WorkspaceImageQuestionRequest request,
        CancellationToken cancellationToken)
    {
        if (_gemma4CliService == null)
        {
            return StatusCode(503, new ErrorResponse
            {
                Error = "Image queries not configured",
                StatusCode = 503,
                Details = "AppConfiguration:Gemma4Cli:ModelPath is not set for this API instance."
            });
        }

        var safeFilename = Path.GetFileName(filename);
        var workspacePath = _workspaceService.GetActiveWorkspace().Path;
        var fullPath = Path.Combine(workspacePath, safeFilename);

        if (!System.IO.File.Exists(fullPath))
        {
            return NotFound(new ErrorResponse { Error = "File not found in active workspace", StatusCode = 404, Details = safeFilename });
        }

        var answer = await _gemma4CliService.ChatWithImageAsync(request.Question, fullPath, cancellationToken);

        return Ok(new QueryResponse
        {
            Answer = answer,
            Model = _gemma4CliService.ModelName,
            UsedRag = false
        });
    }
}
