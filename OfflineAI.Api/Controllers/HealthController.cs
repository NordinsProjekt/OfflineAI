using Microsoft.AspNetCore.Mvc;
using OfflineAI.Api.Models;

namespace OfflineAI.Api.Controllers;

/// <summary>
/// Health and status endpoint.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly ILogger<HealthController> _logger;

    public HealthController(ILogger<HealthController> logger)
    {
        _logger = logger;
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
        // TODO: Get actual models from configuration/service
        var models = new List<ModelInfo>
        {
            new ModelInfo
            {
                Name = "tinyllama",
                DisplayName = "TinyLlama 1.1B",
                Description = "Fast, lightweight model for quick responses",
                IsDefault = true,
                MaxContextLength = 2048,
                IsAvailable = true
            }
        };

        return Ok(models);
    }
}
