using System.Net.Http.Json;
using AgentKit.Api.Models;
using AgentKit.Api.Security;
using Services.Configuration;

namespace AgentKit.Api.Services;

/// <inheritdoc/>
public sealed class ClusterPeerClient : IClusterPeerClient
{
    /// <summary>
    /// Name of the <see cref="IHttpClientFactory"/>-registered client used for every peer call —
    /// see <c>Program.cs</c> for its short timeout (a slow/dead peer must not stall a job
    /// submission for long).
    /// </summary>
    public const string HttpClientName = "AgentClusterPeers";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ClusterPeerClient> _logger;

    public ClusterPeerClient(IHttpClientFactory httpClientFactory, ILogger<ClusterPeerClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Builds a client pointed at <paramref name="peer"/>, authenticated with the peer's own
    /// configured API key — the peer's <c>ApiKeyMiddleware</c> authenticates this exactly like any
    /// other caller; clustering introduces no separate auth mechanism.
    /// </summary>
    private HttpClient CreateClient(ClusterPeerSettings peer)
    {
        // peer.BaseUrl is expected to be a bare authority (e.g. "https://192.168.1.50:7016",
        // no path component), which Uri combines correctly with a relative request path
        // regardless of a trailing slash — no manual normalization needed.
        var client = _httpClientFactory.CreateClient(HttpClientName);
        client.BaseAddress = new Uri(peer.BaseUrl, UriKind.Absolute);
        client.DefaultRequestHeaders.Add(ApiKeyMiddleware.HeaderName, peer.ApiKey);
        return client;
    }

    /// <inheritdoc/>
    public async Task<int?> GetAvailableCapacityAsync(ClusterPeerSettings peer, CancellationToken cancellationToken)
    {
        try
        {
            using var client = CreateClient(peer);
            using var response = await client.GetAsync("api/cluster/status", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Peer {Peer} status check returned {StatusCode}", peer.Name, response.StatusCode);
                return null;
            }

            var status = await response.Content.ReadFromJsonAsync<ClusterStatus>(cancellationToken);
            return status?.AvailableCapacity;
        }
        // A dead/unreachable peer or its own request timeout must never block job submission —
        // treated the same as "no capacity". This also swallows the caller's own cancellation,
        // an accepted simplification: a cancelled job-start request has bigger problems than one
        // swallowed peer probe.
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or NotSupportedException)
        {
            _logger.LogWarning(ex, "Peer {Peer} unreachable during capacity check", peer.Name);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<Guid?> ForwardJobAsync(ClusterPeerSettings peer, string goalDescription, int? maxIterations, CancellationToken cancellationToken)
    {
        try
        {
            using var client = CreateClient(peer);
            using var response = await client.PostAsJsonAsync(
                "api/jobs",
                new StartJobRequest { GoalDescription = goalDescription, MaxIterations = maxIterations },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Peer {Peer} rejected forwarded job ({StatusCode})", peer.Name, response.StatusCode);
                return null;
            }

            var started = await response.Content.ReadFromJsonAsync<StartJobResponse>(cancellationToken);
            return started?.JobId;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or NotSupportedException)
        {
            _logger.LogWarning(ex, "Failed to forward job to peer {Peer}", peer.Name);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<AgentJobStatus?> GetRemoteStatusAsync(ClusterPeerSettings peer, Guid peerJobId, CancellationToken cancellationToken)
    {
        try
        {
            using var client = CreateClient(peer);
            using var response = await client.GetAsync($"api/jobs/{peerJobId}", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<AgentJobStatus>(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or NotSupportedException)
        {
            _logger.LogWarning(ex, "Failed to get remote status from peer {Peer} for job {JobId}", peer.Name, peerJobId);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<Stream?> GetRemoteResultZipAsync(ClusterPeerSettings peer, Guid peerJobId, CancellationToken cancellationToken)
    {
        try
        {
            using var client = CreateClient(peer);
            using var response = await client.GetAsync($"api/jobs/{peerJobId}/result", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            var buffer = new MemoryStream();
            await response.Content.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;
            return buffer;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or NotSupportedException)
        {
            _logger.LogWarning(ex, "Failed to fetch remote result from peer {Peer} for job {JobId}", peer.Name, peerJobId);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> RequestRemoteStopAsync(ClusterPeerSettings peer, Guid peerJobId, CancellationToken cancellationToken)
    {
        try
        {
            using var client = CreateClient(peer);
            using var response = await client.PostAsync($"api/jobs/{peerJobId}/stop", content: null, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or NotSupportedException)
        {
            _logger.LogWarning(ex, "Failed to stop remote job {JobId} on peer {Peer}", peerJobId, peer.Name);
            return false;
        }
    }
}
