using FluentAssertions;
using Services.AgentTools;
using Services.FileAgent;

namespace Services.Tests.AgentTools;

/// <summary>
/// Unit tests for <see cref="AgenticChatService"/>: the lightweight, text-based tool-calling
/// loop that detects file-agent slash commands in an LLM's reply, executes them via a real
/// <see cref="FileAgentService"/> rooted at a temp directory, and feeds the result back to the
/// LLM. Uses <see cref="ScriptedLlm"/> to script a sequence of canned LLM replies and capture
/// every prompt sent to it.
/// </summary>
public class AgenticChatServiceTests : IDisposable
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

    // ── Constructor ───────────────────────────────────────────────────────

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
        cts.Cancel();

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
}
