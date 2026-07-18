using Microsoft.AspNetCore.Mvc;
using OfflineAI.Api.Models;
using Services.Configuration;

namespace OfflineAI.Api.Controllers;

/// <summary>
/// Health and status endpoint.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly AppConfiguration _appConfig;

    public HealthController(AppConfiguration appConfig)
    {
        _appConfig = appConfig;
    }

    /// <summary>
    /// Check API health status.
    /// </summary>
    /// <returns>Health status</returns>
    /// <response code="200">API is healthy</response>
    [HttpGet]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    public ActionResult<HealthResponse> GetHealth()
    {
        return Ok(new HealthResponse
        {
            Status = "healthy",
            Timestamp = DateTime.UtcNow,
            Version = "1.0.0",
            Service = "OfflineAI API"
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
}
