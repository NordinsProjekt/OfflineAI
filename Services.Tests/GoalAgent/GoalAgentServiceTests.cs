using FluentAssertions;
using Services.AgentTools;
using Services.GoalAgent;

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
        // 2 work calls + 2 verify calls, work before verify.
        fake.ReceivedMessages.Should().HaveCount(4);
        fake.ReceivedMessages.Take(2).Should().OnlyContain(m => IsWorkPrompt(m));
        fake.ReceivedMessages.Skip(2).Should().OnlyContain(m => IsVerifyPrompt(m));
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
            var fileAgent = new Services.FileAgent.FileAgentService(workspaceDir);
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
}
