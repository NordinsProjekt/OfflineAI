using AgentKit.Skills.Qb64;
using FluentAssertions;

namespace AgentKit.Tests.Skills.Qb64;

/// <summary>
/// Unit tests for <see cref="QBasicStructureLinter"/>: the heuristic, no-compile structural check
/// that runs before the real QB64 compiler (see <see cref="Qb64ToolServiceTests"/> for the
/// integration point). Covers the exact bug shapes observed in a real 20-iteration agent run that
/// never converged: an extra END IF, a NEXT closing the wrong loop, and a SUB/FUNCTION defined
/// before the program's top-level code (QB64's "Statement cannot be placed between
/// SUB/FUNCTIONs"). Also covers the invented-keyword pass (<c>_LINE</c>, <c>_KEYHIT$</c>) added
/// after a structurally flawless generated game turned out to be uncompilable from its second
/// line onwards.
/// </summary>
public class QBasicStructureLinterTests
{
    [Fact]
    public void Analyze_EmptySource_ReturnsNoIssues()
    {
        QBasicStructureLinter.Analyze("").Should().BeEmpty();
        QBasicStructureLinter.Analyze("   \n  \n").Should().BeEmpty();
    }

    [Fact]
    public void Analyze_SimpleValidProgram_ReturnsNoIssues()
    {
        var source = string.Join('\n',
            "$CONSOLE:ONLY",
            "SCREEN 0",
            "DIM x AS INTEGER",
            "FOR x = 1 TO 10",
            "    IF x = 5 THEN",
            "        PRINT \"halfway: it's here\"",
            "    ELSEIF x = 10 THEN",
            "        PRINT \"done\"",
            "    ELSE",
            "        PRINT x",
            "    END IF",
            "NEXT x",
            "END");

        QBasicStructureLinter.Analyze(source).Should().BeEmpty();
    }

    [Fact]
    public void Analyze_SingleLineIf_DoesNotRequireEndIf()
    {
        var source = "IF x > 0 THEN y = 1\nPRINT y";

        QBasicStructureLinter.Analyze(source).Should().BeEmpty();
    }

    [Fact]
    public void Analyze_ProcedureDefinedBeforeTopLevelCode_FlagsStatementAfterProcedure()
    {
        // Reproduces the exact bug from a real run: a FUNCTION MAX helper was inserted at the
        // very top of the file (via a blind "insert at line 0/1" edit), pushing SCREEN 13 to
        // after it -- QB64's real error was "Statement cannot be placed between SUB/FUNCTIONs"
        // at the SCREEN 13 line, and the model never located the actual cause across 4 iterations.
        var source = string.Join('\n',
            "FUNCTION MAX(a, b)",
            "    IF a > b THEN",
            "        MAX = a",
            "    ELSE",
            "        MAX = b",
            "    END IF",
            "END FUNCTION",
            "SCREEN 13",
            "CLS");

        var issues = QBasicStructureLinter.Analyze(source);

        issues.Should().ContainSingle(i => i.Line == 8 && i.Message.Contains("SUB/FUNCTION"));
    }

    [Fact]
    public void Analyze_StatementBetweenTwoProcedures_IsFlagged()
    {
        var source = string.Join('\n',
            "SUB Greet",
            "    PRINT \"hej\"",
            "END SUB",
            "PRINT \"mellan procedurerna\"", // invalid: sits between two procedure defs
            "SUB Farewell",
            "    PRINT \"hej da\"",
            "END SUB");

        var issues = QBasicStructureLinter.Analyze(source);

        issues.Should().ContainSingle(i => i.Line == 4);
    }

    [Fact]
    public void Analyze_TopLevelCodeBeforeFirstProcedure_IsNotFlagged()
    {
        var source = string.Join('\n',
            "SCREEN 0",
            "CLS",
            "SUB Greet",
            "    PRINT \"hej\"",
            "END SUB");

        QBasicStructureLinter.Analyze(source).Should().BeEmpty();
    }

    [Fact]
    public void Analyze_ExtraEndIf_IsFlaggedAsStrayCloser()
    {
        var source = string.Join('\n',
            "IF x = 1 THEN",
            "    PRINT x",
            "END IF",
            "END IF"); // stray -- no matching IF

        var issues = QBasicStructureLinter.Analyze(source);

        issues.Should().ContainSingle(i => i.Line == 4 && i.Message.Contains("END IF"));
    }

