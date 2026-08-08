using AgentKit.Skills.Utility;

namespace AgentKit.Skills.QBasicGraphics;

/// <inheritdoc/>
/// <remarks>
/// Stateless and dependency-free: the whole reference is compiled into
/// <see cref="QBasicGraphicsReference"/>, so a single instance can be shared by every workspace
/// and job (unlike the file agent or the QB64 tool, which are bound to one directory).
/// </remarks>
public sealed class QBasicGraphicsService : IQBasicGraphicsService
{
    private static readonly StringComparison Cmp = StringComparison.OrdinalIgnoreCase;

    private const string Command = "/qbasic-grafik";

    /// <inheritdoc/>
    public bool IsCommand(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;
        var t = input.TrimStart();
        return t.Equals(Command, Cmp) || t.StartsWith(Command + " ", Cmp);
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
    public IReadOnlyDictionary<string, string> GetToolDescriptions() =>
        new Dictionary<string, string>
        {
            [$"{Command} <ämne>"] =
                "Slår upp exakt QBasic/QB64-syntax för grafik INNAN du skriver koden — använd det i " +
                "stället för att gissa hur ett ritkommando stavas eller vilken ordning argumenten har. " +
                "Ämnen: " + string.Join(", ", QBasicGraphicsReference.Topics.Select(topic => topic.Key)) + ". " +
                "Du kan också skriva ett nyckelord (t.ex. \"" + Command + " circle\") eller en fråga; " +
                "utan ämne får du listan över ämnen."
        };

    /// <inheritdoc/>
    public Task<UtilityToolResult> ExecuteAsync(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Task.FromResult(UtilityToolResult.Failure("Tomt kommando."));

        var trimmed = input.Trim();
        if (!trimmed.Equals(Command, Cmp) && !trimmed.StartsWith(Command + " ", Cmp))
            return Task.FromResult(UtilityToolResult.Failure($"Okänt kommando: \"{trimmed}\"."));

        var argument = trimmed.Length > Command.Length ? trimmed[Command.Length..].Trim() : string.Empty;
        var topic = QBasicGraphicsReference.Find(argument);

        // A miss is answered with the index rather than an error: the model asked a reasonable
        // question and an "unknown topic" failure would spend a tool round teaching it nothing.
        if (topic is null)
        {
            var reason = argument.Length == 0
                ? "Inget ämne angavs."
                : $"Hittade inget avsnitt som matchar \"{argument}\".";

            return Task.FromResult(UtilityToolResult.Success(
                $"📘 QBasic-grafik: ämneslista ({(argument.Length == 0 ? "inget ämne angavs" : "okänt ämne")}).",
                $"{reason}\n\n{QBasicGraphicsReference.BuildIndex()}"));
        }

        return Task.FromResult(UtilityToolResult.Success(
            $"📘 QBasic-grafik: {topic.Key}",
            topic.Body));
    }
}
