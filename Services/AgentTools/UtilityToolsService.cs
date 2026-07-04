using System.Net.Http.Headers;
using System.Text;
using Services.Configuration;

namespace Services.AgentTools;

/// <inheritdoc/>
/// <remarks>
/// Endpoints are resolved exclusively from <see cref="AppConfiguration.AgentTools"/> — the LLM
/// selects a configured endpoint by name and can never supply an arbitrary URL, so outbound
/// calls made by this service are limited to destinations the user has explicitly configured
/// in <c>appsettings.json</c> (or user secrets).
/// </remarks>
public sealed class UtilityToolsService : IUtilityToolsService
{
    private static readonly StringComparison Cmp = StringComparison.OrdinalIgnoreCase;

    private const string TimeCommand = "/tid";
    private const string DateCommand = "/datum";
    private const string ApiCommand = "/api ";

    private readonly AppConfiguration _appConfig;
    private readonly IHttpClientFactory _httpClientFactory;

    public UtilityToolsService(AppConfiguration appConfig, IHttpClientFactory httpClientFactory)
    {
        _appConfig = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    /// <inheritdoc/>
    public bool IsCommand(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;
        var t = input.TrimStart();
        return t.Equals(TimeCommand, Cmp)
            || t.Equals(DateCommand, Cmp)
            || t.StartsWith(ApiCommand, Cmp);
    }

    /// <inheritdoc/>
    public Task<UtilityToolResult> ExecuteAsync(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Task.FromResult(UtilityToolResult.Failure("Tomt kommando."));

        var trimmed = input.Trim();

        if (trimmed.Equals(TimeCommand, Cmp))
            return Task.FromResult(GetCurrentTime());

        if (trimmed.Equals(DateCommand, Cmp))
            return Task.FromResult(GetCurrentDate());

        if (trimmed.StartsWith(ApiCommand, Cmp))
            return ExecuteApiCommandAsync(trimmed[ApiCommand.Length..].Trim());

        return Task.FromResult(UtilityToolResult.Failure("Okänt kommando."));
    }

    // ── /tid ────────────────────────────────────────────────────────────────

    private static UtilityToolResult GetCurrentTime()
    {
        var now = DateTime.Now;
        var text = $"Klockan är nu {now:HH:mm:ss}.";
        return UtilityToolResult.Success($"✓ {text}", text);
    }

    // ── /datum ──────────────────────────────────────────────────────────────

    private static UtilityToolResult GetCurrentDate()
    {
        var today = DateTime.Now;
        var text = $"Dagens datum är {today:yyyy-MM-dd} ({today:dddd}).";
        return UtilityToolResult.Success($"✓ {text}", text);
    }

    // ── /api ────────────────────────────────────────────────────────────────

    private Task<UtilityToolResult> ExecuteApiCommandAsync(string args)
    {
        var spaceIdx = args.IndexOf(' ');
        var endpointName = spaceIdx < 0 ? args : args[..spaceIdx].Trim();
        var instruction = spaceIdx < 0 ? string.Empty : args[(spaceIdx + 1)..].Trim();

        if (string.IsNullOrWhiteSpace(endpointName))
            return Task.FromResult(UtilityToolResult.Failure(
                "Ange en slutpunkt och en instruktion. Exempel: /api väder Hur är vädret i Stockholm?"));

        return CallNamedApiAsync(endpointName, instruction);
    }

    /// <inheritdoc/>
    public async Task<UtilityToolResult> CallNamedApiAsync(string endpointName, string instruction = "")
    {
        if (string.IsNullOrWhiteSpace(endpointName))
            return UtilityToolResult.Failure("Ange namnet på en konfigurerad slutpunkt.");

        var endpoint = _appConfig.AgentTools.Endpoints
            .FirstOrDefault(e => string.Equals(e.Name, endpointName, Cmp));

        if (endpoint is null)
        {
            var available = _appConfig.AgentTools.Endpoints.Count == 0
                ? "Inga slutpunkter är konfigurerade."
                : "Tillgängliga slutpunkter: " + string.Join(", ", _appConfig.AgentTools.Endpoints.Select(e => e.Name));
            return UtilityToolResult.Failure($"Okänd slutpunkt: \"{endpointName}\". {available}");
        }

        if (string.IsNullOrWhiteSpace(endpoint.Url))
            return UtilityToolResult.Failure($"Slutpunkten \"{endpointName}\" saknar en konfigurerad URL.");

        try
        {
            var client = _httpClientFactory.CreateClient("AgentApiTools");
            client.Timeout = TimeSpan.FromMilliseconds(endpoint.TimeoutMs > 0 ? endpoint.TimeoutMs : 15_000);

            var url = endpoint.Url.Replace("{input}", Uri.EscapeDataString(instruction), Cmp);
            var method = string.IsNullOrWhiteSpace(endpoint.Method) ? HttpMethod.Get : new HttpMethod(endpoint.Method.ToUpperInvariant());

            using var request = new HttpRequestMessage(method, url);
            foreach (var (key, value) in endpoint.Headers)
                request.Headers.TryAddWithoutValidation(key, value);

            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return UtilityToolResult.Failure(
                    $"⚠ Anrop till \"{endpointName}\" misslyckades: HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            var maxLength = endpoint.MaxResponseLength > 0 ? endpoint.MaxResponseLength : 4000;
            if (body.Length > maxLength)
                body = body[..maxLength] + "\n...[trunkerat]";

            var context = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(instruction))
            {
                context.AppendLine($"Instruktion: {instruction}");
                context.AppendLine();
            }
            context.AppendLine($"Svar från API \"{endpointName}\":");
            context.Append(body);

            return UtilityToolResult.Success($"✓ API anropat: {endpointName}", context.ToString());
        }
        catch (TaskCanceledException)
        {
            return UtilityToolResult.Failure($"⚠ Anrop till \"{endpointName}\" tog för lång tid (timeout).");
        }
        catch (HttpRequestException ex)
        {
            return UtilityToolResult.Failure($"⚠ Nätverksfel vid anrop till \"{endpointName}\": {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> GetApiEndpointNames() =>
        _appConfig.AgentTools.Endpoints.Select(e => e.Name).ToList();

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> GetToolDescriptions()
    {
        var descriptions = new Dictionary<string, string>
        {
            ["/tid"] = "Returnerar den aktuella klockslaget.",
            ["/datum"] = "Returnerar dagens datum."
        };

        if (_appConfig.AgentTools.Endpoints.Count > 0)
        {
            var names = string.Join(", ", _appConfig.AgentTools.Endpoints.Select(e => e.Name));
            descriptions["/api <slutpunkt> <instruktion>"] =
                $"Anropar en fördefinierad API-slutpunkt och skickar svaret tillsammans med instruktionen till dig. Tillgängliga slutpunkter: {names}.";
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