    [Fact]
    public void Analyze_UnclosedIf_IsReportedAtOpeningLine()
    {
        var source = "IF x = 1 THEN\nPRINT x";

        var issues = QBasicStructureLinter.Analyze(source);

        issues.Should().ContainSingle(i => i.Line == 1 && i.Message.Contains("END IF"));
    }

    [Fact]
    public void Analyze_UnclosedNestedForLoops_ReportsBothOpeningLines()
    {
        var source = string.Join('\n',
            "FOR y = 1 TO 10",
            "    FOR x = 1 TO 10",
            "        PRINT x, y");

        var issues = QBasicStructureLinter.Analyze(source);

        issues.Should().Contain(i => i.Line == 1 && i.Message.Contains("NEXT"));
        issues.Should().Contain(i => i.Line == 2 && i.Message.Contains("NEXT"));
    }

    [Fact]
    public void Analyze_NextClosesWrongOuterLoop_IsFlaggedByVariableMismatch()
    {
        // Reproduces the first-iteration bug from the real run: an inner "FOR x" loop's NEXT
        // was accidentally written as "NEXT y", prematurely closing the outer loop instead.
        var source = string.Join('\n',
            "FOR y = 1 TO 25",
            "    FOR x = 1 TO 30",
            "        PRINT x, y",
            "    NEXT y", // should be "NEXT x" -- closes the wrong (outer) loop
            "NEXT y");

        var issues = QBasicStructureLinter.Analyze(source);

        issues.Should().ContainSingle(i => i.Line == 4 && i.Message.Contains("fel loop"));
    }

    [Fact]
    public void Analyze_NextWithMultipleVariables_ClosesThatManyLoops()
    {
        var source = string.Join('\n',
            "FOR y = 1 TO 10",
            "    FOR x = 1 TO 10",
            "        PRINT x, y",
            "    NEXT x, y");

        QBasicStructureLinter.Analyze(source).Should().BeEmpty();
    }

    [Fact]
    public void Analyze_DoLoopAndWhileWend_AreBalanced()
    {
        var source = string.Join('\n',
            "DO WHILE x < 10",
            "    x = x + 1",
            "LOOP",
            "WHILE y < 10",
            "    y = y + 1",
            "WEND");

        QBasicStructureLinter.Analyze(source).Should().BeEmpty();
    }

    [Fact]
    public void Analyze_SelectCaseBalanced_ReturnsNoIssues()
    {
        var source = string.Join('\n',
            "SELECT CASE x",
            "    CASE 1",
            "        PRINT \"one\"",
            "    CASE ELSE",
            "        PRINT \"other\"",
            "END SELECT");

        QBasicStructureLinter.Analyze(source).Should().BeEmpty();
    }

    [Fact]
    public void Analyze_ElseifWithoutOpenIf_IsFlagged()
    {
        var issues = QBasicStructureLinter.Analyze("ELSEIF x = 1 THEN\nPRINT x");

        issues.Should().ContainSingle(i => i.Line == 1 && i.Message.Contains("ELSEIF"));
    }

    [Fact]
    public void Analyze_CommentsAndStringLiteralsContainingKeywords_AreIgnored()
    {
        // The apostrophe inside the string literal and the block keywords spelled out inside a
        // string/comment must not be mistaken for real code.
        var source = string.Join('\n',
            "IF x = 1 THEN PRINT \"it's fine\"", // single-line IF; needs no END IF
            "PRINT \"this string mentions END IF and NEXT but isn't code\"",
            "' END IF (just a comment, not real)");

        QBasicStructureLinter.Analyze(source).Should().BeEmpty();
    }

    [Fact]
    public void Analyze_RemComment_IsIgnoredLikeApostropheComment()
    {
        var source = "REM END IF NEXT LOOP WEND -- none of this is real code\nPRINT \"ok\"";

        QBasicStructureLinter.Analyze(source).Should().BeEmpty();
    }

    [Fact]
    public void Analyze_MetacommandLineWithColon_IsNeverTreatedAsTwoStatements()
    {
        // "$CONSOLE:ONLY" must not be split on its colon like a normal statement separator.
        var source = "$CONSOLE:ONLY\nSCREEN 0\nPRINT \"hej\"";

        QBasicStructureLinter.Analyze(source).Should().BeEmpty();
    }

