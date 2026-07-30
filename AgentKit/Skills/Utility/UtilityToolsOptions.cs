namespace AgentKit.Skills.Utility;

/// <summary>
/// Options for <see cref="UtilityToolsService"/>: the named HTTP API endpoints the LLM may call
/// via <c>/api &lt;slutpunkt&gt; &lt;instruktion&gt;</c>. Only endpoints listed here can be
/// invoked — the LLM selects an endpoint by name and can never supply an arbitrary URL. Hosts
/// map this from their own configuration system (e.g. appsettings.json) so the library itself
/// has no configuration dependency.
/// </summary>
public class UtilityToolsOptions
{
    /// <summary>The whitelisted endpoints available to the <c>/api</c> tool. Empty = no /api tool offered.</summary>
    public List<ApiEndpointOptions> Endpoints { get; set; } = new();
}

/// <summary>
/// Describes one named, pre-configured HTTP endpoint the agent is allowed to call.
/// The LLM selects an endpoint by <see cref="Name"/> and can never specify a raw URL —
/// this keeps outbound calls limited to destinations the host has explicitly configured.
/// </summary>
public class ApiEndpointOptions
{
    /// <summary>Unique name the LLM uses to select this endpoint, e.g. "weather".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Short description shown to the LLM to help it decide when to use this endpoint.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Full request URL. May contain an <c>{input}</c> placeholder filled in from the LLM's instruction.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>HTTP method to use. Default: "GET".</summary>
    public string Method { get; set; } = "GET";

    /// <summary>Optional static request headers (e.g. API keys) sent with every call to this endpoint.</summary>
    public Dictionary<string, string> Headers { get; set; } = new();

    /// <summary>Per-request timeout in milliseconds. Default: 15000 (15 seconds).</summary>
    public int TimeoutMs { get; set; } = 15_000;

    /// <summary>Maximum characters of the response body returned to the LLM. Default: 4000.</summary>
    public int MaxResponseLength { get; set; } = 4000;
}
