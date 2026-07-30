using System.Diagnostics;
using System.Text;
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
            [$"{RunCommand} <fil.bas>"] =
                "Kompilerar en QBasic-fil (QB64) från agentkatalogen och kör det färdiga programmet. " +
                "Programmets textutdata skickas tillbaka till dig, så du kan kontrollera resultatet och " +
                "rätta koden om något är fel. VIKTIGT: programmet körs utan människa vid tangentbordet — " +
                "skriv $CONSOLE:ONLY som första rad i .bas-filen så att PRINT-utdata kan fångas, och " +
                "undvik INPUT, SLEEP utan argument och andra kommandon som väntar på en användare " +
                "(programmet avbryts annars efter en timeout).",
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

        if (string.IsNullOrWhiteSpace(argument))
            return UtilityToolResult.Failure(
                $"Ange en .bas-fil, t.ex. \"{command} spel.bas\". Använd /lista för att se filerna i agentkatalogen.");

        var sourcePath = ResolveSourcePath(argument, out var error);
        if (sourcePath is null)
            return UtilityToolResult.Failure(error!);

        // Cheap, instant structural pre-check before spending a slow compiler invocation. Unlike
        // the real compiler — which stops at its first error — this reports every structural
        // problem it can find in one pass, so the LLM doesn't burn one iteration per latent bug.
        var structuralIssues = await DescribeStructuralIssuesAsync(sourcePath);
        if (structuralIssues is not null)
            return UtilityToolResult.Failure(
                $"⚠ Strukturkontroll (innan kompilering) hittade problem i {Path.GetFileName(sourcePath)} — " +
                $"rätta ALLA nedan och försök igen:\n{structuralIssues}");

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

        return await RunProgramAsync(sourcePath, outputPath);
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
    /// Runs <see cref="QBasicStructureLinter"/> over the source file and returns its formatted
    /// issue list, or <c>null</c> when the file is unreadable (left for the real compiler to
    /// report) or has no detected structural issues.
    /// </summary>
    private static async Task<string?> DescribeStructuralIssuesAsync(string sourcePath)
    {
        string source;
        try
        {
            source = await File.ReadAllTextAsync(sourcePath);
        }
        catch (Exception)
        {
            return null;
        }

        return QBasicStructureLinter.DescribeIssues(source);
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
    /// output as LLM-facing context. A program that does not terminate within the run timeout is
    /// killed, but any output captured up to that point is still returned — that partial output
    /// is often exactly what the LLM needs to diagnose an infinite loop or a blocking INPUT.
    /// </summary>
    private async Task<UtilityToolResult> RunProgramAsync(string sourcePath, string outputPath)
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
                closeStandardInput: true);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return UtilityToolResult.Failure(
                $"⚠ Kunde inte starta det kompilerade programmet ({Path.GetFileName(outputPath)}): {ex.Message}");
        }

        var output = TruncateKeepingHead(run.CombinedOutput, MaxOutputLength);

        if (run.TimedOut)
        {
            var context = new StringBuilder();
            context.AppendLine(
                $"Programmet {sourceName} kompilerade, men avslutades inte inom {timeoutMs} ms och avbröts. " +
                "Troliga orsaker: programmet väntar på inmatning (INPUT/SLEEP/tangenttryckning) eller " +
                "innehåller en oändlig loop. Kom ihåg $CONSOLE:ONLY överst i filen och låt programmet " +
                "avslutas av sig självt.");
            context.AppendLine();
            context.Append(output.Length > 0 ? $"Utdata fram till avbrottet:\n{output}" : "(ingen utdata fångades)");
            return UtilityToolResult.Failure(
                $"⚠ QB64: {sourceName} kompilerade men programmet avbröts efter timeout.\n{context}");
        }

        var resultContext = new StringBuilder();
        resultContext.AppendLine($"Programmet {sourceName} kompilerade och kördes (avslutningskod {run.ExitCode}).");
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
    /// entire process tree when exceeded. With <paramref name="closeStandardInput"/> the child's
    /// stdin is redirected and closed immediately, so console programs that read input see
    /// end-of-file instead of waiting forever on a keyboard no one is typing at.
    /// </summary>
    private static async Task<ProcessRunResult> RunProcessAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        int timeoutMs,
        bool closeStandardInput = false)
    {
        var psi = new ProcessStartInfo
        {
            FileName               = fileName,
            Arguments              = arguments,
            WorkingDirectory       = workingDirectory,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            RedirectStandardInput  = closeStandardInput,
            CreateNoWindow         = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding  = Encoding.UTF8
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        if (closeStandardInput)
            process.StandardInput.Close();

        // Read both streams concurrently while waiting, so a process that fills the stderr pipe
        // buffer can't deadlock against our stdout read (same pattern as ExternalToolsService).
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        var timedOut = false;
        using var cts = new CancellationTokenSource(timeoutMs);
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

    /// <summary>Keeps the start of the text (program output — the beginning shows what happened first).</summary>
    private static string TruncateKeepingHead(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "\n...[trunkerat]";

    /// <summary>Keeps the end of the text (compiler output — QB64 prints the actual error last).</summary>
    private static string TruncateKeepingTail(string text, int maxLength) =>
        text.Length <= maxLength ? text : "[trunkerat]...\n" + text[^maxLength..];
}
