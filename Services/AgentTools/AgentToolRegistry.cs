namespace Services.AgentTools;

/// <inheritdoc/>
public sealed class AgentToolRegistry : IAgentToolRegistry
{
    private readonly Dictionary<string, (AgentTool Tool, Func<IReadOnlyDictionary<string, string>, Task<string>> Handler)>
        _tools = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public void Register(
        AgentTool tool,
        Func<IReadOnlyDictionary<string, string>, Task<string>> handler)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(handler);
        _tools[tool.Name] = (tool, handler);
    }

    /// <inheritdoc/>
    public IReadOnlyList<AgentTool> GetTools() =>
        _tools.Values.Select(x => x.Tool).ToList();

    /// <inheritdoc/>
    public async Task<string> InvokeAsync(
        string toolName,
        IReadOnlyDictionary<string, string> arguments)
    {
        if (!_tools.TryGetValue(toolName, out var entry))
            throw new KeyNotFoundException($"No tool registered with name '{toolName}'.");

        return await entry.Handler(arguments);
    }
}
