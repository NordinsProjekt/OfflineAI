using AgentKit.Skills.Files;
using AgentKit.Skills.Qb64;
using FluentAssertions;

namespace AgentKit.Tests.Skills.Qb64;

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
        var options = new Qb64Options();
        options.CompilerPath = compilerPath ?? string.Empty;
        if (compilerArguments is not null)
            options.CompilerArguments = compilerArguments;
        if (compileTimeoutMs > 0)
            options.CompileTimeoutMs = compileTimeoutMs;
        if (runTimeoutMs > 0)
            options.RunTimeoutMs = runTimeoutMs;
        if (maxOutputLength > 0)
            options.MaxOutputLength = maxOutputLength;
        return new Qb64ToolService(options, _fileAgent);
    }

    private async Task<string> CreateBasFileAsync(string name = "spel.bas")
    {
        // SYSTEM (not END) is what makes the program headless-runnable at all — without it the
        // run path refuses up front, see ExecuteAsync_RunConsoleProgramWithoutSystem_IsRefused.
        var path = Path.Combine(_tempDir, name);
        await File.WriteAllTextAsync(path, "$CONSOLE:ONLY\nPRINT \"HEJ\"\nSYSTEM\n");
        return path;
    }

    /// <summary>Writes a .bas file that opens a graphics window, i.e. one that cannot be run here.</summary>
    private async Task<string> CreateGraphicsBasFileAsync(string name = "bild.bas")
    {
        var path = Path.Combine(_tempDir, name);
        await File.WriteAllTextAsync(path, "SCREEN 12\nCIRCLE (320, 240), 100, 15\nEND\n");
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

        var runCommand = descriptions.Keys.Should().ContainSingle(key => key.StartsWith("/qb64 ")).Subject;
        descriptions.Should().ContainKey("/qb64-kompilera <fil.bas>");
        runCommand.Should().Contain("<<<");                        // the input syntax is in the signature
        descriptions[runCommand].Should().Contain("$CONSOLE:ONLY"); // the LLM must learn the capture rule
        descriptions[runCommand].Should().Contain("SYSTEM");        // ...that only SYSTEM actually exits
        descriptions[runCommand].Should().Contain("Grafikprogram"); // ...and that graphics programs aren't run
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

    // ── Structural pre-check (QBasicStructureLinter) ──────────────────────────

    [Fact]
    public async Task ExecuteAsync_StructuralIssue_ReturnsFailureWithoutInvokingCompiler()
    {
        // A compiler stub that would otherwise succeed (produces a runnable "exe") proves the
        // real compiler was never invoked: the structural pre-check must short-circuit first.
        var path = Path.Combine(_tempDir, "spel.bas");
        await File.WriteAllTextAsync(path,
            "FUNCTION MAX(a, b)\nEND FUNCTION\nSCREEN 13\n"); // statement after a procedure
        var sut = CreateSut(CmdExe, "/c copy /y \"%ComSpec%\" \"{output}\" > nul");

        var result = await sut.ExecuteAsync("/qb64-kompilera spel.bas");

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("Strukturkontroll");
        result.Message.Should().Contain("SUB/FUNCTION");
        File.Exists(Path.Combine(_tempDir, "spel.exe")).Should().BeFalse("the real compiler must never run when the pre-check already found a problem");
    }

    [Fact]
    public async Task ExecuteAsync_NoStructuralIssue_StillInvokesRealCompiler()
    {
        await CreateBasFileAsync();
        var sut = CreateSut(CmdExe, "/c echo Syntax error on line 3 & exit /b 1");

        var result = await sut.ExecuteAsync("/qb64-kompilera spel.bas");

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("Syntax error on line 3"); // proves the real compiler ran
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

    // ── Keyboard input fed to the program (<<<) ──────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WithStandardInput_TypesItIntoTheRunningProgram()
    {
        await CreateBasFileAsync();
        // The "program" is a copy of cmd.exe, which executes whatever arrives on its stdin —
        // so an echoed marker in the captured output proves the input really reached it.
        var sut = CreateSut(CmdExe, "/c copy /y \"%ComSpec%\" \"{output}\" > nul");

        var result = await sut.ExecuteAsync("/qb64 spel.bas <<< echo SVAR=42");

        result.IsSuccess.Should().BeTrue();
        result.InjectedContext.Should().Contain("SVAR=42");
        result.InjectedContext.Should().Contain("Inmatning som skickades"); // and the LLM is told what it sent
    }

    [Fact]
    public async Task ExecuteAsync_WithSeveralInputLines_SendsThemInOrder()
    {
        await CreateBasFileAsync();
        var sut = CreateSut(CmdExe, "/c copy /y \"%ComSpec%\" \"{output}\" > nul");

        var result = await sut.ExecuteAsync("/qb64 spel.bas <<< echo FORST | echo SEDAN");

        result.IsSuccess.Should().BeTrue();
        result.InjectedContext.Should().Contain("2 rad(er)"); // split on | into two separate lines
        result.InjectedContext.Should().Contain("FORST");     // both were executed by the program
        result.InjectedContext.Should().Contain("SEDAN");
    }

    [Fact]
    public async Task ExecuteAsync_WithTwoAngleBrackets_IsAcceptedAsWell()
    {
        await CreateBasFileAsync();
        var sut = CreateSut(CmdExe, "/c copy /y \"%ComSpec%\" \"{output}\" > nul");

        var result = await sut.ExecuteAsync("/qb64 spel.bas << echo SVAR=42");

        result.IsSuccess.Should().BeTrue();
        result.InjectedContext.Should().Contain("SVAR=42");
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptyInputPayload_SendsASingleEmptyLine()
    {
        await CreateBasFileAsync();
        var sut = CreateSut(CmdExe, "/c copy /y \"%ComSpec%\" \"{output}\" > nul");

        var result = await sut.ExecuteAsync("/qb64 spel.bas <<<");

        result.IsSuccess.Should().BeTrue();
        result.InjectedContext.Should().Contain("1 rad(er)"); // a bare Enter, for INPUT "Tryck retur"; a$
    }

    [Fact]
    public async Task ExecuteAsync_WithoutStandardInput_SaysNothingAboutInput()
    {
        await CreateBasFileAsync();
        var sut = CreateSut(CmdExe, "/c copy /y \"%ComSpec%\" \"{output}\" > nul");

        var result = await sut.ExecuteAsync("/qb64 spel.bas");

        result.IsSuccess.Should().BeTrue();
        result.InjectedContext.Should().NotContain("Inmatning som skickades");
    }

    [Fact]
    public async Task ExecuteAsync_CompileOnlyWithStandardInput_ReturnsFailurePointingAtQb64()
    {
        await CreateBasFileAsync();
        var sut = CreateSut(CmdExe, "/c copy /y \"%ComSpec%\" \"{output}\" > nul");

        var result = await sut.ExecuteAsync("/qb64-kompilera spel.bas <<< 42");

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("/qb64");
        File.Exists(Path.Combine(_tempDir, "spel.exe")).Should().BeFalse("nothing should be compiled for a rejected command");
    }

    [Fact]
    public async Task ExecuteAsync_InputTimeout_TellsTheLlmToSendMoreLines()
    {
        await CreateBasFileAsync();
        var sut = CreateSut(CmdExe, "/c copy /y \"%ComSpec%\" \"{output}\" > nul", runTimeoutMs: 500);

        // The input given keeps the "program" busy far past the timeout — the same shape as a
        // QBasic program that is still waiting for the next INPUT when the run is killed.
        var result = await sut.ExecuteAsync("/qb64 spel.bas <<< ping -n 20 127.0.0.1");

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("timeout");
        result.Message.Should().Contain("fler rader");
    }

    // ── Graphics programs are compiled, never run ────────────────────────────

    [Fact]
    public async Task ExecuteAsync_GraphicsProgram_IsCompiledButNotRun()
    {
        await CreateGraphicsBasFileAsync();
        var sut = CreateSut(CmdExe, "/c copy /y \"%ComSpec%\" \"{output}\" > nul");

        var result = await sut.ExecuteAsync("/qb64 bild.bas");

        result.IsSuccess.Should().BeTrue();
        result.Message.Should().Contain("kompilerade utan fel");
        result.InjectedContext.Should().Contain("kördes INTE");
        result.InjectedContext.Should().Contain("rad 1");    // the line that decided it
        result.InjectedContext.Should().Contain("SCREEN 12"); // and the statement itself
        result.InjectedContext.Should().NotContain("Microsoft"); // the produced exe was never started
        File.Exists(Path.Combine(_tempDir, "bild.exe")).Should().BeTrue("the compile itself must still happen");
    }

    [Fact]
    public async Task ExecuteAsync_GraphicsProgramWithInput_IsStillNotRun()
    {
        // Piped input reaches a console, never a graphics window — the guard must win.
        await CreateGraphicsBasFileAsync();
        var sut = CreateSut(CmdExe, "/c copy /y \"%ComSpec%\" \"{output}\" > nul");

        var result = await sut.ExecuteAsync("/qb64 bild.bas <<< 42");

        result.IsSuccess.Should().BeTrue();
        result.InjectedContext.Should().Contain("kördes INTE");
    }

    [Fact]
    public async Task ExecuteAsync_TextModeProgram_IsStillRun()
    {
        // SCREEN 0 is text mode, not a window: this must not be mistaken for a graphics program.
        var path = Path.Combine(_tempDir, "text.bas");
        await File.WriteAllTextAsync(path, "$CONSOLE:ONLY\nSCREEN 0\nPRINT \"HEJ\"\nSYSTEM\n");
        var sut = CreateSut(CmdExe, "/c copy /y \"%ComSpec%\" \"{output}\" > nul");

        var result = await sut.ExecuteAsync("/qb64 text.bas");

        result.IsSuccess.Should().BeTrue();
        result.InjectedContext.Should().Contain("Microsoft"); // cmd.exe's banner — the program really ran
    }

    [Fact]
    public async Task ExecuteAsync_RunConsoleProgramWithoutSystem_IsRefusedWithSystemHint()
    {
        // A $CONSOLE:ONLY program without SYSTEM never exits (END waits for a keypress), so the
        // run is refused before the compiler is even invoked — the model gets the fix for free.
        var path = Path.Combine(_tempDir, "hang.bas");
        await File.WriteAllTextAsync(path, "$CONSOLE:ONLY\nPRINT \"HEJ\"\nEND\n");
        var sut = CreateSut(CmdExe, "/c copy /y \"%ComSpec%\" \"{output}\" > nul");

        var result = await sut.ExecuteAsync("/qb64 hang.bas");

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("SYSTEM");
        File.Exists(Path.Combine(_tempDir, "hang.exe")).Should().BeFalse("the doomed run must not even compile");
    }

    [Fact]
    public async Task ExecuteAsync_RunConsoleOnlyProgramWithGraphicsScreen_IsRefusedNamingTheContradiction()
    {
        // $CONSOLE:ONLY + SCREEN 13 sends every PRINT to an invisible graphics screen: the
        // console stays black and empty. Running it can only mislead, so it is refused.
        var path = Path.Combine(_tempDir, "svart.bas");
        await File.WriteAllTextAsync(path, "$CONSOLE:ONLY\nSCREEN 13\nPRINT \"HEJ\"\nSYSTEM\n");
        var sut = CreateSut(CmdExe, "/c copy /y \"%ComSpec%\" \"{output}\" > nul");

        var result = await sut.ExecuteAsync("/qb64 svart.bas");

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("SCREEN 13");
        result.Message.Should().Contain("$CONSOLE:ONLY");
    }

    [Fact]
    public async Task ExecuteAsync_CompileOnly_IgnoresRunHazards()
    {
        // /qb64-kompilera only promises a compile check, and the file does compile — refusing
        // here would block the one verification a not-yet-finished program can get.
        var path = Path.Combine(_tempDir, "hang.bas");
        await File.WriteAllTextAsync(path, "$CONSOLE:ONLY\nPRINT \"HEJ\"\nEND\n");
        var sut = CreateSut(CmdExe, "/c copy /y \"%ComSpec%\" \"{output}\" > nul");

        var result = await sut.ExecuteAsync("/qb64-kompilera hang.bas");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_GraphicsProgramCompiledOnly_IsUnchanged()
    {
        await CreateGraphicsBasFileAsync();
        var sut = CreateSut(CmdExe, "/c copy /y \"%ComSpec%\" \"{output}\" > nul");

        var result = await sut.ExecuteAsync("/qb64-kompilera bild.bas");

        result.IsSuccess.Should().BeTrue();
        result.InjectedContext.Should().Contain("kördes inte");
    }

    [Fact]
    public async Task ExecuteAsync_GraphicsProgramThatDoesNotCompile_StillReportsTheCompilerError()
    {
        await CreateGraphicsBasFileAsync();
        var sut = CreateSut(CmdExe, "/c echo Syntax error on line 2 & exit /b 1");

        var result = await sut.ExecuteAsync("/qb64 bild.bas");

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("Syntax error on line 2");
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
