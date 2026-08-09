using System.Text.RegularExpressions;

namespace AgentKit.Skills.Qb64;

/// <summary>
/// The graphics-mode <c>SCREEN</c> statement that makes a program open a window, anchored to the
/// physical source line it sits on.
/// </summary>
public sealed record QBasicGraphicsScreen(int Line, string Statement)
{
    public override string ToString() => $"Rad {Line}: {Statement}";
}

/// <summary>
/// Deterministic, heuristic check for how a QBasic/QB64 program talks to the outside world:
/// does it declare itself console-only (<c>$CONSOLE:ONLY</c>), and does it switch to a graphics
/// screen mode?
/// <para>
/// This exists because the two answers decide whether the program can be run at all in a
/// headless agent run. A program that opens a graphics window has no console to capture, nobody
/// to look at the window, and nobody to press the key QB64 waits for at <c>END</c> — running it
/// can only ever end in the run timeout. A real agent run was observed spending an iteration on
/// exactly that: a correct 640x480 drawing program was compiled, run, killed after 30 seconds
/// and then failed the "the program works when it is run" requirement. Detecting the graphics
/// mode up front lets <see cref="Qb64ToolService"/> report the compile result — the only check
/// that is actually possible — instead of burning the timeout on a doomed run.
/// </para>
/// <para>
/// Like <see cref="QBasicStructureLinter"/> this is a heuristic, not a parser: it looks at the
/// leading keyword of each statement (after string literals and comments are stripped) and at
/// the first argument of <c>SCREEN</c>. A mode chosen at runtime (<c>SCREEN m</c>) is treated as
/// graphics, since only <c>SCREEN 0</c> is text — guessing "graphics" merely means the program
/// is compiled instead of run, which is the safe direction to be wrong in.
/// </para>
/// </summary>
public static class QBasicProgramKind
{
    /// <summary>
    /// Matches the <c>$CONSOLE:ONLY</c> metacommand anywhere in the raw source. Deliberately run
    /// against the unstripped text: QB64 accepts the metacommand both bare and behind a leading
    /// apostrophe (<c>'$CONSOLE:ONLY</c>), and the comment stripper would eat the second form.
    /// </summary>
    private static readonly Regex ConsoleOnlyRegex = new(
        @"\$CONSOLE\s*:\s*ONLY\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Matches a <c>SCREEN</c> statement and captures its first argument. Statement position is
    /// the start of a colon-separated segment, or straight after <c>THEN</c>/<c>ELSE</c> on a
    /// single-line <c>IF</c> — anywhere else <c>SCREEN</c> is the function that reads a character
    /// off the text screen, not a mode switch.
    /// </summary>
    private static readonly Regex ScreenStatementRegex = new(
        @"(?:^|\bTHEN\s+|\bELSE\s+)SCREEN\s+(?<arg>[^,]*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// True when the source declares <c>$CONSOLE:ONLY</c>, i.e. the program runs without a window
    /// and its <c>PRINT</c> output can be captured from stdout (and its <c>INPUT</c> read from
    /// stdin).
    /// </summary>
    public static bool IsConsoleOnly(string? source) =>
        !string.IsNullOrEmpty(source) && ConsoleOnlyRegex.IsMatch(source);

    /// <summary>
    /// Returns the first graphics-mode <c>SCREEN</c> statement in <paramref name="source"/>, or
    /// <c>null</c> when the program never leaves text mode. Note that this answers "does it draw
    /// graphics", not "can it run headlessly" — callers that mean the latter must check
    /// <see cref="IsConsoleOnly"/> as well.
    /// </summary>
    public static QBasicGraphicsScreen? FindGraphicsScreen(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return null;

        var lines = source.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var codeOnly = QBasicStructureLinter.StripStringsAndComments(lines[i]);
            if (string.IsNullOrWhiteSpace(codeOnly))
                continue;

            // Metacommands ($CONSOLE:ONLY, $INCLUDE, ...) use a colon that is not a statement
            // separator, so they must not be split into segments.
            if (codeOnly.TrimStart().StartsWith('$'))
                continue;

            foreach (var rawSegment in codeOnly.Split(':'))
            {
                var segment = rawSegment.Trim();
                if (segment.Length == 0)
                    continue;

                var match = ScreenStatementRegex.Match(segment);
                if (!match.Success || !IsGraphicsMode(match.Groups["arg"].Value))
                    continue;

                return new QBasicGraphicsScreen(i + 1, Shorten(segment));
            }
        }

        return null;
    }

    /// <summary>
    /// Decides whether a <c>SCREEN</c> argument selects a graphics mode. Only the literal
    /// <c>SCREEN 0</c> is text mode; a QB64 image handle (<c>SCREEN _NEWIMAGE(...)</c>) or a
    /// variable is treated as graphics.
    /// </summary>
    private static bool IsGraphicsMode(string argument)
    {
        var arg = argument.Trim();
        if (arg.Length == 0)
            return false; // "SCREEN" with no argument is not a mode switch

        // A leading integer literal is the only case that can be text mode, and only for 0.
        var digits = 0;
        while (digits < arg.Length && char.IsAsciiDigit(arg[digits]))
            digits++;

        if (digits == 0)
            return true; // _NEWIMAGE(...), a variable, an expression — assume graphics

        return int.TryParse(arg[..digits], out var mode) && mode != 0;
    }

    private static string Shorten(string text, int maxLength = 60) =>
        text.Length <= maxLength ? text : text[..maxLength] + "…";
}
