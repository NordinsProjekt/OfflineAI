using AgentKit.Skills.External;
using AgentKit.Skills.Files;
using AgentKit.Skills.QBasicGraphics;
using AgentKit.Skills.Qb64;
using AgentKit.Skills.Utility;

namespace AgentKit.ToolLoop;

/// <inheritdoc/>
public sealed class AgenticChatService : IAgenticChatService
{
    /// <summary>
    /// Default safety cap on tool-call round trips so a confused model can't loop forever
    /// requesting tools instead of answering. Overridable via the constructor — typically from
    /// <c>AppConfiguration.AgentTools.MaxToolCallRounds</c>.
    /// </summary>
    private const int DefaultMaxToolCallRounds = 3;

    private readonly IFileAgentService _fileAgent;
    private readonly IUtilityToolsService? _utilityTools;
    private readonly IExternalToolsService? _externalTools;
    private readonly IQb64ToolService? _qb64Tools;
    private readonly IQBasicGraphicsService? _qbasicGraphics;
    private readonly int _maxToolCallRounds;

    /// <param name="fileAgent">Executes file-agent slash commands (/skapa, /fyll, /läs, /redigera, /lista).</param>
    /// <param name="utilityTools">
    /// Optional. Executes built-in utility commands (/tid, /datum, /api). When <c>null</c>, only
    /// file-agent tools are offered to the LLM.
    /// </param>
    /// <param name="maxToolCallRounds">
    /// Safety cap on internal tool-call round trips. Non-positive values fall back to
    /// <see cref="DefaultMaxToolCallRounds"/>.
    /// </param>
    /// <param name="externalTools">
    /// Optional. Executes operator-configured external executables (see
    /// <c>AppConfiguration.AgentTools.ExternalTools</c>). When <c>null</c> or when no tools are
    /// configured, no external commands are offered to the LLM.
    /// </param>
    /// <param name="qb64Tools">
    /// Optional. Compiles and runs QBasic files via the QB64 compiler (/qb64,
    /// /qb64-kompilera — see <c>AppConfiguration.AgentTools.Qb64</c>). When <c>null</c> or when
    /// no compiler is configured, the QB64 commands are not offered to the LLM.
    /// </param>
    /// <param name="qbasicGraphics">
    /// Optional. Looks up QBasic/QB64 graphics syntax (/qbasic-grafik). Needs no configuration —
    /// the reference is compiled in — so hosts can simply always pass one.
    /// </param>
    public AgenticChatService(
        IFileAgentService fileAgent,
        IUtilityToolsService? utilityTools = null,
        int maxToolCallRounds = DefaultMaxToolCallRounds,
        IExternalToolsService? externalTools = null,
        IQb64ToolService? qb64Tools = null,
        IQBasicGraphicsService? qbasicGraphics = null)
    {
        _fileAgent = fileAgent ?? throw new ArgumentNullException(nameof(fileAgent));
        _utilityTools = utilityTools;
        _externalTools = externalTools;
        _qb64Tools = qb64Tools;
        _qbasicGraphics = qbasicGraphics;
        _maxToolCallRounds = maxToolCallRounds > 0 ? maxToolCallRounds : DefaultMaxToolCallRounds;
    }

    /// <inheritdoc/>
    public async Task<AgenticChatResult> SendWithToolsAsync(
        string userMessage,
        Func<string, Task<string>> sendToLlm,
        CancellationToken cancellationToken = default,
        Action<string>? onToolStatus = null,
        string? recentlyUploadedFilename = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userMessage);
        ArgumentNullException.ThrowIfNull(sendToLlm);

        var invocations = new List<ToolInvocation>();

        // "Start message": primes the LLM with the tool dictionary (slash command → description)
        // alongside the user's actual question. Utility tool descriptions (time/date/api), when
        // available, are appended in the same bullet format so the LLM sees one unified tool list.
        var toolsPrompt = _fileAgent.BuildToolsSystemPrompt();
        if (_utilityTools is not null)
            toolsPrompt = AppendToolDescriptions(toolsPrompt, _utilityTools.GetToolDescriptions());
        if (_externalTools is not null)
            toolsPrompt = AppendToolDescriptions(toolsPrompt, _externalTools.GetToolDescriptions());
        if (_qb64Tools is not null)
            toolsPrompt = AppendToolDescriptions(toolsPrompt, _qb64Tools.GetToolDescriptions());
        if (_qbasicGraphics is not null)
            toolsPrompt = AppendToolDescriptions(toolsPrompt, _qbasicGraphics.GetToolDescriptions());

