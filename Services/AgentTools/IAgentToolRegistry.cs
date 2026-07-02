namespace Services.AgentTools;

/// <summary>
/// Registry that stores agent tool definitions and dispatches LLM tool-call
/// requests to the appropriate handler.
/// <para>
/// For Semantic Kernel-based tool calling with Gemma 4, use <c>BuiltInFileTools</c>
/// (a <c>[KernelFunction]</c> plugin) together with <c>IGemma4AgentService</c>.
/// This registry is useful for lightweight scenarios without SK or for building
/// a custom dispatch layer.
/// </para>
/// </summary>
public interface IAgentToolRegistry
{
    /// <summary>
    /// Registers a tool with its asynchronous handler.
    /// </summary>
    /// <param name="tool">Tool metadata (name, description, parameters).</param>
    /// <param name="handler">
    /// Called when the LLM requests this tool.
    /// The dictionary maps parameter name → value (always as strings).
    /// Returns the tool result as a string to be sent back to the model.
    /// </param>
    void Register(AgentTool tool, Func<IReadOnlyDictionary<string, string>, Task<string>> handler);

    /// <summary>Returns all registered tool definitions.</summary>
    IReadOnlyList<AgentTool> GetTools();

    /// <summary>
    /// Invokes a registered tool by name with the supplied argument map.
    /// Throws <see cref="KeyNotFoundException"/> if no tool with that name is registered.
    /// </summary>
    Task<string> InvokeAsync(string toolName, IReadOnlyDictionary<string, string> arguments);
}
