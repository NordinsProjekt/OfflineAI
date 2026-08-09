using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using AgentKit.Skills.Files;
using AgentKit.Skills.Utility;

namespace AgentKit.Skills.Qb64;

/// <inheritdoc/>
/// <remarks>
/// The compiler is resolved exclusively from <see cref="Qb64Options.CompilerPath"/> — the LLM
/// only ever supplies a bare .bas filename, which is resolved inside the file agent's base
/// directory (the active workspace) with the same path-traversal protection as
/// <c>FileAgentService</c>. The produced executable is written next to the source file in the
/// workspace and is the only executable this service will run.
/// </remarks>
public sealed class Qb64ToolService : IQb64ToolService
{
    private static readonly StringComparison Cmp = StringComparison.OrdinalIgnoreCase;

    private const string RunCommand = "/qb64";
    private const string CompileOnlyCommand = "/qb64-kompilera";

    /// <summary>
    /// Separates the filename from the keyboard input to feed the program: <c>&lt;&lt;&lt;</c>,
    /// with two angle brackets accepted as well since a model that has seen one shell heredoc
    /// will write both. Unambiguous against the filename in front of it — <c>&lt;</c> is not a
    /// legal character in a Windows filename.
    /// </summary>
    private static readonly Regex StandardInputSeparatorRegex = new("<{2,3}", RegexOptions.Compiled);

    /// <summary>Separates one input line from the next inside the <c>&lt;&lt;&lt;</c> payload.</summary>
    private const char InputLineSeparator = '|';

    /// <summary>The separator spelling taught to the LLM (parsing also accepts <c>&lt;&lt;</c>).</summary>
    private const string InputSeparatorExample = "<<<";

    /// <summary>The input part of the <c>/qb64</c> signature shown in the tool list.</summary>
    private const string InputSyntaxHint = "<<< <rad1>|<rad2>";

    private readonly Qb64Options _options;
    private readonly IFileAgentService _fileAgent;

