using FluentAssertions;
using Services.AgentTools;
using Services.BatchJobs;

namespace Services.Tests.BatchJobs;

/// <summary>
/// Unit tests for <see cref="BatchJobService"/>: the sequential job queue that feeds each job's
/// free-text description into <see cref="IAgenticChatService.SendWithToolsAsync"/> so it can use
/// the existing file-agent tools (/läs, /skapa, /fyll, ...). Uses a fake
/// <see cref="IAgenticChatService"/> so these tests focus purely on queueing/sequencing/status
/// behavior rather than the tool-calling loop itself (already covered by AgenticChatServiceTests).
/// </summary>
public class BatchJobServiceTests
{
    /// <summary>
    /// Fake agentic chat service: returns a scripted result (or throws) per call, and records
    /// every job description it was asked to process, in order. The handler is async so tests
    /// can simulate a slow/in-flight call by awaiting a gate without blocking the calling thread
    /// (a synchronous block here would deadlock tests that later signal the same gate).
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

    private static Task<string> NoopSendToLlm(string prompt) => Task.FromResult("unused");

    // ── Constructor ───────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullAgenticChat_ThrowsArgumentNullException()
    {
        var act = () => new BatchJobService(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ── AddJob / RemoveJob / ClearCompleted ──────────────────────────────

    [Fact]
    public void AddJob_ValidDescription_AddsPendingJobAndReturnsIt()
    {
        var sut = new BatchJobService(new FakeAgenticChatService(_ => new AgenticChatResult("ok", Array.Empty<ToolInvocation>())));

        var job = sut.AddJob("Generate a snake game in QBasic");

        job.Description.Should().Be("Generate a snake game in QBasic");
        job.Status.Should().Be(BatchJobStatus.Pending);
        sut.Jobs.Should().ContainSingle().Which.Should().BeSameAs(job);
    }

    [Fact]
    public void RemoveJob_PendingJob_RemovesAndReturnsTrue()
    {
        var sut = new BatchJobService(new FakeAgenticChatService(_ => new AgenticChatResult("ok", Array.Empty<ToolInvocation>())));
        var job = sut.AddJob("Task A");

        var removed = sut.RemoveJob(job.Id);

        removed.Should().BeTrue();
        sut.Jobs.Should().BeEmpty();
    }

    [Fact]
    public void RemoveJob_UnknownId_ReturnsFalse()
    {
        var sut = new BatchJobService(new FakeAgenticChatService(_ => new AgenticChatResult("ok", Array.Empty<ToolInvocation>())));

        sut.RemoveJob(Guid.NewGuid()).Should().BeFalse();
    }

    [Fact]
    public async Task RemoveJob_CompletedJob_ReturnsFalseAndKeepsIt()
    {
        var sut = new BatchJobService(new FakeAgenticChatService(_ => new AgenticChatResult("done", Array.Empty<ToolInvocation>())));
        var job = sut.AddJob("Task A");

        await sut.StartProcessingAsync(NoopSendToLlm);

        job.Status.Should().Be(BatchJobStatus.Done);
        sut.RemoveJob(job.Id).Should().BeFalse();
        sut.Jobs.Should().ContainSingle();
    }

    [Fact]
    public async Task ClearCompleted_RemovesDoneAndFailedJobs_KeepsPending()
    {
        var sut = new BatchJobService(new FakeAgenticChatService(msg =>
            msg == "fails"
                ? throw new InvalidOperationException("boom")
                : new AgenticChatResult("ok", Array.Empty<ToolInvocation>())));

        sut.AddJob("succeeds");
        sut.AddJob("fails");
        await sut.StartProcessingAsync(NoopSendToLlm); // drains both — one Done, one Failed

        var stillPending = sut.AddJob("added after processing finished");

        sut.ClearCompleted();

        sut.Jobs.Select(j => j.Id).Should().BeEquivalentTo(new[] { stillPending.Id });
    }

    // ── StartProcessingAsync: sequencing ──────────────────────────────────

    [Fact]
    public async Task StartProcessingAsync_NullSendToLlm_ThrowsArgumentNullException()
    {
        var sut = new BatchJobService(new FakeAgenticChatService(_ => new AgenticChatResult("ok", Array.Empty<ToolInvocation>())));

        var act = async () => await sut.StartProcessingAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task StartProcessingAsync_NoJobs_CompletesWithoutError()
    {
        var sut = new BatchJobService(new FakeAgenticChatService(_ => new AgenticChatResult("ok", Array.Empty<ToolInvocation>())));

        await sut.StartProcessingAsync(NoopSendToLlm);

        sut.IsProcessing.Should().BeFalse();
    }

    [Fact]
    public async Task StartProcessingAsync_MultipleJobs_ProcessesInOrderAndMarksDone()
    {
        var fake = new FakeAgenticChatService(msg => new AgenticChatResult($"result for: {msg}", Array.Empty<ToolInvocation>()));
        var sut = new BatchJobService(fake);
        var job1 = sut.AddJob("Read rules.txt and write 10 questions to QA.txt");
        var job2 = sut.AddJob("Generate a snake game in QBasic and save it to snake.bas");

        await sut.StartProcessingAsync(NoopSendToLlm);

        fake.ReceivedMessages.Should().Equal(job1.Description, job2.Description);
        job1.Status.Should().Be(BatchJobStatus.Done);
        job1.Result.Should().Be($"result for: {job1.Description}");
        job1.CompletedAt.Should().NotBeNull();
        job2.Status.Should().Be(BatchJobStatus.Done);
        job2.Result.Should().Be($"result for: {job2.Description}");
    }

    [Fact]
    public async Task StartProcessingAsync_JobThrows_MarksFailedWithErrorMessageAndContinues()
    {
        var fake = new FakeAgenticChatService(msg =>
            msg == "bad job"
                ? throw new InvalidOperationException("model backend unavailable")
                : new AgenticChatResult("ok", Array.Empty<ToolInvocation>()));
        var sut = new BatchJobService(fake);
        var failing = sut.AddJob("bad job");
        var succeeding = sut.AddJob("good job");

        await sut.StartProcessingAsync(NoopSendToLlm);

        failing.Status.Should().Be(BatchJobStatus.Failed);
        failing.Result.Should().Be("model backend unavailable");
        succeeding.Status.Should().Be(BatchJobStatus.Done);
    }

    [Fact]
    public async Task StartProcessingAsync_JobAddedWhileRunning_IsPickedUpInSameRun()
    {
        BatchJobService? sut = null;
        var fake = new FakeAgenticChatService(msg =>
        {
            if (msg == "first job")
                sut!.AddJob("added mid-run");
            return new AgenticChatResult("ok", Array.Empty<ToolInvocation>());
        });
        sut = new BatchJobService(fake);
        sut.AddJob("first job");

        await sut.StartProcessingAsync(NoopSendToLlm);

        fake.ReceivedMessages.Should().Equal("first job", "added mid-run");
        sut.Jobs.Should().OnlyContain(j => j.Status == BatchJobStatus.Done);
    }

    [Fact]
    public async Task StartProcessingAsync_AlreadyProcessing_SecondCallIsNoOp()
    {
        var gate = new TaskCompletionSource();
        var fake = new FakeAgenticChatService(async _ =>
        {
            await gate.Task;
            return new AgenticChatResult("ok", Array.Empty<ToolInvocation>());
        });
        var sut = new BatchJobService(fake);
        sut.AddJob("slow job");

        var firstRun = sut.StartProcessingAsync(NoopSendToLlm);
        // Give the first run a moment to enter the loop and flip IsProcessing.
        while (!sut.IsProcessing) await Task.Delay(5);

        var secondRun = sut.StartProcessingAsync(NoopSendToLlm);
        await secondRun; // should return immediately — it's a no-op while already processing

        sut.IsProcessing.Should().BeTrue("the first run is still blocked on the gate");

        gate.SetResult();
        await firstRun;

        fake.ReceivedMessages.Should().ContainSingle();
    }

    // ── RequestStop ───────────────────────────────────────────────────────

    [Fact]
    public async Task RequestStop_HaltsBeforeNextJob_LeavesRemainingJobsPending()
    {
        BatchJobService? sut = null;
        var fake = new FakeAgenticChatService(msg =>
        {
            if (msg == "first job")
                sut!.RequestStop();
            return new AgenticChatResult("ok", Array.Empty<ToolInvocation>());
        });
        sut = new BatchJobService(fake);
        sut.AddJob("first job");
        var second = sut.AddJob("second job");

        await sut.StartProcessingAsync(NoopSendToLlm);

        fake.ReceivedMessages.Should().Equal("first job");
        second.Status.Should().Be(BatchJobStatus.Pending);
        sut.IsProcessing.Should().BeFalse();
    }

    // ── OnChange ──────────────────────────────────────────────────────────

    [Fact]
    public async Task OnChange_RaisedOnAddAndDuringProcessing()
    {
        var sut = new BatchJobService(new FakeAgenticChatService(_ => new AgenticChatResult("ok", Array.Empty<ToolInvocation>())));
        var changeCount = 0;
        sut.OnChange += () => changeCount++;

        sut.AddJob("a job");
        await sut.StartProcessingAsync(NoopSendToLlm);

        changeCount.Should().BeGreaterThan(1);
    }
}