    [Fact]
    public void Analyze_UnderscoreOnClassicKeyword_IsFlaggedWithTheRealSpelling()
    {
        // Straight from a real run: the model applied QB64's underscore convention to LINE, a
        // keyword that never had one. QB64 reserves the underscore prefix for its own keywords,
        // so the compiler stops dead on the first "_LINE".
        var source = string.Join('\n',
            "SCREEN 12",
            "_LINE (10, 10), (20, 20), 1",
            "_PSET (5, 5), 2");

        var issues = QBasicStructureLinter.Analyze(source);

        issues.Should().Contain(i => i.Line == 2 && i.Message.Contains("\"LINE\""));
        issues.Should().Contain(i => i.Line == 3 && i.Message.Contains("\"PSET\""));
    }

    [Fact]
    public void Analyze_UnderscoreOnGraphicsStatements_IsFlaggedWithTheRealSpelling()
    {
        // The graphics statements the QBasicGraphics reference documents are the ones most likely
        // to attract a stray underscore: QB64 does have _PUTIMAGE and _MEMIMAGE, so a model that
        // has seen those reaches for _PUT and _GET too.
        var source = string.Join('\n',
            "_GET (0, 0)-(5, 5), sprite(0)",
            "_PUT (20, 20), sprite(0), PSET",
            "_BSAVE \"bild.bsv\", 0, 40");

        var issues = QBasicStructureLinter.Analyze(source);

        issues.Should().Contain(i => i.Line == 1 && i.Message.Contains("\"GET\""));
        issues.Should().Contain(i => i.Line == 2 && i.Message.Contains("\"PUT\""));
        issues.Should().Contain(i => i.Line == 3 && i.Message.Contains("\"BSAVE\""));
    }

    [Fact]
    public void Analyze_KnownKeywordWithWrongSigil_IsFlaggedWithTheCanonicalSpelling()
    {
        // The run's other invented keyword: _KEYHIT is real but returns a LONG, so "_KEYHIT$"
        // is not a QB64 keyword at all (INKEY$ is the string-returning one the model wanted).
        var source = string.Join('\n',
            "DO",
            "    IF _KEYHIT$ = \"\" THEN _LIMIT 10",
            "LOOP");

        var issues = QBasicStructureLinter.Analyze(source);

        issues.Should().ContainSingle(i => i.Line == 2 && i.Message.Contains("\"_KEYHIT\""));
    }

    [Fact]
    public void Analyze_KnownKeywordMissingItsSigil_IsFlagged()
    {
        var issues = QBasicStructureLinter.Analyze("name$ = _TRIM(other$)");

        issues.Should().ContainSingle(i => i.Line == 1 && i.Message.Contains("\"_TRIM$\""));
    }

    [Fact]
    public void Analyze_CorrectlySpelledQb64Keywords_AreNotFlagged()
    {
        var source = string.Join('\n',
            "img& = _NEWIMAGE(640, 480, 32)",
            "_PUTIMAGE (0, 0), img&",
            "_TITLE \"Pixel Quest\"",
            "c~& = _RGB32(255, 128, 0)",
            "clean$ = _TRIM$(raw$)",
            "IF _KEYHIT = 27 THEN SYSTEM",
            "w = _WIDTH(img&)", // WIDTH also exists without the underscore -- both are valid
            "_LIMIT 30");

        QBasicStructureLinter.Analyze(source).Should().BeEmpty();
    }

    [Fact]
    public void Analyze_UnknownUnderscoreName_IsLeftAloneRatherThanGuessedAt()
    {
        // Deliberate gap: the keyword table cannot be exhaustive against a moving QB64 release,
        // so an unrecognised name is passed through to the real compiler instead of being
        // reported. A missed detection is cheap; a false positive would have the agent rewrite
        // working code.
        QBasicStructureLinter.Analyze("_GLRENDER\n_SOMETHINGNEW 1, 2").Should().BeEmpty();
    }