    /// <param name="options">Compiler path, argument template, timeouts, and output cap.</param>
    /// <param name="fileAgent">
    /// Supplies the base directory (active workspace) in which .bas files are resolved, so the
    /// QB64 tool always works on the same files the LLM creates with /skapa and /fyll.
    /// </param>
    public Qb64ToolService(Qb64Options options, IFileAgentService fileAgent)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _fileAgent = fileAgent ?? throw new ArgumentNullException(nameof(fileAgent));
    }

    private Qb64Options Settings => _options;

    private bool IsConfigured => !string.IsNullOrWhiteSpace(Settings.CompilerPath);

    /// <inheritdoc/>
    public bool IsCommand(string input)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(input)) return false;
        var t = input.TrimStart();
        return t.Equals(RunCommand, Cmp) || t.StartsWith(RunCommand + " ", Cmp)
            || t.Equals(CompileOnlyCommand, Cmp) || t.StartsWith(CompileOnlyCommand + " ", Cmp);
    }

    /// <inheritdoc/>
    public bool TryFindCommand(string llmResponse, out string command)
    {
        command = string.Empty;
        if (string.IsNullOrWhiteSpace(llmResponse)) return false;

        foreach (var rawLine in llmResponse.Split('\n'))
        {
            var line = rawLine.Trim().TrimEnd('\r');
            if (IsCommand(line))
            {
                command = line;
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> GetToolDescriptions()
    {
        if (!IsConfigured)
            return new Dictionary<string, string>();

        return new Dictionary<string, string>
        {
            [$"{RunCommand} <fil.bas> [{InputSyntaxHint}]"] =
                "Kompilerar en QBasic-fil (QB64) från agentkatalogen och kör det färdiga programmet. " +
                "Programmets textutdata skickas tillbaka till dig, så du kan kontrollera resultatet och " +
                "rätta koden om något är fel. VIKTIGT: skriv $CONSOLE:ONLY som första rad i .bas-filen så " +
                "att PRINT-utdata kan fångas. Programmet körs utan människa vid tangentbordet: behöver det " +
                $"inmatning skickar du den själv efter {InputSeparatorExample}, en rad per INPUT, åtskilda med " +
                $"{InputLineSeparator} (t.ex. \"{RunCommand} kalkyl.bas {InputSeparatorExample} 5{InputLineSeparator}7{InputLineSeparator}avsluta\"). " +
                $"Utan {InputSeparatorExample} ser programmet filslut direkt, och ett program som väntar på fler rader " +
                "än du skickat avbryts efter en timeout. Grafikprogram (SCREEN utan $CONSOLE:ONLY) kompileras " +
                "men körs inte — grafikfönstret finns ingen som kan se.",
            [$"{CompileOnlyCommand} <fil.bas>"] =
                "Kompilerar en QBasic-fil (QB64) från agentkatalogen utan att köra den — använd för att " +
                "syntaxkontrollera t.ex. grafiska program eller spel som inte kan köras utan användare. " +
                "Eventuella kompileringsfel skickas tillbaka till dig så att du kan rätta koden."
        };
    }

    /// <inheritdoc/>
    public async Task<UtilityToolResult> ExecuteAsync(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return UtilityToolResult.Failure("Tomt kommando.");

        var trimmed = input.Trim();
        bool compileOnly;
        string argument;

        if (trimmed.Equals(CompileOnlyCommand, Cmp) || trimmed.StartsWith(CompileOnlyCommand + " ", Cmp))
        {
            compileOnly = true;
            argument = trimmed.Length > CompileOnlyCommand.Length ? trimmed[CompileOnlyCommand.Length..].Trim() : string.Empty;
        }
        else if (trimmed.Equals(RunCommand, Cmp) || trimmed.StartsWith(RunCommand + " ", Cmp))
        {
            compileOnly = false;
            argument = trimmed.Length > RunCommand.Length ? trimmed[RunCommand.Length..].Trim() : string.Empty;
        }
        else
        {
            return UtilityToolResult.Failure($"Okänt QB64-kommando: \"{trimmed}\".");
        }

        var command = compileOnly ? CompileOnlyCommand : RunCommand;

        if (!IsConfigured)
            return UtilityToolResult.Failure(
                "⚠ Ingen QB64-kompilator är konfigurerad (AppConfiguration:AgentTools:Qb64:CompilerPath).");

        if (!File.Exists(Settings.CompilerPath))
            return UtilityToolResult.Failure(
                $"⚠ QB64-kompilatorn hittades inte på den konfigurerade sökvägen: {Settings.CompilerPath}");

        var (fileArgument, inputLines) = SplitStandardInput(argument);

        if (compileOnly && inputLines is not null)
            return UtilityToolResult.Failure(
                $"⚠ Inmatning ({InputSeparatorExample}) fungerar bara med {RunCommand}, som kör programmet — " +
                $"{CompileOnlyCommand} kompilerar bara filen.");

        if (string.IsNullOrWhiteSpace(fileArgument))
            return UtilityToolResult.Failure(
                $"Ange en .bas-fil, t.ex. \"{command} spel.bas\". Använd /lista för att se filerna i agentkatalogen.");

        var sourcePath = ResolveSourcePath(fileArgument, out var error);
        if (sourcePath is null)
            return UtilityToolResult.Failure(error!);

        // Read the source once and use it for both static checks below — the file is small and
        // this keeps the compile path down to a single read.
        var source = await ReadSourceOrNullAsync(sourcePath);

        // Cheap, instant structural pre-check before spending a slow compiler invocation. Unlike
        // the real compiler — which stops at its first error — this reports every structural
        // problem it can find in one pass, so the LLM doesn't burn one iteration per latent bug.
        var structuralIssues = source is null ? null : QBasicStructureLinter.DescribeIssues(source);
        if (structuralIssues is not null)
            return UtilityToolResult.Failure(
                $"⚠ Strukturkontroll (innan kompilering) hittade problem i {Path.GetFileName(sourcePath)} — " +
                $"rätta ALLA nedan och försök igen:\n{structuralIssues}");

        // A program that switches to a graphics screen draws into a window nobody can see and
        // waits at END for a key nobody can press, so running it can only end in the timeout.
        // Detected here so the run is skipped rather than paid for — see QBasicProgramKind.
        var graphicsScreen = source is not null && !QBasicProgramKind.IsConsoleOnly(source)
            ? QBasicProgramKind.FindGraphicsScreen(source)
            : null;

        var outputPath = Path.ChangeExtension(sourcePath, ".exe");

        // Remove any stale executable from a previous compile so a failed compile can never be
        // followed by silently running an old binary.
        try
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
        catch (IOException)
        {
            return UtilityToolResult.Failure(
                $"⚠ Kunde inte ersätta {Path.GetFileName(outputPath)} — programmet verkar fortfarande köras. Försök igen om en stund.");
        }

        var compileResult = await CompileAsync(sourcePath, outputPath);
        if (compileResult is not null)
            return compileResult; // compile failed — feed the compiler errors back to the LLM

        if (compileOnly)
            return UtilityToolResult.Success(
                $"✓ QB64: {Path.GetFileName(sourcePath)} kompilerade utan fel.",
                $"Kompileringen av {Path.GetFileName(sourcePath)} lyckades utan fel. Programmet kördes inte ({command}).");

        if (graphicsScreen is not null)
            return DescribeSkippedGraphicsRun(sourcePath, graphicsScreen);

        return await RunProgramAsync(sourcePath, outputPath, inputLines);
    }

    /// <summary>
    /// Splits an argument of the form <c>fil.bas &lt;&lt;&lt; rad1|rad2</c> into the filename and
    /// the lines to feed the program's stdin. The line list is <c>null</c> when no separator was
    /// present (the program then sees end-of-file immediately, as it always has); an empty
    /// payload means a single empty line, i.e. a bare Enter — the answer a program waiting on
    /// <c>INPUT "Tryck retur"; a$</c> needs.
    /// </summary>
    private static (string FileArgument, IReadOnlyList<string>? InputLines) SplitStandardInput(string argument)
    {
        var separator = StandardInputSeparatorRegex.Match(argument);
        if (!separator.Success)
            return (argument.Trim(), null);

        var fileArgument = argument[..separator.Index].Trim();
        var payload = argument[(separator.Index + separator.Length)..].Trim();

        var lines = payload.Length == 0
            ? new[] { string.Empty }
            : payload.Split(InputLineSeparator).Select(line => line.Trim()).ToArray();

        return (fileArgument, lines);
    }

    /// <summary>
    /// Reports a successful compile of a graphics program that was deliberately not run, and
    /// tells the LLM why — so it stops paying a run timeout (and a wasted goal-agent iteration)
    /// for a program that is perfectly correct but cannot be observed in a headless run.
    /// </summary>
    private static UtilityToolResult DescribeSkippedGraphicsRun(string sourcePath, QBasicGraphicsScreen graphicsScreen)
    {
        var sourceName = Path.GetFileName(sourcePath);

        var context = new StringBuilder();
        context.AppendLine($"Kompileringen av {sourceName} lyckades utan fel.");
        context.AppendLine(
            $"Programmet kördes INTE: rad {graphicsScreen.Line} växlar till ett grafikläge " +
            $"(\"{graphicsScreen.Statement}\") och filen saknar $CONSOLE:ONLY, så programmet ritar i ett " +
            "fönster. Här finns ingen som kan se fönstret eller trycka på tangenten QB64 väntar på när " +
            "programmet är klart — en körning skulle bara sluta med timeout.");
        context.AppendLine(
            "Felfri kompilering är därför den kontroll som går att göra på ett grafikprogram: använd " +
            $"{CompileOnlyCommand} direkt nästa gång. Ska programmet lämna ett mätbart resultat, låt det " +
            "skriva till en textfil i arbetsytan och läs filen med /läs.");

        return UtilityToolResult.Success(
            $"✓ QB64: {sourceName} kompilerade utan fel (grafikprogram — kördes inte).",
            context.ToString());
    }

    /// <summary>
    /// Resolves the LLM-supplied filename to a full path inside the file agent's base directory
    /// (the active workspace). Only bare filenames are honoured — any directory component is
    /// stripped — and a missing extension defaults to .bas. Returns null with an LLM-facing
    /// error message when the name is invalid, has the wrong extension, or the file is missing.
    /// </summary>
    private string? ResolveSourcePath(string argument, out string? error)
    {
        error = null;

        // Strip any directory component — only bare filenames are allowed (same confinement
        // rule as FileAgentService.GetSafePath).
        var safeName = Path.GetFileName(argument.Trim().Trim('"'));
        if (string.IsNullOrWhiteSpace(safeName) || safeName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            error = $"Ogiltigt filnamn: \"{argument}\".";
            return null;
        }

        if (!Path.HasExtension(safeName))
            safeName += ".bas";
        else if (!Path.GetExtension(safeName).Equals(".bas", Cmp))
        {
            error = $"⚠ \"{safeName}\" är ingen .bas-fil — QB64-verktyget kompilerar bara QBasic-källfiler (.bas).";
            return null;
        }

        var baseDirectory = Path.GetFullPath(_fileAgent.BaseDirectory);
        var fullPath = Path.GetFullPath(Path.Combine(baseDirectory, safeName));

        var baseWithSeparator = Path.TrimEndingDirectorySeparator(baseDirectory) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(baseWithSeparator, Cmp))
        {
            error = $"Ogiltigt filnamn: \"{argument}\".";
            return null;
        }

        if (!File.Exists(fullPath))
        {
            error = $"⚠ Filen \"{safeName}\" finns inte i agentkatalogen. " +
                    "Skapa den först (t.ex. med /fyll) eller kontrollera namnet med /lista.";
            return null;
        }

        return fullPath;
    }

    /// <summary>
    /// Reads the source file for the static pre-checks, or returns <c>null</c> when it cannot be
    /// read — in which case every check that needs the text is skipped and the real compiler gets
    /// to report the problem.
    /// </summary>
    private static async Task<string?> ReadSourceOrNullAsync(string sourcePath)
    {
        try
        {
            return await File.ReadAllTextAsync(sourcePath);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Runs the configured compiler on <paramref name="sourcePath"/>. Returns <c>null</c> when
    /// compilation succeeds and produced <paramref name="outputPath"/>; otherwise a failure
    /// result carrying the compiler's output (tail-truncated — QB64 prints the actual error at
    /// the end of its progress log) so the LLM can fix the code and retry.
    /// </summary>
    private async Task<UtilityToolResult?> CompileAsync(string sourcePath, string outputPath)
    {
        var arguments = Settings.CompilerArguments
            .Replace("{source}", sourcePath, Cmp)
            .Replace("{output}", outputPath, Cmp);

        // QB64 must run with its own installation directory as working directory so it can find
        // its bundled C++ backend (internal\c\c_compiler).
        var compilerDir = Path.GetDirectoryName(Settings.CompilerPath) ?? string.Empty;

        var timeoutMs = Settings.CompileTimeoutMs > 0 ? Settings.CompileTimeoutMs : 180_000;
        ProcessRunResult run;
        try
        {
            run = await RunProcessAsync(Settings.CompilerPath, arguments, compilerDir, timeoutMs);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return UtilityToolResult.Failure(
                $"⚠ Kunde inte starta QB64-kompilatorn ({Settings.CompilerPath}): {ex.Message}");
        }

        if (run.TimedOut)
            return UtilityToolResult.Failure(
                $"⚠ QB64-kompileringen tog för lång tid (timeout efter {timeoutMs} ms) och avbröts.");

        if (run.ExitCode != 0 || !File.Exists(outputPath))
        {
            var detail = TruncateKeepingTail(run.CombinedOutput, MaxOutputLength);
            return UtilityToolResult.Failure(
                $"⚠ QB64 kunde inte kompilera {Path.GetFileName(sourcePath)}." +
                (detail.Length > 0 ? $" Kompilatorns utdata:\n{detail}" : $" (felkod {run.ExitCode}, ingen utdata)"));
        }

        return null;
    }

    /// <summary>
    /// Runs the compiled program with the workspace as working directory and returns its console
    /// output as LLM-facing context. <paramref name="inputLines"/> is typed on the program's
    /// stdin before it is closed, so a program built around <c>INPUT</c> can be exercised for
    /// real. A program that does not terminate within the run timeout is killed, but any output
    /// captured up to that point is still returned — that partial output is often exactly what
    /// the LLM needs to diagnose an infinite loop or an unanswered INPUT.
    /// </summary>
    private async Task<UtilityToolResult> RunProgramAsync(
        string sourcePath, string outputPath, IReadOnlyList<string>? inputLines)
    {
        var sourceName = Path.GetFileName(sourcePath);
        var timeoutMs = Settings.RunTimeoutMs > 0 ? Settings.RunTimeoutMs : 30_000;

        ProcessRunResult run;
        try
        {
            run = await RunProcessAsync(
                outputPath,
                arguments: string.Empty,
                workingDirectory: Path.GetDirectoryName(sourcePath) ?? string.Empty,
                timeoutMs,
                standardInputLines: inputLines ?? Array.Empty<string>());
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return UtilityToolResult.Failure(
                $"⚠ Kunde inte starta det kompilerade programmet ({Path.GetFileName(outputPath)}): {ex.Message}");
        }

        var output = TruncateKeepingHead(run.CombinedOutput, MaxOutputLength);
        var sentInput = DescribeSentInput(inputLines);

        if (run.TimedOut)
        {
            var context = new StringBuilder();
            context.AppendLine(
                $"Programmet {sourceName} kompilerade, men avslutades inte inom {timeoutMs} ms och avbröts. " +
                "Troliga orsaker: programmet väntar på inmatning (INPUT/SLEEP/tangenttryckning) eller " +
                "innehåller en oändlig loop. Kom ihåg $CONSOLE:ONLY överst i filen och låt programmet " +
                "avslutas av sig självt.");
            if (inputLines is not null)
                context.AppendLine(
                    $"{sentInput} Väntar programmet på fler rader än så, skicka dem också — " +
                    $"annars står det kvar vid sin INPUT tills timeouten slår till.");
            else
                context.AppendLine(
                    $"Behöver programmet inmatning kan du skicka den: {RunCommand} {sourceName} {InputSeparatorExample} <rad1>{InputLineSeparator}<rad2>");
            context.AppendLine();
            context.Append(output.Length > 0 ? $"Utdata fram till avbrottet:\n{output}" : "(ingen utdata fångades)");
            return UtilityToolResult.Failure(
                $"⚠ QB64: {sourceName} kompilerade men programmet avbröts efter timeout.\n{context}");
        }

        var resultContext = new StringBuilder();
        resultContext.AppendLine($"Programmet {sourceName} kompilerade och kördes (avslutningskod {run.ExitCode}).");
        if (inputLines is not null)
            resultContext.AppendLine(sentInput);
        resultContext.AppendLine("Programmets utdata:");
        if (output.Length > 0)
            resultContext.Append(output);
        else
            resultContext.Append(
                "(ingen utdata — om programmet borde ha skrivit något, kontrollera att $CONSOLE:ONLY står som första rad i .bas-filen)");

        if (run.ExitCode != 0)
            return UtilityToolResult.Failure(
                $"⚠ QB64: {sourceName} kördes men avslutades med felkod {run.ExitCode}.\n{resultContext}");

        return UtilityToolResult.Success(
            $"✓ QB64: {sourceName} kompilerade och kördes.",
            resultContext.ToString());
    }

    private int MaxOutputLength => Settings.MaxOutputLength > 0 ? Settings.MaxOutputLength : 4000;

    /// <summary>
    /// Spells out the keyboard input that was typed into the program, so the LLM can read its
    /// output against the answers it gave rather than having to remember them. Empty when no
    /// input was supplied.
    /// </summary>
    private static string DescribeSentInput(IReadOnlyList<string>? inputLines)
    {
        if (inputLines is null)
            return string.Empty;

        var quoted = string.Join(" ⏎ ", inputLines.Select(line => $"\"{line}\""));
        return $"Inmatning som skickades till programmet: {quoted} " +
               $"({inputLines.Count} rad(er), därefter filslut).";
    }

    /// <summary>Outcome of one captured process run. Streams are fully drained even after a timeout kill.</summary>
    private sealed record ProcessRunResult(int ExitCode, string Stdout, string Stderr, bool TimedOut)
    {
        public string CombinedOutput
        {
            get
            {
                if (Stderr.Length == 0) return Stdout;
                if (Stdout.Length == 0) return Stderr;
                return $"{Stdout}\n{Stderr}";
            }
        }
    }

    /// <summary>
    /// Starts a process with redirected output and waits for exit with a timeout, killing the
    /// entire process tree when exceeded. When <paramref name="standardInputLines"/> is non-null
    /// the child's stdin is redirected, those lines are typed into it and it is then closed — an
    /// empty list therefore means "end-of-file straight away", so console programs that read
    /// input never wait forever on a keyboard no one is typing at. <c>null</c> leaves stdin
    /// alone (used for the compiler, which reads none).
    /// </summary>
    private static async Task<ProcessRunResult> RunProcessAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        int timeoutMs,
        IReadOnlyList<string>? standardInputLines = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName               = fileName,
            Arguments              = arguments,
            WorkingDirectory       = workingDirectory,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            RedirectStandardInput  = standardInputLines is not null,
            CreateNoWindow         = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding  = Encoding.UTF8
        };

        using var cts = new CancellationTokenSource(timeoutMs);
        using var process = new Process { StartInfo = psi };
        process.Start();

        // Read both streams concurrently while waiting, so a process that fills the stderr pipe
        // buffer can't deadlock against our stdout read (same pattern as ExternalToolsService).
        // Started before anything is written to stdin for the same reason: a program that is
        // blocked writing output must not be able to block our write.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (standardInputLines is not null)
            await WriteStandardInputAsync(process, standardInputLines, cts.Token);

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
            try { process.Kill(entireProcessTree: true); } catch { /* already exited */ }
        }

        // Killing the process closes its ends of the pipes, so these complete even after a timeout.
        var stdout = (await stdoutTask).Trim();
        var stderr = (await stderrTask).Trim();
        var exitCode = timedOut ? -1 : process.ExitCode;

        return new ProcessRunResult(exitCode, stdout, stderr, timedOut);
    }

    /// <summary>
    /// Types the given lines into the program's stdin and closes it, so whatever it reads next
    /// hits end-of-file instead of blocking. Never throws: a program that exits before reading
    /// (broken pipe) or never drains its input at all (write cancelled by the run timeout) is a
    /// normal outcome here, and the timeout handling in the caller already covers it.
    /// </summary>
    private static async Task WriteStandardInputAsync(
        Process process, IReadOnlyList<string> lines, CancellationToken cancellationToken)
    {
        try
        {
            var payload = lines.Count == 0
                ? string.Empty
                : string.Concat(lines.Select(line => line + Environment.NewLine));

            if (payload.Length > 0)
                await process.StandardInput.WriteAsync(payload.AsMemory(), cancellationToken);

            process.StandardInput.Close();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // Program already gone, or too busy to read — either way the run result speaks for itself.
        }
    }

    /// <summary>Keeps the start of the text (program output — the beginning shows what happened first).</summary>
    private static string TruncateKeepingHead(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "\n...[trunkerat]";

    /// <summary>Keeps the end of the text (compiler output — QB64 prints the actual error last).</summary>
    private static string TruncateKeepingTail(string text, int maxLength) =>
        text.Length <= maxLength ? text : "[trunkerat]...\n" + text[^maxLength..];
}
