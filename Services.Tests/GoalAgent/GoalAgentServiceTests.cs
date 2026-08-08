using Entities;
using FluentAssertions;
using AgentKit.Skills.Files;
using AgentKit.Skills.Qb64;
using AgentKit.Skills.Utility;
using AgentKit.ToolLoop;
using Services.GoalAgent;
using Services.Repositories;
using Services.Workspace;

namespace Services.Tests.GoalAgent;

/// <summary>
/// Unit tests for <see cref="GoalAgentService"/>: the TDD-style loop that turns a free-text
/// workspace goal into checkable requirements, works on them through
/// <see cref="IAgenticChatService"/>, verifies each requirement, and repeats until everything
/// passes. Uses a fake <see cref="IAgenticChatService"/> (same approach as BatchJobServiceTests)
/// so these tests focus on requirement parsing, iteration/feedback sequencing, and status
/// bookkeeping rather than the tool-calling loop itself (covered by AgenticChatServiceTests).
/// </summary>
public class GoalAgentServiceTests
{
    /// <summary>
    /// Fake agentic chat service: returns a scripted result per call and records every prompt
    /// it was asked to process, in order. The handler is async so tests can hold a call open
    /// via a gate without blocking the calling thread.
    /// </summary>
    private sealed class FakeAgenticChatService : IAgenticChatService
    {
        private readonly Func<string, CancellationToken, Task<AgenticChatResult>> _handler;

        public FakeAgenticChatService(Func<string, AgenticChatResult> handler)
            : this((msg, _) => Task.FromResult(handler(msg)))
        {
        }

        public FakeAgenticChatService(Func<string, Task<AgenticChatResult>> handler)
            : this((msg, _) => handler(msg))
        {
        }

        /// <summary>
        /// Token-aware handler, for tests that need to model an LLM call being aborted mid-flight
        /// (the real backend kills its subprocess and throws when the token is cancelled).
        /// </summary>
        public FakeAgenticChatService(Func<string, CancellationToken, Task<AgenticChatResult>> handler) => _handler = handler;

        public List<string> ReceivedMessages { get; } = new();

        public async Task<AgenticChatResult> SendWithToolsAsync(
            string userMessage,
            Func<string, Task<string>> sendToLlm,
            CancellationToken cancellationToken = default,
            Action<string>? onToolStatus = null,
            string? recentlyUploadedFilename = null)
        {
            ReceivedMessages.Add(userMessage);
            return await _handler(userMessage, cancellationToken);
        }
    }

    /// <summary>
    /// Fake run-history repository: records every write in memory. Set <see cref="WriteFailure"/>
    /// to model a database that rejects writes, which the service must survive.
    /// </summary>
    private sealed class FakeAgentRunRepository : IAgentRunRepository
    {
        public List<AgentRunEntity> StartedRuns { get; } = new();
        public List<AgentRunRequirementEntity> SavedRequirements { get; } = new();
        public List<AgentRunEventEntity> SavedEvents { get; } = new();
        public List<(Guid RunId, string Phase, int Iterations)> CompletedRuns { get; } = new();

        /// <summary>When set, every write throws it.</summary>
        public Exception? WriteFailure { get; set; }

        private void FailIfConfigured()
        {
            if (WriteFailure is not null)
                throw WriteFailure;
        }

        public Task InitializeDatabaseAsync() => Task.CompletedTask;

        public Task StartRunAsync(AgentRunEntity run)
        {
            FailIfConfigured();
            StartedRuns.Add(run);
            return Task.CompletedTask;
        }

        public Task SaveRequirementsAsync(IReadOnlyList<AgentRunRequirementEntity> requirements)
        {
            FailIfConfigured();
            SavedRequirements.AddRange(requirements);
            return Task.CompletedTask;
        }

        public Task UpdateRequirementAsync(Guid requirementId, string status, string? lastVerdict)
        {
            FailIfConfigured();
            var row = SavedRequirements.FirstOrDefault(r => r.Id == requirementId);
            if (row is not null)
            {
                row.Status = status;
                row.LastVerdict = lastVerdict;
            }
            return Task.CompletedTask;
        }

        public Task AddEventsAsync(IReadOnlyList<AgentRunEventEntity> events)
        {
            FailIfConfigured();
            SavedEvents.AddRange(events);
            return Task.CompletedTask;
        }

        public Task CompleteRunAsync(Guid runId, string phase, int iterations, DateTime completedAt)
        {
            FailIfConfigured();
            CompletedRuns.Add((runId, phase, iterations));
            return Task.CompletedTask;
        }

        public Task<List<AgentRunEntity>> GetRecentRunsAsync(int count = 25) => Task.FromResult(StartedRuns.ToList());
        public Task<AgentRunEntity?> GetRunAsync(Guid runId) => Task.FromResult(StartedRuns.FirstOrDefault(r => r.Id == runId));
        public Task<List<AgentRunRequirementEntity>> GetRequirementsAsync(Guid runId) =>
            Task.FromResult(SavedRequirements.Where(r => r.RunId == runId).ToList());
        public Task<List<AgentRunEventEntity>> GetEventsAsync(Guid runId) =>
            Task.FromResult(SavedEvents.Where(e => e.RunId == runId).ToList());
        public Task DeleteRunAsync(Guid runId) => Task.CompletedTask;
    }

    private static AgenticChatResult Result(string text) => new(text, Array.Empty<ToolInvocation>());

    /// <summary>The service's prompts are keyed on these markers to tell work and verify calls apart.</summary>
    private static bool IsWorkPrompt(string msg) => msg.StartsWith("Du arbetar med filerna", StringComparison.Ordinal);
    private static bool IsVerifyPrompt(string msg) => msg.StartsWith("Du är en granskare", StringComparison.Ordinal);

    /// <summary>sendToLlm used for requirement generation: returns two KRAV lines.</summary>
    private static Task<string> TwoRequirementsLlm(string prompt) =>
        Task.FromResult("KRAV: Filen recept.txt finns i arbetsytan.\nKRAV: recept.txt innehåller en ingredienslista.");

    // ── Constructor / argument validation ─────────────────────────────────

