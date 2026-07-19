using System.Diagnostics;
using System.Text;
using AgentKit.Skills.Utility;

namespace AgentKit.Skills.External;

/// <inheritdoc/>
/// <remarks>
/// Tools are resolved exclusively from the configured <see cref="ExternalToolOptions"/> list —
/// the LLM selects a configured tool by its command name and can never supply a path, so this
/// service can only ever start executables the host has explicitly whitelisted in its
/// configuration. The LLM's argument text is passed to the process as-is (after any configured
/// fixed arguments) with no shell involved, so no shell-metacharacter expansion can occur.
/// </remarks>
public sealed class ExternalToolsService : IExternalToolsService
{
    private static readonly StringComparison Cmp = StringComparison.OrdinalIgnoreCase;

    private readonly IReadOnlyList<ExternalToolOptions> _tools;

    /// <param name="tools">The whitelisted external tools the LLM may run by slash command.</param>
    public ExternalToolsService(IReadOnlyList<ExternalToolOptions> tools)
    {
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
    }

    private IEnumerable<ExternalToolOptions> ConfiguredTools =>
        _tools.Where(t =>
            !string.IsNullOrWhiteSpace(t.Command) && !string.IsNullOrWhiteSpace(t.ExecutablePath));

    /// <summary>Normalizes a configured command name to its slash form ("/väder").</summary>
    private static string ToSlashCommand(string command) =>
        "/" + command.TrimStart('/').Trim();

    /// <inheritdoc/>
    public bool IsCommand(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;
        var t = input.TrimStart();

        return ConfiguredTools.Any(tool =>
        {
            var slash = ToSlashCommand(tool.Command);
            return t.Equals(slash, Cmp) || t.StartsWith(slash + " ", Cmp);
        });
    }

    /// <inheritdoc/>
    public async Task<UtilityToolResult> ExecuteAsync(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return UtilityToolResult.Failure("Tomt kommando.");

        var trimmed = input.Trim();
        var tool = FindTool(trimmed, out var arguments);
        if (tool is null)
            return UtilityToolResult.Failure($"Okänt verktygskommando: \"{trimmed}\".");

        if (!File.Exists(tool.ExecutablePath))
            return UtilityToolResult.Failure(
                $"⚠ Verktyget \"{ToSlashCommand(tool.Command)}\" pekar på en fil som inte finns: {tool.ExecutablePath}");

        return await RunProcessAsync(tool, arguments);
    }

    /// <summary>
    /// Matches the input line against the configured tools and splits off the LLM-supplied
    /// argument text (everything after the command name).
    /// </summary>
    private ExternalToolOptions? FindTool(string trimmedInput, out string arguments)
    {
        foreach (var tool in ConfiguredTools)
        {
            var slash = ToSlashCommand(tool.Command);
            if (trimmedInput.Equals(slash, Cmp))
            {
                arguments = string.Empty;
                return tool;
            }
            if (trimmedInput.StartsWith(slash + " ", Cmp))
            {
                arguments = trimmedInput[(slash.Length + 1)..].Trim();
                return tool;
            }
        }

        arguments = string.Empty;
        return null;
    }

    private static async Task<UtilityToolResult> RunProcessAsync(ExternalToolOptions tool, string llmArguments)
    {
        var slash = ToSlashCommand(tool.Command);
        var allArguments = string.Join(' ',
            new[] { tool.FixedArguments, llmArguments }.Where(a => !string.IsNullOrWhiteSpace(a)));

        var psi = new ProcessStartInfo
        {
            FileName               = tool.ExecutablePath,
            Arguments              = allArguments,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding  = Encoding.UTF8
        };

        try
        {
            using var process = new Process { StartInfo = psi };
            process.Start();

            // Read both streams concurrently while waiting, so a tool that fills the stderr
            // pipe buffer can't deadlock against our stdout read.
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            var timeoutMs = tool.TimeoutMs > 0 ? tool.TimeoutMs : 30_000;
            using var cts = new CancellationTokenSource(timeoutMs);
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* already exited */ }
                return UtilityToolResult.Failure(
                    $"⚠ Verktyget \"{slash}\" tog för lång tid (timeout efter {timeoutMs} ms) och avbröts.");
            }

            var stdout = (await stdoutTask).Trim();
            var stderr = (await stderrTask).Trim();

            var maxLength = tool.MaxOutputLength > 0 ? tool.MaxOutputLength : 4000;
            if (stdout.Length > maxLength)
                stdout = stdout[..maxLength] + "\n...[trunkerat]";

            if (process.ExitCode != 0)
            {
                var detail = stderr.Length > 0 ? stderr : stdout;
                if (detail.Length > maxLength)
                    detail = detail[..maxLength] + "\n...[trunkerat]";
                return UtilityToolResult.Failure(
                    $"⚠ Verktyget \"{slash}\" avslutades med felkod {process.ExitCode}." +
                    (detail.Length > 0 ? $" Utdata: {detail}" : string.Empty));
            }

            var context = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(llmArguments))
            {
                context.AppendLine($"Anrop: {slash} {llmArguments}");
                context.AppendLine();
            }
            context.AppendLine($"Utdata från verktyget \"{slash}\":");
            context.Append(stdout.Length > 0 ? stdout : "(ingen utdata)");

            return UtilityToolResult.Success($"✓ Verktyg kört: {slash}", context.ToString());
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return UtilityToolResult.Failure(
                $"⚠ Kunde inte starta verktyget \"{slash}\" ({tool.ExecutablePath}): {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> GetToolDescriptions()
    {
        var descriptions = new Dictionary<string, string>();
        foreach (var tool in ConfiguredTools)
        {
            var signature = ToSlashCommand(tool.Command);
            if (!string.IsNullOrWhiteSpace(tool.Usage))
                signature += " " + tool.Usage.Trim();

            var description = string.IsNullOrWhiteSpace(tool.Description)
                ? "Externt verktyg (ingen beskrivning konfigurerad)."
                : tool.Description.Trim();

            descriptions[signature] = description;
        }
        return descriptions;
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
}
