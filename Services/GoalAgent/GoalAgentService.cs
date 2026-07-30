using System.Text;
using System.Text.RegularExpressions;
using AgentKit.Skills.Files;
using AgentKit.Skills.Qb64;
using AgentKit.ToolLoop;
using Entities;
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
    /// How many times an empty/unreadable LLM reply is re-sent before giving up. Empty replies
    /// are intermittent at Gemma's recommended temperature 1.0 — in a real 20-iteration run a
    /// third of all verifications died on them — so retrying at the call level (instead of only
    /// at the verify-verdict level) recovers work steps and requirement generation too.
    /// </summary>
    private const int MaxEmptyReplyRetries = 2;

    /// <summary>Cap on how much of a single file the workspace snapshot inlines into a prompt.</summary>
    private const int SnapshotMaxCharsPerFile = 4000;

    /// <summary>Cap on the total file content a single workspace snapshot may inline (the
    /// deploy machine runs at context size 8192, so prompts must stay lean).</summary>
    private const int SnapshotMaxTotalChars = 10000;

    /// <summary>
    /// File extensions the workspace snapshot never inlines as text (binary or bulk formats —
    /// the reviewer can still reach a PDF via /läs-pdf).
    /// </summary>
    private static readonly HashSet<string> SnapshotSkippedExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".pdf", ".exe", ".dll", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".zip", ".gguf" };

    /// <summary>Matches a filename-looking token (name.ext) inside free text, so the workspace
    /// snapshot can inline exactly the files a requirement or goal actually talks about.</summary>
    private static readonly Regex FilenamePattern = new(@"[\w\-]+\.[A-Za-z0-9]{1,8}", RegexOptions.Compiled);

    /// <summary>
    /// Matches pure file-existence requirements ("Filen calc.bas finns i arbetsytan." /
    /// "The file x.txt exists...") so they can be checked directly on disk instead of spending
    /// an LLM round trip on something a File.Exists answers with certainty.
    /// </summary>
    private static readonly Regex FileExistsRequirementPattern = new(
        @"^\s*(?:\d+[.)]\s*)?(?:filen|the file)\s+(?<name>[\w\-]+\.[A-Za-z0-9]{1,8})\s+(?:finns|exists)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Gemma 4 special-token spellings that occasionally leak into replies (reasoning channel,
    /// native tool calls). See <see cref="ScrubLeakedModelTokens"/>.
    /// </summary>
    private static readonly string[] LeakedTokenOpeners = { "<|channel>", "<|tool_call>", "<|tool_response>", "<|turn>" };

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
    private readonly IQb64ToolService? _qb64Tools;

    /// <summary>Cap used when a run doesn't request its own override — set at construction time,
    /// typically from <c>AppConfiguration.AgentTools.MaxGoalIterations</c>.</summary>
    private readonly int _defaultMaxIterations;

    /// <summary>
    /// The cap actually in effect. Equal to <see cref="_defaultMaxIterations"/> until a run
    /// overrides it via <see cref="RunAsync"/>'s <c>maxIterations</c> parameter; re-derived at
    /// the start of every run, so it never mixes one run's override into the next.
    /// </summary>
    private int _maxIterations;
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
    /// Default cap on work → verify iterations, used whenever a run doesn't supply its own via
    /// <see cref="RunAsync"/>. Non-positive values fall back to <see cref="DefaultMaxIterations"/>.
    /// </param>
    /// <param name="runRepository">
    /// Optional. When provided, each run is recorded as history (the run, its requirements, and
    /// its activity log) so finished runs can be reviewed after the app restarts. Persistence is
    /// best-effort: a failing write disables history for the rest of the run rather than
    /// interrupting it. When null, a run leaves no database trace beyond the question/answer
    /// turns its LLM calls produce.
    /// </param>
    /// <param name="qb64Tools">
    /// Optional. When provided and a compiler is configured, the requirement generator is told
    /// that .bas files can be compiled and run, so "the program compiles" can become a real
    /// requirement (the tool itself is offered to the LLM by <see cref="IAgenticChatService"/>).
    /// Without this, a goal like "test that the app works" silently degrades to file-content
    /// checks only.
    /// </param>
    public GoalAgentService(
        IAgenticChatService agenticChat,
        IFileAgentService? fileAgent = null,
        int maxIterations = DefaultMaxIterations,
        IAgentRunRepository? runRepository = null,
        IQb64ToolService? qb64Tools = null)
    {
        _agenticChat = agenticChat ?? throw new ArgumentNullException(nameof(agenticChat));
        _fileAgent = fileAgent;
        _defaultMaxIterations = maxIterations > 0 ? maxIterations : DefaultMaxIterations;
        _maxIterations = _defaultMaxIterations;
        _runRepository = runRepository;
        _qb64Tools = qb64Tools;
    }

    /// <summary>True when a QB64 compiler is configured, i.e. the /qb64 commands are actually
    /// offered to the LLM (an unconfigured tool service exposes no commands).</summary>
    private bool Qb64Available => _qb64Tools is not null && _qb64Tools.GetToolDescriptions().Count > 0;

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
        Guid? conversationId = null,
        Func<string, Task<string>>? verifySendToLlm = null,
        int? maxIterations = null,
        Guid? runId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(goalDescription);
        ArgumentNullException.ThrowIfNull(sendToLlm);

        if (_isRunning)
            return;

        _isRunning = true;
        _stopRequested = false;
        _maxIterations = maxIterations is > 0 ? maxIterations.Value : _defaultMaxIterations;
        lock (_lock)
        {
            _requirements.Clear();
            _activityLog.Clear();
            _pendingEvents.Clear();
            _eventSequence = 0;
        }
        GoalDescription = goalDescription;
        CurrentIteration = 0;

        await StartRunRecordAsync(goalDescription, modelName, conversationId, runId);
        StartTranscript(goalDescription);

        // Every LLM round trip — including the internal tool-call rounds AgenticChatService
        // performs on our behalf — goes through this wrapper so the transcript captures the
        // complete raw conversation. This is the primary debugging aid when a model ignores
        // the KRAV:/RESULTAT: format or never emits a tool command. The wrapper also scrubs
        // leaked special tokens and retries empty replies before they reach any parsing.
        var loggingSendToLlm = WrapWithLoggingAndRetry(sendToLlm);
        var loggingVerifySendToLlm = verifySendToLlm is null
            ? loggingSendToLlm
            : WrapWithLoggingAndRetry(verifySendToLlm);

        try
        {
            await GenerateRequirementsAsync(goalDescription, loggingSendToLlm);

            for (var iteration = 1; iteration <= _maxIterations; iteration++)
            {
                CurrentIteration = iteration;

                if (!await WorkOnUnmetRequirementsAsync(goalDescription, loggingSendToLlm, onToolStatus, cancellationToken))
                    return; // stop requested mid-work

                if (!await VerifyAllRequirementsAsync(loggingVerifySendToLlm, onToolStatus, cancellationToken))
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
    /// requirement that isn't currently passed — in ONE combined work step per iteration, so
    /// the model sees all remaining requirements at once. The earlier one-step-per-requirement
    /// design made each step optimize its single requirement in isolation: a real run kept
    /// replacing the whole file with content that satisfied only the requirement at hand,
    /// failing all the others again. A requirement that failed its last verification gets the
    /// failure motivation included in the prompt — the "failing test message" that steers the
    /// next attempt — except when the verdict was inconclusive (unparseable review reply), in
    /// which case the requirement is excluded: there is no defect to steer by, and blind edits
    /// have wrecked a good file before. Every executed tool command is surfaced in the
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

        if (_stopRequested)
        {
            SetPhase(GoalAgentPhase.Stopped, "⏹ Stoppad av användaren.");
            return false;
        }

        // An inconclusive verdict (the review reply couldn't be parsed) names no concrete
        // defect, so there is nothing sensible to fix — and blind rework is how a good file
        // gets ruined. Such requirements are left out of the work step; the next verify pass
        // re-checks them (with a retry) and real work resumes once a readable verdict exists.
        var actionable = Requirements
            .Where(r => r.Status != RequirementStatus.Passed && !r.VerdictInconclusive)
            .ToList();

        if (actionable.Count == 0)
        {
            Log("⏭ Ingen åtgärd i denna iteration — de kvarvarande kraven saknar tolkbara granskningsutslag, så det finns ingen konkret brist att åtgärda. Kraven granskas om i nästa kontrollpass.");
            return true;
        }

        foreach (var requirement in actionable)
            requirement.Status = RequirementStatus.Working;
        NotifyChange();

        Transcript("STEG",
            $"Arbete (iteration {CurrentIteration}): {actionable.Count} krav i ett samlat arbetssteg\n" +
            string.Join("\n", actionable.Select(r => $"- {r.Description}")));

        var referenceTexts = actionable.Select(r => r.Description).Prepend(goalDescription).ToList();
        var snapshot = BuildWorkspaceSnapshot(referenceTexts);

        var result = await _agenticChat.SendWithToolsAsync(
            BuildWorkPrompt(goalDescription, actionable, snapshot),
            sendToLlm,
            cancellationToken,
            onToolStatus);

        var recovered = await TryRecoverUnappliedFileWriteAsync(referenceTexts, result.FinalResponse);
        if (recovered is not null)
            result = result with { ToolInvocations = result.ToolInvocations.Append(recovered).ToList() };

        LogToolInvocations(result,
            noToolsMessage: "⚠ Inga verktygskommandon kördes under arbetssteget — inga filer kan ha ändrats.");

        // New work invalidates the old verdicts — the upcoming verification pass decides.
        foreach (var requirement in actionable)
            requirement.Status = RequirementStatus.Unverified;
        await FlushEventsAsync();
        NotifyChange();

        return true;
    }

    /// <summary>
    /// Minimum non-blank lines a recovered code block must have before
    /// <see cref="TryRecoverUnappliedFileWriteAsync"/> treats it as a full file rewrite rather
    /// than a small inline example quoted in prose.
    /// </summary>
    private const int MinRecoveredContentLines = 3;

    /// <summary>
    /// Recovers the specific failure mode where a work step's final reply contains a complete
    /// file rewrite (typically a Markdown code fence) but never issued a <c>/fyll</c> or
    /// <c>/redigera</c> command to apply it — so nothing in the workspace actually changed. A real
    /// run hit this exactly: the model correctly diagnosed and rewrote a broken file, then
    /// delivered the fix as plain chat text instead of a tool command, and the iteration cap was
    /// reached one exchange later with the (still broken) old file untouched.
    /// <para>
    /// Only fires when exactly one file referenced by the goal/requirements already exists in the
    /// workspace — with zero or multiple candidates there is no safe way to guess which file the
    /// content belongs to, so nothing is written. This mirrors (and reuses) the same file
    /// reference/extraction machinery as <see cref="BuildWorkspaceSnapshot"/> and the
    /// <c>/fyll</c> inline-content shortcut in <c>AgenticChatService</c>, just applied
    /// after the fact to a reply that never named a command at all.
    /// </para>
    /// </summary>
    private async Task<ToolInvocation?> TryRecoverUnappliedFileWriteAsync(
        IReadOnlyList<string> referenceTexts, string finalResponse)
    {
        if (_fileAgent is null)
            return null;

        if (!_fileAgent.TryExtractFileContent(finalResponse, out var content))
            return null;

        if (content.Split('\n').Count(l => !string.IsNullOrWhiteSpace(l)) < MinRecoveredContentLines)
            return null; // too small to confidently treat as a full-file rewrite

        var candidates = GetReferencedExistingFiles(referenceTexts);
        if (candidates.Count != 1)
            return null; // ambiguous or no target — guessing wrong would be worse than doing nothing

        var target = candidates[0];
        await _fileAgent.WriteExtractedContentAsync(target, content);

        return new ToolInvocation(
            "(räddad filskrivning)",
            $"✓ Fil sparad: {target} — svaret innehöll ett fullständigt filinnehåll men inget verktygskommando, så det tillämpades automatiskt.");
    }

    /// <summary>
    /// Returns the distinct, existing, non-binary workspace filenames mentioned across
    /// <paramref name="referenceTexts"/> (goal + requirement descriptions) — the same filename
    /// extraction <see cref="BuildWorkspaceSnapshot"/> uses to decide which files to inline.
    /// </summary>
    private IReadOnlyList<string> GetReferencedExistingFiles(IEnumerable<string> referenceTexts)
    {
        if (_fileAgent is null)
            return Array.Empty<string>();

        string[] paths;
        try
        {
            paths = Directory.GetFiles(_fileAgent.BaseDirectory);
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }

        var existing = new HashSet<string>(
            paths.Select(Path.GetFileName).Where(n => n is not null)!,
            StringComparer.OrdinalIgnoreCase);

        var referenced = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Each match branches into multiple continue/skip paths below, so this isn't a simple
        // filter+project that reads better as a LINQ chain (same rationale as FileAgentService's
        // TryExtractLineEdits, which disables S3267 for the same shape of loop).
#pragma warning disable S3267
        foreach (var text in referenceTexts)
        {
            foreach (Match match in FilenamePattern.Matches(text ?? string.Empty))
            {
                var name = match.Value;
                if (!existing.Contains(name)) continue;
                if (name.Equals(TranscriptFileName, StringComparison.OrdinalIgnoreCase)) continue;
                if (SnapshotSkippedExtensions.Contains(Path.GetExtension(name))) continue;
                if (seen.Add(name))
                    referenced.Add(name);
            }
        }
#pragma warning restore S3267

        return referenced;
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

            // Pure existence requirements ("Filen X finns i arbetsytan") are answered with
            // certainty by the file system — no LLM round trip, no chance of a garbled verdict.
            if (TryCheckFileExistenceDirectly(requirement))
            {
                await PersistRequirementStatusAsync(requirement);
                await FlushEventsAsync();
                NotifyChange();
                continue;
            }

            requirement.Status = RequirementStatus.Verifying;
            NotifyChange();
            Transcript("STEG", $"Granskning (iteration {CurrentIteration}): {requirement.Description}");

            var verifyPrompt = BuildVerifyPrompt(
                requirement.Description,
                BuildWorkspaceSnapshot(new[] { requirement.Description }),
                Qb64Available);

            var result = await _agenticChat.SendWithToolsAsync(
                verifyPrompt,
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
                    verifyPrompt,
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
    /// Deterministic pre-check for pure file-existence requirements: when the requirement is of
    /// the shape "Filen X finns ..." (and carries no content condition) and a file agent is
    /// available, the verdict is decided directly against the file system and the requirement's
    /// status/verdict/log/transcript are all updated. Returns true when the requirement was
    /// handled here (the caller skips the LLM verification), false when it needs a real review.
    /// </summary>
    private bool TryCheckFileExistenceDirectly(GoalRequirement requirement)
    {
        if (_fileAgent is null || !TryParseFileExistenceRequirement(requirement.Description, out var filename))
            return false;

        bool exists;
        try
        {
            exists = File.Exists(Path.Combine(_fileAgent.BaseDirectory, filename));
        }
        catch (Exception)
        {
            return false; // fall back to the LLM review rather than guessing
        }

        Transcript("STEG", $"Granskning (iteration {CurrentIteration}, direktkontroll): {requirement.Description}");

        if (exists)
        {
            requirement.Status = RequirementStatus.Passed;
            requirement.LastVerdict = null;
            requirement.VerdictInconclusive = false;
            Log($"✅ Godkänt (direktkontroll): {requirement.Description}", AgentRunEventTypes.Verdict);
            Transcript("BEDÖMNING", "GODKÄNT (direktkontroll: filen finns)");
        }
        else
        {
            requirement.Status = RequirementStatus.Failed;
            requirement.LastVerdict = $"Filen {filename} saknas i arbetsytan.";
            requirement.VerdictInconclusive = false;
            Log($"❌ Underkänt (direktkontroll): {requirement.Description} — filen saknas.", AgentRunEventTypes.Verdict);
            Transcript("BEDÖMNING", $"UNDERKÄNT — Filen {filename} saknas i arbetsytan (direktkontroll).");
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

    private string BuildRequirementsPrompt(string goalDescription)
    {
        // Without this the "test that the app works" part of a goal silently disappears:
        // the requirement generator is otherwise told everything must be checkable by
        // looking at files, which excludes compiling/running by definition.
        var qb64Hint = Qb64Available
            ? "Arbetsytan har ett verktyg som kan kompilera och köra QBasic-program (.bas-filer). " +
              "Om målet gäller ett QBasic-program ska du ta med ett krav om att filen kompilerar utan fel, " +
              "och — om målet uttryckligen ber om test av programmet — ett krav om att programmet fungerar när det körs.\n"
            : string.Empty;

        return
            "Du agerar kravanalytiker. Användaren beskriver ett önskat slutresultat för filerna i en arbetsyta.\n" +
            "Bryt ner beskrivningen i konkreta krav (högst 5 stycken) som vart och ett kan kontrolleras genom att " +
            "titta på filerna i arbetsytan eller köra tillgängliga verktyg (t.ex. att en viss fil finns eller att " +
            "en fil innehåller något specifikt).\n" +
            // Seven fine-grained requirements over one file made a real run rewrite the same
            // file over and over, each work step satisfying only its own sliver. Few and broad
            // beats many and narrow.
            "Skapa hellre få och breda krav än många smala: dela INTE upp innehållet i en och samma fil i flera " +
            "krav. Ett krav får gärna räkna upp allt en fil ska innehålla, t.ex. " +
            "\"KRAV: calc.bas innehåller en komplett miniräknare med addition, subtraktion, multiplikation och division.\"\n" +
            // "Spelet får ha enbart text" once became the requirement "innehåller endast textbaserat
            // innehåll", which a reviewer read as "must not contain code" — an unsatisfiable demand
            // for a program file. Permissions and wishes must not harden into requirements.
            "Ta bara med sådant som beskrivningen faktiskt kräver. Det som bara är tillåtet eller önskvärt " +
            "(uttryck som \"får\", \"kan\", \"gärna\", \"hade varit fint\") ska INTE bli egna krav.\n" +
            qb64Hint +
            "Svara ENDAST med kraven, ett per rad, där varje rad börjar med exakt \"KRAV:\". Svara på svenska.\n" +
            "Exempel på svarsformat:\n" +
            "KRAV: Filen recept.txt finns i arbetsytan.\n" +
            "KRAV: recept.txt innehåller en ingredienslista.\n\n" +
            $"Önskat slutresultat:\n{goalDescription}";
    }

    private static string BuildWorkPrompt(
        string goalDescription,
        IReadOnlyList<GoalRequirement> requirements,
        string workspaceSnapshot)
    {
        var sb = new StringBuilder();
        sb.Append($"Du arbetar med filerna i en arbetsyta. Det övergripande målet är: {goalDescription}\n\n");
        sb.Append("Ditt uppdrag just nu är att uppfylla följande krav. De beskriver samma slutresultat — lös dem tillsammans i samma fil(er) istället för ett i taget:\n");

        for (var i = 0; i < requirements.Count; i++)
        {
            sb.Append($"{i + 1}. {requirements[i].Description}\n");

            // An inconclusive "verdict" is a parse diagnostic, not a review motivation — feeding
            // it to the model as if the file were at fault provokes nonsense edits (such
            // requirements are already filtered out of the work step, so this is belt-and-braces).
            if (!string.IsNullOrWhiteSpace(requirements[i].LastVerdict) && !requirements[i].VerdictInconclusive)
                sb.Append($"   Vid senaste kontrollen underkändes detta krav med motiveringen: \"{requirements[i].LastVerdict}\". Åtgärda det som saknas.\n");
        }

        if (workspaceSnapshot.Length > 0)
            sb.Append('\n').Append(workspaceSnapshot);

        sb.Append(
            "\nSå här arbetar du med filerna:\n" +
            "• Finns filen inte ännu (se ögonblicksbilden ovan)? Skapa den med /fyll <filnamn> <beskrivning> — " +
            "beskriv HELA innehållet som ska genereras så att alla kraven ovan täcks på en gång (skriv INTE själva koden i kommandot).\n" +
            "• Finns filen redan? Använd /redigera <filnamn> <instruktion> för att ändra eller lägga till — det visar dig filen " +
            "med radnummer och du infogar ny kod med ett <REDIGERA INFOGA_EFTER=sista_radnumret>-block.\n" +
            "  Använd INTE /fyll på en befintlig fil — /fyll ERSÄTTER hela filen och raderar allt som redan finns. " +
            "För att bygga vidare på en fil måste du använda /redigera.\n" +
            "\nDu MÅSTE använda ett av verktygskommandona på en egen rad — att bara beskriva innehållet i text ändrar ingenting i arbetsytan.");

        return sb.ToString();
    }

    private static string BuildVerifyPrompt(
        string requirementDescription,
        string workspaceSnapshot,
        bool qb64Available)
    {
        var sb = new StringBuilder();
        sb.Append("Du är en granskare. Kontrollera om följande krav är uppfyllt i arbetsytan:\n");
        sb.Append(requirementDescription).Append("\n\n");

        // Injecting the file content directly removes a whole tool round trip — and with it the
        // failure mode where the reviewer judges without ever reading the file.
        if (workspaceSnapshot.Length > 0)
            sb.Append(workspaceSnapshot).Append('\n');

        sb.Append("Basera din bedömning i första hand på ögonblicksbilden ovan. Använd verktygen bara om " +
                  "ögonblicksbilden inte räcker (t.ex. /läs <filnamn> <instruktion> för en fil som inte visas där). " +
                  "Ändra inga filer.\n");

        if (qb64Available)
            sb.Append("Om kravet gäller att ett QBasic-program kompilerar eller fungerar: kontrollera med " +
                      "/qb64-kompilera <fil.bas> (enbart kompilering) eller /qb64 <fil.bas> (kompilerar och kör programmet).\n");

        // The verdict goes FIRST in the final reply so it survives even if the backend
        // truncates a long answer (low max-tokens settings cut from the end).
        sb.Append(
            "När du är klar med kontrollen ska ditt slutgiltiga svar BÖRJA med exakt en rad i något av formaten:\n" +
            "RESULTAT: GODKÄNT\n" +
            "RESULTAT: UNDERKÄNT - <kort motivering>");

        return sb.ToString();
    }

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

    /// <summary>
    /// Parses a pure file-existence requirement ("Filen calc.bas finns i arbetsytan." /
    /// "The file x.txt exists in the workspace") and extracts the filename. Requirements that
    /// also mention content ("finns och innehåller...") are rejected — those need a real review.
    /// Public static so tests can exercise the pattern directly.
    /// </summary>
    public static bool TryParseFileExistenceRequirement(string description, out string filename)
    {
        filename = string.Empty;
        if (string.IsNullOrWhiteSpace(description))
            return false;

        // A content condition anywhere in the text disqualifies the shortcut.
        if (description.Contains("innehåll", StringComparison.OrdinalIgnoreCase)
            || description.Contains("contain", StringComparison.OrdinalIgnoreCase))
            return false;

        var match = FileExistsRequirementPattern.Match(description);
        if (!match.Success)
            return false;

        filename = match.Groups["name"].Value;
        return true;
    }

    /// <summary>
    /// Removes Gemma 4 special tokens that occasionally leak into a reply (the reasoning
    /// channel and native tool-call tokens): text after a closed reasoning channel is kept,
    /// while everything from an unclosed opener onward is cut — it is reasoning or tool
    /// plumbing, never the answer. A reply that consists only of leaked tokens becomes empty,
    /// which the retry loop in <see cref="WrapWithLoggingAndRetry"/> then treats like any other
    /// empty reply. The token spellings are Gemma-4-specific, so ordinary answers are untouched.
    /// Public static so tests can exercise it directly.
    /// </summary>
    public static string ScrubLeakedModelTokens(string reply)
    {
        if (string.IsNullOrWhiteSpace(reply))
            return string.Empty;

        var text = reply;

        // A closed reasoning channel: the answer is whatever follows the final close token.
        const string channelEnd = "<channel|>";
        var closeIdx = text.LastIndexOf(channelEnd, StringComparison.Ordinal);
        if (closeIdx >= 0)
            text = text[(closeIdx + channelEnd.Length)..];

        foreach (var opener in LeakedTokenOpeners)
        {
            var openerIdx = text.IndexOf(opener, StringComparison.Ordinal);
            if (openerIdx >= 0)
                text = text[..openerIdx];
        }

        return text.Trim();
    }

    /// <summary>
    /// Wraps an LLM call with the run transcript (raw prompt and reply), leaked-token
    /// scrubbing, and an empty-reply retry: the same prompt is re-sent up to
    /// <see cref="MaxEmptyReplyRetries"/> times when the scrubbed reply is empty. In a real run
    /// the iteration cap was reached with the goal actually fulfilled, because the deciding
    /// verification replies happened to be empty — retrying at the call level catches that for
    /// every LLM round trip, including the tool-call rounds inside the agentic chat loop.
    /// </summary>
    private Func<string, Task<string>> WrapWithLoggingAndRetry(Func<string, Task<string>> sendToLlm) =>
        async prompt =>
        {
            Transcript("PROMPT →", prompt);
            var reply = await sendToLlm(prompt);
            Transcript("SVAR ←", reply);

            var cleaned = ScrubLeakedModelTokens(reply);
            for (var attempt = 1; attempt <= MaxEmptyReplyRetries && cleaned.Length == 0; attempt++)
            {
                Log($"🔁 Modellen gav ett tomt eller oläsbart svar — skickar samma prompt igen (försök {attempt + 1}/{MaxEmptyReplyRetries + 1}).");
                Transcript("STEG", $"Tomt/oläsbart svar — nytt försök {attempt + 1}/{MaxEmptyReplyRetries + 1}");

                reply = await sendToLlm(prompt);
                Transcript("SVAR ←", reply);
                cleaned = ScrubLeakedModelTokens(reply);
            }

            return cleaned;
        };

    /// <summary>
    /// Builds a deterministic snapshot of the workspace for prompt injection: the file listing,
    /// plus the inlined content of every text file whose name appears in
    /// <paramref name="referenceTexts"/> (the goal and requirement descriptions). This removes
    /// the /lista and /läs tool round trips in the common case — and with them the failure mode
    /// where a work step blindly rewrites a file it never read, or a reviewer judges a file it
    /// never opened. Content is capped per file and in total (the deploy machine runs at
    /// context size 8192). Returns an empty string when no file agent is available.
    /// </summary>
    private string BuildWorkspaceSnapshot(IEnumerable<string> referenceTexts)
    {
        if (_fileAgent is null)
            return string.Empty;

        string baseDirectory;
        string[] paths;
        try
        {
            baseDirectory = _fileAgent.BaseDirectory;
            paths = Directory.GetFiles(baseDirectory);
        }
        catch (Exception)
        {
            return string.Empty; // a broken workspace just means no snapshot — tools still work
        }

        var names = paths
            .Select(Path.GetFileName)
            .Where(n => n is not null)
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sb = new StringBuilder();
        sb.Append("Ögonblicksbild av arbetsytan (automatiskt inläst — du behöver inte använda /lista eller /läs för det som visas här):\n");
        sb.Append(names.Count == 0
            ? "Arbetsytan är tom — inga filer finns ännu.\n"
            : $"Filer: {string.Join(", ", names)}\n");

        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var text in referenceTexts)
        {
            foreach (Match match in FilenamePattern.Matches(text ?? string.Empty))
                referenced.Add(match.Value);
        }

        var remainingBudget = SnapshotMaxTotalChars;
        foreach (var name in names)
        {
            if (!referenced.Contains(name))
                continue;
            if (name.Equals(TranscriptFileName, StringComparison.OrdinalIgnoreCase))
                continue; // the run's own transcript would recursively bloat every prompt
            if (SnapshotSkippedExtensions.Contains(Path.GetExtension(name)))
                continue;

            string content;
            try
            {
                content = File.ReadAllText(Path.Combine(baseDirectory, name));
            }
            catch (Exception)
            {
                continue;
            }

            var cap = Math.Min(SnapshotMaxCharsPerFile, remainingBudget);
            if (cap <= 0)
                break;

            var truncated = content.Length > cap;
            if (truncated)
                content = content[..cap];
            remainingBudget -= content.Length;

            sb.Append($"\n--- Innehåll i {name} ---\n");
            sb.Append(content.Trim().Length == 0 ? "(filen är tom)\n" : content.TrimEnd() + "\n");
            if (truncated)
                sb.Append($"(… avkortat — använd /läs {name} <instruktion> för resten)\n");
            sb.Append($"--- Slut på {name} ---\n");
        }

        return sb.ToString();
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
    /// <param name="runId">
    /// Optional caller-supplied id for the run row (falls back to a new random id via
    /// <see cref="AgentRunEntity"/>'s default). Lets a caller that already handed out an id for
    /// this run before <see cref="RunAsync"/> was called (e.g. a job id returned from an API
    /// before the run starts) look the same row up later by that id.
    /// </param>
    private async Task StartRunRecordAsync(string goalDescription, string? modelName, Guid? conversationId, Guid? runId)
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
        if (runId is not null)
            run.Id = runId.Value;

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