    [Fact]
    public void Analyze_IdentifiersContainingUnderscores_AreNotMistakenForKeywords()
    {
        // "player_line" must not be read as the keyword "_LINE", and a trailing underscore is
        // QB64's line-continuation character rather than a keyword.
        var source = string.Join('\n',
            "player_line = 5",
            "map_cls = player_line + 1",
            "total = player_line + _",
            "        map_cls");

        QBasicStructureLinter.Analyze(source).Should().BeEmpty();
    }

    [Fact]
    public void Analyze_UnderscoreKeywordInsideStringOrComment_IsIgnored()
    {
        var source = string.Join('\n',
            "PRINT \"use _LINE here\"",
            "' _KEYHIT$ is mentioned in this comment only");

        QBasicStructureLinter.Analyze(source).Should().BeEmpty();
    }

    [Fact]
    public void Analyze_GeneratedGameWithBothInventedKeywords_ReportsThemInOnePass()
    {
        // The shape of the real rpggame.bas the agent produced: structurally perfect (which is
        // why the block-balance pass alone passed it clean) but full of keywords QB64 rejects.
        var source = string.Join('\n',
            "SCREEN 12",
            "FOR y = 5 TO 20",
            "    FOR x = 1 TO 60",
            "        _LINE (x, y), (x, y), 1",
            "    NEXT x",
            "NEXT y",
            "DO",
            "    _KEYHIT$",
            "LOOP");

        var issues = QBasicStructureLinter.Analyze(source);

        issues.Should().HaveCount(2);
        issues.Should().Contain(i => i.Line == 4);
        issues.Should().Contain(i => i.Line == 8);
    }

    // ── DescribeIssuesAfterWrite (write-time hook, no compiler needed) ────

    [Fact]
    public void DescribeIssuesAfterWrite_NonBasFile_ReturnsNullEvenForBrokenBlocks()
    {
        // A .txt that happens to contain block keywords is prose, not source.
        QBasicStructureLinter.DescribeIssuesAfterWrite("anteckningar.txt", "IF vi hinner THEN\nkör vi vidare")
            .Should().BeNull();
    }

    [Fact]
    public void DescribeIssuesAfterWrite_ValidBasFile_ReturnsNull()
    {
        QBasicStructureLinter.DescribeIssuesAfterWrite("spel.bas", "SCREEN 12\nLINE (1, 1)-(2, 2), 1\nEND")
            .Should().BeNull();
    }

    [Fact]
    public void DescribeIssuesAfterWrite_BrokenBasFile_NamesTheFileAndListsEveryIssue()
    {
        var feedback = QBasicStructureLinter.DescribeIssuesAfterWrite(
            "spel.bas", "_LINE (1, 1), (2, 2), 1\nIF x = 1 THEN\nPRINT x");

        feedback.Should().NotBeNull();
        feedback!.Should().Contain("spel.bas").And.Contain("1.").And.Contain("2.");
    }

    [Fact]
    public void DescribeIssuesAfterWrite_NullOrEmptyInput_IsHandled()
    {
        QBasicStructureLinter.DescribeIssuesAfterWrite(null, "PRINT 1").Should().BeNull();
        QBasicStructureLinter.DescribeIssuesAfterWrite("spel.bas", null).Should().BeNull();
        QBasicStructureLinter.DescribeIssuesAfterWrite("spel.bas", "").Should().BeNull();
    }

    [Fact]
    public void DescribeIssues_NoIssues_ReturnsNull()
    {
        QBasicStructureLinter.DescribeIssues("PRINT \"hej\"").Should().BeNull();
    }

    [Fact]
    public void DescribeIssues_MultipleIssues_NumbersEveryOneInOnePass()
    {
        // The whole point of running this before the real compiler: report every issue at once
        // instead of the one-per-iteration whack-a-mole a compiler that stops at its first error
        // produces.
        var source = string.Join('\n',
            "FUNCTION MAX(a, b)",
            "END FUNCTION",
            "SCREEN 13",       // issue: statement after a procedure
            "IF x = 1 THEN",
            "PRINT x",
            "END IF",
            "END IF");         // issue: stray END IF

        var described = QBasicStructureLinter.DescribeIssues(source);

        described.Should().NotBeNull();
        described!.Should().Contain("1.").And.Contain("2.");
        described.Should().Contain("SUB/FUNCTION");
        described.Should().Contain("END IF");
    }
}
