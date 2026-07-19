using Entities;
using FluentAssertions;
using AgentKit.Skills.Files;
using AgentKit.Skills.Qb64;
using AgentKit.Skills.Utility;
using AgentKit.ToolLoop;
using Services.GoalAgent;
using Services.Repositories;

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
        private readonly Func<string, Task<AgenticChatResult>> _handler;

        public FakeAgenticChatService(Func<string, AgenticChatResult> handler)
            : this(msg => Task.FromResult(handler(msg)))
        {
        }

        public FakeAgenticChatService(Func<string, Task<AgenticChatResult>> handler) => _handler = handler;

        public List<string> ReceivedMessages { get; } = new();

        public async Task<AgenticChatResult> SendWithToolsAsync(
            string userMessage,
            Func<string, Task<string>> sendToLlm,
            CancellationToken cancellationToken = default,
            Action<string>? onToolStatus = null,
            string? recentlyUploadedFilename = null)
        {
            ReceivedMessages.Add(userMessage);
            return await _handler(userMessage);
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
}
