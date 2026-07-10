using Microsoft.AspNetCore.Mvc;
using Services.Repositories;

namespace OfflineAI.Api.Controllers;

/// <summary>
/// Knowledge domain metadata for RAG domain filtering.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class DomainsController : ControllerBase
{
    private readonly ILogger<DomainsController> _logger;
    private readonly IKnowledgeDomainRepository? _domainRepository;

    public DomainsController(
        ILogger<DomainsController> logger,
        IKnowledgeDomainRepository? domainRepository = null)
    {
        _logger = logger;
        _domainRepository = domainRepository;
    }

    /// <summary>
    /// Get available domain filters for RAG queries.
    /// Returns all registered knowledge domains that can be used in the domainFilter parameter.
    /// </summary>
    /// <returns>List of available domains with metadata</returns>
    /// <response code="200">Domains retrieved successfully</response>
    /// <response code="503">Domain repository not configured</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult> GetDomains()
    {
        if (_domainRepository == null)
        {
            return StatusCode(503, new
            {
                error = "Domain repository not configured",
                message = "Auto vector search RAG is not enabled. Configure embedding service and database to use domain filtering.",
                availableModes = new[] { "Direct Query (enableRag: false)", "Manual Context RAG (provide context field)" }
            });
        }

        try
        {
            var domains = await _domainRepository.GetAllDomainsAsync();

            if (!domains.Any())
            {
                return Ok(new
                {
                    domains = new object[] { },
                    count = 0,
                    message = "No domains registered yet. Use the dashboard to add knowledge with domain tags.",
                    example = new
                    {
                        domainId = "chess",
                        displayName = "Chess",
                        category = "board-games"
                    }
                });
            }

            var result = domains.Select(d => new
            {
                domainId = d.DomainId,
                displayName = d.DisplayName,
                category = d.Category,
                createdAt = d.CreatedAt,
                source = d.Source
            }).ToList();

            var categories = domains.Select(d => d.Category).Distinct().OrderBy(c => c).ToList();

            return Ok(new
            {
                domains = result,
                count = result.Count,
                categories = categories,
                usage = new
                {
                    message = "Use domainId values in the domainFilter array when making RAG queries",
                    example = new
                    {
                        domainFilter = new[] { result.FirstOrDefault()?.domainId ?? "chess" }
                    }
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving domains");
            return StatusCode(500, new
            {
                error = "Failed to retrieve domains",
                details = "An unexpected error occurred. See the server logs for details."
            });
        }
    }

    /// <summary>
    /// Get domains filtered by category.
    /// </summary>
    /// <param name="category">Category to filter by (e.g., "board-games", "card-games")</param>
    /// <returns>List of domains in the specified category</returns>
    /// <response code="200">Domains retrieved successfully</response>
    /// <response code="503">Domain repository not configured</response>
    [HttpGet("category/{category}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult> GetDomainsByCategory(string category)
    {
        if (_domainRepository == null)
        {
            return StatusCode(503, new
            {
                error = "Domain repository not configured",
                message = "Auto vector search RAG is not enabled."
            });
        }

        try
        {
            var domains = await _domainRepository.GetDomainsByCategoryAsync(category);

            var result = domains.Select(d => new
            {
                domainId = d.DomainId,
                displayName = d.DisplayName,
                category = d.Category
            }).ToList();

            return Ok(new
            {
                category = category,
                domains = result,
                count = result.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving domains for category {Category}", category);
            return StatusCode(500, new
            {
                error = "Failed to retrieve domains",
                details = "An unexpected error occurred. See the server logs for details."
            });
        }
    }

    /// <summary>
    /// Get all available categories.
    /// </summary>
    /// <returns>List of category names</returns>
    /// <response code="200">Categories retrieved successfully</response>
    /// <response code="503">Domain repository not configured</response>
    [HttpGet("categories")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult> GetCategories()
    {
        if (_domainRepository == null)
        {
            return StatusCode(503, new
            {
                error = "Domain repository not configured",
                message = "Auto vector search RAG is not enabled."
            });
        }

        try
        {
            var categories = await _domainRepository.GetCategoriesAsync();

            return Ok(new
            {
                categories = categories.OrderBy(c => c).ToList(),
                count = categories.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving categories");
            return StatusCode(500, new
            {
                error = "Failed to retrieve categories",
                details = "An unexpected error occurred. See the server logs for details."
            });
        }
    }
}
