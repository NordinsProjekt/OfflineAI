namespace AgentKit.Skills.External;

/// <summary>
/// Describes one named, pre-configured local executable the agent is allowed to run.
/// The LLM invokes it as <c>/&lt;Command&gt; &lt;argument&gt;</c> and receives the process's
/// stdout as the tool result. Follows the whitelist principle: paths come only from host
/// configuration, never from the LLM.
/// </summary>
public class ExternalToolOptions
{
    /// <summary>
    /// Slash-command name (without the leading '/') the LLM uses to invoke this tool,
    /// e.g. "väder" → the LLM writes "/väder Stockholm".
    /// </summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>Full path to the executable, e.g. "d:\\tools\\weather.exe".</summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>
    /// Description shown to the LLM of what the tool does and what input it expects.
    /// This is the LLM's only documentation for the tool — describe the parameters here.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Optional usage hint appended to the command signature in the tool list shown to the
    /// LLM, e.g. "&lt;ort&gt;" renders as "/väder &lt;ort&gt;". Empty for tools without input.
    /// </summary>
    public string Usage { get; set; } = string.Empty;

    /// <summary>
    /// Optional arguments always passed to the executable, before any text the LLM supplies.
    /// Useful for interpreter-hosted tools, e.g. ExecutablePath "python.exe" with
    /// FixedArguments "d:\\scripts\\tool.py".
    /// </summary>
    public string FixedArguments { get; set; } = string.Empty;

    /// <summary>Per-run timeout in milliseconds; the process is killed when exceeded. Default: 30000.</summary>
    public int TimeoutMs { get; set; } = 30_000;

    /// <summary>Maximum characters of process output returned to the LLM. Default: 4000.</summary>
    public int MaxOutputLength { get; set; } = 4000;
}
