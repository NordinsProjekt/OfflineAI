namespace Services.AgentTools;

/// <summary>
/// Handles operator-configured "external tool" slash commands: each tool is a local executable
/// listed in <c>AppConfiguration.AgentTools.ExternalTools</c> (typically via user secrets or
/// appsettings) with a command name, a path, and an LLM-facing description. The LLM invokes a
/// tool by writing <c>/&lt;command&gt; &lt;argument&gt;</c>; the service runs the executable,
/// captures its stdout, and returns it so the agentic chat loop can feed it back to the LLM.
/// <para>
/// Only configured executables can ever be started — the LLM selects a tool by name and supplies
/// argument text only, never a path. Mirrors the shape of <c>IUtilityToolsService</c>
/// (command detection + execution + tool descriptions) so <c>IAgenticChatService</c> can drive
/// all tool sources through the same prime → detect → execute → feed-back loop.
/// </para>
/// </summary>
public interface IExternalToolsService
{
    /// <summary>Returns true if the given input starts with a configured external tool command.</summary>
    bool IsCommand(string input);

    /// <summary>
    /// Executes the external tool command encoded in <paramref name="input"/>: resolves the
    /// configured executable, runs it with any configured fixed arguments plus the LLM-supplied
    /// argument text, and returns the captured stdout as LLM-facing context.
    /// </summary>
    Task<UtilityToolResult> ExecuteAsync(string input);

    /// <summary>
    /// Returns a dictionary describing each configured tool: key is the exact command signature
    /// (e.g. <c>/väder &lt;ort&gt;</c>), value is the operator-written description. Used to tell
    /// the LLM which external tools it may invoke and how they work.
    /// </summary>
    IReadOnlyDictionary<string, string> GetToolDescriptions();

    /// <summary>
    /// Scans <paramref name="llmResponse"/> line by line for a configured external tool command
    /// the LLM wants to invoke. Returns <c>true</c> and sets <paramref name="command"/> to the
    /// exact command line when one is found.
    /// </summary>
    bool TryFindCommand(string llmResponse, out string command);
}
