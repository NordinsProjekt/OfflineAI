namespace Services.AgentTools;

/// <summary>
/// Handles the QB64 (QBasic) compiler slash commands:
/// <list type="bullet">
///   <item><c>/qb64 &lt;fil.bas&gt;</c> — compiles a .bas file from the active workspace with
///     the configured QB64 compiler and runs the produced executable, capturing its console
///     output so the agentic chat loop can feed it back to the LLM.</item>
///   <item><c>/qb64-kompilera &lt;fil.bas&gt;</c> — compiles without running, so the LLM can
///     syntax-check programs (e.g. graphical ones) that cannot run unattended.</item>
/// </list>
/// <para>
/// The compiler path comes exclusively from <c>AppConfiguration.AgentTools.Qb64</c>; the LLM
/// only supplies a bare filename, which is resolved inside the file agent's base directory
/// (the active workspace) with the same traversal protection as the file agent itself. When no
/// compiler is configured the commands are never offered to (or accepted from) the LLM.
/// Mirrors the shape of <c>IExternalToolsService</c> (command detection + execution + tool
/// descriptions) so <c>IAgenticChatService</c> can drive all tool sources through the same
/// prime → detect → execute → feed-back loop.
/// </para>
/// </summary>
public interface IQb64ToolService
{
    /// <summary>Returns true if the given input starts with a QB64 command and a compiler is configured.</summary>
    bool IsCommand(string input);

    /// <summary>
    /// Executes the QB64 command encoded in <paramref name="input"/>: resolves the .bas file in
    /// the active workspace, compiles it (and, for <c>/qb64</c>, runs the produced executable),
    /// and returns compiler errors or the program's console output as LLM-facing context.
    /// </summary>
    Task<UtilityToolResult> ExecuteAsync(string input);

    /// <summary>
    /// Returns a dictionary describing the QB64 commands: key is the command signature
    /// (e.g. <c>/qb64 &lt;fil.bas&gt;</c>), value is the LLM-facing description. Empty when no
    /// compiler is configured.
    /// </summary>
    IReadOnlyDictionary<string, string> GetToolDescriptions();

    /// <summary>
    /// Scans <paramref name="llmResponse"/> line by line for a QB64 command the LLM wants to
    /// invoke. Returns <c>true</c> and sets <paramref name="command"/> to the exact command
    /// line when one is found.
    /// </summary>
    bool TryFindCommand(string llmResponse, out string command);
}
