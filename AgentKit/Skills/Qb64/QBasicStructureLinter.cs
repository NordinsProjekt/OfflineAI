using System.Text.RegularExpressions;

namespace AgentKit.Skills.Qb64;

/// <summary>
/// A single structural problem found by <see cref="QBasicStructureLinter"/>, anchored to the
/// physical source line where it was detected.
/// </summary>
public sealed record QBasicStructureIssue(int Line, string Message)
{
    public override string ToString() => $"Rad {Line}: {Message}";
}

/// <summary>
/// Deterministic, heuristic structural check for QBasic/QB64 source: block-keyword balance
/// (IF/END IF, FOR/NEXT, DO/LOOP, WHILE/WEND, SELECT CASE/END SELECT, SUB/END SUB,
/// FUNCTION/END FUNCTION), top-level statements placed after a SUB/FUNCTION has already been
/// defined ("Statement cannot be placed between SUB/FUNCTIONs" in the real QB64 compiler), and
/// invented underscore-prefixed keywords (<c>_LINE</c>, <c>_KEYHIT$</c>) — see
/// <see cref="CheckUnderscoreKeywords"/>.
/// <para>
/// This exists because the real compiler stops at its first error, so a file with several latent
/// structural bugs costs one goal-agent iteration per bug — a real run was observed hitting the
/// exact same "Statement cannot be placed between SUB/FUNCTIONs" error four iterations in a row
/// because nothing ever surfaced the other, unrelated problems in the same file. Running this
/// heuristic pass first reports every issue it can find in one shot, before spending a slow
/// compiler invocation that would only report the first one.
/// </para>
/// <para>
/// This is intentionally a heuristic, not a real parser: it tokenizes each statement by its
/// leading keyword(s) after stripping string literals and comments, and tracks a single block
/// stack. It will not catch every error QB64's own compiler catches (type errors, undeclared
/// variables, etc.) — those still need a real compile. It also does not track individual
/// SUB/FUNCTION nesting depth beyond one level, and it only recognises <c>REM</c> as a comment
/// when it is the first token on a physical line (a colon-separated <c>: REM ...</c> later on the
/// same line is not specially handled) — both rare enough in generated code to accept as gaps in
/// exchange for a simple, fast, false-positive-averse pass.
/// </para>
/// </summary>
public static class QBasicStructureLinter
{
    private static readonly Regex TokenPairRegex = new(
        @"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*([A-Za-z_][A-Za-z0-9_]*)?",
        RegexOptions.Compiled);

