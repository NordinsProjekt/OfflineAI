namespace AgentKit.Api.Security;

/// <summary>
/// Security-related settings for the API, bound from the top-level <c>Security</c> configuration
/// section (appsettings.json / user secrets / environment variables). Keeping these in one place
/// lets a deployment turn on authentication, restrict CORS, and cap concurrency without code
/// changes.
/// </summary>
public sealed class ApiSecurityOptions
{
    /// <summary>Configuration section name these options bind from.</summary>
    public const string SectionName = "Security";

    /// <summary>
    /// When true (the secure default), every request outside the docs/health/preflight allow-list
    /// must present a valid <c>X-API-Key</c> header. Set to false only for a trusted,
    /// localhost-only deployment where anonymous access is acceptable.
    /// </summary>
    public bool RequireApiKey { get; set; } = true;

    /// <summary>
    /// Shared secret compared against the caller's <c>X-API-Key</c> header. Store it in user
    /// secrets or an environment variable, never in a committed appsettings file. When
    /// <see cref="RequireApiKey"/> is true and this is empty, the API fails closed (all requests
    /// are rejected) rather than silently allowing anonymous access.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Exact origins allowed to make browser (CORS) requests. When empty, no cross-origin browser
    /// requests are permitted. Never reflect arbitrary origins together with credentials.
    /// </summary>
    public List<string> AllowedCorsOrigins { get; set; } = new();

    /// <summary>
    /// Maximum number of requests processed concurrently before further requests are queued and
    /// then rejected with HTTP 429. When 0, a sensible value is derived from the model pool size.
    /// </summary>
    public int MaxConcurrentRequests { get; set; } = 0;
}