    [Fact]
    public void Constructor_NullAgenticChat_ThrowsArgumentNullException()
    {
        var act = () => new GoalAgentService(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task RunAsync_EmptyGoal_ThrowsArgumentException()
    {
        var sut = new GoalAgentService(new FakeAgenticChatService(_ => Result("ok")));

        var act = async () => await sut.RunAsync("   ", TwoRequirementsLlm);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RunAsync_NullSendToLlm_ThrowsArgumentNullException()
    {
        var sut = new GoalAgentService(new FakeAgenticChatService(_ => Result("ok")));

        var act = async () => await sut.RunAsync("ett mål", null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ── ParseRequirements ─────────────────────────────────────────────────

    [Fact]
    public void ParseRequirements_PlainKravLines_ReturnsTextsAfterMarker()
    {
        var parsed = GoalAgentService.ParseRequirements(
            "KRAV: Filen a.txt finns.\nKRAV: a.txt innehåller en rubrik.");

        parsed.Should().Equal("Filen a.txt finns.", "a.txt innehåller en rubrik.");
    }

    [Fact]
    public void ParseRequirements_NumberedAndBulletedLines_StillParsed()
    {
        var parsed = GoalAgentService.ParseRequirements(
            "Här är kraven:\n1. KRAV: Första kravet.\n- krav: Andra kravet.\nNågot annat på slutet.");

        parsed.Should().Equal("Första kravet.", "Andra kravet.");
    }

    [Fact]
    public void ParseRequirements_NoMarkerOrEmptyInput_ReturnsEmpty()
    {
        GoalAgentService.ParseRequirements("Modellen pratar bara fritt utan kravrader.").Should().BeEmpty();
        GoalAgentService.ParseRequirements("").Should().BeEmpty();
    }

    [Fact]
    public void ParseRequirements_MarkerWithNoText_IsSkipped()
    {
        GoalAgentService.ParseRequirements("KRAV:\nKRAV: Riktigt krav.").Should().Equal("Riktigt krav.");
    }

    [Fact]
    public void ParseRequirements_EnglishRequirementMarker_IsParsed()
    {
        // Models sometimes answer in English despite the Swedish prompt (especially when the
        // goal itself is written in English).
        var parsed = GoalAgentService.ParseRequirements(
            "REQUIREMENT: The file number.txt exists in the workspace.\n" +
            "requirement: number.txt contains the first 20 Fibonacci numbers.");

        parsed.Should().Equal(
            "The file number.txt exists in the workspace.",
            "number.txt contains the first 20 Fibonacci numbers.");
    }

    // ── TryParseVerdict ───────────────────────────────────────────────────

    [Fact]
    public void TryParseVerdict_Godkant_ReturnsPassed()
    {
        GoalAgentService.TryParseVerdict("RESULTAT: GODKÄNT", out var passed, out _).Should().BeTrue();

        passed.Should().BeTrue();
    }

    [Fact]
    public void TryParseVerdict_UnderkantWithReason_ReturnsFailedAndReason()
    {
        GoalAgentService.TryParseVerdict(
            "RESULTAT: UNDERKÄNT - filen recept.txt saknas helt", out var passed, out var reason).Should().BeTrue();

        passed.Should().BeFalse();
        reason.Should().Be("filen recept.txt saknas helt");
    }

    [Fact]
    public void TryParseVerdict_AsciiSpellingWithoutDiacritics_IsAccepted()
    {
        GoalAgentService.TryParseVerdict("resultat: godkant", out var passed, out _).Should().BeTrue();

        passed.Should().BeTrue();
    }

    [Fact]
    public void TryParseVerdict_ResponseEchoingBothInstructionWords_PrefersResultatLine()
    {
        // Models often repeat the instructions ("svara GODKÄNT eller UNDERKÄNT") before the
        // actual verdict line — only the RESULTAT line should decide.
        var response =
            "Jag ska svara med GODKÄNT eller UNDERKÄNT beroende på kontrollen.\n" +
            "Filen finns och innehåller det som efterfrågas.\n" +
            "RESULTAT: GODKÄNT";

        GoalAgentService.TryParseVerdict(response, out var passed, out _).Should().BeTrue();

        passed.Should().BeTrue();
    }

    [Fact]
    public void TryParseVerdict_BothWordsOnSameLine_FailWins()
    {
        GoalAgentService.TryParseVerdict(
            "RESULTAT: GODKÄNT eller UNDERKÄNT, svårt att säga", out var passed, out _).Should().BeTrue();

        passed.Should().BeFalse("an ambiguous verdict must never count as green");
    }

    [Fact]
    public void TryParseVerdict_NoVerdictWord_ReturnsFalse()
    {
        GoalAgentService.TryParseVerdict("Allt ser bra ut tycker jag!", out _, out _).Should().BeFalse();
    }

    [Fact]
    public void TryParseVerdict_EnglishFailedWithReason_ReturnsFailedAndReason()
    {
        GoalAgentService.TryParseVerdict(
            "RESULT: FAILED - the file number.txt is missing", out var passed, out var reason).Should().BeTrue();

        passed.Should().BeFalse();
        reason.Should().Be("the file number.txt is missing");
    }

    [Fact]
    public void TryParseVerdict_EnglishPassed_ReturnsPassed()
    {
        GoalAgentService.TryParseVerdict("RESULT: PASSED", out var passed, out _).Should().BeTrue();

        passed.Should().BeTrue();
    }

    [Fact]
    public void TryParseVerdict_PassTokenInsideAnotherWord_DoesNotCount()
    {
        // "surpass" must not match the PASS token (word-start boundary check).
        GoalAgentService.TryParseVerdict(
            "The results surpass all expectations.", out _, out _).Should().BeFalse();
    }

    [Fact]
    public void TryParseVerdict_UnderkantWithoutReason_ParsesWithEmptyReason()
    {
        GoalAgentService.TryParseVerdict("RESULTAT: UNDERKÄNT", out var passed, out var reason).Should().BeTrue();

        passed.Should().BeFalse();
        reason.Should().BeEmpty();
    }

    // ── RunAsync: happy path ──────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_AllRequirementsPassFirstIteration_CompletesGreen()
    {
        var fake = new FakeAgenticChatService(msg =>
            IsVerifyPrompt(msg) ? Result("RESULTAT: GODKÄNT") : Result("klart"));
        var sut = new GoalAgentService(fake);

        await sut.RunAsync("Skapa ett recept på pannkakor i recept.txt", TwoRequirementsLlm);

        sut.Phase.Should().Be(GoalAgentPhase.Completed);
        sut.IsRunning.Should().BeFalse();
        sut.CurrentIteration.Should().Be(1);
        sut.GoalDescription.Should().Be("Skapa ett recept på pannkakor i recept.txt");
        sut.Requirements.Should().HaveCount(2);
        sut.Requirements.Should().OnlyContain(r => r.Status == RequirementStatus.Passed);
        // 1 combined work call (all unmet requirements in one step) + 2 verify calls.
        fake.ReceivedMessages.Should().HaveCount(3);
        fake.ReceivedMessages.Take(1).Should().OnlyContain(m => IsWorkPrompt(m));
        fake.ReceivedMessages.Skip(1).Should().OnlyContain(m => IsVerifyPrompt(m));
        // The combined work prompt carries both requirements.
        fake.ReceivedMessages[0].Should().Contain("recept.txt finns").And.Contain("ingredienslista");
    }

    // ── RunAsync: red → green feedback loop ───────────────────────────────

    [Fact]
    public async Task RunAsync_RequirementFailsThenPasses_SecondWorkPromptCarriesFailureReason()
    {
        var verifyCallsForSecond = 0;
        var fake = new FakeAgenticChatService(msg =>
        {
            if (!IsVerifyPrompt(msg))
                return Result("klart");

            if (!msg.Contains("ingredienslista"))
                return Result("RESULTAT: GODKÄNT");

            // Second requirement: red on the first check, green on the second.
            verifyCallsForSecond++;
            return verifyCallsForSecond == 1
                ? Result("RESULTAT: UNDERKÄNT - ingredienslistan saknas")
                : Result("RESULTAT: GODKÄNT");
        });
        var sut = new GoalAgentService(fake);

        await sut.RunAsync("Skapa ett pannkaksrecept", TwoRequirementsLlm);

        sut.Phase.Should().Be(GoalAgentPhase.Completed);
        sut.CurrentIteration.Should().Be(2);
        sut.Requirements.Should().OnlyContain(r => r.Status == RequirementStatus.Passed);

        // The iteration-2 work prompt for the failing requirement must feed the failure
        // motivation back to the model — the "failing test message" of the loop.
        var retryWorkPrompt = fake.ReceivedMessages
            .Last(m => IsWorkPrompt(m) && m.Contains("ingredienslista"));
        retryWorkPrompt.Should().Contain("ingredienslistan saknas");
    }

    [Fact]
    public async Task RunAsync_SecondIteration_ReverifiesAlreadyPassedRequirements()
    {
        var fake = new FakeAgenticChatService(msg =>
        {
            if (!IsVerifyPrompt(msg))
                return Result("klart");
            // First requirement always green; second always red → run uses all iterations.
            return msg.Contains("ingredienslista")
                ? Result("RESULTAT: UNDERKÄNT - saknas")
                : Result("RESULTAT: GODKÄNT");
        });
        var sut = new GoalAgentService(fake, maxIterations: 2);

        await sut.RunAsync("Skapa ett pannkaksrecept", TwoRequirementsLlm);

        // The already-green first requirement is re-verified on every iteration (later work
        // can break earlier requirements), so it appears in two verify prompts.
        fake.ReceivedMessages
            .Count(m => IsVerifyPrompt(m) && m.Contains("recept.txt finns"))
            .Should().Be(2);

        // Only the failing requirement is worked on again in iteration 2.
        fake.ReceivedMessages
            .Count(m => IsWorkPrompt(m) && m.Contains("recept.txt finns"))
            .Should().Be(1);
        fake.ReceivedMessages
            .Count(m => IsWorkPrompt(m) && m.Contains("ingredienslista"))
            .Should().Be(2);
    }

    // ── RunAsync: iteration cap / failure modes ───────────────────────────

    [Fact]
    public async Task RunAsync_RequirementNeverPasses_StopsAtMaxIterations()
    {
        var fake = new FakeAgenticChatService(msg =>
            IsVerifyPrompt(msg) ? Result("RESULTAT: UNDERKÄNT - fel innehåll") : Result("klart"));
        var sut = new GoalAgentService(fake, maxIterations: 2);

        await sut.RunAsync("Skapa ett pannkaksrecept", TwoRequirementsLlm);

        sut.Phase.Should().Be(GoalAgentPhase.MaxIterationsReached);
        sut.CurrentIteration.Should().Be(2);
        sut.Requirements.Should().OnlyContain(r => r.Status == RequirementStatus.Failed);
        sut.Requirements.Should().OnlyContain(r => r.LastVerdict == "fel innehåll");
    }

    [Fact]
    public async Task RunAsync_MaxIterationsOverride_StopsAtPerRunCapNotConstructorDefault()
    {
        // The constructor default (20) would keep going; the per-run override should win.
        var fake = new FakeAgenticChatService(msg =>
            IsVerifyPrompt(msg) ? Result("RESULTAT: UNDERKÄNT - fel innehåll") : Result("klart"));
        var sut = new GoalAgentService(fake, maxIterations: 20);

        await sut.RunAsync("Skapa ett pannkaksrecept", TwoRequirementsLlm, maxIterations: 2);

        sut.Phase.Should().Be(GoalAgentPhase.MaxIterationsReached);
        sut.CurrentIteration.Should().Be(2);
        sut.MaxIterations.Should().Be(2);
    }

    [Fact]
    public async Task RunAsync_NonPositiveMaxIterationsOverride_FallsBackToConstructorDefault()
    {
        var fake = new FakeAgenticChatService(msg =>
            IsVerifyPrompt(msg) ? Result("RESULTAT: GODKÄNT") : Result("klart"));
        var sut = new GoalAgentService(fake, maxIterations: 3);

        await sut.RunAsync("Skapa ett pannkaksrecept", TwoRequirementsLlm, maxIterations: 0);

        sut.MaxIterations.Should().Be(3);
    }

    [Fact]
    public async Task RunAsync_UnparseableVerdict_CountsAsFailedNotGreen()
    {
        var fake = new FakeAgenticChatService(msg =>
            IsVerifyPrompt(msg) ? Result("Allt ser toppen ut!") : Result("klart"));
        var sut = new GoalAgentService(fake, maxIterations: 1);

        await sut.RunAsync("Skapa ett pannkaksrecept", TwoRequirementsLlm);

        sut.Phase.Should().Be(GoalAgentPhase.MaxIterationsReached);
        sut.Requirements.Should().OnlyContain(r => r.Status == RequirementStatus.Failed);
        sut.Requirements.Should().OnlyContain(r => !string.IsNullOrWhiteSpace(r.LastVerdict));
        sut.Requirements.Should().OnlyContain(r => r.VerdictInconclusive);
    }

    [Fact]
    public async Task RunAsync_UnparseableVerdict_IsRetriedOnceAndCanPassOnSecondAttempt()
    {
        // Empty/garbled review replies are intermittent (temperature 1.0), so one immediate
        // retry usually recovers a real verdict — here the second attempt says GODKÄNT.
        var verifyCalls = 0;
        var fake = new FakeAgenticChatService(msg =>
        {
            if (!IsVerifyPrompt(msg))
                return Result("klart");
            verifyCalls++;
            return verifyCalls == 1 ? Result(string.Empty) : Result("RESULTAT: GODKÄNT");
        });
        var sut = new GoalAgentService(fake, maxIterations: 1);

        await sut.RunAsync("Skapa filen a.txt", _ => Task.FromResult("KRAV: Filen a.txt finns i arbetsytan."));

        sut.Phase.Should().Be(GoalAgentPhase.Completed);
        sut.Requirements.Should().ContainSingle().Which.Status.Should().Be(RequirementStatus.Passed);
        verifyCalls.Should().Be(2);
    }

    [Fact]
    public async Task RunAsync_PersistentlyUnparseableVerdict_SkipsReworkAndOmitsMetatextFromWorkPrompts()
    {
        // When even the retried verification is unreadable there is no concrete defect to fix.
        // The next iteration must NOT rework the file (blind edits have corrupted a good file in
        // a real run) and must NOT present the parse diagnostic as a review motivation.
        var fake = new FakeAgenticChatService(msg =>
            IsVerifyPrompt(msg) ? Result("<|channel>thought") : Result("klart"));
        var sut = new GoalAgentService(fake, maxIterations: 2);

        await sut.RunAsync("Skapa filen a.txt", _ => Task.FromResult("KRAV: Filen a.txt finns i arbetsytan."));

        sut.Phase.Should().Be(GoalAgentPhase.MaxIterationsReached);

        // Worked once (initial Unverified state), then skipped in iteration 2.
        fake.ReceivedMessages.Count(IsWorkPrompt).Should().Be(1);
        // Verified with one retry per iteration: 2 iterations × 2 attempts.
        fake.ReceivedMessages.Count(IsVerifyPrompt).Should().Be(4);
        // The parse diagnostic must never be dressed up as a failure motivation.
        fake.ReceivedMessages.Where(IsWorkPrompt).Should()
            .OnlyContain(m => !m.Contains("kunde inte tolkas"));
        sut.ActivityLog.Should().Contain(line => line.Contains("Ingen åtgärd"));
    }

    [Fact]
    public async Task RunAsync_AgenticChatThrows_EndsInFailedPhaseWithoutThrowing()
    {
        Func<string, AgenticChatResult> alwaysThrows = _ => throw new InvalidOperationException("model backend unavailable");
        var fake = new FakeAgenticChatService(alwaysThrows);
        var sut = new GoalAgentService(fake);

        await sut.RunAsync("Skapa ett pannkaksrecept", TwoRequirementsLlm);

        sut.Phase.Should().Be(GoalAgentPhase.Failed);
        sut.IsRunning.Should().BeFalse();
        sut.ActivityLog.Should().Contain(line => line.Contains("model backend unavailable"));
    }

    // ── RunAsync: requirement generation fallback ─────────────────────────

    [Fact]
    public async Task RunAsync_NoKravLinesInResponse_FallsBackToGoalAsSingleRequirement()
    {
        var fake = new FakeAgenticChatService(msg =>
            IsVerifyPrompt(msg) ? Result("RESULTAT: GODKÄNT") : Result("klart"));
        var sut = new GoalAgentService(fake);

        await sut.RunAsync(
            "Skapa filen hej.txt med en hälsning",
            _ => Task.FromResult("Jag förstår inte formatet, men jag ska göra mitt bästa!"));

        sut.Requirements.Should().ContainSingle()
            .Which.Description.Should().Be("Skapa filen hej.txt med en hälsning");
        sut.Phase.Should().Be(GoalAgentPhase.Completed);
    }

    // ── Tool-call visibility ──────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_ExecutedToolCommands_AppearInActivityLog()
    {
        var fake = new FakeAgenticChatService(msg =>
            IsVerifyPrompt(msg)
                ? new AgenticChatResult("RESULTAT: GODKÄNT", new[] { new ToolInvocation("/lista", "2 filer") })
                : new AgenticChatResult("klart", new[] { new ToolInvocation("/skapa number.txt", "✓ Fil skapad: number.txt") }));
        var sut = new GoalAgentService(fake);

        await sut.RunAsync("Skapa number.txt", TwoRequirementsLlm);

        sut.ActivityLog.Should().Contain("🔧 /skapa number.txt → ✓ Fil skapad: number.txt");
        sut.ActivityLog.Should().Contain("🔧 /lista → 2 filer");
    }

    [Fact]
    public async Task RunAsync_WorkStepWithoutToolCalls_LogsWarningThatNothingChanged()
    {
        // The model only *talked* about the task instead of using a tool — the key failure
        // mode when nothing shows up in the workspace. The run must say so explicitly.
        var fake = new FakeAgenticChatService(msg =>
            IsVerifyPrompt(msg) ? Result("RESULTAT: GODKÄNT") : Result("Jag skulle skapa filen så här: ..."));
        var sut = new GoalAgentService(fake);

        await sut.RunAsync("Skapa number.txt", TwoRequirementsLlm);

        sut.ActivityLog.Should().Contain(line => line.Contains("Inga verktygskommandon kördes"));
    }

    // ── Recovery: full-file content delivered without a tool command ──────

    /// <summary>
    /// Reproduces the exact tail-end failure of a real run: after diagnosing a broken file, the
    /// model's final reply contained a complete, correct rewrite — but as a plain Markdown code
    /// fence instead of a <c>/fyll</c> or <c>/redigera</c> command. Nothing in the workspace
    /// tools recognises "no command at all", so the fix was silently discarded and the run hit
    /// its iteration cap one exchange later with the old, still-broken file untouched.
    /// </summary>
    private const string FencedGameRewrite =
        "Har ar den uppdaterade koden:\n```qbasic\nSCREEN 0\nPRINT \"UPPDATERAD\"\nPRINT \"INNEHALL\"\n```\n";

    [Fact]
    public async Task RunAsync_WorkReplyHasFullFileCodeFenceButNoToolCommand_RecoversAndAppliesIt()
    {
        var workspaceDir = Path.Combine(Path.GetTempPath(), "GoalAgentTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(workspaceDir);
            await File.WriteAllTextAsync(Path.Combine(workspaceDir, "game.bas"), "GAMMALT INNEHALL");
            var fileAgent = new FileAgentService(workspaceDir);
            var fake = new FakeAgenticChatService(msg =>
                IsVerifyPrompt(msg) ? Result("RESULTAT: GODKÄNT") : Result(FencedGameRewrite));
            var sut = new GoalAgentService(fake, fileAgent, maxIterations: 1);

            await sut.RunAsync(
                "Fixa game.bas",
                _ => Task.FromResult("KRAV: game.bas innehåller ett fungerande spel."));

            var written = await File.ReadAllTextAsync(Path.Combine(workspaceDir, "game.bas"));
            written.Should().Contain("UPPDATERAD").And.Contain("INNEHALL").And.NotContain("GAMMALT");

            sut.ActivityLog.Should().Contain(line => line.Contains("räddad filskrivning") && line.Contains("game.bas"));
            sut.ActivityLog.Should().NotContain(line => line.Contains("Inga verktygskommandon kördes"));
            sut.Phase.Should().Be(GoalAgentPhase.Completed);
        }
        finally
        {
            if (Directory.Exists(workspaceDir))
                Directory.Delete(workspaceDir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_RecoveredBasRewriteWithInventedKeyword_IsReportedByTheStructuralCheck()
    {
        // This recovery path writes straight through IFileAgentService, bypassing the tool loop
        // that normally runs the QBasic check — without its own call it would be the one write in
        // a run that nothing ever looks at, compiler configured or not.
        var workspaceDir = Path.Combine(Path.GetTempPath(), "GoalAgentTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(workspaceDir);
            await File.WriteAllTextAsync(Path.Combine(workspaceDir, "game.bas"), "GAMMALT INNEHALL");
            var fileAgent = new FileAgentService(workspaceDir);
            var brokenRewrite = "Har ar koden:\n```qbasic\nSCREEN 12\n_LINE (1, 1), (2, 2), 1\nEND\n```\n";
            var fake = new FakeAgenticChatService(msg =>
                IsVerifyPrompt(msg) ? Result("RESULTAT: GODKÄNT") : Result(brokenRewrite));
            var sut = new GoalAgentService(fake, fileAgent, maxIterations: 1);

            await sut.RunAsync(
                "Fixa game.bas",
                _ => Task.FromResult("KRAV: game.bas innehåller ett fungerande spel."));

            sut.ActivityLog.Should().Contain(line =>
                line.Contains("räddad filskrivning") && line.Contains("Strukturkontroll") && line.Contains("\"LINE\""));
        }
        finally
        {
            if (Directory.Exists(workspaceDir))
                Directory.Delete(workspaceDir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_WorkReplyHasCodeFence_ButTwoCandidateFilesExist_DoesNotGuessAndSkipsRecovery()
    {
        var workspaceDir = Path.Combine(Path.GetTempPath(), "GoalAgentTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(workspaceDir);
            await File.WriteAllTextAsync(Path.Combine(workspaceDir, "a.bas"), "A");
            await File.WriteAllTextAsync(Path.Combine(workspaceDir, "b.bas"), "B");
            var fileAgent = new FileAgentService(workspaceDir);
            var fake = new FakeAgenticChatService(msg =>
                IsVerifyPrompt(msg) ? Result("RESULTAT: UNDERKÄNT - fel") : Result(FencedGameRewrite));
            var sut = new GoalAgentService(fake, fileAgent, maxIterations: 1);

            await sut.RunAsync(
                "Fixa a.bas och b.bas",
                _ => Task.FromResult("KRAV: a.bas och b.bas innehåller fungerande kod."));

            (await File.ReadAllTextAsync(Path.Combine(workspaceDir, "a.bas"))).Should().Be("A");
            (await File.ReadAllTextAsync(Path.Combine(workspaceDir, "b.bas"))).Should().Be("B");
            sut.ActivityLog.Should().Contain(line => line.Contains("Inga verktygskommandon kördes"));
        }
        finally
        {
            if (Directory.Exists(workspaceDir))
                Directory.Delete(workspaceDir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_WorkReplyHasTinyInlineSnippet_IsNotTreatedAsFullFileRewrite()
    {
        var workspaceDir = Path.Combine(Path.GetTempPath(), "GoalAgentTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(workspaceDir);
            await File.WriteAllTextAsync(Path.Combine(workspaceDir, "game.bas"), "GAMMALT INNEHALL");
            var fileAgent = new FileAgentService(workspaceDir);
            // Only one non-blank line inside the fence — too small to confidently treat as a
            // full-file rewrite rather than a small illustrative example.
            var fake = new FakeAgenticChatService(msg =>
                IsVerifyPrompt(msg) ? Result("RESULTAT: UNDERKÄNT - fel") : Result("Till exempel:\n```\nPRINT 1\n```\n"));
            var sut = new GoalAgentService(fake, fileAgent, maxIterations: 1);

            await sut.RunAsync(
                "Fixa game.bas",
                _ => Task.FromResult("KRAV: game.bas innehåller ett fungerande spel."));

            (await File.ReadAllTextAsync(Path.Combine(workspaceDir, "game.bas"))).Should().Be("GAMMALT INNEHALL");
            sut.ActivityLog.Should().Contain(line => line.Contains("Inga verktygskommandon kördes"));
        }
        finally
        {
            if (Directory.Exists(workspaceDir))
                Directory.Delete(workspaceDir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_ToolAlreadyRanButFinalReplyAlsoHasFullFileFence_StillRecoversOnTop()
    {
        // Mirrors the real run exactly: a /redigera call already executed earlier in the same
        // work step, and the model's subsequent final answer (no further command) contained a
        // complete replacement — which must still be recovered rather than discarded just
        // because *some* tool already ran this round.
        var workspaceDir = Path.Combine(Path.GetTempPath(), "GoalAgentTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(workspaceDir);
            await File.WriteAllTextAsync(Path.Combine(workspaceDir, "game.bas"), "GAMMALT INNEHALL");
            var fileAgent = new FileAgentService(workspaceDir);
            var fake = new FakeAgenticChatService(msg =>
                IsVerifyPrompt(msg)
                    ? Result("RESULTAT: GODKÄNT")
                    : new AgenticChatResult(FencedGameRewrite, new[] { new ToolInvocation("/redigera game.bas ...", "✓ Fil redigerad: game.bas (rad 1)") }));
            var sut = new GoalAgentService(fake, fileAgent, maxIterations: 1);

            await sut.RunAsync(
                "Fixa game.bas",
                _ => Task.FromResult("KRAV: game.bas innehåller ett fungerande spel."));

            var written = await File.ReadAllTextAsync(Path.Combine(workspaceDir, "game.bas"));
            written.Should().Contain("UPPDATERAD");

            sut.ActivityLog.Should().Contain(line => line.Contains("/redigera game.bas"));
            sut.ActivityLog.Should().Contain(line => line.Contains("räddad filskrivning"));
        }
        finally
        {
            if (Directory.Exists(workspaceDir))
                Directory.Delete(workspaceDir, recursive: true);
        }
    }

    // ── Workspace transcript ──────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_WithFileAgent_WritesFullTranscriptToWorkspace()
    {
        var workspaceDir = Path.Combine(Path.GetTempPath(), "GoalAgentTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            var fileAgent = new FileAgentService(workspaceDir);
            // The first requirement ("Filen recept.txt finns...") is now checked directly on
            // disk, so the file must actually exist for the run to complete green.
            Directory.CreateDirectory(workspaceDir);
            await File.WriteAllTextAsync(Path.Combine(workspaceDir, "recept.txt"), "Pannkakor: mjölk, ägg, mjöl");
            var fake = new FakeAgenticChatService(msg =>
                IsVerifyPrompt(msg) ? Result("RESULTAT: GODKÄNT") : Result("klart"));
            var sut = new GoalAgentService(fake, fileAgent);

            await sut.RunAsync("Skapa ett pannkaksrecept i recept.txt", TwoRequirementsLlm);

            var transcriptPath = Path.Combine(workspaceDir, GoalAgentService.TranscriptFileName);
            File.Exists(transcriptPath).Should().BeTrue();
            var transcript = await File.ReadAllTextAsync(transcriptPath);
            transcript.Should().Contain("Skapa ett pannkaksrecept i recept.txt");   // run header with the goal
            transcript.Should().Contain("PROMPT");                                  // requirement-generation prompt
            transcript.Should().Contain("SVAR");                                    // raw LLM reply
            transcript.Should().Contain("KRAV");                                    // parsed requirement list
            transcript.Should().Contain("BEDÖMNING");                               // per-requirement verdicts
        }
        finally
        {
            if (Directory.Exists(workspaceDir))
                Directory.Delete(workspaceDir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_WithoutFileAgent_WritesNoTranscript()
    {
        var fake = new FakeAgenticChatService(msg =>
            IsVerifyPrompt(msg) ? Result("RESULTAT: GODKÄNT") : Result("klart"));
        var sut = new GoalAgentService(fake);

        await sut.RunAsync("Skapa ett pannkaksrecept", TwoRequirementsLlm);

        sut.ActivityLog.Should().NotContain(line => line.Contains("Transkript"));
        sut.Phase.Should().Be(GoalAgentPhase.Completed);
    }

    // ── RequestStop / concurrent runs ─────────────────────────────────────

    [Fact]
    public async Task RequestStop_DuringWork_HaltsBeforeNextRequirement()
    {
        GoalAgentService? sut = null;
        var fake = new FakeAgenticChatService(msg =>
        {
            if (IsWorkPrompt(msg))
                sut!.RequestStop();
            return Result("klart");
        });
        sut = new GoalAgentService(fake);

        await sut.RunAsync("Skapa ett pannkaksrecept", TwoRequirementsLlm);

        sut.Phase.Should().Be(GoalAgentPhase.Stopped);
        sut.IsRunning.Should().BeFalse();
        // Only the first requirement's work call ran — no second work call, no verification.
        fake.ReceivedMessages.Should().ContainSingle(m => IsWorkPrompt(m));
    }

    [Fact]
    public async Task RunAsync_AlreadyRunning_SecondCallIsNoOp()
    {
        var gate = new TaskCompletionSource();
        var fake = new FakeAgenticChatService(async msg =>
        {
            if (IsWorkPrompt(msg))
                await gate.Task;
            return Result(IsVerifyPrompt(msg) ? "RESULTAT: GODKÄNT" : "klart");
        });
        var sut = new GoalAgentService(fake);

        var firstRun = sut.RunAsync("Mål ett", TwoRequirementsLlm);
        while (!sut.IsRunning) await Task.Delay(5);

        await sut.RunAsync("Mål två", TwoRequirementsLlm); // no-op while the first run is active

        sut.GoalDescription.Should().Be("Mål ett");

        gate.SetResult();
        await firstRun;

        sut.Phase.Should().Be(GoalAgentPhase.Completed);
    }

    // ── Reset ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reset_AfterCompletedRun_ClearsStateBackToIdle()
    {
        var fake = new FakeAgenticChatService(msg =>
            IsVerifyPrompt(msg) ? Result("RESULTAT: GODKÄNT") : Result("klart"));
        var sut = new GoalAgentService(fake);
        await sut.RunAsync("Skapa ett pannkaksrecept", TwoRequirementsLlm);

        sut.Reset();

        sut.Phase.Should().Be(GoalAgentPhase.Idle);
        sut.Requirements.Should().BeEmpty();
        sut.ActivityLog.Should().BeEmpty();
        sut.GoalDescription.Should().BeNull();
        sut.CurrentIteration.Should().Be(0);
    }

    [Fact]
    public async Task Reset_WhileRunning_IsIgnored()
    {
        var gate = new TaskCompletionSource();
        var fake = new FakeAgenticChatService(async msg =>
        {
            if (IsWorkPrompt(msg))
                await gate.Task;
            return Result(IsVerifyPrompt(msg) ? "RESULTAT: GODKÄNT" : "klart");
        });
        var sut = new GoalAgentService(fake);

        var run = sut.RunAsync("Skapa ett pannkaksrecept", TwoRequirementsLlm);
        while (!sut.IsRunning) await Task.Delay(5);

        sut.Reset();

        sut.GoalDescription.Should().Be("Skapa ett pannkaksrecept", "reset must not clear an active run");
        sut.Requirements.Should().NotBeEmpty();

        gate.SetResult();
        await run;
    }

    // ── OnChange ──────────────────────────────────────────────────────────

    [Fact]
    public async Task OnChange_RaisedThroughoutRun()
    {
        var fake = new FakeAgenticChatService(msg =>
            IsVerifyPrompt(msg) ? Result("RESULTAT: GODKÄNT") : Result("klart"));
        var sut = new GoalAgentService(fake);
        var changeCount = 0;
        sut.OnChange += () => changeCount++;

        await sut.RunAsync("Skapa ett pannkaksrecept", TwoRequirementsLlm);

        changeCount.Should().BeGreaterThan(3, "phase changes and per-requirement status flips should all notify");
    }

    // ── Run history (IAgentRunRepository) ─────────────────────────────────

    [Fact]
    public async Task RunAsync_WithRepository_RecordsRunWithCallerSuppliedMetadata()
    {
        var fake = new FakeAgenticChatService(msg =>
            IsVerifyPrompt(msg) ? Result("RESULTAT: GODKÄNT") : Result("klart"));
        var repository = new FakeAgentRunRepository();
        var conversationId = Guid.NewGuid();
        var sut = new GoalAgentService(fake, runRepository: repository);

        await sut.RunAsync(
            "Skapa ett pannkaksrecept",
            TwoRequirementsLlm,
            modelName: "gemma-4.gguf",
            conversationId: conversationId);

        var run = repository.StartedRuns.Should().ContainSingle().Subject;
        run.GoalDescription.Should().Be("Skapa ett pannkaksrecept");
        run.ModelName.Should().Be("gemma-4.gguf");
        run.ConversationId.Should().Be(conversationId);
        run.MaxIterations.Should().Be(sut.MaxIterations);
    }

    [Fact]
    public async Task RunAsync_WithRepository_RecordsRequirementsWithFinalVerdicts()
    {
        var fake = new FakeAgenticChatService(msg =>
        {
            if (!IsVerifyPrompt(msg))
                return Result("klart");
            return msg.Contains("ingredienslista")
                ? Result("RESULTAT: UNDERKÄNT - saknas")
                : Result("RESULTAT: GODKÄNT");
        });
        var repository = new FakeAgentRunRepository();
        var sut = new GoalAgentService(fake, maxIterations: 1, runRepository: repository);

        await sut.RunAsync("Skapa ett pannkaksrecept", TwoRequirementsLlm);

        repository.SavedRequirements.Should().HaveCount(2);
        repository.SavedRequirements.Should().OnlyContain(r => r.RunId == repository.StartedRuns[0].Id);
        repository.SavedRequirements.Select(r => r.Ordinal).Should().Equal(1, 2);

        var passed = repository.SavedRequirements.Single(r => r.Description.Contains("recept.txt finns"));
        passed.Status.Should().Be(nameof(RequirementStatus.Passed));
        passed.LastVerdict.Should().BeNull();

        var failed = repository.SavedRequirements.Single(r => r.Description.Contains("ingredienslista"));
        failed.Status.Should().Be(nameof(RequirementStatus.Failed));
        failed.LastVerdict.Should().Be("saknas");
    }

    [Fact]
    public async Task RunAsync_WithRepository_RecordsActivityLogAsOrderedEvents()
    {
        var fake = new FakeAgenticChatService(msg =>
            IsVerifyPrompt(msg) ? Result("RESULTAT: GODKÄNT") : Result("klart"));
        var repository = new FakeAgentRunRepository();
        var sut = new GoalAgentService(fake, runRepository: repository);

        await sut.RunAsync("Skapa ett pannkaksrecept", TwoRequirementsLlm);

        // Every recorded event belongs to the run and carries a gap-free sequence, so the stored
        // order is the order things happened rather than the order the batches were flushed.
        repository.SavedEvents.Should().OnlyContain(e => e.RunId == repository.StartedRuns[0].Id);
        repository.SavedEvents.Select(e => e.Sequence).Should().Equal(Enumerable.Range(1, repository.SavedEvents.Count));

        // The event log is the activity log — the whole point is being able to read it back later.
        repository.SavedEvents.Select(e => e.Message).Should().Equal(sut.ActivityLog);
        repository.SavedEvents.Should().Contain(e => e.EventType == AgentRunEventTypes.Verdict);
    }

    [Fact]
    public async Task RunAsync_WithRepository_RecordsTerminalPhaseAndIterationCount()
    {
        var fake = new FakeAgenticChatService(msg =>
            IsVerifyPrompt(msg) ? Result("RESULTAT: UNDERKÄNT - fel innehåll") : Result("klart"));
        var repository = new FakeAgentRunRepository();
        var sut = new GoalAgentService(fake, maxIterations: 2, runRepository: repository);

        await sut.RunAsync("Skapa ett pannkaksrecept", TwoRequirementsLlm);

        var completed = repository.CompletedRuns.Should().ContainSingle().Subject;
        completed.RunId.Should().Be(repository.StartedRuns[0].Id);
        completed.Phase.Should().Be(nameof(GoalAgentPhase.MaxIterationsReached));
        completed.Iterations.Should().Be(2);
    }

    [Fact]
    public async Task RunAsync_RepositoryThrows_RunStillCompletesAndWarnsOnce()
    {
        var fake = new FakeAgenticChatService(msg =>
            IsVerifyPrompt(msg) ? Result("RESULTAT: GODKÄNT") : Result("klart"));
        var repository = new FakeAgentRunRepository { WriteFailure = new InvalidOperationException("db down") };
        var sut = new GoalAgentService(fake, runRepository: repository);

        await sut.RunAsync("Skapa ett pannkaksrecept", TwoRequirementsLlm);

        // Losing history must never cost the run itself — that's the expensive part.
        sut.Phase.Should().Be(GoalAgentPhase.Completed);
        sut.Requirements.Should().OnlyContain(r => r.Status == RequirementStatus.Passed);
        sut.ActivityLog.Should().ContainSingle(l => l.Contains("Kunde inte spara agentkörningen"));
    }

    [Fact]
    public async Task RunAsync_RepositoryFailsMidRun_StopsRecordingWithoutBreakingTheRun()
    {
        var repository = new FakeAgentRunRepository();
        // Let the run start cleanly, then take the database away once work begins.
        var fake = new FakeAgenticChatService(msg =>
        {
            repository.WriteFailure = new InvalidOperationException("db down");
            return Result(IsVerifyPrompt(msg) ? "RESULTAT: GODKÄNT" : "klart");
        });
        var sut = new GoalAgentService(fake, runRepository: repository);

        await sut.RunAsync("Skapa ett pannkaksrecept", TwoRequirementsLlm);

        sut.Phase.Should().Be(GoalAgentPhase.Completed);
        repository.StartedRuns.Should().ContainSingle("the run row was written before the database went away");
        repository.CompletedRuns.Should().BeEmpty("recording is disabled after the first failed write");
        sut.ActivityLog.Should().ContainSingle(l => l.Contains("Kunde inte spara körningshistoriken"),
            "the user should be told once, not once per log line");
    }

    [Fact]
    public async Task RunAsync_WithoutRepository_RunsNormally()
    {
        var fake = new FakeAgenticChatService(msg =>
            IsVerifyPrompt(msg) ? Result("RESULTAT: GODKÄNT") : Result("klart"));
        var sut = new GoalAgentService(fake);

        await sut.RunAsync("Skapa ett pannkaksrecept", TwoRequirementsLlm);

        sut.Phase.Should().Be(GoalAgentPhase.Completed);
    }

    // ── ScrubLeakedModelTokens ────────────────────────────────────────────

    [Fact]
    public void ScrubLeakedModelTokens_PlainReply_IsUnchanged()
    {
        GoalAgentService.ScrubLeakedModelTokens("RESULTAT: GODKÄNT\nAllt ser bra ut.")
            .Should().Be("RESULTAT: GODKÄNT\nAllt ser bra ut.");
    }

    [Fact]
    public void ScrubLeakedModelTokens_UnclosedReasoningChannel_BecomesEmpty()
    {
        // Observed leak in a real run: the reply was only "<|channel>thought" — unterminated
        // reasoning, no answer. It must scrub to empty so the retry loop treats it as such.
        GoalAgentService.ScrubLeakedModelTokens("<|channel>thought").Should().BeEmpty();
    }

    [Fact]
    public void ScrubLeakedModelTokens_ClosedReasoningChannel_KeepsOnlyTheAnswer()
    {
        GoalAgentService.ScrubLeakedModelTokens("<|channel>tänker högt...<channel|>RESULTAT: GODKÄNT")
            .Should().Be("RESULTAT: GODKÄNT");
    }

    [Fact]
    public void ScrubLeakedModelTokens_DanglingToolCall_IsCutAway()
    {
        // Observed leak: "<|tool_call>call: /lista <tool_call|><|tool_response>" — tool plumbing
        // with nothing wired to service it is not an answer.
        GoalAgentService.ScrubLeakedModelTokens("<|tool_call>call: /lista <tool_call|><|tool_response>")
            .Should().BeEmpty();

        GoalAgentService.ScrubLeakedModelTokens("RESULTAT: GODKÄNT\n<|tool_call>call: /lista <tool_call|>")
            .Should().Be("RESULTAT: GODKÄNT");
    }

    // ── Empty-reply retry (call level) ────────────────────────────────────

    [Fact]
    public async Task RunAsync_EmptyLlmReplies_AreRetriedBeforeParsing()
    {
        // Empty replies are intermittent at temperature 1.0. The first two requirement-
        // generation attempts return nothing; the third returns a real KRAV line — the run
        // must recover instead of falling back to the whole goal as a single requirement.
        var llmCalls = 0;
        Task<string> FlakyLlm(string prompt)
        {
            llmCalls++;
            return Task.FromResult(llmCalls < 3 ? string.Empty : "KRAV: Filen a.txt finns i arbetsytan.");
        }

        var fake = new FakeAgenticChatService(msg =>
            IsVerifyPrompt(msg) ? Result("RESULTAT: GODKÄNT") : Result("klart"));
        var sut = new GoalAgentService(fake, maxIterations: 1);

        await sut.RunAsync("Skapa filen a.txt", FlakyLlm);

        llmCalls.Should().Be(3, "two empty replies should each trigger a retry of the same prompt");
        sut.Requirements.Should().ContainSingle()
            .Which.Description.Should().Be("Filen a.txt finns i arbetsytan.");
        sut.ActivityLog.Should().Contain(line => line.Contains("tomt"));
    }

    // ── TryParseFileExistenceRequirement ──────────────────────────────────

    [Theory]
    [InlineData("Filen calc.bas finns i arbetsytan.", "calc.bas")]
    [InlineData("1. Filen calc.bas finns i arbetsytan", "calc.bas")]
    [InlineData("filen notes.txt finns.", "notes.txt")]
    [InlineData("The file notes.txt exists in the workspace.", "notes.txt")]
    public void TryParseFileExistenceRequirement_PureExistenceShapes_AreParsed(string description, string expected)
    {
        GoalAgentService.TryParseFileExistenceRequirement(description, out var filename).Should().BeTrue();
        filename.Should().Be(expected);
    }

    [Theory]
    [InlineData("Filen calc.bas finns och innehåller kod.")]           // content condition
    [InlineData("calc.bas innehåller en miniräknare.")]                // content requirement
    [InlineData("Filen finns i arbetsytan.")]                          // no filename
    [InlineData("The file readme.md contains a heading and exists.")]  // content condition (en)
    public void TryParseFileExistenceRequirement_NonPureShapes_AreRejected(string description)
    {
        GoalAgentService.TryParseFileExistenceRequirement(description, out _).Should().BeFalse();
    }

    // ── Deterministic file-existence verification ─────────────────────────

    [Fact]
    public async Task RunAsync_FileExistenceRequirement_IsCheckedOnDiskWithoutLlmReview()
    {
        var workspaceDir = Path.Combine(Path.GetTempPath(), "GoalAgentTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(workspaceDir);
            await File.WriteAllTextAsync(Path.Combine(workspaceDir, "a.txt"), "innehåll");
            var fileAgent = new FileAgentService(workspaceDir);
            var fake = new FakeAgenticChatService(msg =>
                IsVerifyPrompt(msg) ? Result("RESULTAT: GODKÄNT") : Result("klart"));
            var sut = new GoalAgentService(fake, fileAgent, maxIterations: 1);

            await sut.RunAsync("Skapa filen a.txt", _ => Task.FromResult("KRAV: Filen a.txt finns i arbetsytan."));

            sut.Phase.Should().Be(GoalAgentPhase.Completed);
            fake.ReceivedMessages.Should().NotContain(m => IsVerifyPrompt(m),
                "a pure existence requirement should never reach the LLM reviewer");
            sut.ActivityLog.Should().Contain(line => line.Contains("direktkontroll"));
        }
        finally
        {
            if (Directory.Exists(workspaceDir))
                Directory.Delete(workspaceDir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_FileExistenceRequirementFileMissing_FailsDeterministically()
    {
        var workspaceDir = Path.Combine(Path.GetTempPath(), "GoalAgentTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(workspaceDir);
            var fileAgent = new FileAgentService(workspaceDir);
            var fake = new FakeAgenticChatService(msg =>
                IsVerifyPrompt(msg) ? Result("RESULTAT: GODKÄNT") : Result("klart"));
            var sut = new GoalAgentService(fake, fileAgent, maxIterations: 1);

            await sut.RunAsync("Skapa filen b.txt", _ => Task.FromResult("KRAV: Filen b.txt finns i arbetsytan."));

            sut.Phase.Should().Be(GoalAgentPhase.MaxIterationsReached);
            var requirement = sut.Requirements.Should().ContainSingle().Subject;
            requirement.Status.Should().Be(RequirementStatus.Failed);
            requirement.LastVerdict.Should().Contain("saknas");
            fake.ReceivedMessages.Should().NotContain(m => IsVerifyPrompt(m));
        }
        finally
        {
            if (Directory.Exists(workspaceDir))
                Directory.Delete(workspaceDir, recursive: true);
        }
    }

    // ── Workspace snapshot injection ──────────────────────────────────────

    [Fact]
    public async Task RunAsync_WithFileAgent_WorkAndVerifyPromptsCarryReferencedFileContent()
    {
        var workspaceDir = Path.Combine(Path.GetTempPath(), "GoalAgentTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(workspaceDir);
            await File.WriteAllTextAsync(Path.Combine(workspaceDir, "fil.txt"), "UNIKT_INNEHALL_123");
            var fileAgent = new FileAgentService(workspaceDir);
            var fake = new FakeAgenticChatService(msg =>
                IsVerifyPrompt(msg) ? Result("RESULTAT: GODKÄNT") : Result("klart"));
            var sut = new GoalAgentService(fake, fileAgent, maxIterations: 1);

            await sut.RunAsync(
                "Fyll fil.txt med en hälsning",
                _ => Task.FromResult("KRAV: fil.txt innehåller en hälsning."));

            // The model must see the current file content without having to call /läs — the
            // old blind work steps kept rewriting files they had never read.
            var workPrompt = fake.ReceivedMessages.Should().ContainSingle(m => IsWorkPrompt(m)).Subject;
            workPrompt.Should().Contain("Ögonblicksbild").And.Contain("UNIKT_INNEHALL_123");

            var verifyPrompt = fake.ReceivedMessages.Should().ContainSingle(m => IsVerifyPrompt(m)).Subject;
            verifyPrompt.Should().Contain("UNIKT_INNEHALL_123");
        }
        finally
        {
            if (Directory.Exists(workspaceDir))
                Directory.Delete(workspaceDir, recursive: true);
        }
    }

    /// <summary>
    /// A behavioural requirement ("hjälten kan styras med piltangenterna") names no file, so the
    /// verify snapshot used to inline nothing and the reviewer got a bare file listing. In a real
    /// run that is where it started guessing at read tools — it aimed /läs-pdf at a .bas file and
    /// then failed the requirement because it could not read the code. The goal names the file, so
    /// the reviewer is given the same reference texts the work step gets.
    /// </summary>
    [Fact]
    public async Task RunAsync_RequirementNamingNoFile_VerifyPromptStillCarriesTheGoalsFileContent()
    {
        var workspaceDir = Path.Combine(Path.GetTempPath(), "GoalAgentTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(workspaceDir);
            await File.WriteAllTextAsync(Path.Combine(workspaceDir, "rpggame.bas"), "UNIKT_INNEHALL_123");
            var fileAgent = new FileAgentService(workspaceDir);
            var fake = new FakeAgenticChatService(msg =>
                IsVerifyPrompt(msg) ? Result("RESULTAT: GODKÄNT") : Result("klart"));
            var sut = new GoalAgentService(fake, fileAgent, maxIterations: 1);

            await sut.RunAsync(
                "Skriv ett RPG-spel i rpggame.bas",
                _ => Task.FromResult("KRAV: Hjälten kan styras med piltangenterna och attackera med space."));

            var verifyPrompt = fake.ReceivedMessages.Should().ContainSingle(m => IsVerifyPrompt(m)).Subject;
            verifyPrompt.Should().Contain("UNIKT_INNEHALL_123");
        }
        finally
        {
            if (Directory.Exists(workspaceDir))
                Directory.Delete(workspaceDir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_VerifyPrompt_TellsReviewerAFailedToolCallIsNotAFailedRequirement()
    {
        var fake = new FakeAgenticChatService(msg =>
            IsVerifyPrompt(msg) ? Result("RESULTAT: GODKÄNT") : Result("klart"));
        var sut = new GoalAgentService(fake, maxIterations: 1);

        await sut.RunAsync("Skapa ett pannkaksrecept", TwoRequirementsLlm);

        var verifyPrompt = fake.ReceivedMessages.First(IsVerifyPrompt);
        verifyPrompt.Should().Contain("/läs-pdf är BARA för filer som slutar på .pdf");
        verifyPrompt.Should().Contain("Underkänn aldrig ett krav enbart för att du inte lyckades läsa en fil");
    }

    // ── QB64 awareness ────────────────────────────────────────────────────

    private sealed class FakeQb64ToolService : IQb64ToolService
    {
        public bool Configured { get; init; } = true;

        public bool IsCommand(string input) => false;

        public Task<UtilityToolResult> ExecuteAsync(string input) =>
            throw new NotSupportedException("Not used by these tests.");

        public IReadOnlyDictionary<string, string> GetToolDescriptions() =>
            Configured
                ? new Dictionary<string, string> { ["/qb64 <fil.bas>"] = "Kompilerar och kör en QBasic-fil." }
                : new Dictionary<string, string>();

        public bool TryFindCommand(string llmResponse, out string command)
        {
            command = string.Empty;
            return false;
        }
    }

    [Fact]
    public async Task RunAsync_WithConfiguredQb64_RequirementsPromptMentionsCompilation()
    {
        string? requirementsPrompt = null;
        Task<string> CapturingLlm(string prompt)
        {
            requirementsPrompt ??= prompt;
            return Task.FromResult("KRAV: Filen calc.bas innehåller en miniräknare.");
        }

        var fake = new FakeAgenticChatService(msg =>
            IsVerifyPrompt(msg) ? Result("RESULTAT: GODKÄNT") : Result("klart"));
        var sut = new GoalAgentService(fake, maxIterations: 1, qb64Tools: new FakeQb64ToolService());

        await sut.RunAsync("Skapa en miniräknare i QBasic och testa den", CapturingLlm);

        requirementsPrompt.Should().NotBeNull();
        requirementsPrompt.Should().Contain("kompilerar", "the requirement generator must know compiling is checkable");
    }

    [Fact]
    public async Task RunAsync_WithoutConfiguredQb64_RequirementsPromptOmitsCompilation()
    {
        string? requirementsPrompt = null;
        Task<string> CapturingLlm(string prompt)
        {
            requirementsPrompt ??= prompt;
            return Task.FromResult("KRAV: Filen calc.bas innehåller en miniräknare.");
        }

        var fake = new FakeAgenticChatService(msg =>
            IsVerifyPrompt(msg) ? Result("RESULTAT: GODKÄNT") : Result("klart"));
        var sut = new GoalAgentService(fake, maxIterations: 1, qb64Tools: new FakeQb64ToolService { Configured = false });

        await sut.RunAsync("Skapa en miniräknare i QBasic", CapturingLlm);

        requirementsPrompt.Should().NotBeNull();
        requirementsPrompt.Should().NotContain("kompilerar");
    }

    // ── Requirement approval gate ─────────────────────────────────────────

    /// <summary>
    /// Spins until <paramref name="condition"/> holds, so a test can meet a run that is executing
    /// on a background task at a known point instead of guessing with a fixed delay.
    /// </summary>
    private static async Task WaitForAsync(Func<bool> condition, string description)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Timed out waiting for {description}.");
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task RunAsync_RequireApproval_PausesBeforeDoingAnyWork()
    {
        var fake = new FakeAgenticChatService(msg =>
            IsVerifyPrompt(msg) ? Result("RESULTAT: GODKÄNT") : Result("klart"));
        var sut = new GoalAgentService(fake);

        var run = sut.RunAsync("Skapa ett pannkaksrecept", TwoRequirementsLlm, requireApproval: true);
        await WaitForAsync(() => sut.IsAwaitingApproval, "the run to park on the approval gate");

        // The whole point: the requirements exist, but nothing has touched the workspace yet.
        sut.Phase.Should().Be(GoalAgentPhase.AwaitingApproval);
        sut.IsRunning.Should().BeTrue();
        sut.Requirements.Should().HaveCount(2);
        fake.ReceivedMessages.Should().BeEmpty();

        sut.ApproveRequirements();
        await run;

        sut.Phase.Should().Be(GoalAgentPhase.Completed);
        sut.IsAwaitingApproval.Should().BeFalse();
        fake.ReceivedMessages.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ApproveRequirements_WithEditedList_RunsAgainstTheEditedRequirements()
    {
        var fake = new FakeAgenticChatService(msg =>
            IsVerifyPrompt(msg) ? Result("RESULTAT: GODKÄNT") : Result("klart"));
        var sut = new GoalAgentService(fake);

        var run = sut.RunAsync("Skapa ett pannkaksrecept", TwoRequirementsLlm, requireApproval: true);
        await WaitForAsync(() => sut.IsAwaitingApproval, "the approval gate");

        sut.ApproveRequirements(new[]
        {
            "recept.txt innehåller exakt fyra ingredienser.",
            "   ",
            "recept.txt har numrerade steg."
        });
        await run;

        sut.Requirements.Select(r => r.Description).Should().Equal(
            "recept.txt innehåller exakt fyra ingredienser.",
            "recept.txt har numrerade steg.");
        // The work step must be driven by the edited list, not the generated one.
        var workPrompt = fake.ReceivedMessages.First(IsWorkPrompt);
        workPrompt.Should().Contain("exakt fyra ingredienser").And.NotContain("ingredienslista");
    }

    [Fact]
    public async Task ApproveRequirements_EmptyEditedList_KeepsTheGeneratedRequirements()
    {
        // An empty suite verifies trivially, so the run would claim "all green" without doing
        // anything — keeping the generated list is the safe reading of a nonsensical edit.
        var fake = new FakeAgenticChatService(msg =>
            IsVerifyPrompt(msg) ? Result("RESULTAT: GODKÄNT") : Result("klart"));
        var sut = new GoalAgentService(fake);

        var run = sut.RunAsync("Skapa ett pannkaksrecept", TwoRequirementsLlm, requireApproval: true);
        await WaitForAsync(() => sut.IsAwaitingApproval, "the approval gate");

        sut.ApproveRequirements(new[] { "  ", string.Empty });
        await run;

        sut.Requirements.Should().HaveCount(2);
        sut.Phase.Should().Be(GoalAgentPhase.Completed);
    }

    [Fact]
    public async Task RequestStop_WhileAwaitingApproval_EndsTheRunWithoutWorking()
    {
        var fake = new FakeAgenticChatService(msg =>
            IsVerifyPrompt(msg) ? Result("RESULTAT: GODKÄNT") : Result("klart"));
        var sut = new GoalAgentService(fake);

        var run = sut.RunAsync("Skapa ett pannkaksrecept", TwoRequirementsLlm, requireApproval: true);
        await WaitForAsync(() => sut.IsAwaitingApproval, "the approval gate");

        sut.RequestStop();
        await run;

        sut.Phase.Should().Be(GoalAgentPhase.Stopped);
        sut.IsRunning.Should().BeFalse();
        fake.ReceivedMessages.Should().BeEmpty();
    }

    [Fact]
    public void ApproveRequirements_WhenNoRunIsWaiting_IsANoOp()
    {
        var fake = new FakeAgenticChatService(msg =>
            IsVerifyPrompt(msg) ? Result("RESULTAT: GODKÄNT") : Result("klart"));
        var sut = new GoalAgentService(fake);

        sut.ApproveRequirements(new[] { "påhittat krav" });

        sut.Requirements.Should().BeEmpty();
        sut.Phase.Should().Be(GoalAgentPhase.Idle);
    }

    [Fact]
    public async Task RunAsync_RequireApproval_PersistsOnlyTheApprovedRequirements()
    {
        var repository = new FakeAgentRunRepository();
        var fake = new FakeAgenticChatService(msg =>
            IsVerifyPrompt(msg) ? Result("RESULTAT: GODKÄNT") : Result("klart"));
        var sut = new GoalAgentService(fake, runRepository: repository);

        var run = sut.RunAsync("Skapa ett pannkaksrecept", TwoRequirementsLlm, requireApproval: true);
        await WaitForAsync(() => sut.IsAwaitingApproval, "the approval gate");

        sut.ApproveRequirements(new[] { "recept.txt innehåller exakt fyra ingredienser." });
        await run;

        // The repository inserts rows, so writing the generated list before approval would leave
        // the discarded requirements in the history alongside the ones actually worked on.
        repository.SavedRequirements.Select(r => r.Description).Should()
            .Equal("recept.txt innehåller exakt fyra ingredienser.");
    }

    [Fact]
    public async Task RunAsync_WithoutApprovalRequired_NeverPauses()
    {
        var fake = new FakeAgenticChatService(msg =>
            IsVerifyPrompt(msg) ? Result("RESULTAT: GODKÄNT") : Result("klart"));
        var sut = new GoalAgentService(fake);

        await sut.RunAsync("Skapa ett pannkaksrecept", TwoRequirementsLlm);

        sut.Phase.Should().Be(GoalAgentPhase.Completed);
        sut.IsAwaitingApproval.Should().BeFalse();
    }

    // ── Stop cancels the call in flight ───────────────────────────────────

    [Fact]
    public void RunToken_WhenIdle_IsNotCancellable()
    {
        var sut = new GoalAgentService(new FakeAgenticChatService(_ => Result("ok")));

        sut.RunToken.CanBeCanceled.Should().BeFalse();
    }

    [Fact]
    public async Task RequestStop_CancelsRunToken_AbortingTheCallInFlight()
    {
        var workStarted = new TaskCompletionSource();
        var fake = new FakeAgenticChatService(async (msg, ct) =>
        {
            if (!IsWorkPrompt(msg))
                return Result("RESULTAT: GODKÄNT");

            workStarted.TrySetResult();
            // Models the real backend: the call runs until the token says otherwise.
            await Task.Delay(Timeout.Infinite, ct);
            return Result("klart");
        });
        var sut = new GoalAgentService(fake);

        var run = sut.RunAsync("Skapa ett pannkaksrecept", TwoRequirementsLlm);
        await workStarted.Task;
        sut.RunToken.IsCancellationRequested.Should().BeFalse();

        sut.RequestStop();
        await run;

        // Stopped without waiting out the generation that was in flight.
        sut.Phase.Should().Be(GoalAgentPhase.Stopped);
        sut.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_ExternalTokenCancelled_StopsTheRun()
    {
        using var cts = new CancellationTokenSource();
        var workStarted = new TaskCompletionSource();
        var fake = new FakeAgenticChatService(async (msg, ct) =>
        {
            if (!IsWorkPrompt(msg))
                return Result("RESULTAT: GODKÄNT");

            workStarted.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return Result("klart");
        });
        var sut = new GoalAgentService(fake);

        var run = sut.RunAsync("Skapa ett pannkaksrecept", TwoRequirementsLlm, cancellationToken: cts.Token);
        await workStarted.Task;

        // The run's own token is linked to the caller's, so an external cancel reaches the steps.
        await cts.CancelAsync();
        await run;

        sut.Phase.Should().Be(GoalAgentPhase.Stopped);
    }

    // ── ContinueAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task CanContinue_ReflectsWhetherThereIsAnUnfinishedRunToResume()
    {
        var fake = new FakeAgenticChatService(msg =>
            IsVerifyPrompt(msg) ? Result("RESULTAT: UNDERKÄNT - saknas") : Result("klart"));
        var sut = new GoalAgentService(fake, maxIterations: 1);

        sut.CanContinue.Should().BeFalse("nothing has run yet");

        await sut.RunAsync("Skapa ett pannkaksrecept", TwoRequirementsLlm);

        sut.Phase.Should().Be(GoalAgentPhase.MaxIterationsReached);
        sut.CanContinue.Should().BeTrue();

        sut.Reset();
        sut.CanContinue.Should().BeFalse("a reset clears the requirements");
    }

    [Fact]
    public async Task CanContinue_AfterAnAllGreenRun_IsFalse()
    {
        var fake = new FakeAgenticChatService(msg =>
            IsVerifyPrompt(msg) ? Result("RESULTAT: GODKÄNT") : Result("klart"));
        var sut = new GoalAgentService(fake);

        await sut.RunAsync("Skapa ett pannkaksrecept", TwoRequirementsLlm);

        sut.Phase.Should().Be(GoalAgentPhase.Completed);
        sut.CanContinue.Should().BeFalse("there is nothing left to work on");
    }

    [Fact]
    public async Task ContinueAsync_ResumesWithoutRegeneratingRequirements()
    {
        var requirementGenerations = 0;
        Task<string> CountingLlm(string prompt)
        {
            requirementGenerations++;
            return TwoRequirementsLlm(prompt);
        }

        var green = false;
        var fake = new FakeAgenticChatService(msg =>
        {
            if (!IsVerifyPrompt(msg))
                return Result("klart");
            return green ? Result("RESULTAT: GODKÄNT") : Result("RESULTAT: UNDERKÄNT - saknas");
        });
        var sut = new GoalAgentService(fake, maxIterations: 1);

        await sut.RunAsync("Skapa ett pannkaksrecept", CountingLlm);
        sut.Phase.Should().Be(GoalAgentPhase.MaxIterationsReached);
        requirementGenerations.Should().Be(1);

        var idsBefore = sut.Requirements.Select(r => r.Id).ToList();
        green = true;
        await sut.ContinueAsync(CountingLlm);

        sut.Phase.Should().Be(GoalAgentPhase.Completed);
        sut.Requirements.Select(r => r.Id).Should().Equal(idsBefore, "the continuation works on the same requirements");
        requirementGenerations.Should().Be(1, "the requirements must not be derived a second time");
    }

    [Fact]
    public async Task ContinueAsync_KeepsTheActivityLogAndRecordsItsOwnRun()
    {
        var repository = new FakeAgentRunRepository();
        var fake = new FakeAgenticChatService(msg =>
            IsVerifyPrompt(msg) ? Result("RESULTAT: UNDERKÄNT - saknas") : Result("klart"));
        var sut = new GoalAgentService(fake, maxIterations: 1, runRepository: repository);

        await sut.RunAsync("Skapa ett pannkaksrecept", TwoRequirementsLlm);
        var logLinesAfterFirstRun = sut.ActivityLog.Count;

        await sut.ContinueAsync(TwoRequirementsLlm);

        sut.ActivityLog.Count.Should().BeGreaterThan(logLinesAfterFirstRun, "the on-screen log continues the story");
        sut.ActivityLog.Should().Contain(line => line.Contains("Fortsätter tidigare körning"));
        repository.StartedRuns.Should().HaveCount(2, "a continuation is its own row in the history");
        repository.CompletedRuns.Should().HaveCount(2);
    }

    [Fact]
    public async Task ContinueAsync_PersistsItsRequirementsAsNewRowsWithoutCollidingWithTheFirstRun()
    {
        // Regression: the continuation reused each requirement's own id as the history row id, so
        // re-inserting the same requirements under the new run violated the primary key — which
        // disabled history recording for the entire continuation.
        var repository = new FakeAgentRunRepository();
        var fake = new FakeAgenticChatService(msg =>
            IsVerifyPrompt(msg) ? Result("RESULTAT: UNDERKÄNT - saknas") : Result("klart"));
        var sut = new GoalAgentService(fake, maxIterations: 1, runRepository: repository);

        await sut.RunAsync("Skapa ett pannkaksrecept", TwoRequirementsLlm);
        await sut.ContinueAsync(TwoRequirementsLlm);

        repository.SavedRequirements.Should().HaveCount(4, "two requirements recorded for each of the two runs");
        repository.SavedRequirements.Select(r => r.Id).Should().OnlyHaveUniqueItems();
        repository.SavedRequirements.Select(r => r.RunId).Distinct().Should().HaveCount(2);
        // The continuation's own rows must carry the verdicts its verifications produced, which
        // only works if the status updates were routed to the new rows.
        var continuationRunId = repository.StartedRuns[1].Id;
        repository.SavedRequirements
            .Where(r => r.RunId == continuationRunId)
            .Should().OnlyContain(r => r.Status == nameof(RequirementStatus.Failed));
        sut.ActivityLog.Should().NotContain(line => line.Contains("Kunde inte spara körningshistoriken"));
    }

    [Fact]
    public async Task ContinueAsync_WhenThereIsNothingToContinue_IsANoOp()
    {
        var fake = new FakeAgenticChatService(msg =>
            IsVerifyPrompt(msg) ? Result("RESULTAT: GODKÄNT") : Result("klart"));
        var sut = new GoalAgentService(fake);

        await sut.ContinueAsync(TwoRequirementsLlm);

        sut.Phase.Should().Be(GoalAgentPhase.Idle);
        fake.ReceivedMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task ContinueAsync_NullSendToLlm_ThrowsArgumentNullException()
    {
        var sut = new GoalAgentService(new FakeAgenticChatService(_ => Result("ok")));

        var act = async () => await sut.ContinueAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ── Workspace backups before each work step ───────────────────────────

    /// <summary>Records the backups a run asked for, so tests can assert on the timing.</summary>
    private sealed class FakeWorkspaceBackupService : IWorkspaceBackupService
    {
        public List<string> CreatedLabels { get; } = new();

        /// <summary>When true, <see cref="Create"/> reports failure the way a real disk error would.</summary>
        public bool FailToCreate { get; set; }

        public string BackupFolderName => ".agent-backup";

        public WorkspaceBackupInfo? Create(string label)
        {
            CreatedLabels.Add(label);
            return FailToCreate ? null : new WorkspaceBackupInfo(label, label, DateTime.Now, 2, 100);
        }

        public IReadOnlyList<WorkspaceBackupInfo> GetBackups() => Array.Empty<WorkspaceBackupInfo>();

        public int Restore(string backupId) => 0;
    }

    [Fact]
    public async Task RunAsync_TakesABackupBeforeEveryWorkStep()
    {
        var fake = new FakeAgenticChatService(msg =>
            IsVerifyPrompt(msg) ? Result("RESULTAT: UNDERKÄNT - saknas") : Result("klart"));
        var backups = new FakeWorkspaceBackupService();
        var sut = new GoalAgentService(fake, maxIterations: 2, backups: backups, stallLimit: 0);

        await sut.RunAsync("Skapa ett pannkaksrecept", TwoRequirementsLlm);

        backups.CreatedLabels.Should().Equal("iteration-1", "iteration-2");
        sut.ActivityLog.Should().Contain(line => line.Contains("Säkerhetskopia"));
    }

    [Fact]
    public async Task RunAsync_BackupFails_RunContinuesAndSaysSo()
    {
        // Insurance that can't be written must not stop the thing it was insuring.
        var fake = new FakeAgenticChatService(msg =>
            IsVerifyPrompt(msg) ? Result("RESULTAT: GODKÄNT") : Result("klart"));
        var backups = new FakeWorkspaceBackupService { FailToCreate = true };
        var sut = new GoalAgentService(fake, maxIterations: 1, backups: backups);

        await sut.RunAsync("Skapa ett pannkaksrecept", TwoRequirementsLlm);

        sut.Phase.Should().Be(GoalAgentPhase.Completed);
        sut.ActivityLog.Should().Contain(line => line.Contains("Ingen säkerhetskopia kunde sparas"));
    }

    [Fact]
    public async Task RunAsync_NothingToWorkOn_TakesNoBackup()
    {
        // Every requirement passes on the first verification, so iteration 2 never happens and
        // there is no destructive step to insure against.
        var fake = new FakeAgenticChatService(msg =>
            IsVerifyPrompt(msg) ? Result("RESULTAT: GODKÄNT") : Result("klart"));
        var backups = new FakeWorkspaceBackupService();
        var sut = new GoalAgentService(fake, maxIterations: 5, backups: backups);

        await sut.RunAsync("Skapa ett pannkaksrecept", TwoRequirementsLlm);

        backups.CreatedLabels.Should().Equal("iteration-1");
    }

    // ── Skipping re-verification of unchanged files ───────────────────────

    [Fact]
    public async Task RunAsync_PassedRequirementWhoseFilesDidNotChange_IsNotReverified()
    {
        var workspaceDir = Path.Combine(Path.GetTempPath(), "GoalAgentTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(workspaceDir);
            await File.WriteAllTextAsync(Path.Combine(workspaceDir, "a.txt"), "A");
            await File.WriteAllTextAsync(Path.Combine(workspaceDir, "b.txt"), "B");
            var fileAgent = new FileAgentService(workspaceDir);

            // The verify prompt carries a workspace snapshot that lists every file, so the
            // requirements are told apart by a marker word instead of by the filename.
            // a.txt passes and is never touched again; b.txt keeps failing, so the run keeps going.
            var fake = new FakeAgenticChatService(msg =>
            {
                if (!IsVerifyPrompt(msg))
                    return Result("klart");
                return msg.Contains("ALFA") ? Result("RESULTAT: GODKÄNT") : Result("RESULTAT: UNDERKÄNT - fel innehåll");
            });
            var sut = new GoalAgentService(fake, fileAgent, maxIterations: 3, stallLimit: 0);

            await sut.RunAsync(
                "Fixa filerna",
                _ => Task.FromResult("KRAV: a.txt uppfyller ALFA.\nKRAV: b.txt uppfyller BETA."));

            // Verified once in iteration 1; iterations 2 and 3 skip it because a.txt is untouched.
            fake.ReceivedMessages.Count(m => IsVerifyPrompt(m) && m.Contains("ALFA")).Should().Be(1);
            fake.ReceivedMessages.Count(m => IsVerifyPrompt(m) && m.Contains("BETA")).Should().Be(3);
            sut.Requirements.Single(r => r.Description.Contains("a.txt")).Status
                .Should().Be(RequirementStatus.Passed, "skipping the check keeps the verdict, it doesn't drop it");
            sut.ActivityLog.Should().Contain(line => line.Contains("kontrollerades inte om"));
        }
        finally
        {
            if (Directory.Exists(workspaceDir))
                Directory.Delete(workspaceDir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_PassedRequirementWhoseFileChanges_IsVerifiedAgain()
    {
        var workspaceDir = Path.Combine(Path.GetTempPath(), "GoalAgentTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(workspaceDir);
            var pathA = Path.Combine(workspaceDir, "a.txt");
            await File.WriteAllTextAsync(pathA, "A");
            var fileAgent = new FileAgentService(workspaceDir);

            var workSteps = 0;
            var fake = new FakeAgenticChatService(msg =>
            {
                if (!IsVerifyPrompt(msg))
                {
                    // Model the work step editing the very file the passed requirement covers —
                    // the reason every requirement is re-verified after real work in the first place.
                    workSteps++;
                    File.WriteAllText(pathA, "A" + new string('!', workSteps));
                    return Result("klart");
                }
                return msg.Contains("ALFA") ? Result("RESULTAT: GODKÄNT") : Result("RESULTAT: UNDERKÄNT - fel");
            });
            var sut = new GoalAgentService(fake, fileAgent, maxIterations: 2, stallLimit: 0);

            await sut.RunAsync(
                "Fixa filerna",
                _ => Task.FromResult("KRAV: a.txt uppfyller ALFA.\nKRAV: b.txt uppfyller BETA."));

            fake.ReceivedMessages.Count(m => IsVerifyPrompt(m) && m.Contains("ALFA"))
                .Should().Be(2, "a changed file invalidates the earlier pass");
        }
        finally
        {
            if (Directory.Exists(workspaceDir))
                Directory.Delete(workspaceDir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_WithoutAFileAgent_AlwaysReverifies()
    {
        // No workspace to fingerprint — the optimisation must disable itself rather than assume
        // nothing changed and leave a requirement green on stale evidence.
        var fake = new FakeAgenticChatService(msg =>
        {
            if (!IsVerifyPrompt(msg))
                return Result("klart");
            return msg.Contains("recept.txt finns") ? Result("RESULTAT: GODKÄNT") : Result("RESULTAT: UNDERKÄNT - saknas");
        });
        var sut = new GoalAgentService(fake, maxIterations: 2, stallLimit: 0);

        await sut.RunAsync("Skapa ett pannkaksrecept", TwoRequirementsLlm);

        fake.ReceivedMessages.Count(m => IsVerifyPrompt(m) && m.Contains("recept.txt finns")).Should().Be(2);
    }

    // ── Stall detection ───────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_EveryRequirementFailsIdenticallyRepeatedly_StopsEarlyAsStalled()
    {
        var fake = new FakeAgenticChatService(msg =>
            IsVerifyPrompt(msg) ? Result("RESULTAT: UNDERKÄNT - fel innehåll") : Result("klart"));
        var sut = new GoalAgentService(fake, maxIterations: 20, stallLimit: 3);

        await sut.RunAsync("Skapa ett pannkaksrecept", TwoRequirementsLlm);

        sut.Phase.Should().Be(GoalAgentPhase.Stalled);
        sut.CurrentIteration.Should().Be(3, "the run gives up as soon as the third identical verdict lands");
        sut.Requirements.Should().OnlyContain(r => r.RepeatedFailureCount == 3);
        sut.CanContinue.Should().BeTrue("the user may still push it further by hand");
    }

    [Fact]
    public async Task RunAsync_VerdictChanges_ResetsTheRepeatCounterAndKeepsGoing()
    {
        var verifyCalls = 0;
        var fake = new FakeAgenticChatService(msg =>
        {
            if (!IsVerifyPrompt(msg))
                return Result("klart");

            verifyCalls++;
            // A different complaint each time means the loop is still making progress, however
            // slowly — that must not be mistaken for going in circles.
            return Result($"RESULTAT: UNDERKÄNT - brist nummer {verifyCalls}");
        });
        var sut = new GoalAgentService(fake, maxIterations: 4, stallLimit: 2);

        await sut.RunAsync("Skapa ett pannkaksrecept", TwoRequirementsLlm);

        sut.Phase.Should().Be(GoalAgentPhase.MaxIterationsReached);
        sut.CurrentIteration.Should().Be(4);
    }

    [Fact]
    public async Task RunAsync_StallLimitZero_UsesTheWholeIterationBudget()
    {
        var fake = new FakeAgenticChatService(msg =>
            IsVerifyPrompt(msg) ? Result("RESULTAT: UNDERKÄNT - fel innehåll") : Result("klart"));
        var sut = new GoalAgentService(fake, maxIterations: 4, stallLimit: 0);

        await sut.RunAsync("Skapa ett pannkaksrecept", TwoRequirementsLlm);

        sut.Phase.Should().Be(GoalAgentPhase.MaxIterationsReached);
        sut.CurrentIteration.Should().Be(4);
    }

    [Fact]
    public async Task RunAsync_OneRequirementStillMoving_DoesNotCountAsStalled()
    {
        // A run is only stuck when *nothing* is progressing; one requirement that keeps failing
        // the same way while another is being worked out is normal.
        var secondRequirementChecks = 0;
        var fake = new FakeAgenticChatService(msg =>
        {
            if (!IsVerifyPrompt(msg))
                return Result("klart");

            if (msg.Contains("recept.txt finns"))
                return Result("RESULTAT: UNDERKÄNT - samma fel varje gång");

            secondRequirementChecks++;
            return Result($"RESULTAT: UNDERKÄNT - annat fel {secondRequirementChecks}");
        });
        var sut = new GoalAgentService(fake, maxIterations: 4, stallLimit: 2);

        await sut.RunAsync("Skapa ett pannkaksrecept", TwoRequirementsLlm);

        sut.Phase.Should().Be(GoalAgentPhase.MaxIterationsReached);
    }

    [Fact]
    public async Task RunAsync_RepeatedFailure_TellsTheModelToChangeApproach()
    {
        var fake = new FakeAgenticChatService(msg =>
            IsVerifyPrompt(msg) ? Result("RESULTAT: UNDERKÄNT - fel innehåll") : Result("klart"));
        var sut = new GoalAgentService(fake, maxIterations: 3, stallLimit: 0);

        await sut.RunAsync("Skapa ett pannkaksrecept", TwoRequirementsLlm);

        // Iteration 1's prompt has no history to escalate on; by iteration 3 the same verdict has
        // come back twice and the prompt must say so instead of asking for the same fix again.
        var workPrompts = fake.ReceivedMessages.Where(IsWorkPrompt).ToList();
        workPrompts[0].Should().NotContain("byt angreppssätt");
        workPrompts[2].Should().Contain("byt angreppssätt");
    }

    [Fact]
    public async Task ContinueAsync_AfterAStall_StartsTheRepeatCountersOver()
    {
        var fake = new FakeAgenticChatService(msg =>
            IsVerifyPrompt(msg) ? Result("RESULTAT: UNDERKÄNT - fel innehåll") : Result("klart"));
        var sut = new GoalAgentService(fake, maxIterations: 20, stallLimit: 2);

        await sut.RunAsync("Skapa ett pannkaksrecept", TwoRequirementsLlm);
        sut.Phase.Should().Be(GoalAgentPhase.Stalled);

        await sut.ContinueAsync(TwoRequirementsLlm);

        // Two more iterations before stalling again, rather than giving up on the first check
        // for a failure the continuation had not yet seen.
        sut.Phase.Should().Be(GoalAgentPhase.Stalled);
        sut.CurrentIteration.Should().Be(2);
    }
}