    private static readonly Regex BlockIfRegex = new(
        @"^IF\b.*\bTHEN\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ForVariableRegex = new(
        @"^FOR\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Matches an underscore-prefixed name anywhere in a statement, with any trailing type sigil.
    /// The lookbehind is what keeps ordinary identifiers out: in <c>player_x</c> the <c>_x</c> is
    /// part of a longer name, not a QB64 keyword.
    /// </summary>
    private static readonly Regex UnderscoreNameRegex = new(
        @"(?<![A-Za-z0-9_])_[A-Za-z0-9_]*[$%&!#]?", RegexOptions.Compiled);

    private sealed record BlockFrame(string Kind, int OpenLine, string? Variable = null);

    /// <summary>
    /// Analyzes <paramref name="source"/> and returns every structural issue found, in the order
    /// encountered (unclosed blocks are reported last, at their opening line). Empty when the
    /// source has no detected issues — which does not guarantee the file compiles, only that this
    /// heuristic pass found nothing wrong.
    /// </summary>
    public static IReadOnlyList<QBasicStructureIssue> Analyze(string source)
    {
        var issues = new List<QBasicStructureIssue>();
        if (string.IsNullOrWhiteSpace(source))
            return issues;

        var stack = new List<BlockFrame>();
        var hasSeenProcedureOpener = false;

        // A top-level statement or block-opener reached once a SUB/FUNCTION has already been
        // defined is the exact "Statement cannot be placed between SUB/FUNCTIONs" error QB64's
        // real compiler raises — but it only ever reports the first such line it hits. Flagging
        // every one heuristically here is the point of this whole linter: a real agent run spent
        // four full iterations re-hitting that same compiler error on the same line because
        // nothing else in the file ever got surfaced to the model in the meantime.
        void FlagIfInterleaved(string statementText, int line)
        {
            if (hasSeenProcedureOpener && stack.Count == 0)
                issues.Add(new QBasicStructureIssue(line,
                    $"Körbar sats (\"{Shorten(statementText)}\") ligger efter att en SUB/FUNCTION redan påbörjats i filen. " +
                    "QB64 kräver att allt huvudprogram (all körbar kod utanför Sub/Function) ligger samlat " +
                    "före den FÖRSTA SUB/FUNCTION-definitionen — flytta raden dit, eller in i en Sub/Function."));
        }

        var lines = source.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var lineNumber = i + 1;
            var codeOnly = StripStringsAndComments(lines[i]);
            if (string.IsNullOrWhiteSpace(codeOnly))
                continue;

            // QB64 metacommands ($CONSOLE:ONLY, $INCLUDE, ...) use a colon that is not a
            // statement separator and are never subject to the placement rules below.
            if (codeOnly.TrimStart().StartsWith('$'))
                continue;

            // Scans the whole physical line rather than each statement's leading keyword: an
            // invented "_KEYHIT$" is just as broken in the middle of an expression as at the
            // start of a statement.
            CheckUnderscoreKeywords(codeOnly, lineNumber, issues);

            foreach (var rawSegment in codeOnly.Split(':'))
            {
                var segment = rawSegment.Trim();
                if (segment.Length == 0)
                    continue;

                var tokens = TokenPairRegex.Match(segment);
                if (!tokens.Groups[1].Success)
                    continue; // no recognisable keyword/identifier at all (stray punctuation etc.)

                var first = tokens.Groups[1].Value.ToUpperInvariant();
                var second = tokens.Groups[2].Success ? tokens.Groups[2].Value.ToUpperInvariant() : string.Empty;

                // ---- Closers (checked first so "END IF"/"END SUB" aren't read as a bare "END") ----
                if (first == "END" && second is "IF" or "SUB" or "FUNCTION" or "SELECT")
                {
                    PopExpected(stack, second, lineNumber, issues);
                    continue;
                }
                if (first == "NEXT")
                {
                    PopFor(stack, segment, tokens.Groups[1].Length, lineNumber, issues);
                    continue;
                }
                if (first == "LOOP") { PopExpected(stack, "DO", lineNumber, issues); continue; }
                if (first == "WEND") { PopExpected(stack, "WHILE", lineNumber, issues); continue; }

                // ---- Keywords that require but don't change an open block ----
                if (first is "ELSEIF" or "ELSE")
                {
                    if (!TopIs(stack, "IF"))
                        issues.Add(new QBasicStructureIssue(lineNumber, $"{first} utan någon öppen IF."));
                    continue;
                }
                if (first == "CASE")
                {
                    if (!TopIs(stack, "SELECT"))
                        issues.Add(new QBasicStructureIssue(lineNumber, "CASE utan någon öppen SELECT CASE."));
                    continue;
                }
                if (first is "EXIT" or "DECLARE")
                    continue; // EXIT DO/FOR/SUB/FUNCTION and forward declarations never touch the block stack

                // ---- Openers ----
                // A new top-level block (IF/FOR/DO/WHILE/SELECT) is just as invalid as a plain
                // statement once a SUB/FUNCTION has already been defined, so it's flagged here
                // too — only SUB/FUNCTION themselves are exempt (a new procedure starting right
                // after another one, or after a gap of blank/comment lines, is normal).
                if (first == "IF")
                {
                    FlagIfInterleaved(segment, lineNumber);
                    if (BlockIfRegex.IsMatch(segment))
                        stack.Add(new BlockFrame("IF", lineNumber));
                    continue; // single-line "IF x THEN y" needs no END IF
                }
                if (first == "FOR")
                {
                    FlagIfInterleaved(segment, lineNumber);
                    var varMatch = ForVariableRegex.Match(segment);
                    stack.Add(new BlockFrame("FOR", lineNumber, varMatch.Success ? varMatch.Groups[1].Value : null));
                    continue;
                }
                if (first == "DO")
                {
                    FlagIfInterleaved(segment, lineNumber);
                    stack.Add(new BlockFrame("DO", lineNumber));
                    continue;
                }
                if (first == "WHILE")
                {
                    FlagIfInterleaved(segment, lineNumber);
                    stack.Add(new BlockFrame("WHILE", lineNumber));
                    continue;
                }
                if (first == "SELECT")
                {
                    FlagIfInterleaved(segment, lineNumber);
                    stack.Add(new BlockFrame("SELECT", lineNumber));
                    continue;
                }
                if (first is "SUB" or "FUNCTION")
                {
                    hasSeenProcedureOpener = true;
                    stack.Add(new BlockFrame(first, lineNumber));
                    continue;
                }

                // ---- Anything else is a plain statement (DIM, assignment, PRINT, SCREEN, ...) ----
                FlagIfInterleaved(segment, lineNumber);
            }
        }

        foreach (var frame in stack)
            issues.Add(new QBasicStructureIssue(frame.OpenLine,
                $"{frame.Kind} öppnas här men stängs aldrig (saknar matchande {CloserFor(frame.Kind)})."));

        return issues;
    }

