using AiDashboard.Services.Interfaces;
using AiDashboard.State;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using AgentKit.ToolLoop;
using Services.BatchJobs;
using BatchProcessingPage = AiDashboard.Components.Pages.BatchProcessingPage;
using LlmResponseFormatterService = AiDashboard.Services.LlmResponseFormatterService;

namespace Presentation.AiDashboard.Tests.Components;

/// <summary>
/// Tests for <see cref="BatchProcessingPage"/>: the "queue up tasks, run them one by one" page.
/// Uses a real <see cref="BatchJobService"/> backed by a fake <see cref="IAgenticChatService"/>
/// (scripted results, no real LLM/file-agent involved), mirroring
/// <see cref="Services.Tests.BatchJobs.BatchJobServiceTests"/>'s conventions.
/// </summary>
public class BatchProcessingPageTests : TestContext
{
    private sealed class FakeAgenticChatService : IAgenticChatService
    {
        private readonly Func<string, Task<AgenticChatResult>> _handler;

        public FakeAgenticChatService(Func<string, AgenticChatResult> handler)
            : this(msg => Task.FromResult(handler(msg)))
        {
        }

        public FakeAgenticChatService(Func<string, Task<AgenticChatResult>> handler) => _handler = handler;

        public async Task<AgenticChatResult> SendWithToolsAsync(
            string userMessage,
            Func<string, Task<string>> sendToLlm,
            CancellationToken cancellationToken = default,
            Action<string>? onToolStatus = null,
            string? recentlyUploadedFilename = null)
            => await _handler(userMessage);
    }

    private IBatchJobService RegisterServices(IAgenticChatService fakeAgenticChat)
    {
        Services.AddSingleton(new DashboardState());
        Services.AddSingleton<ILlmResponseFormatterService, LlmResponseFormatterService>();
        var batchJobs = new BatchJobService(fakeAgenticChat);
        Services.AddSingleton<IBatchJobService>(batchJobs);
        return batchJobs;
    }

    [Fact]
    public void Renders_WithEmptyJobList_ShowsEmptyMessage()
    {
        RegisterServices(new FakeAgenticChatService(_ => new AgenticChatResult("ok", Array.Empty<ToolInvocation>())));

        var cut = RenderComponent<BatchProcessingPage>();

        Assert.Contains("No jobs yet", cut.Find(".oa-batch-empty").TextContent);
    }

    [Fact]
    public void AddJob_ViaButton_AddsJobToListWithPendingBadge()
    {
        RegisterServices(new FakeAgenticChatService(_ => new AgenticChatResult("ok", Array.Empty<ToolInvocation>())));
        var cut = RenderComponent<BatchProcessingPage>();

        cut.Find(".oa-batch-input").Input("Read rules.txt and write 10 questions to QA.txt");
        cut.Find(".oa-batch-add-btn").Click();

        var row = cut.Find(".oa-batch-job-row");
        Assert.Contains("Read rules.txt and write 10 questions to QA.txt", row.TextContent);
        var badge = cut.Find(".oa-batch-badge");
        Assert.Contains("Pending", badge.TextContent);
        Assert.Contains("gray", badge.ClassName);
    }

    [Fact]
    public void AddJob_ClearsInputAfterAdding()
    {
        RegisterServices(new FakeAgenticChatService(_ => new AgenticChatResult("ok", Array.Empty<ToolInvocation>())));
        var cut = RenderComponent<BatchProcessingPage>();

        cut.Find(".oa-batch-input").Input("Generate a snake game in QBasic");
        cut.Find(".oa-batch-add-btn").Click();

        Assert.Equal(string.Empty, cut.Find(".oa-batch-input").GetAttribute("value") ?? string.Empty);
    }

    [Fact]
    public void AddJobButton_DisabledWhenInputEmpty()
    {
        RegisterServices(new FakeAgenticChatService(_ => new AgenticChatResult("ok", Array.Empty<ToolInvocation>())));
        var cut = RenderComponent<BatchProcessingPage>();

        Assert.NotNull(cut.Find(".oa-batch-add-btn").GetAttribute("disabled"));
    }

    [Fact]
    public void RemoveButton_OnPendingJob_RemovesIt()
    {
        var batchJobs = RegisterServices(new FakeAgenticChatService(_ => new AgenticChatResult("ok", Array.Empty<ToolInvocation>())));
        batchJobs.AddJob("a job to remove");
        var cut = RenderComponent<BatchProcessingPage>();

        cut.Find(".oa-batch-remove-btn").Click();

        Assert.Empty(cut.FindAll(".oa-batch-job-row"));
    }

    [Fact]
    public void StartButton_DisabledWhenNoPendingJobs()
    {
        RegisterServices(new FakeAgenticChatService(_ => new AgenticChatResult("ok", Array.Empty<ToolInvocation>())));
        var cut = RenderComponent<BatchProcessingPage>();

        Assert.NotNull(cut.Find(".oa-batch-start-btn").GetAttribute("disabled"));
    }

    [Fact]
    public void ClearCompletedButton_DisabledWhenNothingCompleted()
    {
        var batchJobs = RegisterServices(new FakeAgenticChatService(_ => new AgenticChatResult("ok", Array.Empty<ToolInvocation>())));
        batchJobs.AddJob("still pending");
        var cut = RenderComponent<BatchProcessingPage>();

        Assert.NotNull(cut.Find(".oa-batch-clear-btn").GetAttribute("disabled"));
    }

    [Fact]
    public void StartButton_ProcessesJob_ShowsDoneBadgeAndResult()
    {
        var batchJobs = RegisterServices(new FakeAgenticChatService(
            msg => new AgenticChatResult($"✓ Wrote 10 questions to QA.txt based on: {msg}", Array.Empty<ToolInvocation>())));
        batchJobs.AddJob("Read rules.txt and write 10 questions to QA.txt");
        var cut = RenderComponent<BatchProcessingPage>();

        cut.Find(".oa-batch-start-btn").Click();

        cut.WaitForState(() => cut.FindAll(".oa-batch-badge.green").Count == 1, TimeSpan.FromSeconds(5));

        var result = cut.Find(".oa-batch-job-result");
        Assert.Contains("Wrote 10 questions to QA.txt", result.TextContent);
    }

    [Fact]
    public void StartButton_JobFails_ShowsFailedBadgeAndErrorResult()
    {
        var batchJobs = RegisterServices(new FakeAgenticChatService(
            (Func<string, AgenticChatResult>)(_ => throw new InvalidOperationException("model backend unavailable"))));
        batchJobs.AddJob("a job that will fail");
        var cut = RenderComponent<BatchProcessingPage>();

        cut.Find(".oa-batch-start-btn").Click();

        cut.WaitForState(() => cut.FindAll(".oa-batch-badge.red").Count == 1, TimeSpan.FromSeconds(5));

        var result = cut.Find(".oa-batch-job-result");
        Assert.Contains("model backend unavailable", result.TextContent);
    }
}
