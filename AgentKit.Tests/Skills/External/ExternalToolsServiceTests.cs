using AgentKit.Skills.External;
using FluentAssertions;

namespace AgentKit.Tests.Skills.External;

/// <summary>
/// Unit tests for <see cref="ExternalToolsService"/>: operator-configured local executables the
/// LLM may run by slash command. Command detection/description tests use pure configuration;
/// execution tests run the real Windows <c>cmd.exe</c> (with <c>/c echo</c> etc. as fixed
/// arguments) so the whole start-process → capture-stdout → feed-back path is exercised
/// deterministically without any custom test binary.
/// </summary>
public sealed class ExternalToolsServiceTests
{
    private static readonly string CmdExe =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");

    private static ExternalToolsService CreateSut(params ExternalToolOptions[] tools)
    {
        return new ExternalToolsService(tools);
    }

    private static ExternalToolOptions EchoTool(string command = "eko") => new()
    {
        Command        = command,
        ExecutablePath = CmdExe,
        FixedArguments = "/c echo",
        Description    = "Ekar tillbaka texten du skickar in.",
        Usage          = "<text>"
    };

    // ── Command detection ────────────────────────────────────────────────────

    [Fact]
    public void IsCommand_WithNoConfiguredTools_ReturnsFalse()
    {
        var sut = CreateSut();
        sut.IsCommand("/eko hej").Should().BeFalse();
    }

    [Theory]
    [InlineData("/eko")]
    [InlineData("/eko hej på dig")]
    [InlineData("  /EKO Hej")] // case-insensitive + leading whitespace
    public void IsCommand_WithConfiguredTool_MatchesCommandLine(string input)
    {
        var sut = CreateSut(EchoTool());
        sut.IsCommand(input).Should().BeTrue();
    }

    [Theory]
    [InlineData("/ekot hej")] // longer word must not match the /eko prefix
    [InlineData("/annat")]
    [InlineData("")]
    public void IsCommand_WithNonMatchingInput_ReturnsFalse(string input)
    {
        var sut = CreateSut(EchoTool());
        sut.IsCommand(input).Should().BeFalse();
    }

    [Fact]
    public void IsCommand_ConfiguredWithLeadingSlash_IsNormalized()
    {
        var sut = CreateSut(EchoTool(command: "/eko"));
        sut.IsCommand("/eko hej").Should().BeTrue();
    }

    [Fact]
    public void TryFindCommand_FindsCommandLineInsideMultiLineResponse()
    {
        var sut = CreateSut(EchoTool());
        var response = "Jag använder verktyget för att svara.\n/eko hej världen\nSedan återkommer jag.";

        sut.TryFindCommand(response, out var command).Should().BeTrue();
        command.Should().Be("/eko hej världen");
    }

    [Fact]
    public void TryFindCommand_WithNoCommandInResponse_ReturnsFalse()
    {
        var sut = CreateSut(EchoTool());
        sut.TryFindCommand("Här är mitt svar utan verktyg.", out _).Should().BeFalse();
    }

    // ── Tool descriptions ────────────────────────────────────────────────────

    [Fact]
    public void GetToolDescriptions_IncludesSignatureWithUsageAndDescription()
    {
        var sut = CreateSut(EchoTool());

        var descriptions = sut.GetToolDescriptions();

        descriptions.Should().ContainKey("/eko <text>");
        descriptions["/eko <text>"].Should().Be("Ekar tillbaka texten du skickar in.");
    }

    [Fact]
    public void GetToolDescriptions_SkipsToolsWithoutCommandOrPath()
    {
        var sut = CreateSut(
            new ExternalToolOptions { Command = "", ExecutablePath = CmdExe },
            new ExternalToolOptions { Command = "utanpath", ExecutablePath = "" });

        sut.GetToolDescriptions().Should().BeEmpty();
    }

    // ── Execution ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_RunsExecutableAndReturnsStdout()
    {
        var sut = CreateSut(EchoTool());

        var result = await sut.ExecuteAsync("/eko hej världen");

        result.IsSuccess.Should().BeTrue();
        result.InjectedContext.Should().Contain("hej världen");
        result.InjectedContext.Should().Contain("/eko"); // names the tool for the LLM
    }

    [Fact]
    public async Task ExecuteAsync_ToolWithoutArguments_Runs()
    {
        var tool = new ExternalToolOptions
        {
            Command        = "version",
            ExecutablePath = CmdExe,
            FixedArguments = "/c ver",
            Description    = "Visar Windows-versionen."
        };
        var sut = CreateSut(tool);

        var result = await sut.ExecuteAsync("/version");

        result.IsSuccess.Should().BeTrue();
        result.InjectedContext.Should().Contain("Windows");
    }

    [Fact]
    public async Task ExecuteAsync_NonZeroExitCode_ReturnsFailureWithCode()
    {
        var tool = new ExternalToolOptions
        {
            Command        = "fel",
            ExecutablePath = CmdExe,
            FixedArguments = "/c exit 3",
            Description    = "Misslyckas alltid."
        };
        var sut = CreateSut(tool);

        var result = await sut.ExecuteAsync("/fel");

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("felkod 3");
    }

    [Fact]
    public async Task ExecuteAsync_MissingExecutable_ReturnsFailureNamingPath()
    {
        var tool = new ExternalToolOptions
        {
            Command        = "saknas",
            ExecutablePath = @"C:\finns\inte\alls.exe",
            Description    = "Pekar fel."
        };
        var sut = CreateSut(tool);

        var result = await sut.ExecuteAsync("/saknas");

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain(@"C:\finns\inte\alls.exe");
    }

    [Fact]
    public async Task ExecuteAsync_UnknownCommand_ReturnsFailure()
    {
        var sut = CreateSut(EchoTool());

        var result = await sut.ExecuteAsync("/okänt");

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_ProcessExceedingTimeout_IsKilledAndReturnsFailure()
    {
        var tool = new ExternalToolOptions
        {
            Command        = "seg",
            ExecutablePath = CmdExe,
            FixedArguments = "/c ping -n 10 127.0.0.1",
            Description    = "Tar ca 9 sekunder.",
            TimeoutMs      = 500
        };
        var sut = CreateSut(tool);

        var result = await sut.ExecuteAsync("/seg");

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("timeout");
    }

    [Fact]
    public async Task ExecuteAsync_OutputLongerThanMax_IsTruncated()
    {
        var tool = EchoTool();
        tool.MaxOutputLength = 10;
        var sut = CreateSut(tool);

        var result = await sut.ExecuteAsync("/eko " + new string('a', 100));

        result.IsSuccess.Should().BeTrue();
        result.InjectedContext.Should().Contain("[trunkerat]");
    }
}
