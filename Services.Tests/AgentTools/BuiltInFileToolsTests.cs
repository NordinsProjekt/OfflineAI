using FluentAssertions;
using Services.AgentTools;
using Services.FileAgent;

namespace Services.Tests.AgentTools;

/// <summary>
/// Unit tests for <see cref="BuiltInFileTools"/>: the Semantic Kernel plugin exposing
/// <see cref="IFileAgentService"/> operations as <c>[KernelFunction]</c> methods. Uses a real
/// <see cref="FileAgentService"/> rooted at a temp directory, mirroring the convention in
/// <see cref="Services.Tests.FileAgent.FileAgentServiceTests"/>.
/// </summary>
public class BuiltInFileToolsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileAgentService _fileAgent;
    private readonly BuiltInFileTools _sut;

    public BuiltInFileToolsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "BuiltInFileToolsTests_" + Guid.NewGuid());
        _fileAgent = new FileAgentService(_tempDir);
        _sut = new BuiltInFileTools(_fileAgent);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Constructor ───────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullFileAgent_ThrowsArgumentNullException()
    {
        var act = () => new BuiltInFileTools(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ── create_file ───────────────────────────────────────────────────────

    [Fact]
    public async Task CreateFileAsync_NewFilename_CreatesEmptyFileAndReturnsConfirmation()
    {
        var message = await _sut.CreateFileAsync("notes.txt");

        message.Should().Contain("notes.txt");
        File.Exists(Path.Combine(_tempDir, "notes.txt")).Should().BeTrue();
    }

    // ── read_file ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadFileAsync_ExistingFile_ReturnsRawContentWithoutRequiringInstruction()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "notes.txt"), "hej v\u00e4rlden");

        var content = await _sut.ReadFileAsync("notes.txt");

        content.Should().Be("hej v\u00e4rlden");
    }

    [Fact]
    public async Task ReadFileAsync_MissingFile_ReturnsErrorMessage()
    {
        var content = await _sut.ReadFileAsync("saknas.txt");

        content.Should().Contain("hittades inte");
    }

    // ── write_file ────────────────────────────────────────────────────────

    [Fact]
    public async Task WriteFileAsync_NewContent_WritesFileAndReturnsConfirmation()
    {
        var message = await _sut.WriteFileAsync("notes.txt", "nytt inneh\u00e5ll");

        message.Should().Contain("notes.txt");
        var written = await File.ReadAllTextAsync(Path.Combine(_tempDir, "notes.txt"));
        written.Should().Be("nytt inneh\u00e5ll");
    }

    [Fact]
    public async Task WriteFileAsync_ExistingFile_OverwritesPreviousContent()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "notes.txt"), "gammalt");

        await _sut.WriteFileAsync("notes.txt", "nytt");

        var written = await File.ReadAllTextAsync(Path.Combine(_tempDir, "notes.txt"));
        written.Should().Be("nytt");
    }

    // ── edit_file_lines ───────────────────────────────────────────────────

    [Fact]
    public async Task EditFileLinesAsync_SingleLine_ReplacesOnlyThatLine()
    {
        await File.WriteAllLinesAsync(Path.Combine(_tempDir, "notes.txt"), ["rad 1", "rad 2", "rad 3"]);

        var message = await _sut.EditFileLinesAsync("notes.txt", 2, 2, "ny rad 2");

        message.Should().Contain("redigerad");
        var content = await File.ReadAllLinesAsync(Path.Combine(_tempDir, "notes.txt"));
        content.Should().Equal("rad 1", "ny rad 2", "rad 3");
    }

    [Fact]
    public async Task EditFileLinesAsync_Range_ReplacesAllLinesInRange()
    {
        await File.WriteAllLinesAsync(Path.Combine(_tempDir, "notes.txt"), ["rad 1", "rad 2", "rad 3", "rad 4"]);

        await _sut.EditFileLinesAsync("notes.txt", 2, 3, "ersatt block");

        var content = await File.ReadAllLinesAsync(Path.Combine(_tempDir, "notes.txt"));
        content.Should().Equal("rad 1", "ersatt block", "rad 4");
    }

    [Fact]
    public async Task EditFileLinesAsync_OutOfRangeLine_ReturnsErrorAndLeavesFileUnchanged()
    {
        await File.WriteAllLinesAsync(Path.Combine(_tempDir, "notes.txt"), ["rad 1"]);

        var message = await _sut.EditFileLinesAsync("notes.txt", 5, 5, "f\u00f6r l\u00e5ngt");

        message.Should().Contain("Ogiltigt");
        var content = await File.ReadAllLinesAsync(Path.Combine(_tempDir, "notes.txt"));
        content.Should().Equal("rad 1");
    }

    // ── insert_file_lines ─────────────────────────────────────────────────

    [Fact]
    public async Task InsertFileLinesAsync_InsertAfterTrue_AddsLineWithoutRemovingExisting()
    {
        await File.WriteAllLinesAsync(Path.Combine(_tempDir, "notes.txt"), ["rad 1", "rad 2"]);

        var message = await _sut.InsertFileLinesAsync("notes.txt", 1, insertAfter: true, "ny rad");

        message.Should().Contain("redigerad");
        var content = await File.ReadAllLinesAsync(Path.Combine(_tempDir, "notes.txt"));
        content.Should().Equal("rad 1", "ny rad", "rad 2");
    }

    [Fact]
    public async Task InsertFileLinesAsync_InsertAfterFalse_InsertsBeforeAnchorLine()
    {
        await File.WriteAllLinesAsync(Path.Combine(_tempDir, "notes.txt"), ["rad 1", "rad 2"]);

        await _sut.InsertFileLinesAsync("notes.txt", 2, insertAfter: false, "ny rad");

        var content = await File.ReadAllLinesAsync(Path.Combine(_tempDir, "notes.txt"));
        content.Should().Equal("rad 1", "ny rad", "rad 2");
    }

    [Fact]
    public async Task InsertFileLinesAsync_AnchorZeroInsertAfter_InsertsAtTopOfFile()
    {
        await File.WriteAllLinesAsync(Path.Combine(_tempDir, "notes.txt"), ["rad 1"]);

        await _sut.InsertFileLinesAsync("notes.txt", 0, insertAfter: true, "f\u00f6rsta raden");

        var content = await File.ReadAllLinesAsync(Path.Combine(_tempDir, "notes.txt"));
        content.Should().Equal("f\u00f6rsta raden", "rad 1");
    }

    [Fact]
    public async Task InsertFileLinesAsync_NewMethodBody_InsertsAllLinesOfMultiLineContent()
    {
        await File.WriteAllLinesAsync(Path.Combine(_tempDir, "notes.txt"), ["class Foo", "{", "}"]);

        await _sut.InsertFileLinesAsync(
            "notes.txt", 2, insertAfter: true,
            "    public void NyMetod()\n    {\n    }");

        var content = await File.ReadAllLinesAsync(Path.Combine(_tempDir, "notes.txt"));
        content.Should().Equal("class Foo", "{", "    public void NyMetod()", "    {", "    }", "}");
    }

    // ── list_files ────────────────────────────────────────────────────────

    [Fact]
    public void ListFiles_EmptyDirectory_ReturnsNoFilesMessage()
    {
        var message = _sut.ListFiles();

        message.Should().Be("No files in the agent directory.");
    }

    [Fact]
    public async Task ListFiles_WithFiles_ReturnsCommaSeparatedFilenames()
    {
        await _sut.CreateFileAsync("a.txt");
        await _sut.CreateFileAsync("b.txt");

        var message = _sut.ListFiles();

        message.Should().Contain("a.txt").And.Contain("b.txt");
    }
}
