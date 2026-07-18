using Entities;
using Services.AgentTools;
using Services.FileAgent;
using Services.Repositories;

namespace Services.GoalAgent;

/// <inheritdoc/>
public sealed class GoalAgentService : IGoalAgentService
{
    /// <summary>
    /// Default cap on work → verify iterations so a model that never satisfies a requirement
    /// can't loop forever. Overridable via the constructor — typically from
    /// <c>AppConfiguration.AgentTools.MaxGoalIterations</c>.
    /// </summary>
    private const int DefaultMaxIterations = 20;

    /// <summary>
    /// Name of the per-run transcript file written to the active workspace (the file agent's
    /// base directory) when a file agent is provided: every prompt sent to the LLM, every raw
    /// reply (including the internal tool-call rounds inside <see cref="IAgenticChatService"/>),
    /// each executed tool command, and each verification verdict. The previous run's log is
    /// overwritten when a new run starts.
    /// </summary>
    public const string TranscriptFileName = "agentlogg.txt";

    /// <summary>Markers a generated requirement line may start with (see <see cref="ParseRequirements"/>).</summary>
    private static readonly string[] RequirementMarkers = { "KRAV:", "REQUIREMENT:" };

    /// <summary>
    /// Verdict tokens searched fail-first so an ambiguous reply is never green by accident.
    /// Deliberately stem-shaped (UNDERKÄN matches UNDERKÄNT/UNDERKÄND/UNDERKÄNDA...) with
    /// ASCII fallbacks for models that drop diacritics, plus English forms for models that
    /// answer in English despite the Swedish prompt. Matched with a word-start boundary so e.g.
    /// "surpass" never counts as PASS.
    /// </summary>
    private static readonly string[] FailTokens = { "UNDERKÄN", "UNDERKAN", "INTE UPPFYLL", "EJ UPPFYLL", "FAIL", "REJECT" };
    private static readonly string[] PassTokens = { "GODKÄN", "GODKAN", "PASS", "APPROV" };

    private readonly IAgenticChatService _agenticChat;
    private readonly IFileAgentService? _fileAgent;
    private readonly IAgentRunRepository? _runRepository;
    private readonly int _maxIterations;
    private readonly List<GoalRequirement> _requirements = new();
    private readonly List<string> _activityLog = new();
    private readonly List<AgentRunEventEntity> _pendingEvents = new();
    private readonly object _lock = new();
    private volatile bool _isRunning;
    private volatile bool _stopRequested;
    private string? _transcriptPath;

    /// <summary>
    /// Id of the current run's database row, or null when history persistence is off (no
    /// repository) or has been disabled for this run after a failed write.
    /// </summary>
    private Guid? _runId;

    /// <summary>Ordering counter for this run's events; guarded by <see cref="_lock"/>.</summary>
    private int _eventSequence;

    public event Action? OnChange;

    /// <param name="agenticChat">Runs the tool-calling work/verify steps.</param>
    /// <param name="fileAgent">
    /// Optional. When provided, a full run transcript is written to
    /// <see cref="TranscriptFileName"/> in the file agent's base directory (the active
    /// workspace) so a run can be debugged after the fact. When null, no transcript is written.
    /// </param>
    /// <param name="maxIterations">
    /// Cap on work → verify iterations. Non-positive values fall back to
    /// <see cref="DefaultMaxIterations"/>.
    /// </param>
    /// <param name="runRepository">
    /// Optional. When provided, each run is recorded as history (the run, its requirements, and
    /// its activity log) so finished runs can be reviewed after the app restarts. Persistence is
    /// best-effort: a failing write disables history for the rest of the run rather than
    /// interrupting it. When null, a run leaves no database trace beyond the question/answer
    /// turns its LLM calls produce.
    /// </param>
    public GoalAgentService(
        IAgenticChatService agenticChat,
        IFileAgentService? fileAgent = null,
        int maxIterations = DefaultMaxIterations,
        IAgentRunRepository? runRepository = null)
    {
        _agenticChat = agenticChat ?? throw new ArgumentNullException(nameof(agenticChat));
        _fileAgent = fileAgent;
        _maxIterations = maxIterations > 0 ? maxIterations : DefaultMaxIterations;
        _runRepository = runRepository;
    }