        if (!string.IsNullOrWhiteSpace(recentlyUploadedFilename))
            toolsPrompt +=
                $"\n\nObs: Användaren har nyss laddat upp filen \"{recentlyUploadedFilename}\" till agentkatalogen. " +
                $"Om frågan nedan handlar om ett uppladdat dokument utan att själv ange ett filnamn, anta att det " +
                $"gäller \"{recentlyUploadedFilename}\" och använd rätt verktyg med exakt det filnamnet (t.ex. " +
                $"/läs-pdf {recentlyUploadedFilename} <instruktion> om filen är en PDF, annars " +
                $"/läs {recentlyUploadedFilename} <instruktion>).";

        var startMessage = $"{toolsPrompt}\n\nFråga: {userMessage}";
        var response = await sendToLlm(startMessage);

        for (var round = 0; round < _maxToolCallRounds; round++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Parse the LLM's reply using plain string search for a known slash command — file
            // agent commands first, then utility commands (time/date/api), then the QB64
            // compiler commands, the QBasic graphics reference, and finally operator-configured
            // external executables.
            var isFileCommand = _fileAgent.TryFindAgentCommand(response, out var command);
            var isUtilityCommand = !isFileCommand
                && _utilityTools is not null
                && _utilityTools.TryFindCommand(response, out command);
            var isQb64Command = !isFileCommand && !isUtilityCommand
                && _qb64Tools is not null
                && _qb64Tools.TryFindCommand(response, out command);
            var isQbasicReferenceCommand = !isFileCommand && !isUtilityCommand && !isQb64Command
                && _qbasicGraphics is not null
                && _qbasicGraphics.TryFindCommand(response, out command);
            var isExternalCommand = !isFileCommand && !isUtilityCommand && !isQb64Command
                && !isQbasicReferenceCommand
                && _externalTools is not null
                && _externalTools.TryFindCommand(response, out command);

            if (!isFileCommand && !isUtilityCommand && !isQb64Command && !isQbasicReferenceCommand && !isExternalCommand)
                break; // No tool requested — treat this reply as the final answer.

            onToolStatus?.Invoke($"🔧 Kör: {command}");

            if (isUtilityCommand || isQb64Command || isQbasicReferenceCommand || isExternalCommand)
            {
                UtilityToolResult toolResult;
                if (isUtilityCommand)
                    toolResult = await _utilityTools!.ExecuteAsync(command);
                else if (isQb64Command)
                    toolResult = await _qb64Tools!.ExecuteAsync(command);
                else if (isQbasicReferenceCommand)
                    toolResult = await _qbasicGraphics!.ExecuteAsync(command);
                else
                    toolResult = await _externalTools!.ExecuteAsync(command);
                var toolText = toolResult.InjectedContext ?? toolResult.Message;
                invocations.Add(new ToolInvocation(command, toolResult.Message));

                response = await sendToLlm(
                    $"Verktygsresultat för \"{command}\":\n{toolText}\n\n" +
                    $"Använd informationen ovan för att besvara den ursprungliga frågan: {userMessage}\n" +
                    "Skriv inget nytt kommando om du inte behöver ytterligare information.");
                continue;
            }

            // Inline-content shortcut: the model issued /fyll or /skapa and put the file body
            // directly in the same message (typically a Markdown code fence) instead of using the
            // describe-then-generate round. This is a very common pattern; without it the fenced
            // content is discarded and the write is silently lost. Only fires when the command
            // names a valid file AND the response actually carries extractable content — otherwise
            // it falls through to the normal /fyll (generate) or /skapa (empty file) behaviour.
            if (_fileAgent.TryGetInlineWriteTarget(command, out var inlineTarget)
                && _fileAgent.TryExtractFileContent(response, out var inlineContent))
            {
                await _fileAgent.WriteExtractedContentAsync(inlineTarget, inlineContent);
                var inlineSummary = WithQbasicCheck($"✓ Fil sparad: {inlineTarget}", inlineTarget, inlineContent);
                invocations.Add(new ToolInvocation(command, inlineSummary));

                response = await sendToLlm(
                    $"Verktyget \"{command}\" har körts. Resultat: {inlineSummary}\n\n" +
                    $"Bekräfta kort för användaren och besvara annars den ursprungliga frågan: {userMessage}\n" +
                    "Skriv inget nytt kommando om du inte behöver ytterligare information.");
                continue;
            }

            var result = await _fileAgent.ExecuteAsync(command);

            if (result.ResultType == FileAgentResultType.FillRequested && result.LlmPrompt is not null)
            {
                // /fyll needs a second LLM round-trip: the model must generate the file content
                // itself before anything can be saved.
                var fillResponse = await sendToLlm(result.LlmPrompt);

                string summary;
                if (_fileAgent.TryExtractFileContent(fillResponse, out var content))
                {
                    await _fileAgent.WriteExtractedContentAsync(result.TargetFilename!, content);
                    summary = WithQbasicCheck($"✓ Fil sparad: {result.TargetFilename}", result.TargetFilename!, content);
                }
                else
                {
                    summary = "⚠ Kunde inte extrahera filinnehåll — filen sparades inte.";
                }

                invocations.Add(new ToolInvocation(command, summary));

                response = await sendToLlm(
                    $"Verktyget \"{command}\" har körts. Resultat: {summary}\n\n" +
                    $"Bekräfta kort för användaren och besvara annars den ursprungliga frågan: {userMessage}\n" +
                    "Skriv inget nytt kommando om du inte behöver ytterligare information.");
                continue;
            }

            if (result.ResultType == FileAgentResultType.EditRequested && result.LlmPrompt is not null)
            {
                // /redigera needs a second LLM round-trip: the model must specify which line
                // ranges to replace and with what before the service can apply any changes.
                var editResponse = await sendToLlm(result.LlmPrompt);

                string summary;
                if (_fileAgent.TryExtractLineEdits(editResponse, out var edits))
                {
                    var applyResult = await _fileAgent.ApplyLineEditsAsync(result.TargetFilename!, edits);
                    summary = applyResult.IsSuccess
                        ? await WithQbasicCheckAfterEditAsync(applyResult.Message, result.TargetFilename!)
                        : applyResult.Message;
                }
                else
                {
                    summary = "⚠ Kunde inte tolka radändringar — filen ändrades inte.";
                }

                invocations.Add(new ToolInvocation(command, summary));

                response = await sendToLlm(
                    $"Verktyget \"{command}\" har körts. Resultat: {summary}\n\n" +
                    $"Bekräfta kort för användaren och besvara annars den ursprungliga frågan: {userMessage}\n" +
                    "Skriv inget nytt kommando om du inte behöver ytterligare information.");
                continue;
            }

            var resultText = result.InjectedContext ?? result.Message;
            invocations.Add(new ToolInvocation(command, result.Message));

            response = await sendToLlm(
                $"Verktygsresultat för \"{command}\":\n{resultText}\n\n" +
                $"Använd informationen ovan för att besvara den ursprungliga frågan: {userMessage}\n" +
                "Skriv inget nytt kommando om du inte behöver ytterligare information.");
        }

