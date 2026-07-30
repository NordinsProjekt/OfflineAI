using AgentKit.Api.Models;
using Services.Configuration;

namespace AgentKit.Api.Services;

/// <summary>
/// All outbound HTTP calls from this node to a peer <c>AgentKit.Api</c> node. Isolated behind an
/// interface so <see cref="AgentJobService"/>'s forwarding decision can be unit-tested with a
/// mock — no real network or LLM call involved in exercising that logic.
/// </summary>
public interface IClusterPeerClient
{
    /// <summary>
    /// Asks <paramref name="peer"/> how much free capacity it currently has
    /// (<c>GET /api/cluster/status</c>). Null means the peer is unreachable, misconfigured, or
    /// returned something unexpected — callers should treat that the same as "no capacity".
    /// </summary>
    Task<int?> GetAvailableCapacityAsync(ClusterPeerSettings peer, CancellationToken cancellationToken);

    /// <summary>
    /// Starts a job on <paramref name="peer"/> on this node's behalf. Returns the peer's own job
    /// id on success, or null if the peer rejected the request or is unreachable.
    /// </summary>
    Task<Guid?> ForwardJobAsync(ClusterPeerSettings peer, string goalDescription, int? maxIterations, CancellationToken cancellationToken);

    /// <summary>Status of a job this node forwarded to <paramref name="peer"/>, or null if unreachable/not found.</summary>
    Task<AgentJobStatus?> GetRemoteStatusAsync(ClusterPeerSettings peer, Guid peerJobId, CancellationToken cancellationToken);

    /// <summary>
    /// The finished result zip of a job this node forwarded to <paramref name="peer"/>, fully
    /// buffered into memory (same simplification as the local result path — jobs are expected to
    /// produce at most a few MB), or null if unreachable/not ready/not found.
    /// </summary>
    Task<Stream?> GetRemoteResultZipAsync(ClusterPeerSettings peer, Guid peerJobId, CancellationToken cancellationToken);

    /// <summary>Requests a stop on a job this node forwarded to <paramref name="peer"/>. Returns false if unreachable/not found.</summary>
    Task<bool> RequestRemoteStopAsync(ClusterPeerSettings peer, Guid peerJobId, CancellationToken cancellationToken);
}
