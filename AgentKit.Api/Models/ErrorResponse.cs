namespace AgentKit.Api.Models;

/// <summary>Standardized error response for API endpoints.</summary>
public class ErrorResponse
{
    /// <summary>Error message describing what went wrong.</summary>
    public required string Error { get; set; }

    /// <summary>HTTP status code.</summary>
    public int StatusCode { get; set; }

    /// <summary>Detailed error information (for debugging).</summary>
    public string? Details { get; set; }

    /// <summary>Timestamp when the error occurred.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
