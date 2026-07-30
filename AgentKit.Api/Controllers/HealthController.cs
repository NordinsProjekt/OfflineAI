using Microsoft.AspNetCore.Mvc;

namespace AgentKit.Api.Controllers;

/// <summary>Health and status endpoint — always anonymous, see <c>Security.ApiKeyMiddleware</c>.</summary>
[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public ActionResult<object> GetHealth() =>
        Ok(new { status = "healthy", timestamp = DateTime.UtcNow, service = "AgentKit API" });
}