        return new AgenticChatResult(response, invocations);
    }

    /// <summary>
    /// Appends the QBasic structural/keyword findings for a freshly written .bas file to the
    /// summary the LLM is about to be shown, so a broken program comes back in the very next turn
    /// instead of surviving the run. A no-op for every other file type.
    /// <para>
    /// Runs on every write rather than only when the QB64 tool is unavailable: even with a
    /// compiler configured, hearing about an invented keyword at write time beats hearing about
    /// it one <c>/qb64</c> round trip later, and the check costs a string scan.
    /// </para>
    /// </summary>
    private static string WithQbasicCheck(string summary, string filename, string content)
    {
        var issues = QBasicStructureLinter.DescribeIssuesAfterWrite(filename, content);
        return issues is null ? summary : $"{summary}\n{issues}";
    }

    /// <summary>
    /// The same check for <c>/redigera</c>, which applies its line edits inside the file service
    /// and never hands the resulting content back — so the file is read from the workspace
    /// instead. A failed read is ignored rather than reported: the edit itself succeeded, and a
    /// missing lint result must not read to the LLM as a failed write.
    /// </summary>
    private async Task<string> WithQbasicCheckAfterEditAsync(string summary, string filename)
    {
        if (!filename.Trim().EndsWith(".bas", StringComparison.OrdinalIgnoreCase))
            return summary;

        var reread = await _fileAgent.ReadFileRawAsync(filename);
        return reread.IsSuccess
            ? WithQbasicCheck(summary, filename, reread.InjectedContext ?? string.Empty)
            : summary;
    }

    /// <summary>
    /// Appends additional tool descriptions (utility commands like /tid and /datum, or
    /// operator-configured external tools) to the file-agent tools system prompt, in the same
    /// "- command : description" bullet format, so the LLM sees one unified tool list
    /// regardless of which service ultimately executes the command.
    /// </summary>
    private static string AppendToolDescriptions(string toolsPrompt, IReadOnlyDictionary<string, string> descriptions)
    {
        if (descriptions.Count == 0)
            return toolsPrompt;

        var lines = descriptions.Select(kv => $"- {kv.Key} : {kv.Value}");
        return toolsPrompt + "\n" + string.Join("\n", lines);
    }
}
