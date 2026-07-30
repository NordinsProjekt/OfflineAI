using AgentKit.Api.Models;
using Application.AI.Pooling;
using Microsoft.AspNetCore.Mvc;

namespace AgentKit.Api.Controllers;

/// <summary>
/// Lets a peer node ask this one how busy it is before forwarding a job here. Goes through the
/// normal <c>ApiKeyMiddleware</c> gate like any other endpoint — a peer authenticates with this
/// node's configured API key, same as any other caller.
/// </summary>
[ApiController]
[Route("api/cluster")]
[Produces("application/json")]
public class ClusterController : ControllerBase
{
    private readonly IModelInstancePool _modelPool;

    public ClusterController(IModelInstancePool modelPool)
    {
        _modelPool = modelPool;
    }

    /// <summary>Current local LLM pool capacity — the signal peers use to decide whether to forward a job here.</summary>
    [HttpGet("status")]
    [ProducesResponseType(typeof(ClusterStatus), StatusCodes.Status200OK)]
    public ActionResult<ClusterStatus> GetStatus() =>
        Ok(new ClusterStatus(_modelPool.AvailableCount, _modelPool.MaxInstances));
}
