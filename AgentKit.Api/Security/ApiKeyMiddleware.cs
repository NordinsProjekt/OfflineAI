using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentKit.Api.Models;

namespace AgentKit.Api.Security;

/// <summary>
/// Rejects requests that do not present a valid API key. The Swagger UI, health checks, and CORS
/// preflight (OPTIONS) requests are allowed through unauthenticated; everything else requires the
/// configured <c>X-API-Key</c> header when <see cref="ApiSecurityOptions.RequireApiKey"/> is on.
/// Fails closed: if a key is required but none is configured, all protected requests are rejected.
/// </summary>
public sealed class ApiKeyMiddleware
{
    public const string HeaderName = "X-API-Key";

    private readonly RequestDelegate _next;
    private readonly ApiSecurityOptions _options;
    private readonly ILogger<ApiKeyMiddleware> _logger;

    public ApiKeyMiddleware(RequestDelegate next, ApiSecurityOptions options, ILogger<ApiKeyMiddleware> logger)
    {
        _next = next;
        _options = options;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;

        // Docs, health probes, and CORS preflight are intentionally anonymous.
        if (HttpMethods.IsOptions(context.Request.Method)
            || path.StartsWithSegments("/swagger")
            || path.StartsWithSegments("/api/health"))
        {
            await _next(context);
            return;
        }

        if (!_options.RequireApiKey)
        {
            await _next(context);
            return;
        }

        if (string.IsNullOrEmpty(_options.ApiKey))
        {
            _logger.LogError(
                "Security:RequireApiKey is true but Security:ApiKey is not configured. Rejecting request to {Path}.",
                path);
            await WriteUnauthorizedAsync(context,
                "API key authentication is required but no key is configured on the server. " +
                "Set Security:ApiKey (via user secrets) or set Security:RequireApiKey to false to allow anonymous localhost access.");
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var provided)
            || !KeysMatch(provided.ToString(), _options.ApiKey))
        {
            await WriteUnauthorizedAsync(context, $"A valid {HeaderName} header is required.");
            return;
        }

        await _next(context);
    }

    /// <summary>Constant-time comparison that also treats a length mismatch as a non-match.</summary>
    private static bool KeysMatch(string provided, string expected)
    {
        var providedBytes = Encoding.UTF8.GetBytes(provided);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);

        if (providedBytes.Length != expectedBytes.Length)
        {
            // Still perform a fixed-time comparison to avoid leaking length via timing.
            CryptographicOperations.FixedTimeEquals(expectedBytes, expectedBytes);
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }

    private static async Task WriteUnauthorizedAsync(HttpContext context, string details)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";

        var body = JsonSerializer.Serialize(new ErrorResponse
        {
            Error = "Unauthorized",
            StatusCode = StatusCodes.Status401Unauthorized,
            Details = details
        });

        await context.Response.WriteAsync(body);
    }
}
