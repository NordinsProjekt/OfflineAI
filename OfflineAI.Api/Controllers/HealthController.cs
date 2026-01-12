using Microsoft.AspNetCore.Mvc;
using OfflineAI.Api.Models;
using Services.Configuration;
using Services.Repositories;

namespace OfflineAI.Api.Controllers;

/// <summary>
/// Health and status endpoint.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly ILogger<HealthController> _logger;
    private readonly AppConfiguration _appConfig;
    private readonly IKnowledgeDomainRepository? _domainRepository;

    public HealthController(
        ILogger<HealthController> logger,
        AppConfiguration appConfig,
        IKnowledgeDomainRepository? domainRepository = null)
    {
        _logger = logger;
        _appConfig = appConfig;
        _domainRepository = domainRepository;
    }

    /// <summary>
    /// Check API health status.
    /// </summary>
    /// <returns>Health status</returns>
    /// <response code="200">API is healthy</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetHealth()
    {
        return Ok(new
        {
            status = "healthy",
            timestamp = DateTime.UtcNow,
            version = "1.0.0",
            service = "OfflineAI API"
        });
    }

    /// <summary>
    /// Get available models.
    /// </summary>
    /// <returns>List of available models</returns>
    /// <response code="200">Models retrieved successfully</response>
    [HttpGet("models")]
    [ProducesResponseType(typeof(List<ModelInfo>), StatusCodes.Status200OK)]
    public ActionResult<List<ModelInfo>> GetModels()
    {
        var models = new List<ModelInfo>();
        var llmSettings = _appConfig.Llm;

        if (llmSettings != null && !string.IsNullOrEmpty(llmSettings.ModelPath))
        {
            // Extract model name from path if not explicitly set
            var modelName = llmSettings.ModelName;
            if (string.IsNullOrEmpty(modelName))
            {
                modelName = Path.GetFileNameWithoutExtension(llmSettings.ModelPath);
            }

            // Determine display name
            var displayName = llmSettings.ModelType ?? modelName ?? "Local LLM";

            // Check if model file exists
            var isAvailable = !string.IsNullOrEmpty(llmSettings.ModelPath) &&
                            !string.IsNullOrEmpty(llmSettings.ExecutablePath) &&
                            System.IO.File.Exists(llmSettings.ModelPath) &&
                            System.IO.File.Exists(llmSettings.ExecutablePath);

            models.Add(new ModelInfo
            {
                Name = modelName?.ToLowerInvariant() ?? "local-llm",
                DisplayName = displayName,
                Description = $"Local LLM model{(llmSettings.UseGpu ? " (GPU-accelerated)" : "")}",
                IsDefault = true,
                MaxContextLength = llmSettings.ContextSize > 0 ? llmSettings.ContextSize : 2048,
                IsAvailable = isAvailable
            });
        }
        else
        {
            // Return default entry if no configuration is available
            models.Add(new ModelInfo
            {
                Name = "not-configured",
                DisplayName = "No Model Configured",
                Description = "Please configure LLM settings in appsettings.json or User Secrets",
                IsDefault = true,
                MaxContextLength = 0,
                IsAvailable = false
            });
        }

        return Ok(models);
    }

    /// <summary>
    /// Get available domain filters for RAG queries.
    /// Returns all registered knowledge domains that can be used in the domainFilter parameter.
    /// </summary>
    /// <returns>List of available domains with metadata</returns>
    /// <response code="200">Domains retrieved successfully</response>
    /// <response code="503">Domain repository not configured</response>
    [HttpGet("domains")]
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
                details = ex.Message
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
    [HttpGet("domains/category/{category}")]
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
                details = ex.Message
            });
        }
    }

    /// <summary>
    /// Get all available categories.
    /// </summary>
    /// <returns>List of category names</returns>
    /// <response code="200">Categories retrieved successfully</response>
    /// <response code="503">Domain repository not configured</response>
    [HttpGet("domains/categories")]
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
                details = ex.Message
            });
        }
    }
}