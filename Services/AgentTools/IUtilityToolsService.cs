namespace Services.AgentTools;

/// <summary>
/// Result of a <see cref="IUtilityToolsService"/> command.
/// </summary>
/// <param name="IsSuccess">Whether the command completed successfully.</param>
/// <param name="Message">Short, user-facing status message (success confirmation or error detail).</param>
/// <param name="InjectedContext">
/// For successful commands, the text that should be forwarded to the LLM as context (e.g. the
/// current time, or an API response combined with the caller's instruction). Null on failure.
/// </param>
public sealed record UtilityToolResult(bool IsSuccess, string Message, string? InjectedContext = null)
{
    /// <summary>Creates a successful result, optionally carrying LLM-facing context.</summary>
    public static UtilityToolResult Success(string message, string? injectedContext = null) =>
        new(true, message, injectedContext);

    /// <summary>Creates a failed result.</summary>
    public static UtilityToolResult Failure(string message) =>
        new(false, message);
}

/// <summary>
/// Handles built-in "utility" agent slash commands that are not file operations:
/// <list type="bullet">
///   <item><c>/tid</c> — returns the current time.</item>
///   <item><c>/datum</c> — returns today's date.</item>
///   <item><c>/api &lt;slutpunkt&gt; &lt;instruktion&gt;</c> — calls a named, pre-configured HTTP
///     endpoint (see <c>AppConfiguration.AgentTools.Endpoints</c>) and combines its response with
///     the given instruction, ready to forward to the LLM. The LLM can only select an endpoint by
///     name — it can never supply an arbitrary URL — so outbound calls are limited to
///     destinations the user has explicitly configured.</item>
/// </list>
/// Mirrors the shape of <c>IFileAgentService</c> (command detection + execution + tool
/// descriptions) so <c>IAgenticChatService</c> can drive both services through the same
/// prime → detect → execute → feed-back loop.
/// </summary>
public interface IUtilityToolsService
{
    /// <summary>Returns true if the given input starts with a recognised utility command (/tid, /datum, /api).</summary>
    bool IsCommand(string input);

    /// <summary>Executes the utility command encoded in <paramref name="input"/>.</summary>
    Task<UtilityToolResult> ExecuteAsync(string input);

    /// <summary>
    /// Calls the named, pre-configured API endpoint directly (used by Semantic Kernel tool
    /// calling via <c>BuiltInUtilityTools</c>, where the model supplies structured arguments
    /// instead of a slash-command string).
    /// </summary>
    Task<UtilityToolResult> CallNamedApiAsync(string endpointName, string instruction = "");

    /// <summary>Returns the names of all API endpoints configured for <c>/api</c> / <c>call_api</c>.</summary>
    IReadOnlyList<string> GetApiEndpointNames();

    /// <summary>
    /// Returns a dictionary describing each available slash-command tool: key is the exact
    /// command signature, value is a natural-language description. Used to tell the LLM which
    /// utility tools it may invoke.
    /// </summary>
    IReadOnlyDictionary<string, string> GetToolDescriptions();

    /// <summary>
    /// Scans <paramref name="llmResponse"/> line by line for a known utility slash command the
    /// LLM wants to invoke. Returns <c>true</c> and sets <paramref name="command"/> to the exact
    /// command line when one is found.
    /// </summary>
    bool TryFindCommand(string llmResponse, out string command);
}
