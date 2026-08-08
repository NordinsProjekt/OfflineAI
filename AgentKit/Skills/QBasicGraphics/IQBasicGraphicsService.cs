using AgentKit.Skills.Utility;

namespace AgentKit.Skills.QBasicGraphics;

/// <summary>
/// Handles the QBasic graphics reference command:
/// <list type="bullet">
///   <item><c>/qbasic-grafik &lt;ämne&gt;</c> — returns a short article on one graphics topic
///     (screen modes, pixels, shapes, DATA figures, GET/PUT sprites, pages, masking, palette,
///     speed, and the rules for graphics programs in this headless environment). Without an
///     argument — or with a word no article matches — it returns the topic index instead.</item>
/// </list>
/// <para>
/// Unlike the other skills this one runs entirely offline against a compiled-in text
/// (<see cref="QBasicGraphicsReference"/>): nothing is configured, nothing is executed, and it is
/// therefore always available. It is the "look it up before you write it" half of the QBasic
/// support, with <c>QBasicStructureLinter</c> (checks after the write) and <c>Qb64ToolService</c>
/// (compiles) as the other two.
/// </para>
/// <para>
/// Mirrors the shape of <c>IQb64ToolService</c> and <c>IUtilityToolsService</c> (command detection
/// + execution + tool descriptions) so <c>IAgenticChatService</c> can drive it through the same
/// prime → detect → execute → feed-back loop.
/// </para>
/// </summary>
public interface IQBasicGraphicsService
{
    /// <summary>Returns true if the given input starts with the reference command.</summary>
    bool IsCommand(string input);

    /// <summary>
    /// Looks up the topic named in <paramref name="input"/> and returns the article as LLM-facing
    /// context. Always succeeds: an unknown or missing topic returns the topic index, which tells
    /// the model exactly which words work instead of costing it a wasted round.
    /// </summary>
    Task<UtilityToolResult> ExecuteAsync(string input);

    /// <summary>
    /// Returns a dictionary describing the command: key is the command signature, value is the
    /// LLM-facing description (which lists the available topics).
    /// </summary>
    IReadOnlyDictionary<string, string> GetToolDescriptions();

    /// <summary>
    /// Scans <paramref name="llmResponse"/> line by line for the reference command. Returns
    /// <c>true</c> and sets <paramref name="command"/> to the exact command line when one is found.
    /// </summary>
    bool TryFindCommand(string llmResponse, out string command);
}
