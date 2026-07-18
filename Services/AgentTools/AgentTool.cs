namespace Services.AgentTools;

/// <summary>
/// Describes a single parameter accepted by an agent tool.
/// </summary>
/// <param name="Type">JSON Schema type string, e.g. <c>"string"</c>, <c>"integer"</c>.</param>
/// <param name="Description">Natural-language description shown to the LLM.</param>
/// <param name="Required">Whether the LLM must supply this argument. Default: <c>true</c>.</param>
public sealed record ToolParameter(string Type, string Description, bool Required = true);

/// <summary>
/// Metadata describing an agent tool that the LLM can request during tool calling.
/// <para>
/// Register tools via <see cref="IAgentToolRegistry"/> and use the Semantic Kernel
/// plugin <c>BuiltInFileTools</c> to expose them as <c>[KernelFunction]</c> methods
/// that Gemma 4 can auto-invoke.
/// </para>
/// </summary>
/// <param name="Name">Unique tool name (snake_case recommended, e.g. <c>"create_file"</c>).</param>
/// <param name="Description">What the tool does — shown to the LLM to help it decide when to use it.</param>
/// <param name="Parameters">Named parameters the tool accepts.</param>
public sealed record AgentTool(
    string Name,
    string Description,
    IReadOnlyDictionary<string, ToolParameter> Parameters);