    /// <summary>
    /// Convenience wrapper for tool integration: returns <c>null</c> when the source has no
    /// detected issues, otherwise a numbered, human-readable list of every issue found (not just
    /// the first) suitable for feeding straight back to an LLM.
    /// </summary>
    public static string? DescribeIssues(string source)
    {
        var issues = Analyze(source);
        return issues.Count == 0
            ? null
            : string.Join("\n", issues.Select((issue, i) => $"{i + 1}. {issue}"));
    }

    /// <summary>
    /// Flags underscore-prefixed names that QB64 cannot compile. Two shapes are checked, both
    /// straight from a real agent run that produced <c>_LINE (x, y), (x, y), 1</c> and
    /// <c>_KEYHIT$</c>: a classic QBasic keyword that the model decorated with QB64's underscore
    /// convention, and a real QB64 keyword written with the wrong type sigil.
    /// <para>
    /// Deliberately asymmetric: a name this linter does not recognise is left alone rather than
    /// reported. QB64 does reserve the underscore prefix, so every unknown <c>_name</c> really is
    /// a compile error — but <see cref="KnownKeywords"/> can never be exhaustive against a moving
    /// QB64 release (and this linter ships in a NuGet package that does not follow it), so a gap
    /// in the list must cost a missed detection, never a false positive that sends the agent off
    /// rewriting code that already worked.
    /// </para>
    /// </summary>
    private static void CheckUnderscoreKeywords(string codeOnly, int lineNumber, List<QBasicStructureIssue> issues)
    {
        foreach (var written in UnderscoreNameRegex.Matches(codeOnly).Select(match => match.Value))
        {
            // A lone underscore is QB64's line-continuation character, not a keyword.
            if (written.Length <= 1)
                continue;

            if (KnownKeywords.TryGetValue(TrimSigil(written), out var canonical))
            {
                if (written.Equals(canonical, StringComparison.OrdinalIgnoreCase))
                    continue;

                issues.Add(new QBasicStructureIssue(lineNumber,
                    $"\"{written}\" har fel typtecken — QB64:s nyckelord skrivs \"{canonical}\"." +
                    (written.EndsWith('$') && !canonical.EndsWith('$')
                        ? " Det returnerar inget strängvärde, så $ hör inte dit."
                        : string.Empty)));
                continue;
            }

            // "_LINE" -> suggest "LINE", "_INKEY$" -> suggest "INKEY$" (sigil carried along).
            var withoutUnderscore = written[1..];
            if (LegacyOnlyKeywords.Contains(TrimSigil(withoutUnderscore)))
                issues.Add(new QBasicStructureIssue(lineNumber,
                    $"\"{written}\" finns inte i QB64 — nyckelordet heter \"{withoutUnderscore}\" utan inledande " +
                    "understreck. Understrecket är bara till för QB64:s egna tillägg, inte för klassiska " +
                    "QBasic-nyckelord."));
        }
    }

    /// <summary>
    /// Canonical spelling (type sigil included) of the QB64 keywords this linter knows about,
    /// keyed by the same name with any sigil removed. Intentionally not exhaustive — see
    /// <see cref="CheckUnderscoreKeywords"/> for why an omission here is harmless and an
    /// erroneous entry is not.
    /// </summary>
    private static readonly Dictionary<string, string> KnownKeywords = BuildKnownKeywords();

