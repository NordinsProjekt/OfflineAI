namespace OfflineAI.Api.Models;

/// <summary>
/// API health status response.
/// </summary>
public class HealthResponse
{
    /// <summary>
    /// Health status ("healthy").
    /// </summary>
    public required string Status { get; set; }

    /// <summary>
    /// Timestamp of the health check (UTC).
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// API version.
    /// </summary>
    public required string Version { get; set; }

    /// <summary>
    /// Service name.
    /// </summary>
    public required string Service { get; set; }
}
