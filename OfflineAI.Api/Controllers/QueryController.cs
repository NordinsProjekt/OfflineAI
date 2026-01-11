using Microsoft.AspNetCore.Mvc;
using OfflineAI.Api.Models;
using OfflineAI.Api.Services;
using System.Diagnostics;

namespace OfflineAI.Api.Controllers;

/// <summary>
/// Main API controller for LLM query operations with RAG support.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class QueryController : ControllerBase
{
    private readonly ILlmQueryService _llmQueryService;
    private readonly ILogger<QueryController> _logger;
    private const int MaxTimeoutSeconds = 30;

    public QueryController(
        ILlmQueryService llmQueryService,
        ILogger<QueryController> logger)
    {
        _llmQueryService = llmQueryService;
        _logger = logger;
    }

    /// <summary>
    /// Query the LLM with optional RAG context retrieval.
    /// </summary>
    /// <param name="request">Query request parameters</param>
    /// <param name="cancellationToken">Cancellation token (30 second timeout)</param>
    /// <returns>LLM response with metadata</returns>
    /// <response code="200">Query successful</response>
    /// <response code="400">Invalid request parameters</response>
    /// <response code="408">Request timeout (exceeded 30 seconds)</response>
    /// <response code="500">Internal server error</response>
    [HttpPost]
    [ProducesResponseType(typeof(QueryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status408RequestTimeout)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<QueryResponse>> Query(
        [FromBody] QueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            // Validate request
            if (string.IsNullOrWhiteSpace(request.Question))
            {
                return BadRequest(new ErrorResponse
                {
                    Error = "Question is required",
                    StatusCode = 400,
                    Suggestions = new List<string> { "Provide a non-empty 'question' field in the request" }
                });
            }

            if (request.MaxTokens < 1 || request.MaxTokens > 4096)
            {
                return BadRequest(new ErrorResponse
                {
                    Error = "MaxTokens must be between 1 and 4096",
                    StatusCode = 400,
                    Details = $"Received: {request.MaxTokens}"
                });
            }

            if (request.Temperature < 0 || request.Temperature > 2)
            {
                return BadRequest(new ErrorResponse
                {
                    Error = "Temperature must be between 0 and 2",
                    StatusCode = 400,
                    Details = $"Received: {request.Temperature}"
                });
            }

            _logger.LogInformation(
                "Received query request. Question length: {Length}, RAG enabled: {RagEnabled}",
                request.Question.Length,
                request.EnableRag);

            // Create timeout source (30 seconds max)
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(MaxTimeoutSeconds));

            // Execute query
            var response = await _llmQueryService.QueryAsync(request, timeoutCts.Token);

            stopwatch.Stop();
            response.ResponseTimeMs = stopwatch.ElapsedMilliseconds;

            // Add warning if close to timeout
            if (response.ResponseTimeMs > (MaxTimeoutSeconds * 1000 * 0.9))
            {
                response.Warnings.Add($"Query took {response.ResponseTimeMs}ms, approaching {MaxTimeoutSeconds}s timeout limit");
            }

            _logger.LogInformation(
                "Query completed successfully. Time: {Time}ms, Tokens: {Tokens}, Model: {Model}",
                response.ResponseTimeMs,
                response.TotalTokens,
                response.Model);

            return Ok(response);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Query was cancelled by client");
            return StatusCode(408, new ErrorResponse
            {
                Error = "Request cancelled",
                StatusCode = 408,
                Details = "The request was cancelled before completion"
            });
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Query exceeded {Timeout} second timeout", MaxTimeoutSeconds);
            return StatusCode(408, new ErrorResponse
            {
                Error = $"Request timeout after {MaxTimeoutSeconds} seconds",
                StatusCode = 408,
                Details = $"Query took longer than the maximum allowed {MaxTimeoutSeconds} seconds",
                Suggestions = new List<string>
                {
                    "Try a simpler question",
                    "Reduce the maxTokens parameter",
                    "Disable RAG if not needed"
                }
            });
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid request parameters");
            return BadRequest(new ErrorResponse
            {
                Error = "Invalid request parameters",
                StatusCode = 400,
                Details = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing query");
            return StatusCode(500, new ErrorResponse
            {
                Error = "Internal server error",
                StatusCode = 500,
                Details = ex.Message,
                Suggestions = new List<string>
                {
                    "Check server logs for details",
                    "Verify LLM service is running",
                    "Ensure knowledge base is accessible"
                }
            });
        }
    }

    /// <summary>
    /// Validate a query request without executing it.
    /// </summary>
    /// <param name="request">Query request to validate</param>
    /// <returns>Validation result</returns>
    /// <response code="200">Request is valid</response>
    /// <response code="400">Request has validation errors</response>
    [HttpPost("validate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public ActionResult ValidateRequest([FromBody] QueryRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.Question))
            errors.Add("Question is required");

        if (request.MaxTokens < 1 || request.MaxTokens > 4096)
            errors.Add("MaxTokens must be between 1 and 4096");

        if (request.Temperature < 0 || request.Temperature > 2)
            errors.Add("Temperature must be between 0 and 2");

        if (request.TopK < 1 || request.TopK > 20)
            errors.Add("TopK must be between 1 and 20");

        if (request.MinRelevanceScore < 0 || request.MinRelevanceScore > 1)
            errors.Add("MinRelevanceScore must be between 0 and 1");

        if (errors.Any())
        {
            return BadRequest(new ErrorResponse
            {
                Error = "Validation failed",
                StatusCode = 400,
                Details = string.Join("; ", errors),
                Suggestions = errors
            });
        }

        return Ok(new { message = "Request is valid", estimatedTimeSeconds = EstimateQueryTime(request) });
    }

    private static int EstimateQueryTime(QueryRequest request)
    {
        // Simple estimation based on parameters
        var baseTime = request.EnableRag ? 5 : 2; // RAG adds overhead
        var tokenTime = (request.MaxTokens / 100) * 2; // ~2s per 100 tokens
        return Math.Min(baseTime + tokenTime, MaxTimeoutSeconds);
    }
}
