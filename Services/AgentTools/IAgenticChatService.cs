using Services.FileAgent;

namespace Services.AgentTools;

/// <summary>
/// Describes a single tool invocation performed automatically by <see cref="IAgenticChatService"/>
/// while fulfilling a user request.
/// </summary>
/// <param name="Command">The exact slash command the LLM requested (as found via string search).</param>
/// <param name="ResultSummary">A short, user-facing summary of what the tool call produced.</param>
public sealed record ToolInvocation(string Command, string ResultSummary);

/// <summary>
/// Result of an agentic chat turn: the LLM's final answer plus a log of any tool calls
/// it made along the way.
/// </summary>
/// <param name="FinalResponse">The LLM's final, user-facing answer.</param>
/// <param name="ToolInvocations">Tool calls executed while producing the final answer, in order. Empty if no tool was used.</param>
public sealed record AgenticChatResult(string FinalResponse, IReadOnlyList<ToolInvocation> ToolInvocations);

/// <summary>
/// Lightweight, text-based agentic pattern for LLM backends that don't support structured
/// JSON tool calling (the "Classic" pooled subprocess and the Gemma 4 CLI backend reached via
/// <c>DashboardState.SendActiveAsync</c> / <c>SendQuickAskActiveAsync</c>).
/// <para>
/// The LLM is told about the available <see cref="IFileAgentService"/> slash commands
/// (a dictionary of command → description) in the outgoing prompt. If its reply contains one
/// of those commands, it is detected with plain string search, executed, and the result is fed
/// back to the LLM so it can produce a final answer that uses the tool's output.
/// </para>
/// <para>
/// This is a separate, simpler mechanism than the JSON/Semantic-Kernel tool calling used by
/// <c>IGemma4CliService.ChatWithToolsAsync</c> / <c>IAgentToolRegistry</c> — it works with any
/// backend that can only produce plain text.
/// </para>
/// </summary>
public interface IAgenticChatService
{
    /// <summary>
    /// Sends <paramref name="userMessage"/> to the LLM (via <paramref name="sendToLlm"/>) together
    /// with the available tool descriptions, detects any slash-command tool call in the reply via
    /// string search, executes it through <see cref="IFileAgentService"/>, and asks the LLM to
    /// produce a final answer using the tool result. If no tool is requested, the first reply is
    /// returned as-is.
    /// </summary>
    /// <param name="userMessage">The user's question.</param>
    /// <param name="sendToLlm">
    /// Delegate that sends a prompt to whichever LLM backend is currently active and returns its
    /// raw text reply (e.g. <c>Dashboard.SendQuickAskActiveAsync</c> or <c>Dashboard.SendActiveAsync</c>).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<AgenticChatResult> SendWithToolsAsync(
        string userMessage,
        Func<string, Task<string>> sendToLlm,
        CancellationToken cancellationToken = default);
}
