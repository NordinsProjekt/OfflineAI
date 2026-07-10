using FluentAssertions;
using Services.AgentTools;
using Services.FileAgent;
using Services.Tests.TestHelpers;

namespace Services.Tests.AgentTools;

/// <summary>
/// Unit tests for <see cref="AgenticChatService"/>: the lightweight, text-based tool-calling
/// loop that detects file-agent slash commands in an LLM's reply, executes them via a real
/// <see cref="FileAgentService"/> rooted at a temp directory, and feeds the result back to the
/// LLM. Uses <see cref="ScriptedLlm"/> to script a sequence of canned LLM replies and capture
/// every prompt sent to it.
/// </summary>
public sealed class AgenticChatServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileAgentService _fileAgent;
    private readonly AgenticChatService _sut;

    public AgenticChatServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AgenticChatServiceTests_" + Guid.NewGuid());
        _fileAgent = new FileAgentService(_tempDir);
        _sut = new AgenticChatService(_fileAgent);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    /// <summary>
    /// Fake LLM backend: returns a scripted sequence of replies (one per call, in order) and
    /// records every prompt it was sent so tests can assert on the round-trip conversation.
    /// </summary>
    private sealed class ScriptedLlm
    {
        private readonly Queue<string> _responses;

        public ScriptedLlm(params string[] responses) => _responses = new Queue<string>(responses);

        public List<string> Prompts { get; } = new();

        public Task<string> SendAsync(string prompt)
        {
            Prompts.Add(prompt);
            if (_responses.Count == 0)
                throw new InvalidOperationException("ScriptedLlm ran out of scripted responses.");
            return Task.FromResult(_responses.Dequeue());
        }
    }

    /// <summary>
    /// Fake <see cref="IUtilityToolsService"/>: mirrors the real command-detection/execution
    /// shape (line-scan for a known command prefix, then a caller-supplied executor) without any
    /// HTTP or configuration dependency, so the loop's utility-tool branch can be tested in
    /// isolation.
    /// </summary>
    private sealed class FakeUtilityToolsService : IUtilityToolsService
    {
        private readonly Func<string, UtilityToolResult> _executor;
        private readonly IReadOnlyDictionary<string, string> _descriptions;

        public FakeUtilityToolsService(
            Func<string, UtilityToolResult> executor,
            IReadOnlyDictionary<string, string>? descriptions = null)
        {
            _executor = executor;
            _descriptions = descriptions ?? new Dictionary<string, string> { ["/tid"] = "Returnerar aktuell tid." };
        }

        public List<string> ExecutedCommands { get; } = new();

        public bool IsCommand(string input) =>
            !string.IsNullOrWhiteSpace(input)
            && _descriptions.Keys.Any(k => input.TrimStart().StartsWith(k, StringComparison.OrdinalIgnoreCase));

        public Task<UtilityToolResult> ExecuteAsync(string input)
        {
            ExecutedCommands.Add(input);
            return Task.FromResult(_executor(input));
        }

        public Task<UtilityToolResult> CallNamedApiAsync(string endpointName, string instruction = "") =>
            Task.FromResult(UtilityToolResult.Success("not used"));

        public IReadOnlyList<string> GetApiEndpointNames() => Array.Empty<string>();

        public IReadOnlyDictionary<string, string> GetToolDescriptions() => _descriptions;

        public bool TryFindCommand(string llmResponse, out string command)
        {
            command = string.Empty;
            if (string.IsNullOrWhiteSpace(llmResponse)) return false;

            foreach (var rawLine in llmResponse.Split('\n'))
            {
                var line = rawLine.Trim().TrimEnd('\r');
                if (IsCommand(line))
                {
                    command = line;
                    return true;
                }
            }

            return false;
        }
    }

    // ── Constructor ──────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullFileAgent_ThrowsArgumentNullException()
    {
        var act = () => new AgenticChatService(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ── Argument guards ───────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SendWithToolsAsync_NullOrWhitespaceUserMessage_ThrowsArgumentException(string? userMessage)
    {
        var llm = new ScriptedLlm("svar");

        var act = async () => await _sut.SendWithToolsAsync(userMessage!, llm.SendAsync);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SendWithToolsAsync_NullSendToLlm_ThrowsArgumentNullException()
    {
        var act = async () => await _sut.SendWithToolsAsync("Fråga", null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SendWithToolsAsync_AlreadyCancelledToken_ThrowsOperationCanceledException()
    {
        var llm = new ScriptedLlm("Stockholm är huvudstaden i Sverige.");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await _sut.SendWithToolsAsync("Fråga", llm.SendAsync, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── No tool requested ─────────────────────────────────────────────────

    [Fact]
    public async Task SendWithToolsAsync_NoCommandInReply_ReturnsFirstReplyWithNoInvocations()
    {
        var llm = new ScriptedLlm("Stockholm är huvudstaden i Sverige.");

        var result = await _sut.SendWithToolsAsync("Vad är huvudstaden i Sverige?", llm.SendAsync);

        result.FinalResponse.Should().Be("Stockholm är huvudstaden i Sverige.");
        result.ToolInvocations.Should().BeEmpty();
        llm.Prompts.Should().ContainSingle();
        llm.Prompts[0].Should().Contain("Fråga: Vad är huvudstaden i Sverige?");
    }

    // ── recentlyUploadedFilename hint ─────────────────────────────────────

    [Fact]
    public async Task SendWithToolsAsync_RecentlyUploadedFilename_AddsHintNamingTheFileToThePrompt()
    {
        var llm = new ScriptedLlm("Svar utan verktyg.");

        await _sut.SendWithToolsAsync(
            "Sammanfatta",
            llm.SendAsync,
            recentlyUploadedFilename: "rapport.pdf");

        llm.Prompts.Should().ContainSingle();
        llm.Prompts[0].Should().Contain("rapport.pdf");
        llm.Prompts[0].Should().Contain("Fråga: Sammanfatta");
    }

    [Fact]
    public async Task SendWithToolsAsync_NoRecentlyUploadedFilename_PromptHasNoUploadHint()
    {
        var llm = new ScriptedLlm("Svar utan verktyg.");

        await _sut.SendWithToolsAsync("Sammanfatta", llm.SendAsync);

        llm.Prompts[0].Should().NotContain("laddat upp");
    }

    [Fact]
    public async Task SendWithToolsAsync_RecentlyUploadedFilename_LlmCanUseItToBuildLasPdfCommand()
    {
        var pdfPath = Path.Combine(_tempDir, "rapport.pdf");
        await File.WriteAllBytesAsync(pdfPath, MinimalPdfBuilder.CreateWithText("Kvartalsresultat upp"));

        var llm = new ScriptedLlm(
            "/läs-pdf rapport.pdf Sammanfatta innehållet",
            "Kvartalsresultatet har ökat.");

        var result = await _sut.SendWithToolsAsync(
            "Sammanfatta",
            llm.SendAsync,
            recentlyUploadedFilename: "rapport.pdf");

        result.FinalResponse.Should().Be("Kvartalsresultatet har ökat.");
        result.ToolInvocations.Should().ContainSingle();
    }

    [Fact]
    public async Task SendWithToolsAsync_LlmNarratesCommandInQuotesInsteadOfOwnLine_StillExecutesIt()
    {
        // Regression test: a real local model (gemma-4-12b), given the recentlyUploadedFilename
        // hint for a terse "Sammanfatta" prompt, replied by narrating its intent instead of
        // writing the command alone on its own line as instructed:
        //   Based on the context provided, I will use the "/läs-pdf quarterly-report.pdf
        //   Sammanfatta innehållet" command to fulfill your request.
        // TryFindAgentCommand's quoted-command fallback must still pick this up so the tool
        // actually runs instead of silently returning the narration as the final answer.
        var pdfPath = Path.Combine(_tempDir, "quarterly-report.pdf");
        await File.WriteAllBytesAsync(pdfPath, MinimalPdfBuilder.CreateWithText("Quarterly revenue is up"));

        var llm = new ScriptedLlm(
            "Based on the context provided, I will use the \"/läs-pdf quarterly-report.pdf " +
            "Sammanfatta innehållet\" command to fulfill your request.",
            "Quarterly revenue has increased.");

        var result = await _sut.SendWithToolsAsync(
            "Sammanfatta",
            llm.SendAsync,
            recentlyUploadedFilename: "quarterly-report.pdf");

        result.FinalResponse.Should().Be("Quarterly revenue has increased.");
        result.ToolInvocations.Should().ContainSingle();
        result.ToolInvocations[0].Command.Should().Be("/läs-pdf quarterly-report.pdf Sammanfatta innehållet");
    }

    // ── Generic tool round trip (/lista) ─────────────────────────────────

    [Fact]
    public async Task SendWithToolsAsync_ListCommand_ExecutesToolAndFeedsResultBackToLlm()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "a.txt"), "x");
        var llm = new ScriptedLlm("/lista", "Du har en fil: a.txt");

        var result = await _sut.SendWithToolsAsync("Vilka filer finns?", llm.SendAsync);

        result.FinalResponse.Should().Be("Du har en fil: a.txt");
        result.ToolInvocations.Should().ContainSingle();
        result.ToolInvocations[0].Command.Should().Be("/lista");
        result.ToolInvocations[0].ResultSummary.Should().Contain("a.txt");
        llm.Prompts.Should().HaveCount(2);
        llm.Prompts[1].Should().Contain("a.txt").And.Contain("Vilka filer finns?");
    }

    // ── /fyll round trip ──────────────────────────────────────────────────

    [Fact]
    public async Task SendWithToolsAsync_FyllCommandWithValidMarkers_WritesFileAndReturnsFinalAnswer()
    {
        var llm = new ScriptedLlm(
            "/fyll config.txt Skriv en enkel konfiguration",
            "<FILE>innehåll här<ENDFILE>",
            "Klart! Jag har skapat filen åt dig.");

        var result = await _sut.SendWithToolsAsync("Skapa en konfigurationsfil", llm.SendAsync);

        result.FinalResponse.Should().Be("Klart! Jag har skapat filen åt dig.");
        result.ToolInvocations.Should().ContainSingle();
        result.ToolInvocations[0].ResultSummary.Should().Be("✓ Fil sparad: config.txt");
        var written = await File.ReadAllTextAsync(Path.Combine(_tempDir, "config.txt"));
        written.Should().Be("innehåll här");
        llm.Prompts.Should().HaveCount(3);
        llm.Prompts[1].Should().Contain("config.txt").And.Contain("Skriv en enkel konfiguration");
    }

    [Fact]
    public async Task SendWithToolsAsync_FyllCommandWithoutMarkers_DoesNotWriteFileAndReportsWarning()
    {
        var llm = new ScriptedLlm(
            "/fyll config.txt Skriv en enkel konfiguration",
            "Jag glömde markörerna helt.",
            "Tyvärr kunde jag inte skapa filen.");

        var result = await _sut.SendWithToolsAsync("Skapa en konfigurationsfil", llm.SendAsync);

        result.ToolInvocations.Should().ContainSingle();
        result.ToolInvocations[0].ResultSummary.Should().Be("⚠ Kunde inte extrahera filinnehåll — filen sparades inte.");
        File.Exists(Path.Combine(_tempDir, "config.txt")).Should().BeFalse();
    }

    // ── /redigera round trip ──────────────────────────────────────────────

    [Fact]
    public async Task SendWithToolsAsync_RedigeraCommandWithValidBlock_AppliesEditAndReturnsFinalAnswer()
    {
        await File.WriteAllLinesAsync(Path.Combine(_tempDir, "code.txt"), ["rad 1", "rad 2", "rad 3"]);
        var llm = new ScriptedLlm(
            "/redigera code.txt Lägg till en kommentar överst",
            "<REDIGERA RAD=1>// kommentar</REDIGERA>",
            "Klart, jag har uppdaterat filen.");

        var result = await _sut.SendWithToolsAsync("Lägg till en rad", llm.SendAsync);

        result.FinalResponse.Should().Be("Klart, jag har uppdaterat filen.");
        result.ToolInvocations.Should().ContainSingle();
        result.ToolInvocations[0].ResultSummary.Should().Be("✓ Fil redigerad: code.txt (rad 1)");
        var content = await File.ReadAllLinesAsync(Path.Combine(_tempDir, "code.txt"));
        content.Should().Equal("// kommentar", "rad 2", "rad 3");
    }

    [Fact]
    public async Task SendWithToolsAsync_RedigeraCommandWithoutBlocks_LeavesFileUnchangedAndReportsWarning()
    {
        await File.WriteAllLinesAsync(Path.Combine(_tempDir, "code.txt"), ["rad 1", "rad 2"]);
        var llm = new ScriptedLlm(
            "/redigera code.txt Lägg till en kommentar överst",
            "Jag hittar inga ändringar att föreslå.",
            "Tyvärr kunde jag inte redigera filen.");

        var result = await _sut.SendWithToolsAsync("Lägg till en rad", llm.SendAsync);

        result.ToolInvocations.Should().ContainSingle();
        result.ToolInvocations[0].ResultSummary.Should().Be("⚠ Kunde inte tolka radändringar — filen ändrades inte.");
        var content = await File.ReadAllLinesAsync(Path.Combine(_tempDir, "code.txt"));
        content.Should().Equal("rad 1", "rad 2");
    }

    // ── Safety cap ────────────────────────────────────────────────────────

    [Fact]
    public async Task SendWithToolsAsync_ModelKeepsRequestingCommands_StopsAfterMaxToolCallRounds()
    {
        // Initial start-message reply, plus one reply per loop round (safety cap = 3 rounds).
        var llm = new ScriptedLlm("/lista", "/lista", "/lista", "/lista");

        var result = await _sut.SendWithToolsAsync("Vilka filer finns?", llm.SendAsync);

        result.ToolInvocations.Should().HaveCount(3);
        result.FinalResponse.Should().Be("/lista");
        llm.Prompts.Should().HaveCount(4);
    }

    // ── Utility tool round trip ───────────────────────────────────────────

    [Fact]
    public async Task SendWithToolsAsync_UtilityToolConfigured_AppendsDescriptionsToStartPrompt()
    {
        var utilityTools = new FakeUtilityToolsService(
            _ => UtilityToolResult.Success("ok"),
            new Dictionary<string, string> { ["/tid"] = "Returnerar aktuell tid." });
        var sut = new AgenticChatService(_fileAgent, utilityTools);
        var llm = new ScriptedLlm("Stockholm är huvudstaden i Sverige.");

        await sut.SendWithToolsAsync("Vad är huvudstaden i Sverige?", llm.SendAsync);

        llm.Prompts[0].Should().Contain("/tid").And.Contain("Returnerar aktuell tid.");
    }

    [Fact]
    public async Task SendWithToolsAsync_UtilityCommandRequested_ExecutesViaUtilityServiceAndFeedsResultBackToLlm()
    {
        var utilityTools = new FakeUtilityToolsService(
            _ => UtilityToolResult.Success("Klockan är nu 14:30.", "Klockan är nu 14:30 (Europe/Stockholm)."));
        var sut = new AgenticChatService(_fileAgent, utilityTools);
        var llm = new ScriptedLlm("/tid", "Klockan är 14:30.");

        var result = await sut.SendWithToolsAsync("Vad är klockan?", llm.SendAsync);

        result.FinalResponse.Should().Be("Klockan är 14:30.");
        result.ToolInvocations.Should().ContainSingle();
        result.ToolInvocations[0].Command.Should().Be("/tid");
        result.ToolInvocations[0].ResultSummary.Should().Be("Klockan är nu 14:30.");
        utilityTools.ExecutedCommands.Should().ContainSingle().Which.Should().Be("/tid");
        llm.Prompts.Should().HaveCount(2);
        llm.Prompts[1].Should().Contain("Klockan är nu 14:30 (Europe/Stockholm).").And.Contain("Vad är klockan?");
    }

    [Fact]
    public async Task SendWithToolsAsync_UtilityCommandFailure_StillFeedsMessageBackToLlm()
    {
        var utilityTools = new FakeUtilityToolsService(_ => UtilityToolResult.Failure("Okänt kommando."));
        var sut = new AgenticChatService(_fileAgent, utilityTools);
        var llm = new ScriptedLlm("/tid", "Tyvärr kunde jag inte ta reda på tiden.");

        var result = await sut.SendWithToolsAsync("Vad är klockan?", llm.SendAsync);

        result.FinalResponse.Should().Be("Tyvärr kunde jag inte ta reda på tiden.");
        result.ToolInvocations.Should().ContainSingle();
        result.ToolInvocations[0].ResultSummary.Should().Be("Okänt kommando.");
        llm.Prompts[1].Should().Contain("Okänt kommando.");
    }

    [Fact]
    public async Task SendWithToolsAsync_NoUtilityToolsConfigured_FileCommandStillTakesPrecedence()
    {
        // _sut is built without an IUtilityToolsService: file-agent commands must still work.
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "a.txt"), "x");
        var llm = new ScriptedLlm("/lista", "Du har en fil: a.txt");

        var result = await _sut.SendWithToolsAsync("Vilka filer finns?", llm.SendAsync);

        result.FinalResponse.Should().Be("Du har en fil: a.txt");
        result.ToolInvocations.Should().ContainSingle();
        result.ToolInvocations[0].Command.Should().Be("/lista");
    }

    [Fact]
    public async Task SendWithToolsAsync_FileCommandTakesPrecedenceOverUtilityCommand_WhenBothCouldMatch()
    {
        // The loop checks file-agent commands first; /lista is a file command, so even with
        // utility tools configured, a /lista reply must be routed to the file agent.
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "a.txt"), "x");
        var utilityTools = new FakeUtilityToolsService(_ => UtilityToolResult.Success("should not run"));
        var sut = new AgenticChatService(_fileAgent, utilityTools);
        var llm = new ScriptedLlm("/lista", "Du har en fil: a.txt");

        var result = await sut.SendWithToolsAsync("Vilka filer finns?", llm.SendAsync);

        result.ToolInvocations[0].Command.Should().Be("/lista");
        utilityTools.ExecutedCommands.Should().BeEmpty();
    }

    // ── onToolStatus callback ──────────────────────────────────────────────

    [Fact]
    public async Task SendWithToolsAsync_FileCommandRequested_InvokesOnToolStatusBeforeExecution()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "a.txt"), "x");
        var llm = new ScriptedLlm("/lista", "Du har en fil: a.txt");
        var statusMessages = new List<string>();

        await _sut.SendWithToolsAsync("Vilka filer finns?", llm.SendAsync, onToolStatus: statusMessages.Add);

        statusMessages.Should().ContainSingle();
        statusMessages[0].Should().Contain("/lista");
    }

    [Fact]
    public async Task SendWithToolsAsync_UtilityCommandRequested_InvokesOnToolStatusBeforeExecution()
    {
        var utilityTools = new FakeUtilityToolsService(_ => UtilityToolResult.Success("Klockan är nu 14:30."));
        var sut = new AgenticChatService(_fileAgent, utilityTools);
        var llm = new ScriptedLlm("/tid", "Klockan är 14:30.");
        var statusMessages = new List<string>();

        await sut.SendWithToolsAsync("Vad är klockan?", llm.SendAsync, onToolStatus: statusMessages.Add);

        statusMessages.Should().ContainSingle();
        statusMessages[0].Should().Contain("/tid");
    }

    [Fact]
    public async Task SendWithToolsAsync_NoCommandRequested_NeverInvokesOnToolStatus()
    {
        var llm = new ScriptedLlm("Stockholm är huvudstaden i Sverige.");
        var invoked = false;

        await _sut.SendWithToolsAsync("Vad är huvudstaden i Sverige?", llm.SendAsync, onToolStatus: _ => invoked = true);

        invoked.Should().BeFalse();
    }

    [Fact]
    public async Task SendWithToolsAsync_MultipleToolRounds_InvokesOnToolStatusOncePerRound()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "a.txt"), "x");
        var llm = new ScriptedLlm("/lista", "/lista", "Klart, jag har listat filerna två gånger.");
        var statusMessages = new List<string>();

        var result = await _sut.SendWithToolsAsync("Vilka filer finns?", llm.SendAsync, onToolStatus: statusMessages.Add);

        result.ToolInvocations.Should().HaveCount(2);
        statusMessages.Should().HaveCount(2);
        statusMessages.Should().AllSatisfy(m => m.Should().Contain("/lista"));
    }

    [Fact]
    public async Task SendWithToolsAsync_OnToolStatusNull_DoesNotThrow()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "a.txt"), "x");
        var llm = new ScriptedLlm("/lista", "Du har en fil: a.txt");

        var act = async () => await _sut.SendWithToolsAsync("Vilka filer finns?", llm.SendAsync, onToolStatus: null);

        await act.Should().NotThrowAsync();
    }
}
