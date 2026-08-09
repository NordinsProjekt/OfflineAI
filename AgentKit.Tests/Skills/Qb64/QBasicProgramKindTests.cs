using AgentKit.Skills.Qb64;
using FluentAssertions;

namespace AgentKit.Tests.Skills.Qb64;

/// <summary>
/// Unit tests for <see cref="QBasicProgramKind"/>: the check that decides whether a .bas file can
/// be run at all in a headless agent run, i.e. whether it declares <c>$CONSOLE:ONLY</c> and
/// whether it switches to a graphics screen mode.
/// </summary>
public sealed class QBasicProgramKindTests
{
    // ── $CONSOLE:ONLY ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("$CONSOLE:ONLY\nPRINT \"HEJ\"\n")]
    [InlineData("$console:only\nPRINT \"HEJ\"\n")]      // case-insensitive
    [InlineData("$CONSOLE : ONLY\nPRINT \"HEJ\"\n")]    // spaces around the colon
    [InlineData("'$CONSOLE:ONLY\nPRINT \"HEJ\"\n")]     // QBasic-style leading apostrophe
    public void IsConsoleOnly_WithMetacommand_ReturnsTrue(string source)
    {
        QBasicProgramKind.IsConsoleOnly(source).Should().BeTrue();
    }

    [Theory]
    [InlineData("PRINT \"HEJ\"\n")]
    [InlineData("$CONSOLE\nPRINT \"HEJ\"\n")] // console *plus* a window is not console-only
    [InlineData("")]
    [InlineData(null)]
    public void IsConsoleOnly_WithoutMetacommand_ReturnsFalse(string? source)
    {
        QBasicProgramKind.IsConsoleOnly(source).Should().BeFalse();
    }

    // ── Graphics detection ───────────────────────────────────────────────────

    [Theory]
    [InlineData("SCREEN 12")]
    [InlineData("SCREEN 13")]
    [InlineData("screen 7")]                              // case-insensitive
    [InlineData("SCREEN 12, , 0, 0")]                     // extra SCREEN arguments
    [InlineData("SCREEN _NEWIMAGE(640, 480, 32)")]        // QB64 image handle
    [InlineData("SCREEN grafiklage")]                     // mode in a variable — assume graphics
    public void FindGraphicsScreen_WithGraphicsMode_ReportsTheStatement(string statement)
    {
        var source = $"CLS\n{statement}\nEND\n";

        var found = QBasicProgramKind.FindGraphicsScreen(source);

        found.Should().NotBeNull();
        found!.Line.Should().Be(2);
        found.Statement.Should().Be(statement); // the exact statement, for the LLM-facing message
    }

    [Fact]
    public void FindGraphicsScreen_AfterThenOnASingleLineIf_IsFound()
    {
        var source = "IF grafik THEN SCREEN 13\nEND\n";

        QBasicProgramKind.FindGraphicsScreen(source)!.Line.Should().Be(1);
    }

    [Fact]
    public void FindGraphicsScreen_InAColonSeparatedStatement_IsFound()
    {
        var source = "CLS: SCREEN 12: CLS\nEND\n";

        QBasicProgramKind.FindGraphicsScreen(source)!.Line.Should().Be(1);
    }

    [Fact]
    public void FindGraphicsScreen_ReportsTheFirstOccurrence()
    {
        var source = "PRINT \"start\"\nSCREEN 13\nSCREEN 12\n";

        var found = QBasicProgramKind.FindGraphicsScreen(source);

        found!.Line.Should().Be(2);
        found.Statement.Should().Contain("13");
    }

    [Theory]
    [InlineData("SCREEN 0")]              // text mode
    [InlineData("SCREEN 0, 0, 0")]        // text mode with extra arguments
    [InlineData("PRINT SCREEN(1, 1)")]    // the SCREEN *function*, not a mode switch
    [InlineData("tecken = SCREEN(1, 1)")]
    [InlineData("' SCREEN 13")]           // in a comment
    [InlineData("REM SCREEN 13")]
    [InlineData("PRINT \"SCREEN 13\"")]   // in a string literal
    [InlineData("PRINT \"HEJ\"")]
    public void FindGraphicsScreen_WithoutGraphicsMode_ReturnsNull(string statement)
    {
        var source = $"$CONSOLE:ONLY\n{statement}\nEND\n";

        QBasicProgramKind.FindGraphicsScreen(source).Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\n")]
    [InlineData(null)]
    public void FindGraphicsScreen_WithoutSource_ReturnsNull(string? source)
    {
        QBasicProgramKind.FindGraphicsScreen(source).Should().BeNull();
    }

    [Fact]
    public void FindGraphicsScreen_LongStatement_IsShortenedForTheMessage()
    {
        var source = "SCREEN _NEWIMAGE(skarmbredd% * 2 + 100, skarmhojd% * 2 + 100, 32)\n";

        var statement = QBasicProgramKind.FindGraphicsScreen(source)!.Statement;

        statement.Should().EndWith("…");
        statement.Length.Should().Be(61); // 60 characters plus the ellipsis
    }
}
