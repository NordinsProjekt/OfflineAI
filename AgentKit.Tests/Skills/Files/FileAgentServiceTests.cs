using AgentKit.Skills.Files;
using AgentKit.Tests.TestHelpers;
using FluentAssertions;
using Xunit.Abstractions;

namespace AgentKit.Tests.Skills.Files;

/// <summary>
/// Unit tests for the <c>/redigera</c> line-editing workflow in <see cref="FileAgentService"/>:
/// parsing &lt;REDIGERA RAD=...&gt; blocks from an LLM response and applying the resulting
/// <see cref="LineEdit"/> replacements to a file.
/// </summary>
public sealed class FileAgentServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileAgentService _sut;
    private readonly ITestOutputHelper _output;

    public FileAgentServiceTests(ITestOutputHelper output)
    {
        _output = output;
        _tempDir = Path.Combine(Path.GetTempPath(), "FileAgentServiceTests_" + Guid.NewGuid());
        _sut = new FileAgentService(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private async Task<string> CreateFileAsync(string filename, params string[] lines)
    {
        var path = Path.Combine(_tempDir, filename);
        await File.WriteAllLinesAsync(path, lines);
        return path;
    }

    // ── IsCommand ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/redigera notes.txt Rätta stavfelet")]
    [InlineData("/REDIGERA notes.txt Rätta stavfelet")]
    public void IsCommand_RecognisesRedigera(string input)
    {
        _sut.IsCommand(input).Should().BeTrue();
    }

    // ── TryExtractFileContent ─────────────────────────────────────────────

    [Fact]
    public void TryExtractFileContent_WithMarkers_ReturnsInnerContent()
    {
        var response = "Här är filen:\n<FILE>\nPRINT \"Hej\"\n<ENDFILE>\nKlart!";

        _sut.TryExtractFileContent(response, out var content).Should().BeTrue();
        content.Should().Be("PRINT \"Hej\"");
    }

    [Fact]
    public void TryExtractFileContent_MarkersWrappingCodeFence_StripsFence()
    {
        // The model obeyed the markers but ALSO fenced the code — the ``` lines must not be
        // written into the file.
        var response = "<FILE>\n```qbasic\nPRINT \"Hej\"\n```\n<ENDFILE>";

        _sut.TryExtractFileContent(response, out var content).Should().BeTrue();
        content.Should().Be("PRINT \"Hej\"");
    }

    [Fact]
    public void TryExtractFileContent_UnclosedFileMarker_KeepsBodyToEnd()
    {
        // <FILE> opened but no <ENDFILE> — previously dropped the whole write.
        var response = "<FILE>\nPRINT \"rad 1\"\nPRINT \"rad 2\"";

        _sut.TryExtractFileContent(response, out var content).Should().BeTrue();
        content.Should().Be("PRINT \"rad 1\"\nPRINT \"rad 2\"");
    }

    [Fact]
    public void TryExtractFileContent_NoMarkersButCodeFence_ExtractsFencedBlock()
    {
        // The dominant real-world failure: model ignores the markers and uses a Markdown fence.
        var response = "Visst! Här kommer koden:\n```qbasic\nCLS\nPRINT \"Start\"\n```";

        _sut.TryExtractFileContent(response, out var content).Should().BeTrue();
        content.Should().Be("CLS\nPRINT \"Start\"");
    }

    [Fact]
    public void TryExtractFileContent_PlainProseNoMarkersNoFence_ReturnsFalse()
    {
        var response = "Jag kan tyvärr inte skapa filen just nu.";

        _sut.TryExtractFileContent(response, out var content).Should().BeFalse();
        content.Should().BeEmpty();
    }

    // ── GetSafePath / invalid filenames ───────────────────────────────────

    [Theory]
    [InlineData("/fyll Fil: rpg2.bas, Innehåll: SCREEN 0")]
    [InlineData("/skapa Fil:")]
    public async Task ExecuteAsync_InvalidFilenameCharacters_FailsInsteadOfThrowing(string command)
    {
        // A mangled model command produced the filename "Fil:" (colon illegal on Windows). The
        // invalid name used to reach File.WriteAllTextAsync and abort the whole agent run.
        var result = await _sut.ExecuteAsync(command);

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("Ogiltigt filnamn");
    }

    // ── TryGetInlineWriteTarget ────────────────────────────────────────────

    [Theory]
    [InlineData("/fyll rpg2.bas Skapa ett spel", "rpg2.bas")]
    [InlineData("/fyll rpg2.bas", "rpg2.bas")]
    [InlineData("/skapa notes.txt", "notes.txt")]
    public void TryGetInlineWriteTarget_FyllOrSkapaWithFilename_ReturnsFilename(string command, string expected)
    {
        _sut.TryGetInlineWriteTarget(command, out var filename).Should().BeTrue();
        filename.Should().Be(expected);
    }

    [Theory]
    [InlineData("/fyll")]                 // no filename
    [InlineData("/fyll Fil: beskrivning")] // invalid filename character
    [InlineData("/läs rpg2.bas Sammanfatta")] // not a write command
    [InlineData("/lista")]
    public void TryGetInlineWriteTarget_UnsupportedOrInvalid_ReturnsFalse(string command)
    {
        _sut.TryGetInlineWriteTarget(command, out var filename).Should().BeFalse();
        filename.Should().BeEmpty();
    }

    // ── /läs without an instruction ────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ReadWithoutInstruction_ReturnsRawContent()
    {
        await CreateFileAsync("rpg2.bas", "CLS", "PRINT \"Hej\"");

        // Bare "/läs rpg2.bas" — the shape the model kept emitting — must return content, not error.
        var result = await _sut.ExecuteAsync("/läs rpg2.bas");

        result.IsSuccess.Should().BeTrue();
        result.InjectedContext.Should().Contain("PRINT \"Hej\"");
    }

    [Fact]
    public async Task ExecuteAsync_ReadWithNoFilenameAtAll_Fails()
    {
        var result = await _sut.ExecuteAsync("/läs");

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("Ange ett filnamn");
    }

    // ── TryExtractLineEdits ───────────────────────────────────────────────

    [Fact]
    public void TryExtractLineEdits_SingleLineBlock_ReturnsOneEdit()
    {
        var response = "<REDIGERA RAD=2>ny rad två</REDIGERA>";

        var success = _sut.TryExtractLineEdits(response, out var edits);

        success.Should().BeTrue();
        edits.Should().ContainSingle();
        edits[0].StartLine.Should().Be(2);
        edits[0].EndLine.Should().Be(2);
        edits[0].NewContent.Should().Be("ny rad två");
    }

    [Fact]
    public void TryExtractLineEdits_RangeBlock_ReturnsStartAndEndLine()
    {
        var response = "<REDIGERA RAD=5-7>rad A\nrad B\nrad C</REDIGERA>";

        var success = _sut.TryExtractLineEdits(response, out var edits);

        success.Should().BeTrue();
        edits.Should().ContainSingle();
        edits[0].StartLine.Should().Be(5);
        edits[0].EndLine.Should().Be(7);
        edits[0].NewContent.Should().Be("rad A\nrad B\nrad C");
    }

    [Fact]
    public void TryExtractLineEdits_MultipleBlocks_ReturnsAllEdits()
    {
        var response =
            "Här är ändringarna:\n" +
            "<REDIGERA RAD=1>första raden</REDIGERA>\n" +
            "<REDIGERA RAD=3-4>tredje och fjärde</REDIGERA>";

        var success = _sut.TryExtractLineEdits(response, out var edits);

        success.Should().BeTrue();
        edits.Should().HaveCount(2);
        edits[0].StartLine.Should().Be(1);
        edits[1].StartLine.Should().Be(3);
        edits[1].EndLine.Should().Be(4);
    }

    [Fact]
    public void TryExtractLineEdits_NoBlocks_ReturnsFalse()
    {
        var response = "Inga ändringar behövs.";

        var success = _sut.TryExtractLineEdits(response, out var edits);

        success.Should().BeFalse();
        edits.Should().BeEmpty();
    }

    [Fact]
    public void TryExtractLineEdits_EndBeforeStart_IsIgnored()
    {
        var response = "<REDIGERA RAD=9-2>ogiltigt intervall</REDIGERA>";

        var success = _sut.TryExtractLineEdits(response, out var edits);

        success.Should().BeFalse();
        edits.Should().BeEmpty();
    }

    // ── TryExtractLineEdits (insertions) ─────────────────────────────────

    [Fact]
    public void TryExtractLineEdits_InsertAfterBlock_ReturnsInsertAfterEdit()
    {
        var response = "<REDIGERA INFOGA_EFTER=2>ny kod</REDIGERA>";

        var success = _sut.TryExtractLineEdits(response, out var edits);

        success.Should().BeTrue();
        edits.Should().ContainSingle();
        edits[0].Kind.Should().Be(LineEditKind.InsertAfter);
        edits[0].StartLine.Should().Be(2);
        edits[0].NewContent.Should().Be("ny kod");
    }

    [Fact]
    public void TryExtractLineEdits_InsertAfterZero_IsAllowed()
    {
        var response = "<REDIGERA INFOGA_EFTER=0>allra först</REDIGERA>";

        var success = _sut.TryExtractLineEdits(response, out var edits);

        success.Should().BeTrue();
        edits[0].Kind.Should().Be(LineEditKind.InsertAfter);
        edits[0].StartLine.Should().Be(0);
    }

    [Fact]
    public void TryExtractLineEdits_InsertBeforeBlock_ReturnsInsertBeforeEdit()
    {
        var response = "<REDIGERA INFOGA_FÖRE=3>ny kod</REDIGERA>";

        var success = _sut.TryExtractLineEdits(response, out var edits);

        success.Should().BeTrue();
        edits.Should().ContainSingle();
        edits[0].Kind.Should().Be(LineEditKind.InsertBefore);
        edits[0].StartLine.Should().Be(3);
        edits[0].NewContent.Should().Be("ny kod");
    }

    [Fact]
    public void TryExtractLineEdits_InsertBeforeAsciiFallback_ReturnsInsertBeforeEdit()
    {
        var response = "<REDIGERA INFOGA_FORE=3>ny kod</REDIGERA>";

        var success = _sut.TryExtractLineEdits(response, out var edits);

        success.Should().BeTrue();
        edits[0].Kind.Should().Be(LineEditKind.InsertBefore);
        edits[0].StartLine.Should().Be(3);
    }

    [Fact]
    public void TryExtractLineEdits_InsertBeforeZero_IsIgnored()
    {
        var response = "<REDIGERA INFOGA_FÖRE=0>ogiltigt</REDIGERA>";

        var success = _sut.TryExtractLineEdits(response, out var edits);

        success.Should().BeFalse();
        edits.Should().BeEmpty();
    }

    [Fact]
    public void TryExtractLineEdits_MixedReplaceAndInsertBlocks_ReturnsBoth()
    {
        var response =
            "<REDIGERA RAD=1>rättad rad</REDIGERA>\n" +
            "<REDIGERA INFOGA_EFTER=3>public void NyMetod() { }</REDIGERA>";

        var success = _sut.TryExtractLineEdits(response, out var edits);

        success.Should().BeTrue();
        edits.Should().HaveCount(2);
        edits[0].Kind.Should().Be(LineEditKind.Replace);
        edits[1].Kind.Should().Be(LineEditKind.InsertAfter);
        edits[1].NewContent.Should().Be("public void NyMetod() { }");
    }

    // ── ApplyLineEditsAsync ───────────────────────────────────────────────

    [Fact]
    public async Task ApplyLineEditsAsync_SingleLineEdit_ReplacesOnlyThatLine()
    {
        await CreateFileAsync("notes.txt", "rad 1", "rad 2", "rad 3");

        var result = await _sut.ApplyLineEditsAsync("notes.txt", new[] { new LineEdit(2, 2, "ny rad 2") });

        result.IsSuccess.Should().BeTrue();
        result.ResultType.Should().Be(FileAgentResultType.FileEdited);

        var content = await File.ReadAllLinesAsync(Path.Combine(_tempDir, "notes.txt"));
        content.Should().Equal("rad 1", "ny rad 2", "rad 3");
    }

    [Fact]
    public async Task ApplyLineEditsAsync_RangeEdit_ReplacesAllLinesInRange()
    {
        await CreateFileAsync("notes.txt", "rad 1", "rad 2", "rad 3", "rad 4");

        var result = await _sut.ApplyLineEditsAsync("notes.txt", new[] { new LineEdit(2, 3, "ersättning") });

        result.IsSuccess.Should().BeTrue();

        var content = await File.ReadAllLinesAsync(Path.Combine(_tempDir, "notes.txt"));
        content.Should().Equal("rad 1", "ersättning", "rad 4");
    }

    [Fact]
    public async Task ApplyLineEditsAsync_MultipleNonOverlappingEdits_AppliesAllCorrectly()
    {
        await CreateFileAsync("notes.txt", "rad 1", "rad 2", "rad 3", "rad 4", "rad 5");

        var edits = new[]
        {
            new LineEdit(4, 4, "ny rad 4"),
            new LineEdit(1, 1, "ny rad 1"),
        };

        var result = await _sut.ApplyLineEditsAsync("notes.txt", edits);

        result.IsSuccess.Should().BeTrue();

        var content = await File.ReadAllLinesAsync(Path.Combine(_tempDir, "notes.txt"));
        content.Should().Equal("ny rad 1", "rad 2", "rad 3", "ny rad 4", "rad 5");
    }

    [Fact]
    public async Task ApplyLineEditsAsync_MultiLineReplacement_ShiftsSubsequentLines()
    {
        await CreateFileAsync("notes.txt", "rad 1", "rad 2", "rad 3");

        var result = await _sut.ApplyLineEditsAsync(
            "notes.txt",
            new[] { new LineEdit(2, 2, "ny A\nny B") });

        result.IsSuccess.Should().BeTrue();

        var content = await File.ReadAllLinesAsync(Path.Combine(_tempDir, "notes.txt"));
        content.Should().Equal("rad 1", "ny A", "ny B", "rad 3");
    }

    [Fact]
    public async Task ApplyLineEditsAsync_OutOfRangeLine_FailsAndLeavesFileUnchanged()
    {
        await CreateFileAsync("notes.txt", "rad 1", "rad 2");

        var result = await _sut.ApplyLineEditsAsync("notes.txt", new[] { new LineEdit(5, 5, "för långt") });

        result.IsSuccess.Should().BeFalse();
        result.ResultType.Should().Be(FileAgentResultType.Error);

        var content = await File.ReadAllLinesAsync(Path.Combine(_tempDir, "notes.txt"));
        content.Should().Equal("rad 1", "rad 2");
    }

    [Fact]
    public async Task ApplyLineEditsAsync_OverlappingEdits_FailsAndLeavesFileUnchanged()
    {
        await CreateFileAsync("notes.txt", "rad 1", "rad 2", "rad 3", "rad 4");

        var edits = new[]
        {
            new LineEdit(1, 3, "block A"),
            new LineEdit(2, 4, "block B"),
        };

        var result = await _sut.ApplyLineEditsAsync("notes.txt", edits);

        result.IsSuccess.Should().BeFalse();
        result.ResultType.Should().Be(FileAgentResultType.Error);

        var content = await File.ReadAllLinesAsync(Path.Combine(_tempDir, "notes.txt"));
        content.Should().Equal("rad 1", "rad 2", "rad 3", "rad 4");
    }

    // ── ApplyLineEditsAsync (insertions) ─────────────────────────────────

    [Fact]
    public async Task ApplyLineEditsAsync_InsertAfter_AddsNewLineWithoutRemovingExisting()
    {
        await CreateFileAsync("notes.txt", "rad 1", "rad 2", "rad 3");

        var result = await _sut.ApplyLineEditsAsync("notes.txt", new[] { LineEdit.InsertAfterLine(2, "ny rad") });

        result.IsSuccess.Should().BeTrue();
        result.ResultType.Should().Be(FileAgentResultType.FileEdited);

        var content = await File.ReadAllLinesAsync(Path.Combine(_tempDir, "notes.txt"));
        content.Should().Equal("rad 1", "rad 2", "ny rad", "rad 3");
    }

    [Fact]
    public async Task ApplyLineEditsAsync_InsertBefore_AddsNewLineWithoutRemovingExisting()
    {
        await CreateFileAsync("notes.txt", "rad 1", "rad 2", "rad 3");

        var result = await _sut.ApplyLineEditsAsync("notes.txt", new[] { LineEdit.InsertBeforeLine(2, "ny rad") });

        result.IsSuccess.Should().BeTrue();

        var content = await File.ReadAllLinesAsync(Path.Combine(_tempDir, "notes.txt"));
        content.Should().Equal("rad 1", "ny rad", "rad 2", "rad 3");
    }

    [Fact]
    public async Task ApplyLineEditsAsync_InsertAfterZero_InsertsAtTopOfFile()
    {
        await CreateFileAsync("notes.txt", "rad 1", "rad 2");

        var result = await _sut.ApplyLineEditsAsync("notes.txt", new[] { LineEdit.InsertAfterLine(0, "ny första rad") });

        result.IsSuccess.Should().BeTrue();

        var content = await File.ReadAllLinesAsync(Path.Combine(_tempDir, "notes.txt"));
        content.Should().Equal("ny första rad", "rad 1", "rad 2");
    }

    [Fact]
    public async Task ApplyLineEditsAsync_InsertAfterLastLine_AppendsAtEndOfFile()
    {
        await CreateFileAsync("notes.txt", "rad 1", "rad 2");

        var result = await _sut.ApplyLineEditsAsync("notes.txt", new[] { LineEdit.InsertAfterLine(2, "ny sista rad") });

        result.IsSuccess.Should().BeTrue();

        var content = await File.ReadAllLinesAsync(Path.Combine(_tempDir, "notes.txt"));
        content.Should().Equal("rad 1", "rad 2", "ny sista rad");
    }

    [Fact]
    public async Task ApplyLineEditsAsync_InsertMultiLineContent_InsertsAllLines()
    {
        await CreateFileAsync("notes.txt", "rad 1", "rad 2");

        var result = await _sut.ApplyLineEditsAsync(
            "notes.txt",
            new[] { LineEdit.InsertAfterLine(1, "public void NyMetod()\n{\n}") });

        result.IsSuccess.Should().BeTrue();

        var content = await File.ReadAllLinesAsync(Path.Combine(_tempDir, "notes.txt"));
        content.Should().Equal("rad 1", "public void NyMetod()", "{", "}", "rad 2");
    }

    [Fact]
    public async Task ApplyLineEditsAsync_MultipleInsertionsAtSameAnchor_MergeInOriginalOrder()
    {
        await CreateFileAsync("notes.txt", "rad 1", "rad 2");

        var edits = new[]
        {
            LineEdit.InsertAfterLine(1, "första tillägget"),
            LineEdit.InsertBeforeLine(2, "andra tillägget"),
        };

        var result = await _sut.ApplyLineEditsAsync("notes.txt", edits);

        result.IsSuccess.Should().BeTrue();

        var content = await File.ReadAllLinesAsync(Path.Combine(_tempDir, "notes.txt"));
        content.Should().Equal("rad 1", "första tillägget", "andra tillägget", "rad 2");
    }

    [Fact]
    public async Task ApplyLineEditsAsync_InsertAfterEndOfReplaceRange_DoesNotOverlap()
    {
        await CreateFileAsync("notes.txt", "rad 1", "rad 2", "rad 3", "rad 4");

        var edits = new[]
        {
            new LineEdit(2, 3, "ersatt block"),
            LineEdit.InsertAfterLine(3, "ny rad direkt efter"),
        };

        var result = await _sut.ApplyLineEditsAsync("notes.txt", edits);

        result.IsSuccess.Should().BeTrue();

        var content = await File.ReadAllLinesAsync(Path.Combine(_tempDir, "notes.txt"));
        content.Should().Equal("rad 1", "ersatt block", "ny rad direkt efter", "rad 4");
    }

    [Fact]
    public async Task ApplyLineEditsAsync_InsertBeforeStartOfReplaceRange_DoesNotOverlap()
    {
        await CreateFileAsync("notes.txt", "rad 1", "rad 2", "rad 3", "rad 4");

        var edits = new[]
        {
            new LineEdit(2, 3, "ersatt block"),
            LineEdit.InsertBeforeLine(2, "ny rad direkt före"),
        };

        var result = await _sut.ApplyLineEditsAsync("notes.txt", edits);

        result.IsSuccess.Should().BeTrue();

        var content = await File.ReadAllLinesAsync(Path.Combine(_tempDir, "notes.txt"));
        content.Should().Equal("rad 1", "ny rad direkt före", "ersatt block", "rad 4");
    }

    [Fact]
    public async Task ApplyLineEditsAsync_InsertStrictlyInsideReplaceRange_FailsAsOverlap()
    {
        await CreateFileAsync("notes.txt", "rad 1", "rad 2", "rad 3", "rad 4");

        var edits = new[]
        {
            new LineEdit(1, 4, "ersatt allt"),
            LineEdit.InsertAfterLine(2, "mitt i"),
        };

        var result = await _sut.ApplyLineEditsAsync("notes.txt", edits);

        result.IsSuccess.Should().BeFalse();
        result.ResultType.Should().Be(FileAgentResultType.Error);

        var content = await File.ReadAllLinesAsync(Path.Combine(_tempDir, "notes.txt"));
        content.Should().Equal("rad 1", "rad 2", "rad 3", "rad 4");
    }

    [Fact]
    public async Task ApplyLineEditsAsync_InsertAfterLineBeyondEndOfFile_FailsAndLeavesFileUnchanged()
    {
        await CreateFileAsync("notes.txt", "rad 1", "rad 2");

        var result = await _sut.ApplyLineEditsAsync("notes.txt", new[] { LineEdit.InsertAfterLine(5, "för långt") });

        result.IsSuccess.Should().BeFalse();
        result.ResultType.Should().Be(FileAgentResultType.Error);

        var content = await File.ReadAllLinesAsync(Path.Combine(_tempDir, "notes.txt"));
        content.Should().Equal("rad 1", "rad 2");
    }

    [Fact]
    public async Task ApplyLineEditsAsync_FileDoesNotExist_ReturnsFailure()
    {
        var result = await _sut.ApplyLineEditsAsync("missing.txt", new[] { new LineEdit(1, 1, "x") });

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ApplyLineEditsAsync_NoEdits_ReturnsFailure()
    {
        await CreateFileAsync("notes.txt", "rad 1");

        var result = await _sut.ApplyLineEditsAsync("notes.txt", Array.Empty<LineEdit>());

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ApplyLineEditsAsync_PathTraversalFilename_ReturnsFailure()
    {
        var result = await _sut.ApplyLineEditsAsync("../outside.txt", new[] { new LineEdit(1, 1, "x") });

        result.IsSuccess.Should().BeFalse();
    }

    // ── StripEditMarkers ──────────────────────────────────────────────────

    [Fact]
    public void StripEditMarkers_RemovesEditBlocks_KeepsSurroundingText()
    {
        var response = "Klart!\n<REDIGERA RAD=1>ny text</REDIGERA>\nHoppas det hjälper.";

        var stripped = _sut.StripEditMarkers(response);

        stripped.Should().NotContain("REDIGERA");
        stripped.Should().Contain("Klart!");
        stripped.Should().Contain("Hoppas det hjälper.");
    }

    // ── /redigera via ExecuteAsync ────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Redigera_ReturnsEditRequestedWithNumberedContent()
    {
        await CreateFileAsync("notes.txt", "första", "andra");

        var result = await _sut.ExecuteAsync("/redigera notes.txt Rätta stavfel på rad 2.");

        result.ResultType.Should().Be(FileAgentResultType.EditRequested);
        result.IsSuccess.Should().BeTrue();
        result.TargetFilename.Should().Be("notes.txt");
        result.LlmPrompt.Should().Contain("1: första");
        result.LlmPrompt.Should().Contain("2: andra");
        result.LlmPrompt.Should().Contain("REDIGERA");
    }

    [Fact]
    public async Task ExecuteAsync_RedigeraWithoutInstruction_ReturnsFailure()
    {
        await CreateFileAsync("notes.txt", "rad 1");

        var result = await _sut.ExecuteAsync("/redigera notes.txt");

        result.ResultType.Should().Be(FileAgentResultType.Error);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_RedigeraMissingFile_ReturnsFailure()
    {
        var result = await _sut.ExecuteAsync("/redigera saknas.txt Gör något.");

        result.ResultType.Should().Be(FileAgentResultType.Error);
        result.IsSuccess.Should().BeFalse();
    }

    // ── /skapa via ExecuteAsync ───────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Skapa_CreatesEmptyFileAndReturnsSuccess()
    {
        var result = await _sut.ExecuteAsync("/skapa notes.txt");

        result.IsSuccess.Should().BeTrue();
        result.ResultType.Should().Be(FileAgentResultType.FileCreated);
        result.Message.Should().Contain("notes.txt");
        File.Exists(Path.Combine(_tempDir, "notes.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_SkapaExistingFile_LeavesExistingContentUntouched()
    {
        // /skapa must never truncate: in a real goal-agent run the model re-issued
        // "/skapa calc.bas" on a file it had already filled, and every call wiped the file.
        await CreateFileAsync("notes.txt", "gammalt innehåll");

        var result = await _sut.ExecuteAsync("/skapa notes.txt");

        result.IsSuccess.Should().BeTrue();
        result.Message.Should().Contain("finns redan");
        (await File.ReadAllTextAsync(Path.Combine(_tempDir, "notes.txt"))).TrimEnd()
            .Should().Be("gammalt innehåll");
    }

    [Fact]
    public async Task ExecuteAsync_SkapaMissingFilename_ReturnsFailureAskingForFilename()
    {
        var result = await _sut.ExecuteAsync("/skapa");

        result.IsSuccess.Should().BeFalse();
        result.ResultType.Should().Be(FileAgentResultType.Error);
        result.Message.Should().Contain("filnamn");
    }

    // ── /fyll via ExecuteAsync ────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Fyll_ReturnsFillRequestedWithPromptAndTargetFilename()
    {
        var result = await _sut.ExecuteAsync("/fyll config.txt Skriv en enkel konfiguration");

        result.ResultType.Should().Be(FileAgentResultType.FillRequested);
        result.IsSuccess.Should().BeTrue();
        result.TargetFilename.Should().Be("config.txt");
        result.LlmPrompt.Should().Contain("config.txt");
        result.LlmPrompt.Should().Contain("Skriv en enkel konfiguration");
    }

    [Fact]
    public async Task ExecuteAsync_FyllMissingDescription_ReturnsFailure()
    {
        var result = await _sut.ExecuteAsync("/fyll config.txt");

        result.IsSuccess.Should().BeFalse();
        result.ResultType.Should().Be(FileAgentResultType.Error);
    }

    // ── /läs via ExecuteAsync ─────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Las_ReturnsFileReadWithInstructionAndContent()
    {
        await CreateFileAsync("notes.txt", "Hej världen");

        var result = await _sut.ExecuteAsync("/läs notes.txt Sammanfatta innehållet.");

        result.ResultType.Should().Be(FileAgentResultType.FileRead);
        result.IsSuccess.Should().BeTrue();
        result.InjectedContext.Should().Contain("Sammanfatta innehållet.");
        result.InjectedContext.Should().Contain("Hej världen");
    }

    [Fact]
    public async Task ExecuteAsync_LasAsciiFallback_BehavesSameAsLäs()
    {
        await CreateFileAsync("notes.txt", "Hej världen");

        var result = await _sut.ExecuteAsync("/las notes.txt Sammanfatta innehållet.");

        result.ResultType.Should().Be(FileAgentResultType.FileRead);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_LasWithoutInstruction_ReturnsRawContent()
    {
        // Contract change: a bare "/läs <fil>" (no instruction) now returns the file's raw
        // content instead of erroring — models emit this shape constantly and the old failure
        // wasted a tool round. See ExecuteAsync_ReadWithoutInstruction_ReturnsRawContent.
        await CreateFileAsync("notes.txt", "Hej världen");

        var result = await _sut.ExecuteAsync("/läs notes.txt");

        result.IsSuccess.Should().BeTrue();
        result.ResultType.Should().Be(FileAgentResultType.FileRead);
        result.InjectedContext.Should().Contain("Hej världen");
    }

    [Fact]
    public async Task ExecuteAsync_LasMissingFile_ReturnsFailureSuggestingSkapa()
    {
        var result = await _sut.ExecuteAsync("/läs saknas.txt Sammanfatta innehållet.");

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("/skapa");
    }

    [Fact]
    public async Task ExecuteAsync_LasWithHugeFile_TruncatesContentWithNotice()
    {
        var hugeContent = new string('a', 250_000);
        var path = Path.Combine(_tempDir, "stor.txt");
        await File.WriteAllTextAsync(path, hugeContent);

        var result = await _sut.ExecuteAsync("/läs stor.txt Sammanfatta innehållet.");

        result.IsSuccess.Should().BeTrue();
        result.InjectedContext.Should().NotContain(new string('a', 250_000));
        result.InjectedContext.Should().Contain("OBS: Innehållet har trunkerats");
    }

    [Fact]
    public async Task ExecuteAsync_LasWithSmallFile_DoesNotTruncate()
    {
        await CreateFileAsync("liten.txt", "Ett kort textinnehåll som inte ska trunkeras.");

        var result = await _sut.ExecuteAsync("/läs liten.txt Sammanfatta innehållet.");

        result.IsSuccess.Should().BeTrue();
        result.InjectedContext.Should().NotContain("OBS: Innehållet har trunkerats");
    }

    // ── /lista via ExecuteAsync ───────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ListaEmptyDirectory_ReturnsNoFilesMessage()
    {
        var result = await _sut.ExecuteAsync("/lista");

        result.IsSuccess.Should().BeTrue();
        result.ResultType.Should().Be(FileAgentResultType.FilesListed);
        result.InjectedContext.Should().Be("Inga filer finns i agentkatalogen.");
    }

    [Fact]
    public async Task ExecuteAsync_ListaWithFiles_ReturnsCommaSeparatedListing()
    {
        await CreateFileAsync("a.txt", "x");
        await CreateFileAsync("b.txt", "y");

        var result = await _sut.ExecuteAsync("/lista");

        result.InjectedContext.Should().Contain("a.txt").And.Contain("b.txt");
    }

    // ── Regression: "/lista" vs. "/listaXXX" (ExecuteAsync must agree with IsCommand) ────

    [Fact]
    public void IsCommand_UnknownWordStartingWithListaPrefix_ReturnsFalse()
    {
        _sut.IsCommand("/listafoo").Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_UnknownWordStartingWithListaPrefix_IsNotTreatedAsListCommand()
    {
        // Regression test: ExecuteAsync's "/lista" match used to be StartsWith("/lista") with no
        // trailing space, looser than IsCommand's StartsWith("/lista "). That meant "/listafoo"
        // would silently run the file listing instead of being rejected as an unrecognised command.
        var result = await _sut.ExecuteAsync("/listafoo");

        result.ResultType.Should().Be(FileAgentResultType.NotACommand);
    }

    // ── Regression: bare command name with no argument must still be recognised ──────────

    [Theory]
    [InlineData("/skapa")]
    [InlineData("/fyll")]
    [InlineData("/läs")]
    [InlineData("/las")]
    [InlineData("/redigera")]
    public void IsCommand_RecognisesBareCommandNameWithoutArguments(string input)
    {
        // These commands all require an argument, but the bare command name on its own (e.g.
        // just "/skapa", no filename) must still be recognised as an attempted command so the
        // user gets a helpful validation message instead of it silently falling through to the
        // LLM as an ordinary chat message.
        _sut.IsCommand(input).Should().BeTrue();
    }

    [Theory]
    [InlineData("/skapa")]
    [InlineData("/fyll")]
    [InlineData("/läs")]
    [InlineData("/las")]
    [InlineData("/redigera")]
    public async Task ExecuteAsync_BareCommandNameWithoutArguments_ReturnsHelpfulErrorInsteadOfNotACommand(string input)
    {
        var result = await _sut.ExecuteAsync(input);

        result.ResultType.Should().Be(FileAgentResultType.Error);
        result.IsSuccess.Should().BeFalse();
        result.Message.Should().NotBeNullOrEmpty();
    }

    // ── ReadPdfFileAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task ReadPdfFileAsync_ExistingPdf_ExtractsTextWithPageMarker()
    {
        var path = Path.Combine(_tempDir, "rapport.pdf");
        await File.WriteAllBytesAsync(path, MinimalPdfBuilder.CreateWithText("Hello PDF world"));

        var result = await _sut.ReadPdfFileAsync("rapport.pdf");

        result.IsSuccess.Should().BeTrue();
        result.ResultType.Should().Be(FileAgentResultType.FileRead);
        result.InjectedContext.Should().Contain("Hello PDF world");
        result.InjectedContext.Should().Contain("Page 1");
    }

    [Fact]
    public async Task ReadPdfFileAsync_MissingFile_ReturnsFailure()
    {
        var result = await _sut.ReadPdfFileAsync("saknas.pdf");

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("hittades inte");
    }

    /// <summary>
    /// Regression test for a real-world PDF (51 MB rulebook, 74 pages) that reproduced "same
    /// problem again" after the earlier context-overflow fix: PdfPig extracts zero letters from
    /// every page (the book's text was flattened to vector outlines/curves at export, so there
    /// are no embedded text objects to read — confirmed via <c>page.Letters.Count == 0</c> on
    /// every page during investigation). Before this test, <see cref="FileAgentService"/> still
    /// reported success because the joined "--- Page N ---" markers alone made the content
    /// non-blank, so the LLM silently received zero real information about the file instead of
    /// a clear error. Reads the PDF straight from the user's Desktop rather than checking a 51 MB
    /// fixture into the repo, so it's a no-op on machines without the file (e.g. CI).
    /// </summary>
    [Fact]
    public async Task ReadPdfFileAsync_GloomhavenRulebook_NoEmbeddedText_ReturnsFailureInsteadOfEmptyContent()
    {
        var desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var pdfFilename = "Gloomhaven-2025-Rulebook.pdf";
        if (!File.Exists(Path.Combine(desktopDir, pdfFilename)))
        {
            _output.WriteLine($"Skipping: {pdfFilename} not found in {desktopDir} on this machine.");
            return;
        }

        var sut = new FileAgentService(desktopDir);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var result = await sut.ReadPdfFileAsync(pdfFilename);

        stopwatch.Stop();
        _output.WriteLine($"Extraction took {stopwatch.Elapsed.TotalSeconds:F1}s");
        _output.WriteLine($"IsSuccess={result.IsSuccess}, Message={result.Message}");

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("Ingen text kunde extraheras");
    }

    [Fact]
    public async Task ReadPdfFileAsync_NonPdfExtension_ReturnsFailure()
    {
        await CreateFileAsync("notes.txt", "hej");

        var result = await _sut.ReadPdfFileAsync("notes.txt");

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("inte en PDF");
    }

    [Fact]
    public async Task ReadPdfFileAsync_CorruptPdf_ReturnsFailureInsteadOfThrowing()
    {
        var path = Path.Combine(_tempDir, "trasig.pdf");
        await File.WriteAllTextAsync(path, "det här är inte en giltig PDF");

        var result = await _sut.ReadPdfFileAsync("trasig.pdf");

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("trasig.pdf");
    }

    [Fact]
    public async Task ReadPdfFileAsync_EmptyFilename_ReturnsFailure()
    {
        var result = await _sut.ReadPdfFileAsync("");

        result.IsSuccess.Should().BeFalse();
    }

    // ── /läs-pdf command ──────────────────────────────────────────────────

    [Theory]
    [InlineData("/läs-pdf rapport.pdf Sammanfatta innehållet")]
    [InlineData("/las-pdf rapport.pdf Sammanfatta innehållet")]
    public void IsCommand_RecognisesLasPdf(string input)
    {
        _sut.IsCommand(input).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_LasPdf_ReturnsInstructionCombinedWithExtractedText()
    {
        var path = Path.Combine(_tempDir, "rapport.pdf");
        await File.WriteAllBytesAsync(path, MinimalPdfBuilder.CreateWithText("Kvartalsresultat"));

        var result = await _sut.ExecuteAsync("/läs-pdf rapport.pdf Sammanfatta innehållet");

        result.IsSuccess.Should().BeTrue();
        result.ResultType.Should().Be(FileAgentResultType.FileRead);
        result.InjectedContext.Should().Contain("Sammanfatta innehållet");
        result.InjectedContext.Should().Contain("Kvartalsresultat");
    }

    [Fact]
    public async Task ExecuteAsync_LasPdfWithoutInstruction_ReturnsFailure()
    {
        var result = await _sut.ExecuteAsync("/läs-pdf rapport.pdf");

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_LasPdfMissingFile_ReturnsFailure()
    {
        var result = await _sut.ExecuteAsync("/läs-pdf saknas.pdf Sammanfatta innehållet");

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("hittades inte");
    }

    // ── SaveUploadedFileAsync ─────────────────────────────────────────────

    [Fact]
    public async Task SaveUploadedFileAsync_ValidStream_WritesFileByteForByte()
    {
        var bytes = MinimalPdfBuilder.CreateWithText("Uploaded content");
        using var stream = new MemoryStream(bytes);

        var result = await _sut.SaveUploadedFileAsync("uploaded.pdf", stream);

        result.IsSuccess.Should().BeTrue();
        var written = await File.ReadAllBytesAsync(Path.Combine(_tempDir, "uploaded.pdf"));
        written.Should().Equal(bytes);
    }

    [Fact]
    public async Task SaveUploadedFileAsync_ExistingFile_Overwrites()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "notes.txt"), "gammalt innehåll");
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("nytt innehåll"));

        await _sut.SaveUploadedFileAsync("notes.txt", stream);

        var written = await File.ReadAllTextAsync(Path.Combine(_tempDir, "notes.txt"));
        written.Should().Be("nytt innehåll");
    }

    [Fact]
    public async Task SaveUploadedFileAsync_EmptyFilename_ReturnsFailure()
    {
        using var stream = new MemoryStream();

        var result = await _sut.SaveUploadedFileAsync("", stream);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task SaveUploadedFileAsync_PathTraversalFilename_IsConfinedToBaseDirectory()
    {
        // The directory component is stripped to a bare filename, so the upload lands safely
        // inside the workspace instead of escaping it (path-traversal safety).
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("payload"));

        var result = await _sut.SaveUploadedFileAsync("../../evil.txt", stream);

        result.IsSuccess.Should().BeTrue();
        File.Exists(Path.Combine(_tempDir, "evil.txt")).Should().BeTrue();
        File.Exists(Path.Combine(Path.GetDirectoryName(_tempDir)!, "evil.txt")).Should().BeFalse();
    }

    // ── SetBaseDirectory ─────────────────────────────────────────────────

    [Fact]
    public void SetBaseDirectory_ExistingPath_SwitchesBaseDirectory()
    {
        var newDir = Path.Combine(_tempDir, "workspace2");
        Directory.CreateDirectory(newDir);

        _sut.SetBaseDirectory(newDir);

        _sut.BaseDirectory.Should().Be(Path.GetFullPath(newDir));
    }

    [Fact]
    public void SetBaseDirectory_MissingPath_CreatesDirectory()
    {
        var newDir = Path.Combine(_tempDir, "not-yet-created");

        _sut.SetBaseDirectory(newDir);

        Directory.Exists(newDir).Should().BeTrue();
    }

    [Fact]
    public async Task SetBaseDirectory_AfterSwitch_SubsequentWritesLandInNewDirectory()
    {
        var newDir = Path.Combine(_tempDir, "workspace2");
        _sut.SetBaseDirectory(newDir);

        await _sut.ExecuteAsync("/skapa notes.txt");

        File.Exists(Path.Combine(newDir, "notes.txt")).Should().BeTrue();
        File.Exists(Path.Combine(_tempDir, "notes.txt")).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SetBaseDirectory_NullOrWhitespace_Throws(string? baseDirectory)
    {
        var act = () => _sut.SetBaseDirectory(baseDirectory!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ── ReadFileRawAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task ReadFileRawAsync_ExistingFile_ReturnsRawContentWithoutCombiningInstruction()
    {
        await CreateFileAsync("notes.txt", "rad 1", "rad 2");

        var result = await _sut.ReadFileRawAsync("notes.txt");

        result.IsSuccess.Should().BeTrue();
        result.ResultType.Should().Be(FileAgentResultType.FileRead);
        result.InjectedContext.Should().Contain("rad 1").And.Contain("rad 2");
    }

    [Fact]
    public async Task ReadFileRawAsync_MissingFile_ReturnsFailureSuggestingSkapa()
    {
        var result = await _sut.ReadFileRawAsync("missing.txt");

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("/skapa missing.txt");
    }

    [Fact]
    public async Task ReadFileRawAsync_EmptyFilename_ReturnsFailure()
    {
        var result = await _sut.ReadFileRawAsync(string.Empty);

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("Ange ett filnamn");
    }

    [Fact]
    public async Task ReadFileRawAsync_EmptyFile_ReturnsFailure()
    {
        await CreateFileAsync("empty.txt");

        var result = await _sut.ReadFileRawAsync("empty.txt");

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("tom");
    }

    // ── WriteExtractedContentAsync ──────────────────────────────────────────

    [Fact]
    public async Task WriteExtractedContentAsync_NewFile_WritesContent()
    {
        await _sut.WriteExtractedContentAsync("generated.txt", "hej\nvärlden");

        (await File.ReadAllTextAsync(Path.Combine(_tempDir, "generated.txt")))
            .Should().Be("hej\nvärlden");
    }

    [Fact]
    public async Task WriteExtractedContentAsync_ExistingFile_Overwrites()
    {
        await CreateFileAsync("generated.txt", "gammalt innehåll");

        await _sut.WriteExtractedContentAsync("generated.txt", "nytt innehåll");

        (await File.ReadAllTextAsync(Path.Combine(_tempDir, "generated.txt")))
            .Should().Be("nytt innehåll");
    }

    [Fact]
    public async Task WriteExtractedContentAsync_InvalidFilename_ThrowsInvalidOperationException()
    {
        var act = async () => await _sut.WriteExtractedContentAsync("Fil:", "innehåll");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ── StripFileMarkers ────────────────────────────────────────────────

    [Fact]
    public void StripFileMarkers_RemovesMarkers_KeepsContent()
    {
        var response = "<FILE>\nPRINT \"Hej\"\n<ENDFILE>";

        _sut.StripFileMarkers(response).Should().Be("PRINT \"Hej\"");
    }

    [Fact]
    public void StripFileMarkers_NoMarkersPresent_ReturnsTrimmedInputUnchanged()
    {
        var response = "  Bara vanlig text utan markörer.  ";

        _sut.StripFileMarkers(response).Should().Be("Bara vanlig text utan markörer.");
    }

    // ── GetToolDescriptions / BuildToolsSystemPrompt ───────────────────────

    [Fact]
    public void GetToolDescriptions_ReturnsAllSixFileCommands()
    {
        var descriptions = _sut.GetToolDescriptions();

        descriptions.Keys.Should().BeEquivalentTo(
            "/läs <filnamn> <instruktion>",
            "/läs-pdf <filnamn> <instruktion>",
            "/skapa <filnamn>",
            "/fyll <filnamn> <beskrivning>",
            "/redigera <filnamn> <instruktion>",
            "/lista");
    }

    [Fact]
    public void BuildToolsSystemPrompt_IncludesEveryToolSignatureAndDescription()
    {
        var prompt = _sut.BuildToolsSystemPrompt();

        foreach (var (signature, description) in _sut.GetToolDescriptions())
        {
            prompt.Should().Contain(signature);
            prompt.Should().Contain(description);
        }
    }

    // ── TryFindAgentCommand ───────────────────────────────────────────────

    [Fact]
    public void TryFindAgentCommand_CommandAloneOnItsOwnLine_IsFound()
    {
        var response = "Här är svaret.\n/läs-pdf rapport.pdf Sammanfatta innehållet\n";

        var found = _sut.TryFindAgentCommand(response, out var command);

        found.Should().BeTrue();
        command.Should().Be("/läs-pdf rapport.pdf Sammanfatta innehållet");
    }

    [Fact]
    public void TryFindAgentCommand_CommandNarratedInStraightQuotes_IsFoundAsFallback()
    {
        // Some models explain their intent instead of writing the command alone on its own line.
        var response = "Based on the context provided, I will use the " +
            "\"/läs-pdf quarterly-report.pdf Sammanfatta innehållet\" command to fulfill your request.";

        var found = _sut.TryFindAgentCommand(response, out var command);

        found.Should().BeTrue();
        command.Should().Be("/läs-pdf quarterly-report.pdf Sammanfatta innehållet");
    }

    [Fact]
    public void TryFindAgentCommand_CommandNarratedInCurlyQuotes_IsFoundAsFallback()
    {
        var response = "Jag använder verktyget “/läs rapport.txt Sammanfatta innehållet” nu.";

        var found = _sut.TryFindAgentCommand(response, out var command);

        found.Should().BeTrue();
        command.Should().Be("/läs rapport.txt Sammanfatta innehållet");
    }

    [Fact]
    public void TryFindAgentCommand_NoCommandAnywhere_ReturnsFalse()
    {
        var response = "Stockholm är huvudstaden i Sverige.";

        var found = _sut.TryFindAgentCommand(response, out var command);

        found.Should().BeFalse();
        command.Should().BeEmpty();
    }

    [Fact]
    public void TryFindAgentCommand_QuotedTextThatIsNotACommand_ReturnsFalse()
    {
        var response = "Filen innehåller texten \"hej och välkommen\" på första raden.";

        var found = _sut.TryFindAgentCommand(response, out var command);

        found.Should().BeFalse();
        command.Should().BeEmpty();
    }
}