    private static Dictionary<string, string> BuildKnownKeywords()
    {
        string[] canonical =
        [
            // Images and display
            "_NEWIMAGE", "_LOADIMAGE", "_FREEIMAGE", "_COPYIMAGE", "_PUTIMAGE", "_SAVEIMAGE",
            "_DEST", "_SOURCE", "_DISPLAY", "_AUTODISPLAY", "_DISPLAYORDER", "_LIMIT", "_DELAY",
            "_FULLSCREEN", "_ALLOWFULLSCREEN", "_RESIZE", "_RESIZEWIDTH", "_RESIZEHEIGHT",
            "_TITLE", "_ICON", "_SCREENMOVE", "_SCREENX", "_SCREENY", "_SCREENIMAGE",
            "_SCREENHIDE", "_SCREENSHOW", "_SCREENEXISTS", "_SCREENICON", "_WIDTH", "_HEIGHT",
            "_PIXELSIZE", "_DEPTHBUFFER", "_MAPTRIANGLE", "_MAPUNICODE",
            // Colour
            "_RGB", "_RGB32", "_RGBA", "_RGBA32", "_RED", "_GREEN", "_BLUE", "_ALPHA",
            "_RED32", "_GREEN32", "_BLUE32", "_ALPHA32", "_CLEARCOLOR", "_BLEND", "_DONTBLEND",
            "_PALETTECOLOR", "_DEFAULTCOLOR", "_BACKGROUNDCOLOR",
            // Text and fonts
            "_PRINTSTRING", "_PRINTWIDTH", "_PRINTMODE", "_FONT", "_LOADFONT", "_FREEFONT",
            "_FONTWIDTH", "_FONTHEIGHT", "_CONTROLCHR",
            // Keyboard and mouse
            "_KEYHIT", "_KEYDOWN", "_KEYCLEAR", "_MOUSEX", "_MOUSEY", "_MOUSEBUTTON",
            "_MOUSEINPUT", "_MOUSEWHEEL", "_MOUSEMOVE", "_MOUSESHOW", "_MOUSEHIDE",
            "_MOUSEMOVEMENTX", "_MOUSEMOVEMENTY", "_CAPSLOCK", "_NUMLOCK", "_SCROLLLOCK", "_EXIT",
            // Game controllers
            "_DEVICES", "_DEVICE$", "_DEVICEINPUT", "_LASTBUTTON", "_LASTAXIS", "_LASTWHEEL",
            "_BUTTON", "_BUTTONCHANGE", "_AXIS", "_WHEEL",
            // Sound
            "_SNDOPEN", "_SNDCLOSE", "_SNDPLAY", "_SNDPLAYFILE", "_SNDLOOP", "_SNDSTOP",
            "_SNDPAUSE", "_SNDPLAYING", "_SNDPAUSED", "_SNDVOL", "_SNDLEN", "_SNDGETPOS",
            "_SNDSETPOS", "_SNDLIMIT", "_SNDCOPY", "_SNDBAL", "_SNDRAW", "_SNDRAWDONE",
            "_SNDRAWLEN", "_SNDOPENRAW", "_SNDNEW",
            // Maths
            "_PI", "_HYPOT", "_ATAN2", "_ASIN", "_ACOS", "_SINH", "_COSH", "_TANH",
            "_ASINH", "_ACOSH", "_ATANH", "_CEIL", "_ROUND", "_SHL", "_SHR", "_ROL", "_ROR",
            "_D2R", "_D2G", "_R2D", "_R2G", "_G2D", "_G2R", "_SEC", "_CSC", "_COT",
            "_READBIT", "_SETBIT", "_RESETBIT", "_TOGGLEBIT",
            // Strings, files and system
            "_TRIM$", "_INSTRREV", "_STRCMP", "_STRICMP", "_TOSTR$", "_OS$", "_CWD$",
            "_STARTDIR$", "_DIR$", "_FILES$", "_CLIPBOARD$", "_CLIPBOARDIMAGE", "_READFILE$",
            "_WRITEFILE", "_INFLATE$", "_DEFLATE$", "_MD5$", "_CV", "_MK$", "_FILEEXISTS",
            "_DIREXISTS", "_SHELLHIDE", "_ENVIRONCOUNT",
            // Types, memory and language extensions
            "_BIT", "_BYTE", "_INTEGER64", "_FLOAT", "_OFFSET", "_UNSIGNED", "_SIGNED",
            "_MEM", "_MEMNEW", "_MEMFREE", "_MEMCOPY", "_MEMFILL", "_MEMGET", "_MEMPUT",
            "_MEMELEMENT", "_MEMIMAGE", "_MEMSOUND", "_MEMEXISTS", "_DEFINE", "_PRESERVE",
            "_CONTINUE", "_ANDALSO", "_ORELSE", "_RECURSIVE", "_ASSERT", "_ERRORLINE",
            // Console and networking
            "_CONSOLE", "_ECHO", "_CONSOLETITLE", "_CONSOLECURSOR", "_CONSOLEFONT",
            "_OPENCLIENT", "_OPENHOST", "_OPENCONNECTION", "_CONNECTED", "_CONNECTIONADDRESS$"
        ];

        var map = new Dictionary<string, string>(canonical.Length, StringComparer.OrdinalIgnoreCase);
        foreach (var name in canonical)
            map[TrimSigil(name)] = name;
        return map;
    }

