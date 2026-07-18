using FluentAssertions;
using Services.AgentTools;
using Services.Configuration;
using Services.FileAgent;

namespace Services.Tests.AgentTools;

/// <summary>
/// Unit tests for <see cref="Qb64ToolService"/>: the QB64 compiler tool that lets the LLM
/// compile and run QBasic (.bas) files from the active workspace. Command detection and
/// filename-resolution tests use pure configuration; compile/run tests use the real Windows
/// <c>cmd.exe</c> as a stand-in compiler via the CompilerArguments template — e.g.
/// <c>/c copy /y "%ComSpec%" "{output}"</c> "compiles" by producing a runnable executable
/// (a copy of cmd.exe, which prints its banner and exits on closed stdin), so the whole
/// compile → run → capture-output path is exercised deterministically without QB64 installed.
/// </summary>
public sealed class Qb64ToolServiceTests : IDisposable
{
    private static readonly string CmdExe =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");

    private readonly string _tempDir;
    private readonly FileAgentService _fileAgent;

    public Qb64ToolServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Qb64ToolServiceTests_" + Guid.NewGuid());
        _fileAgent = new FileAgentService(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private Qb64ToolService CreateSut(
        string? compilerPath = null,
        string? compilerArguments = null,
        int compileTimeoutMs = 0,
        int runTimeoutMs = 0,
        int maxOutputLength = 0)
    {
        var config = new AppConfiguration();
        config.AgentTools.Qb64.CompilerPath = compilerPath ?? string.Empty;
        if (compilerArguments is not null)
            config.AgentTools.Qb64.CompilerArguments = compilerArguments;
        if (compileTimeoutMs > 0)
            config.AgentTools.Qb64.CompileTimeoutMs = compileTimeoutMs;
        if (runTimeoutMs > 0)
            config.AgentTools.Qb64.RunTimeoutMs = runTimeoutMs;
        if (maxOutputLength > 0)
            config.AgentTools.Qb64.MaxOutputLength = maxOutputLength;
        return new Qb64ToolService(config, _fileAgent);
    }

    private async Task<string> CreateBasFileAsync(string name = "spel.bas")
    {
        var path = Path.Combine(_tempDir, name);
        await File.WriteAllTextAsync(path, "$CONSOLE:ONLY\nPRINT \"HEJ\"\n");
        return path;
    }

    // ── Command detection ────────────────────────────────────────────────────

    [Fact]
    public void IsCommand_WithoutConfiguredCompiler_ReturnsFalse()
    {
        var sut = CreateSut(compilerPath: null);
        sut.IsCommand("/qb64 spel.bas").Should().BeFalse();
    }

    [Theory]
    [InlineData("/qb64 spel.bas")]
    [InlineData("/QB64 spel.bas")] // case-insensitive
    [InlineData("  /qb64 spel.bas")] // leading whitespace
    [InlineData("/qb64")]
    [InlineData("/qb64-kompilera spel.bas")]
    [InlineData("/qb64-kompilera")]
    public void IsCommand_WithConfiguredCompiler_MatchesQb64Commands(string input)
    {
        var sut = CreateSut(CmdExe);
        sut.IsCommand(input).Should().BeTrue();
    }

    [Theory]
    [InlineData("/qb64pe spel.bas")] // longer word must not match the /qb64 prefix
    [InlineData("/qbasic spel.bas")]
    [InlineData("Kompilera spel.bas med /qb64, tack.")] // command not at line start
    [InlineData("")]
    public void IsCommand_WithNonMatchingInput_ReturnsFalse(string input)
    {
        var sut = CreateSut(CmdExe);
        sut.IsCommand(input).Should().BeFalse();
    }

    [Fact]
    public void TryFindCommand_FindsCommandLineInsideMultiLineResponse()
    {
        var sut = CreateSut(CmdExe);
        var response = "Nu testar jag programmet.\n/qb64 spel.bas\nSedan återkommer jag.";

        sut.TryFindCommand(response, out var command).Should().BeTrue();
        command.Should().Be("/qb64 spel.bas");
    }

    [Fact]
    public void TryFindCommand_WithNoCommandInResponse_ReturnsFalse()
    {
        var sut = CreateSut(CmdExe);
        sut.TryFindCommand("Här är mitt svar utan verktyg.", out _).Should().BeFalse();
    }

    // ── Tool descriptions ────────────────────────────────────────────────────

    [Fact]
    public void GetToolDescriptions_WithoutConfiguredCompiler_IsEmpty()
    {
        var sut = CreateSut(compilerPath: null);
        sut.GetToolDescriptions().Should().BeEmpty();
    }

    [Fact]
    public void GetToolDescriptions_WithConfiguredCompiler_DescribesBothCommands()
    {
        var sut = CreateSut(CmdExe);

        var descriptions = sut.GetToolDescriptions();

        descriptions.Should().ContainKey("/qb64 <fil.bas>");
        descriptions.Should().ContainKey("/qb64-kompilera <fil.bas>");
        descriptions["/qb64 <fil.bas>"].Should().Contain("$CONSOLE:ONLY"); // the LLM must learn the capture rule
    }

    // ── Argument validation ──────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WithoutConfiguredCompiler_ReturnsFailureNamingSetting()
    {
        var sut = CreateSut(compilerPath: null);

        var result = await sut.ExecuteAsync("/qb64 spel.bas");

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("CompilerPath");
    }

    [Fact]
    public async Task ExecuteAsync_CompilerPathMissingOnDisk_ReturnsFailureNamingPath()
    {
        var sut = CreateSut(@"C:\finns\inte\qb64.exe");

        var result = await sut.ExecuteAsync("/qb64 spel.bas");

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain(@"C:\finns\inte\qb64.exe");
    }

    [Fact]
    public async Task ExecuteAsync_WithoutFilename_ReturnsFailureWithUsageHint()
    {
        var sut = CreateSut(CmdExe);

        var result = await sut.ExecuteAsync("/qb64");

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain(".bas");
    }

    [Fact]
    public async Task ExecuteAsync_NonBasExtension_ReturnsFailure()
    {
        var sut = CreateSut(CmdExe);

        var result = await sut.ExecuteAsync("/qb64 anteckningar.txt");

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain(".bas");
    }

    [Fact]
    public async Task ExecuteAsync_InvalidFilename_ReturnsFailure()
    {
        var sut = CreateSut(CmdExe);

        var result = await sut.ExecuteAsync("/qb64 spel:fel.bas");

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("Ogiltigt filnamn");
    }

    [Fact]
    public async Task ExecuteAsync_MissingSourceFile_ReturnsFailureWithListaHint()
    {
        var sut = CreateSut(CmdExe);

        var result = await sut.ExecuteAsync("/qb64 finnsinte.bas");

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("finnsinte.bas");
        result.Message.Should().Contain("/lista");
    }

    [Fact]
    public async Task ExecuteAsync_FilenameWithoutExtension_DefaultsToBas()
    {
        var sut = CreateSut(CmdExe);

        var result = await sut.ExecuteAsync("/qb64 finnsinte");

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("finnsinte.bas"); // resolved with the .bas default
    }

    // ── Compile ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_CompilerReportsError_ReturnsFailureWithCompilerOutput()
    {
        await CreateBasFileAsync();
        var sut = CreateSut(CmdExe, "/c echo Syntax error on line 3 & exit /b 1");

        var result = await sut.ExecuteAsync("/qb64 spel.bas");

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("Syntax error on line 3"); // fed back so the LLM can fix the code
    }

    [Fact]
    public async Task ExecuteAsync_CompilerExitsCleanlyWithoutProducingExe_ReturnsFailure()
    {
        await CreateBasFileAsync();
        var sut = CreateSut(CmdExe, "/c echo Klar");

        var result = await sut.ExecuteAsync("/qb64 spel.bas");

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("kunde inte kompilera");
    }

    [Fact]
    public async Task ExecuteAsync_CompilerExceedingTimeout_IsKilledAndReturnsFailure()
    {
        await CreateBasFileAsync();
        var sut = CreateSut(CmdExe, "/c ping -n 10 127.0.0.1", compileTimeoutMs: 500);

        var result = await sut.ExecuteAsync("/qb64 spel.bas");

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("timeout");
    }

    [Fact]
    public async Task ExecuteAsync_LongCompilerError_IsTruncatedKeepingTail()
    {
        await CreateBasFileAsync();
        var sut = CreateSut(
            CmdExe,
            "/c echo Massor av kompilatorbrus fore det viktiga felet pa slutet & exit /b 1",
            maxOutputLength: 20);

        var result = await sut.ExecuteAsync("/qb64 spel.bas");

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("[trunkerat]");
        result.Message.Should().Contain("slutet"); // the end of the output survives truncation
    }

    [Fact]
    public async Task ExecuteAsync_CompileOnly_CompilesButDoesNotRun()
    {
        await CreateBasFileAsync();
        // "Compiles" by copying cmd.exe to the output path — running it would print the banner.
        var sut = CreateSut(CmdExe, "/c copy /y \"%ComSpec%\" \"{output}\" > nul");

        var result = await sut.ExecuteAsync("/qb64-kompilera spel.bas");

        result.IsSuccess.Should().BeTrue();
        result.Message.Should().Contain("kompilerade utan fel");
        result.InjectedContext.Should().Contain("kördes inte");
        result.InjectedContext.Should().NotContain("Microsoft"); // the produced exe was never run
        File.Exists(Path.Combine(_tempDir, "spel.exe")).Should().BeTrue();
    }

    // ── Compile + run ────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_CompileAndRun_ReturnsProgramOutput()
    {
        await CreateBasFileAsync();
        // The produced "program" is a copy of cmd.exe: with its stdin closed it prints the
        // version banner to stdout and exits 0, exercising the full run/capture path.
        var sut = CreateSut(CmdExe, "/c copy /y \"%ComSpec%\" \"{output}\" > nul");

        var result = await sut.ExecuteAsync("/qb64 spel.bas");

        result.IsSuccess.Should().BeTrue();
        result.Message.Should().Contain("spel.bas");
        result.InjectedContext.Should().Contain("Programmets utdata");
        result.InjectedContext.Should().Contain("Microsoft"); // cmd.exe's banner was captured
    }

    [Fact]
    public async Task ExecuteAsync_SourcePlaceholderIsSubstituted()
    {
        var sourcePath = await CreateBasFileAsync();
        // Echo the substituted source path and fail, proving {source} reaches the compiler.
        var sut = CreateSut(CmdExe, "/c echo KALLA={source} & exit /b 1");

        var result = await sut.ExecuteAsync("/qb64 spel.bas");

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain(sourcePath);
    }

    [Fact]
    public async Task ExecuteAsync_StaleExeLockedByRunningProgram_ReturnsFailure()
    {
        await CreateBasFileAsync();
        var stale = Path.Combine(_tempDir, "spel.exe");
        await File.WriteAllTextAsync(stale, "gammal exe");
        var sut = CreateSut(CmdExe, "/c echo aldrig hit & exit /b 1");

        using (File.Open(stale, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = await sut.ExecuteAsync("/qb64 spel.bas");

            result.IsSuccess.Should().BeFalse();
            result.Message.Should().Contain("spel.exe");
        }
    }
}