    /// <inheritdoc/>
    public GoalAgentPhase Phase { get; private set; } = GoalAgentPhase.Idle;

    /// <inheritdoc/>
    public bool IsRunning => _isRunning;

    /// <inheritdoc/>
    public string? GoalDescription { get; private set; }

    /// <inheritdoc/>
    // Returns a thread-safe snapshot copy (not a live view) since _requirements is mutated from
    // the background run; IGoalAgentService exposes this as a property, so keep the shape.
#pragma warning disable S2365
    public IReadOnlyList<GoalRequirement> Requirements
    {
        get
        {
            lock (_lock)
            {
                return _requirements.ToList();
            }
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> ActivityLog
    {
        get
        {
            lock (_lock)
            {
                return _activityLog.ToList();
            }
        }
    }
#pragma warning restore S2365

    /// <inheritdoc/>
    public int CurrentIteration { get; private set; }

    /// <inheritdoc/>
    public int MaxIterations => _maxIterations;

    /// <inheritdoc/>
    public void RequestStop() => _stopRequested = true;

    /// <inheritdoc/>
    public void Reset()
    {
        if (_isRunning)
            return;

        lock (_lock)
        {
            _requirements.Clear();
            _activityLog.Clear();
            _pendingEvents.Clear();
        }
        _runId = null;
        GoalDescription = null;
        CurrentIteration = 0;
        Phase = GoalAgentPhase.Idle;
        NotifyChange();
    }

    /// <inheritdoc/>
    public async Task RunAsync(
        string goalDescription,
        Func<string, Task<string>> sendToLlm,
        Action<string>? onToolStatus = null,
        CancellationToken cancellationToken = default,
        string? modelName = null,
        Guid? conversationId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(goalDescription);
        ArgumentNullException.ThrowIfNull(sendToLlm);

        if (_isRunning)
            return;

        _isRunning = true;
        _stopRequested = false;
        lock (_lock)
        {
            _requirements.Clear();
            _activityLog.Clear();
            _pendingEvents.Clear();
            _eventSequence = 0;
        }
        GoalDescription = goalDescription;
        CurrentIteration = 0;

        await StartRunRecordAsync(goalDescription, modelName, conversationId);
        StartTranscript(goalDescription);

        // Every LLM round trip — including the internal tool-call rounds AgenticChatService
        // performs on our behalf — goes through this wrapper so the transcript captures the
        // complete raw conversation. This is the primary debugging aid when a model ignores
        // the KRAV:/RESULTAT: format or never emits a tool command.
        Func<string, Task<string>> loggingSendToLlm = async prompt =>
        {
            Transcript("PROMPT →", prompt);
            var reply = await sendToLlm(prompt);
            Transcript("SVAR ←", reply);
            return reply;
        };

        try
        {
            await GenerateRequirementsAsync(goalDescription, loggingSendToLlm);

            for (var iteration = 1; iteration <= _maxIterations; iteration++)
            {
                CurrentIteration = iteration;

                if (!await WorkOnUnmetRequirementsAsync(goalDescription, loggingSendToLlm, onToolStatus, cancellationToken))
                    return; // stop requested mid-work

                if (!await VerifyAllRequirementsAsync(loggingSendToLlm, onToolStatus, cancellationToken))
                    return; // stop requested mid-verification

                if (Requirements.All(r => r.Status == RequirementStatus.Passed))
                {
                    SetPhase(GoalAgentPhase.Completed, "🎉 Alla krav uppfyllda — målet är uppnått.");
                    return;
                }

                Log($"Iteration {iteration}/{_maxIterations} klar — {CountFailed()} krav kvarstår.");
            }

            SetPhase(GoalAgentPhase.MaxIterationsReached,
                $"⏹ Iterationstaket ({_maxIterations}) nått med {CountFailed()} krav som ännu inte är uppfyllda.");
        }
        catch (OperationCanceledException)
        {
            SetPhase(GoalAgentPhase.Stopped, "⏹ Körningen avbröts.");
        }
        catch (Exception ex)
        {
            SetPhase(GoalAgentPhase.Failed, $"⚠ Körningen misslyckades: {ex.Message}");
        }
        finally
        {
            // Runs on every exit path (including the early returns above), so the run's row always
            // gets its terminal phase — an uncompleted row means the process itself died.
            await CompleteRunRecordAsync();
            _isRunning = false;
            NotifyChange();
        }
    }

    /// <summary>
    /// Phase 1: asks the LLM (plain call, no tools needed) to break the goal down into
    /// "KRAV:" lines and stores the parsed requirements. Falls back to treating the whole
    /// goal description as a single requirement if no line could be parsed, so a model that
    /// ignores the format instruction still yields a runnable (if coarse) requirement.
    /// </summary>
    private async Task GenerateRequirementsAsync(string goalDescription, Func<string, Task<string>> sendToLlm)
    {
        SetPhase(GoalAgentPhase.GeneratingRequirements, "🧭 Genererar kravlista från målbeskrivningen...");
        Transcript("STEG", "Kravgenerering");

        var response = await sendToLlm(BuildRequirementsPrompt(goalDescription));
        var parsed = ParseRequirements(response);

        if (parsed.Count == 0)
        {
            Log("⚠ Kunde inte tolka några \"KRAV:\"-rader ur modellens svar — använder hela målbeskrivningen som ett enda krav.");
            parsed = new[] { goalDescription.Trim() };
        }

        lock (_lock)
        {
            foreach (var description in parsed)
                _requirements.Add(new GoalRequirement(description));
        }

        Log($"📋 {parsed.Count} krav identifierade.");
        Transcript("KRAV", string.Join("\n", parsed.Select((r, i) => $"{i + 1}. {r}")));
        await PersistRequirementsAsync();
        await FlushEventsAsync();
        NotifyChange();
    }

    /// <summary>
    /// Phase 2a: lets the agent do file work (via the tool-calling chat loop) for every
    /// requirement that isn't currently passed. A requirement that failed its last
    /// verification gets the failure motivation included in the prompt — the "failing test
    /// message" that steers the next attempt — except when the verdict was inconclusive
    /// (unparseable review reply), in which case the requirement is skipped entirely: there is
    /// no defect to steer by, and blind edits have wrecked a good file before. Every executed
    /// tool command is surfaced in the
    /// activity log, and a work step that executed no tool at all logs a warning (nothing can
    /// have changed in the workspace). Returns false if a stop was requested.
    /// </summary>
    private async Task<bool> WorkOnUnmetRequirementsAsync(
        string goalDescription,
        Func<string, Task<string>> sendToLlm,
        Action<string>? onToolStatus,
        CancellationToken cancellationToken)
    {
        SetPhase(GoalAgentPhase.Working, $"🔧 Iteration {CurrentIteration}/{_maxIterations}: arbetar med ouppfyllda krav...");

        foreach (var requirement in Requirements.Where(r => r.Status != RequirementStatus.Passed))
        {
            if (_stopRequested)
            {
                SetPhase(GoalAgentPhase.Stopped, "⏹ Stoppad av användaren.");
                return false;
            }

            // An inconclusive verdict (the review reply couldn't be parsed) names no concrete
            // defect, so there is nothing sensible to fix — and blind rework is how a good file
            // gets ruined. Leave the workspace alone; the next verify pass re-checks the
            // requirement (with a retry) and real work resumes once a readable verdict exists.
            if (requirement.VerdictInconclusive)
            {
                Log($"⏭ Ingen åtgärd för \"{Shorten(requirement.Description, 60)}\" — förra granskningen gav inget tolkbart utslag, så det finns ingen konkret brist att åtgärda. Kravet granskas om i nästa kontrollpass.");
                continue;
            }

            requirement.Status = RequirementStatus.Working;
            NotifyChange();
            Transcript("STEG", $"Arbete (iteration {CurrentIteration}): {requirement.Description}");

            var result = await _agenticChat.SendWithToolsAsync(
                BuildWorkPrompt(goalDescription, requirement),
                sendToLlm,
                cancellationToken,
                onToolStatus);

            LogToolInvocations(result,
                noToolsMessage: $"⚠ Inga verktygskommandon kördes under arbetet med kravet \"{Shorten(requirement.Description, 60)}\" — inga filer kan ha ändrats.");

            // New work invalidates the old verdict — the upcoming verification pass decides.
            requirement.Status = RequirementStatus.Unverified;
            await FlushEventsAsync();
            NotifyChange();
        }

        return true;
    }

    /// <summary>
    /// Phase 2b: verifies every requirement (also previously passed ones — later work can
    /// break an earlier requirement, so the whole "suite" runs each iteration) by letting the
    /// LLM inspect the workspace with the read tools and reply with a
    /// RESULTAT: GODKÄNT / UNDERKÄNT verdict. An unparseable reply gets one immediate retry —
    /// empty/garbled replies are intermittent at Gemma's recommended temperature 1.0, so a
    /// second attempt usually yields a real verdict. If both attempts are unreadable the
    /// requirement counts as failed — never green by accident — but is flagged
    /// <see cref="GoalRequirement.VerdictInconclusive"/> so the work phase doesn't "fix" a
    /// plumbing hiccup, and the start of the reply is surfaced in the activity log so the user
    /// can see what the model actually said. Returns false if a stop was requested.
    /// </summary>
    private async Task<bool> VerifyAllRequirementsAsync(
        Func<string, Task<string>> sendToLlm,
        Action<string>? onToolStatus,
        CancellationToken cancellationToken)
    {
        SetPhase(GoalAgentPhase.Verifying, $"🔎 Iteration {CurrentIteration}/{_maxIterations}: kontrollerar kraven mot arbetsytan...");

        foreach (var requirement in Requirements)
        {
            if (_stopRequested)
            {
                SetPhase(GoalAgentPhase.Stopped, "⏹ Stoppad av användaren.");
                return false;
            }

            requirement.Status = RequirementStatus.Verifying;
            NotifyChange();
            Transcript("STEG", $"Granskning (iteration {CurrentIteration}): {requirement.Description}");

            var result = await _agenticChat.SendWithToolsAsync(
                BuildVerifyPrompt(requirement.Description),
                sendToLlm,
                cancellationToken,
                onToolStatus);

            LogToolInvocations(result, noToolsMessage: null);

            var verdictParsed = TryParseVerdict(result.FinalResponse, out var passed, out var reason);
            if (!verdictParsed)
            {
                Log($"🔁 Granskningssvaret gick inte att tolka (\"{Shorten(result.FinalResponse, 60)}\") — granskar kravet en gång till.");
                Transcript("STEG", $"Granskning (iteration {CurrentIteration}, nytt försök): {requirement.Description}");

                result = await _agenticChat.SendWithToolsAsync(
                    BuildVerifyPrompt(requirement.Description),
                    sendToLlm,
                    cancellationToken,
                    onToolStatus);

                LogToolInvocations(result, noToolsMessage: null);
                verdictParsed = TryParseVerdict(result.FinalResponse, out passed, out reason);
            }

            if (verdictParsed && passed)
            {
                requirement.Status = RequirementStatus.Passed;
                requirement.LastVerdict = null;
                requirement.VerdictInconclusive = false;
                Log($"✅ Godkänt: {requirement.Description}", AgentRunEventTypes.Verdict);
                Transcript("BEDÖMNING", "GODKÄNT");
            }
            else
            {
                string verdict;
                if (!verdictParsed)
                    verdict = $"Granskningssvaret kunde inte tolkas som GODKÄNT/UNDERKÄNT. Svaret började: \"{Shorten(result.FinalResponse, 160)}\"";
                else
                    verdict = string.IsNullOrWhiteSpace(reason) ? "Underkänt utan motivering." : reason;

                requirement.Status = RequirementStatus.Failed;
                requirement.LastVerdict = verdict;
                requirement.VerdictInconclusive = !verdictParsed;
                Log($"❌ Underkänt: {requirement.Description} — {verdict}", AgentRunEventTypes.Verdict);
                Transcript("BEDÖMNING", $"UNDERKÄNT — {verdict}");
            }

            // The verdict is the only requirement state worth a write: the transient
            // Working/Verifying flickers in between would triple the traffic for a long run
            // without telling the history view anything the event log doesn't already say.
            await PersistRequirementStatusAsync(requirement);
            await FlushEventsAsync();
            NotifyChange();
        }

        return true;
    }

    /// <summary>
    /// Surfaces the tool commands a work/verify step actually executed in both the activity
    /// log and the transcript. When the step executed no tool and <paramref name="noToolsMessage"/>
    /// is set, that warning is logged instead — the key signal that the model talked about the
    /// task instead of using its tools.
    /// </summary>
    private void LogToolInvocations(AgenticChatResult result, string? noToolsMessage)
    {
        if (result.ToolInvocations.Count == 0)
        {
            if (noToolsMessage is not null)
            {
                Log(noToolsMessage, AgentRunEventTypes.Tool);
                Transcript("VERKTYG", "(inga verktygskommandon hittades i modellens svar)");
                NotifyChange();
            }
            return;
        }

        foreach (var invocation in result.ToolInvocations)
        {
            Log($"🔧 {invocation.Command} → {invocation.ResultSummary}", AgentRunEventTypes.Tool);
            Transcript("VERKTYG", $"{invocation.Command}\n→ {invocation.ResultSummary}");
        }
        NotifyChange();
    }

    // ── Prompts (Swedish, matching the AgenticChatService tool-loop prompts) ──

    private static string BuildRequirementsPrompt(string goalDescription) =>
        "Du agerar kravanalytiker. Användaren beskriver ett önskat slutresultat för filerna i en arbetsyta.\n" +
        "Bryt ner beskrivningen i konkreta krav (högst 7 stycken) som vart och ett kan kontrolleras genom att " +
        "titta på filerna i arbetsytan (t.ex. att en viss fil finns eller att en fil innehåller något specifikt).\n" +
        // "Spelet får ha enbart text" once became the requirement "innehåller endast textbaserat
        // innehåll", which a reviewer read as "must not contain code" — an unsatisfiable demand
        // for a program file. Permissions and wishes must not harden into requirements.
        "Ta bara med sådant som beskrivningen faktiskt kräver. Det som bara är tillåtet eller önskvärt " +
        "(uttryck som \"får\", \"kan\", \"gärna\", \"hade varit fint\") ska INTE bli egna krav.\n" +
        "Svara ENDAST med kraven, ett per rad, där varje rad börjar med exakt \"KRAV:\". Svara på svenska.\n" +
        "Exempel på svarsformat:\n" +
        "KRAV: Filen recept.txt finns i arbetsytan.\n" +
        "KRAV: recept.txt innehåller en ingredienslista.\n\n" +
        $"Önskat slutresultat:\n{goalDescription}";

    private static string BuildWorkPrompt(string goalDescription, GoalRequirement requirement)
    {
        // An inconclusive "verdict" is a parse diagnostic, not a review motivation — feeding it
        // to the model as if the file were at fault provokes nonsense edits.
        var feedback = string.IsNullOrWhiteSpace(requirement.LastVerdict) || requirement.VerdictInconclusive
            ? string.Empty
            : $"\nVid senaste kontrollen underkändes kravet med motiveringen: \"{requirement.LastVerdict}\". Åtgärda det som saknas.\n";

        return
            $"Du arbetar med filerna i en arbetsyta. Det övergripande målet är: {goalDescription}\n\n" +
            $"Ditt uppdrag just nu är att uppfylla exakt detta krav:\n{requirement.Description}\n" +
            feedback +
            "\nSå här arbetar du med filen:\n" +
            "• Finns filen inte ännu? Skapa den med /fyll <filnamn> <beskrivning> — beskriv vad som ska genereras " +
            "(skriv INTE själva koden i kommandot).\n" +
            "• Finns filen redan och du ska LÄGGA TILL mer (t.ex. fler rader kod)? Använd /redigera <filnamn> " +
            "<beskrivning> — det visar dig filen med radnummer och du infogar ny kod med ett <REDIGERA INFOGA_EFTER=sista_radnumret>-block.\n" +
            "  Använd INTE /fyll för att utöka en befintlig fil — /fyll ERSÄTTER hela filen och raderar allt som redan " +
            "finns, så filen kan aldrig växa. För att bygga upp en stor fil steg för steg måste du infoga med /redigera.\n" +
            "\nDu MÅSTE använda ett av verktygskommandona på en egen rad — att bara beskriva innehållet i text ändrar ingenting i arbetsytan.";
    }

    private static string BuildVerifyPrompt(string requirementDescription) =>
        "Du är en granskare. Kontrollera om följande krav är uppfyllt i arbetsytan:\n" +
        $"{requirementDescription}\n\n" +
        "Använd verktygen (t.ex. /lista för att se filerna och /läs <filnamn> <instruktion> för att läsa innehåll) " +
        "för att kontrollera. Ändra inga filer.\n" +
        // The verdict goes FIRST in the final reply so it survives even if the backend
        // truncates a long answer (low max-tokens settings cut from the end).
        "När du är klar med kontrollen ska ditt slutgiltiga svar BÖRJA med exakt en rad i något av formaten:\n" +
        "RESULTAT: GODKÄNT\n" +
        "RESULTAT: UNDERKÄNT - <kort motivering>";

    // ── Parsing (public static so tests can exercise them directly, mirroring the plain
    //    string-search parsing style used for weak models throughout the codebase) ──

    /// <summary>
    /// Extracts the requirement texts from an LLM reply: every line containing a requirement
    /// marker ("KRAV:", or "REQUIREMENT:" for models that answer in English) yields the text
    /// after the marker, so numbered or bulleted lines like "1. KRAV: ..." work too.
    /// Case-insensitive; blank results are skipped.
    /// </summary>
    public static IReadOnlyList<string> ParseRequirements(string llmResponse)
    {
        if (string.IsNullOrWhiteSpace(llmResponse))
            return Array.Empty<string>();

        var requirements = new List<string>();
        foreach (var line in llmResponse.Split('\n'))
        {
            foreach (var marker in RequirementMarkers)
            {
                var index = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                    continue;

                var text = line[(index + marker.Length)..].Trim();
                if (text.Length > 0)
                    requirements.Add(text);
                break;
            }
        }

        return requirements;
    }

    /// <summary>
    /// Extracts the verdict from a verification reply. Lines containing "RESULT"/"RESULTAT"
    /// are preferred over the rest of the text (models often echo the instructions, which
    /// mention both verdict words); within the searched text a fail token wins over a pass
    /// token so an ambiguous reply is never green by accident. Swedish (GODKÄNT/UNDERKÄNT,
    /// with ASCII spellings) and English (PASS/FAIL/APPROVED/REJECTED) forms are both
    /// accepted, matched at word starts so e.g. "surpass" doesn't count. Returns false when no
    /// verdict is found; <paramref name="reason"/> carries the failure motivation, if any.
    /// </summary>
    public static bool TryParseVerdict(string llmResponse, out bool passed, out string reason)
    {
        passed = false;
        reason = string.Empty;

        if (string.IsNullOrWhiteSpace(llmResponse))
            return false;

        var resultLines = llmResponse
            .Split('\n')
            .Where(l => l.Contains("RESULT", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var candidate in resultLines.Count > 0 ? resultLines : new List<string> { llmResponse })
        {
            if (TryParseVerdictFromText(candidate, ref passed, ref reason))
                return true;
        }

        return false;
    }

    private static bool TryParseVerdictFromText(string text, ref bool passed, ref string reason)
    {
        var failIndex = IndexOfAnyToken(text, FailTokens, out var failToken);
        if (failIndex >= 0)
        {
            passed = false;
            reason = ExtractReason(text, failIndex + failToken.Length);
            return true;
        }

        if (IndexOfAnyToken(text, PassTokens, out _) >= 0)
        {
            passed = true;
            reason = string.Empty;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Finds the first occurrence of any token that starts at a word boundary (the preceding
    /// character is not a letter), so short English tokens like PASS can't match inside words
    /// like "surpass". Suffixes are deliberately allowed — the tokens are stems (GODKÄN matches
    /// both GODKÄNT and GODKÄNDA).
    /// </summary>
    private static int IndexOfAnyToken(string text, string[] tokens, out string matchedToken)
    {
        foreach (var token in tokens)
        {
            var searchFrom = 0;
            while (searchFrom < text.Length)
            {
                var index = text.IndexOf(token, searchFrom, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                    break;

                if (index == 0 || !char.IsLetter(text[index - 1]))
                {
                    matchedToken = token;
                    return index;
                }

                searchFrom = index + 1;
            }
        }

        matchedToken = string.Empty;
        return -1;
    }

    /// <summary>
    /// Returns the failure motivation following a fail token: the rest of that line with the
    /// token's own suffix (e.g. the "T" in UNDERKÄNT) and leading separator punctuation
    /// (dashes, colons, dots, commas) trimmed away.
    /// </summary>
    private static string ExtractReason(string text, int startIndex)
    {
        var rest = text[startIndex..];
        var lineEnd = rest.IndexOf('\n');
        if (lineEnd >= 0)
            rest = rest[..lineEnd];

        // If the stem token ended mid-word (e.g. UNDERKÄN|T, FAIL|ED), drop the rest of that
        // word — but leave a reason that starts directly with its own word untouched.
        var wordRest = 0;
        while (wordRest < rest.Length && char.IsLetter(rest[wordRest]))
            wordRest++;
        rest = rest[wordRest..];

        return rest.Trim().TrimStart('-', '–', '—', ':', '.', ',', ' ').Trim();
    }

    private static string Shorten(string text, int maxLength)
    {
        var flattened = text.Replace('\n', ' ').Replace("\r", string.Empty).Trim();
        return flattened.Length <= maxLength ? flattened : flattened[..maxLength] + "…";
    }

    // ── Transcript (workspace debug log) ──

    /// <summary>
    /// Starts a fresh transcript file for this run in the file agent's base directory (the
    /// active workspace). Transcript failures never break the run — logging is disabled for
    /// the rest of the run and a warning is added to the activity log instead.
    /// </summary>
    private void StartTranscript(string goalDescription)
    {
        _transcriptPath = null;
        if (_fileAgent is null)
            return;

        try
        {
            var path = Path.Combine(_fileAgent.BaseDirectory, TranscriptFileName);
            File.WriteAllText(path,
                $"=== AGENTKÖRNING {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n" +
                $"Mål: {goalDescription}\n" +
                $"Max iterationer: {_maxIterations}\n\n");
            _transcriptPath = path;
            Log($"📄 Transkript loggas till {TranscriptFileName} i arbetsytan.");
        }
        catch (Exception ex)
        {
            Log($"⚠ Kunde inte skapa loggfilen {TranscriptFileName}: {ex.Message}");
        }
    }

    private void Transcript(string heading, string body)
    {
        if (_transcriptPath is null)
            return;

        try
        {
            File.AppendAllText(_transcriptPath,
                $"--- [{DateTime.Now:HH:mm:ss}] {heading} ---\n{body}\n\n");
        }
        catch (Exception ex)
        {
            // Never let logging break the run: disable the transcript and tell the user once.
            _transcriptPath = null;
            Log($"⚠ Kunde inte skriva till loggfilen {TranscriptFileName}: {ex.Message}");
        }
    }

    // ── Run history (database) ──
    //
    // Best-effort throughout, on the same principle as the transcript above: a run is long and
    // expensive, so losing its history is bad, but losing the run itself because history could
    // not be written would be worse. The first failed write disables recording for the rest of
    // the run and says so once in the activity log.

    /// <summary>
    /// Inserts the run row. On success <see cref="_runId"/> is set, which is what enables all
    /// other recording — so a failure here simply means this run isn't recorded.
    /// </summary>
    private async Task StartRunRecordAsync(string goalDescription, string? modelName, Guid? conversationId)
    {
        _runId = null;
        if (_runRepository is null)
            return;

        var run = new AgentRunEntity
        {
            GoalDescription = goalDescription,
            WorkspacePath = _fileAgent?.BaseDirectory,
            ModelName = modelName,
            ConversationId = conversationId,
            MaxIterations = _maxIterations,
            Iterations = 0,
            Phase = GoalAgentPhase.GeneratingRequirements.ToString(),
            StartedAt = DateTime.UtcNow
        };

        try
        {
            await _runRepository.StartRunAsync(run);
            _runId = run.Id;
        }
        catch (Exception ex)
        {
            Log($"⚠ Kunde inte spara agentkörningen i databasen: {ex.Message}");
        }
    }

    private async Task PersistRequirementsAsync()
    {
        if (_runRepository is null || _runId is null)
            return;

        var rows = Requirements
            .Select((requirement, index) => new AgentRunRequirementEntity
            {
                Id = requirement.Id,
                RunId = _runId.Value,
                Ordinal = index + 1,
                Description = requirement.Description,
                Status = requirement.Status.ToString(),
                LastVerdict = requirement.LastVerdict,
                UpdatedAt = DateTime.UtcNow
            })
            .ToList();

        try
        {
            await _runRepository.SaveRequirementsAsync(rows);
        }
        catch (Exception ex)
        {
            DisableRunRecording(ex);
        }
    }

    private async Task PersistRequirementStatusAsync(GoalRequirement requirement)
    {
        if (_runRepository is null || _runId is null)
            return;

        try
        {
            await _runRepository.UpdateRequirementAsync(
                requirement.Id,
                requirement.Status.ToString(),
                requirement.LastVerdict);
        }
        catch (Exception ex)
        {
            DisableRunRecording(ex);
        }
    }

    /// <summary>
    /// Writes the events queued by <see cref="Log"/> since the last flush. Called at step
    /// boundaries so a run costs one round trip per work/verify step instead of one per log line.
    /// </summary>
    private async Task FlushEventsAsync()
    {
        if (_runRepository is null || _runId is null)
            return;

        List<AgentRunEventEntity> batch;
        lock (_lock)
        {
            if (_pendingEvents.Count == 0)
                return;

            batch = _pendingEvents.ToList();
            _pendingEvents.Clear();
        }

        try
        {
            await _runRepository.AddEventsAsync(batch);
        }
        catch (Exception ex)
        {
            DisableRunRecording(ex);
        }
    }

    private async Task CompleteRunRecordAsync()
    {
        if (_runRepository is null || _runId is null)
            return;

        await FlushEventsAsync();

        // FlushEventsAsync may have just disabled recording.
        if (_runId is null)
            return;

        try
        {
            await _runRepository.CompleteRunAsync(_runId.Value, Phase.ToString(), CurrentIteration, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            DisableRunRecording(ex);
        }
    }

    /// <summary>
    /// Stops recording this run after a failed write and tells the user once. Queued events are
    /// dropped so they can't grow unbounded for the rest of a long run.
    /// </summary>
    private void DisableRunRecording(Exception ex)
    {
        _runId = null;
        lock (_lock)
        {
            _pendingEvents.Clear();
        }
        Log($"⚠ Kunde inte spara körningshistoriken i databasen: {ex.Message}");
    }

    // ── State helpers ──

    private void SetPhase(GoalAgentPhase phase, string logMessage)
    {
        Phase = phase;
        Log(logMessage, AgentRunEventTypes.Phase);
        Transcript("FAS", logMessage);
        NotifyChange();
    }

    /// <summary>
    /// Adds a line to the activity log and, when the run is being recorded, queues the same line
    /// as an event for the next <see cref="FlushEventsAsync"/>. Sequence numbers are assigned
    /// here rather than at flush time so the stored order is the order things happened, not the
    /// order the batches landed.
    /// </summary>
    private void Log(string message, string eventType = AgentRunEventTypes.Info)
    {
        lock (_lock)
        {
            _activityLog.Add(message);

            if (_runId is null)
                return;

            _pendingEvents.Add(new AgentRunEventEntity
            {
                RunId = _runId.Value,
                Sequence = ++_eventSequence,
                EventType = eventType,
                Iteration = CurrentIteration > 0 ? CurrentIteration : null,
                Message = message,
                CreatedAt = DateTime.UtcNow
            });
        }
    }

    private int CountFailed()
    {
        lock (_lock)
        {
            return _requirements.Count(r => r.Status != RequirementStatus.Passed);
        }
    }

    private void NotifyChange() => OnChange?.Invoke();
}