    /// <summary>
    /// Classic QBasic keywords that have no underscore-prefixed QB64 counterpart at all, so
    /// <c>_LINE</c>/<c>_CLS</c> is unambiguously the model over-applying QB64's underscore
    /// convention. Names that do have both spellings (WIDTH/_WIDTH, FILES/_FILES$, EXIT/_EXIT)
    /// are deliberately absent — flagging those would be a false positive.
    /// </summary>
    private static readonly HashSet<string> LegacyOnlyKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Graphics and console
        "CLS", "COLOR", "LOCATE", "PRINT", "LPRINT", "SCREEN", "PSET", "PRESET", "LINE",
        "CIRCLE", "PAINT", "DRAW", "POINT", "PALETTE", "PCOPY", "VIEW", "WINDOW", "CSRLIN",
        // Input, sound and timing
        "INPUT", "INKEY", "SLEEP", "BEEP", "SOUND", "PLAY", "TIMER", "RANDOMIZE", "RND",
        // Strings
        "CHR", "ASC", "LEN", "VAL", "STR", "MID", "LEFT", "RIGHT", "LTRIM", "RTRIM",
        "UCASE", "LCASE", "SPACE", "STRING", "INSTR",
        // Maths
        "ABS", "SGN", "INT", "FIX", "SQR", "SIN", "COS", "TAN", "ATN", "LOG", "EXP",
        // Declarations and flow
        "DIM", "REDIM", "SHARED", "STATIC", "CONST", "TYPE", "SUB", "FUNCTION", "CALL",
        "DECLARE", "GOTO", "GOSUB", "RETURN", "END", "SYSTEM", "SHELL",
        "IF", "THEN", "ELSE", "ELSEIF", "SELECT", "CASE", "FOR", "NEXT", "DO", "LOOP",
        "WHILE", "WEND", "STEP",
        // Files and data
        "OPEN", "CLOSE", "EOF", "LOF", "LOC", "FREEFILE", "SEEK", "KILL", "MKDIR", "RMDIR",
        "CHDIR", "NAME", "DATA", "READ", "RESTORE", "SWAP", "ERASE", "LBOUND", "UBOUND",
        "DATE", "TIME"
    };

    /// <summary>Removes a trailing QBasic type sigil (<c>$ % &amp; ! #</c>) from a name.</summary>
    private static string TrimSigil(string name) =>
        name.Length > 0 && name[^1] is '$' or '%' or '&' or '!' or '#' ? name[..^1] : name;

    /// <summary>
    /// Post-write feedback for the tool loop: <see cref="DescribeIssues"/> wrapped in a short
    /// instruction and ready to append to the "file saved" summary the LLM gets back. Returns
    /// <c>null</c> when <paramref name="filename"/> is not a QBasic source file or nothing was
    /// found.
    /// <para>
    /// This is what makes the check useful with no compiler installed. <see cref="Qb64ToolService"/>
    /// runs the same pass before compiling, but it is only reachable when a compiler path is
    /// configured — with the QB64 tool disabled, an agent could (and did) write a .bas file full
    /// of invented keywords and never hear about it for the rest of the run. Running the pass at
    /// write time instead puts the finding in the very next turn either way.
    /// </para>
    /// </summary>
    public static string? DescribeIssuesAfterWrite(string? filename, string? content)
    {
        if (filename is null || !filename.Trim().EndsWith(".bas", StringComparison.OrdinalIgnoreCase))
            return null;

        var issues = DescribeIssues(content ?? string.Empty);
        return issues is null
            ? null
            : $"⚠ Strukturkontroll av {filename.Trim()} hittade problem som QB64 inte kan kompilera — " +
              $"rätta ALLA nedan och spara filen igen:\n{issues}";
    }

    private static bool TopIs(List<BlockFrame> stack, string kind) =>
        stack.Count > 0 && stack[^1].Kind.Equals(kind, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Pops the block stack for a closer that expects a specific opener kind (END IF/LOOP/WEND/
    /// END SUB/END FUNCTION/END SELECT). A missing or mismatched opener is reported, but the frame
    /// is popped regardless (best-effort recovery) so one mistake doesn't cascade into a wall of
    /// further false mismatches for the rest of the file.
    /// </summary>
    private static void PopExpected(List<BlockFrame> stack, string expectedKind, int lineNumber, List<QBasicStructureIssue> issues)
    {
        if (stack.Count == 0)
        {
            issues.Add(new QBasicStructureIssue(lineNumber, $"{CloserFor(expectedKind)} utan någon öppen {expectedKind}-sats."));
            return;
        }

        var top = stack[^1];
        if (!top.Kind.Equals(expectedKind, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new QBasicStructureIssue(lineNumber,
                $"{CloserFor(expectedKind)} matchar inte den öppna {top.Kind}-satsen som startade på rad {top.OpenLine}."));
        }

        stack.RemoveAt(stack.Count - 1);
    }

    /// <summary>
    /// Pops one FOR frame per loop variable named on a NEXT statement (<c>NEXT i, j</c> closes
    /// two loops at once; a bare <c>NEXT</c> closes exactly one). Each pop also checks the loop
    /// variable name against the FOR frame it closes when both are known, catching the "NEXT
    /// closes the wrong (outer) loop" bug pattern seen in real generated code.
    /// </summary>
    private static void PopFor(List<BlockFrame> stack, string segment, int firstTokenLength, int lineNumber, List<QBasicStructureIssue> issues)
    {
        var namedVars = segment[firstTokenLength..]
            .Split(',')
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

        var popsNeeded = Math.Max(namedVars.Count, 1);
        for (var n = 0; n < popsNeeded; n++)
        {
            var expectedVar = n < namedVars.Count ? namedVars[n] : null;

            if (stack.Count == 0)
            {
                issues.Add(new QBasicStructureIssue(lineNumber, "NEXT utan någon öppen FOR-sats."));
                continue;
            }

            var top = stack[^1];
            if (!top.Kind.Equals("FOR", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new QBasicStructureIssue(lineNumber,
                    $"NEXT matchar inte den öppna {top.Kind}-satsen som startade på rad {top.OpenLine}."));
            }
            else if (expectedVar is not null && top.Variable is not null
                     && !expectedVar.Equals(top.Variable, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new QBasicStructureIssue(lineNumber,
                    $"NEXT {expectedVar} stänger troligen fel loop — den öppna FOR-slingan här är \"{top.Variable}\" " +
                    $"från rad {top.OpenLine}. Kontrollera att varje FOR har sin egen matchande NEXT i rätt ordning."));
            }

            stack.RemoveAt(stack.Count - 1);
        }
    }

    private static string CloserFor(string kind) => kind.ToUpperInvariant() switch
    {
        "IF" => "END IF",
        "FOR" => "NEXT",
        "DO" => "LOOP",
        "WHILE" => "WEND",
        "SELECT" => "END SELECT",
        "SUB" => "END SUB",
        "FUNCTION" => "END FUNCTION",
        _ => $"END {kind}"
    };

    /// <summary>
    /// Strips string-literal content and trailing <c>'</c> comments from a physical line (a
    /// leading <c>REM</c> token is treated the same way — see the type-level remarks for the
    /// limits of that heuristic). Positions are not preserved; only line numbers matter to this
    /// linter, and those are tracked per physical line by the caller.
    /// </summary>
    private static string StripStringsAndComments(string line)
    {
        var sb = new System.Text.StringBuilder(line.Length);
        var inString = false;
        foreach (var c in line)
        {
            if (inString)
            {
                if (c == '"') inString = false;
                continue;
            }
            if (c == '"') { inString = true; continue; }
            if (c == '\'') break; // rest of the physical line is a comment
            sb.Append(c);
        }

        var result = sb.ToString();
        var trimmed = result.TrimStart();
        if (trimmed.StartsWith("REM", StringComparison.OrdinalIgnoreCase)
            && (trimmed.Length == 3 || !char.IsLetterOrDigit(trimmed[3])))
            return string.Empty;

        return result;
    }

    private static string Shorten(string text, int maxLength = 60) =>
        text.Length <= maxLength ? text : text[..maxLength] + "…";
}
