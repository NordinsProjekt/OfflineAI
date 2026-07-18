using Microsoft.AspNetCore.Mvc;
using Application.AI.Gemma4;
using OfflineAI.Api.Models;

namespace OfflineAI.Api.Controllers;

/// <summary>
/// Image-based LLM query operations using the Gemma 4 multimodal CLI backend. Text queries live
/// in <see cref="QueryController"/>.
/// </summary>
[ApiController]
[Route("api/Query")]
[Produces("application/json")]
public class ImageQueryController : ControllerBase
{
    private readonly IGemma4CliService? _gemma4CliService;
    private const int MaxTimeoutSeconds = 30;
    private const long MaxImageSizeBytes = 20 * 1024 * 1024; // 20 MB

    public ImageQueryController(IGemma4CliService? gemma4CliService = null)
    {
        _gemma4CliService = gemma4CliService;
    }

    /// <summary>
    /// Ask a question about an uploaded picture using the Gemma 4 multimodal backend. This is a
    /// one-shot query — the image is not stored. To keep an image around for repeated questions,
    /// upload it via <c>POST api/files/upload</c> and use <c>POST api/files/{filename}/ask-image</c>
    /// instead.
    /// </summary>
    /// <param name="image">The image file (jpeg/png/gif/webp).</param>
    /// <param name="question">The question to ask about the image.</param>
    /// <param name="cancellationToken">Cancellation token (30 second timeout).</param>
    /// <response code="200">Query successful</response>
    /// <response code="400">Invalid request parameters</response>
    /// <response code="503">Image queries are not configured (Gemma 4 CLI not available)</response>
    [HttpPost("image")]
    [ProducesResponseType(typeof(QueryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    [RequestSizeLimit(MaxImageSizeBytes)]
    public async Task<ActionResult<QueryResponse>> QueryImage(
        IFormFile image,
        [FromForm] string question,
        CancellationToken cancellationToken = default)
    {
        if (_gemma4CliService == null)
        {
            return StatusCode(503, new ErrorResponse
            {
                Error = "Image queries not configured",
                StatusCode = 503,
                Details = "AppConfiguration:Gemma4Cli:ModelPath is not set for this API instance.",
                Suggestions = new List<string> { "Configure AppConfiguration:Gemma4Cli:ModelPath and LlamaCliPath" }
            });
        }

        if (string.IsNullOrWhiteSpace(question))
        {
            return BadRequest(new ErrorResponse { Error = "Question is required", StatusCode = 400 });
        }

        if (image.Length == 0)
        {
            return BadRequest(new ErrorResponse { Error = "Image is required", StatusCode = 400 });
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(MaxTimeoutSeconds));

        await using var stream = image.OpenReadStream();
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, timeoutCts.Token);

        var mimeType = string.IsNullOrWhiteSpace(image.ContentType) ? "image/jpeg" : image.ContentType;
        var answer = await _gemma4CliService.ChatWithImageBytesAsync(
            question,
            memoryStream.ToArray(),
            mimeType,
            timeoutCts.Token);

        stopwatch.Stop();

        return Ok(new QueryResponse
        {
            Answer = answer,
            Model = _gemma4CliService.ModelName,
            UsedRag = false,
            ResponseTimeMs = stopwatch.ElapsedMilliseconds
        });
    }
}
