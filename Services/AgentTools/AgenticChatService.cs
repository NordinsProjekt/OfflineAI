using Services.FileAgent;

namespace Services.AgentTools;

/// <inheritdoc/>
public sealed class AgenticChatService : IAgenticChatService
{
    /// <summary>
    /// Safety cap on tool-call round trips so a confused model can't loop forever
    /// requesting tools instead of answering.
    /// </summary>
    private const int MaxToolCallRounds = 3;

    private readonly IFileAgentService _fileAgent;

    public AgenticChatService(IFileAgentService fileAgent)
    {
        _fileAgent = fileAgent ?? throw new ArgumentNullException(nameof(fileAgent));
    }

    /// <inheritdoc/>
    public async Task<AgenticChatResult> SendWithToolsAsync(
        string userMessage,
        Func<string, Task<string>> sendToLlm,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userMessage);
        ArgumentNullException.ThrowIfNull(sendToLlm);

        var invocations = new List<ToolInvocation>();

        // "Start message": primes the LLM with the tool dictionary (slash command → description)
        // alongside the user's actual question.
        var startMessage = $"{_fileAgent.BuildToolsSystemPrompt()}\n\nFråga: {userMessage}";
        var response = await sendToLlm(startMessage);

        for (var round = 0; round < MaxToolCallRounds; round++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Parse the LLM's reply using plain string search for a known slash command.
            if (!_fileAgent.TryFindAgentCommand(response, out var command))
                break; // No tool requested — treat this reply as the final answer.

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
                    summary = $"✓ Fil sparad: {result.TargetFilename}";
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
                    summary = applyResult.Message;
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
}
