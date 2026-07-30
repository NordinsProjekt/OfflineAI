using System.Collections.Concurrent;
using AgentKit.Api.Models;
using AgentKit.Skills.External;
using AgentKit.Skills.Files;
using AgentKit.Skills.Utility;
using AgentKit.ToolLoop;
using Application.AI.Chat;
using Application.AI.Models;
using Application.AI.Pooling;
using Services.Configuration;
using Services.GoalAgent;
using Services.Repositories;

namespace AgentKit.Api.Services;

/// <inheritdoc/>
public sealed class AgentJobService : IAgentJobService
{
    /// <summary>Tracks a live job: the running agent plus the workspace directory it owns.</summary>
    private sealed record JobEntry(GoalAgentService Agent, string WorkspacePath);

    private readonly ConcurrentDictionary<Guid, JobEntry> _jobs = new();

    private readonly IModelInstancePool _modelPool;
    private readonly AppConfiguration _appConfig;
    private readonly IAgentRunRepository? _runRepository;
    private readonly IUtilityToolsService? _utilityTools;
    private readonly IExternalToolsService? _externalTools;
    private readonly ILogger<AgentJobService> _logger;
    private readonly string _jobsRootFolder;

    public AgentJobService(
        IModelInstancePool modelPool,
        AppConfiguration appConfig,
        ILogger<AgentJobService> logger,
        IAgentRunRepository? runRepository = null,
        IUtilityToolsService? utilityTools = null,
        IExternalToolsService? externalTools = null)
    {
        _modelPool = modelPool ?? throw new ArgumentNullException(nameof(modelPool));
        _appConfig = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
        _logger = logger;
        _runRepository = runRepository;
        _utilityTools = utilityTools;
        _externalTools = externalTools;

        _jobsRootFolder = !string.IsNullOrWhiteSpace(appConfig.Jobs.RootFolder)
            ? appConfig.Jobs.RootFolder
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "OfflineAI", "AgentJobs");
    }

    /// <inheritdoc/>
    public Guid StartJob(string goalDescription, int? maxIterations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(goalDescription);

        var jobId = Guid.NewGuid();
        var workspacePath = Path.Combine(_jobsRootFolder, jobId.ToString());

        // Each job gets its own FileAgentService (and, transitively, AgenticChatService +
        // GoalAgentService) confined to its own directory, so concurrent jobs never touch each
        // other's files. QB64 is intentionally left out for this first pass — it needs a
        // per-job Qb64ToolService (it holds a reference to its FileAgentService) which isn't
        // worth the extra wiring until a job actually needs to compile something.
        var fileAgent = new FileAgentService(workspacePath);
        var agenticChat = new AgenticChatService(
            fileAgent,
            _utilityTools,
            _appConfig.AgentTools.MaxToolCallRounds,
            _externalTools);
        var goalAgent = new GoalAgentService(
            agenticChat,
            fileAgent,
            maxIterations ?? _appConfig.AgentTools.MaxGoalIterations,
            _runRepository);

        _jobs[jobId] = new JobEntry(goalAgent, workspacePath);

        // Fire-and-forget from this singleton (not from the controller) so the run outlives the
        // HTTP request that started it — same pattern the dashboard uses from a button click.
        _ = RunGuardedAsync(goalAgent, jobId, goalDescription);

        return jobId;
    }

    /// <summary>
    /// Runs the job and swallows anything that escapes it. <see cref="GoalAgentService.RunAsync"/>
    /// already turns every failure into a terminal <see cref="GoalAgentPhase.Failed"/> phase
    /// rather than throwing, so this is belt-and-braces — it exists so a bug in that guarantee
    /// can never surface as an unobserved task exception that crashes the process.
    /// </summary>
    private async Task RunGuardedAsync(GoalAgentService goalAgent, Guid jobId, string goalDescription)
    {
        try
        {
            await goalAgent.RunAsync(goalDescription, SendToLlmAsync, maxIterations: null, runId: jobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception running agent job {JobId}", jobId);
        }
    }

    /// <summary>
    /// Sends one prompt through the pooled LLM backend and returns its reply. A fresh
    /// <see cref="AiChatServicePooled"/> (and fresh memory) is built per call rather than reused
    /// across a job's many calls — mirroring <c>OfflineAI.Api</c>'s <c>LlmQueryService</c> — since
    /// the goal agent's prompts are already self-contained and accumulating conversation memory
    /// across dozens of calls in one job would only bloat the context for no benefit.
    /// </summary>
    private async Task<string> SendToLlmAsync(string prompt)
    {
        var chat = new AiChatServicePooled(
            new SimpleMemory(),
            new SimpleMemory(),
            _modelPool,
            _appConfig.Generation,
            _appConfig.Llm,
            debugMode: false,
            enableRag: false,
            showPerformanceMetrics: false);

        return await chat.SendMessageAsync(prompt);
    }

    /// <inheritdoc/>
    public AgentJobStatus? GetStatus(Guid jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var entry))
            return null;

        var agent = entry.Agent;
        return new AgentJobStatus(
            jobId,
            agent.Phase.ToString(),
            agent.GoalDescription ?? string.Empty,
            agent.CurrentIteration,
            agent.MaxIterations,
            agent.Requirements
                .Select(r => new AgentJobRequirementStatus(r.Description, r.Status.ToString(), r.LastVerdict))
                .ToList(),
            agent.ActivityLog.ToList());
    }

    /// <inheritdoc/>
    public async Task<AgentJobStatus?> GetPersistedStatusAsync(Guid jobId)
    {
        if (_runRepository is null)
            return null;

        var run = await _runRepository.GetRunAsync(jobId);
        if (run is null)
            return null;

        var requirements = await _runRepository.GetRequirementsAsync(jobId);
        var events = await _runRepository.GetEventsAsync(jobId);

        return new AgentJobStatus(
            jobId,
            run.Phase,
            run.GoalDescription,
            run.Iterations,
            run.MaxIterations,
            requirements
                .OrderBy(r => r.Ordinal)
                .Select(r => new AgentJobRequirementStatus(r.Description, r.Status, r.LastVerdict))
                .ToList(),
            events.Select(e => e.Message).ToList());
    }

    /// <inheritdoc/>
    public string? GetWorkspacePath(Guid jobId) =>
        _jobs.TryGetValue(jobId, out var entry) ? entry.WorkspacePath : null;

    /// <inheritdoc/>
    public bool RequestStop(Guid jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var entry))
            return false;

        entry.Agent.RequestStop();
        return true;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<AgentJobSummary>> GetRecentJobsAsync(int count)
    {
        if (_runRepository is null)
            return Array.Empty<AgentJobSummary>();

        var runs = await _runRepository.GetRecentRunsAsync(count);
        return runs
            .Select(r => new AgentJobSummary(r.Id, r.GoalDescription, r.Phase, r.Iterations, r.MaxIterations, r.StartedAt, r.CompletedAt))
            .ToList();
    }
}
