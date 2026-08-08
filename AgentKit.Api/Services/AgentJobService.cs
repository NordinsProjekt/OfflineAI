using System.Collections.Concurrent;
using System.IO.Compression;
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
using Services.Workspace;

namespace AgentKit.Api.Services;

/// <inheritdoc/>
public sealed class AgentJobService : IAgentJobService
{
    /// <summary>Marks a job tracked by this process: either running here, or forwarded to a peer.</summary>
    private interface IJobEntry
    {
    }

    /// <summary>A job running locally: the agent plus the workspace directory it owns.</summary>
    private sealed record LocalJobEntry(GoalAgentService Agent, string WorkspacePath) : IJobEntry;

    /// <summary>A job forwarded to a peer node — proxied through <see cref="IClusterPeerClient"/> for everything.</summary>
    private sealed record RemoteJobEntry(ClusterPeerSettings Peer, Guid PeerJobId) : IJobEntry;

    private readonly ConcurrentDictionary<Guid, IJobEntry> _jobs = new();

    private readonly IModelInstancePool _modelPool;
    private readonly AppConfiguration _appConfig;
    private readonly IClusterPeerClient _clusterPeerClient;
    private readonly IAgentRunRepository? _runRepository;
    private readonly IUtilityToolsService? _utilityTools;
    private readonly IExternalToolsService? _externalTools;
    private readonly ILogger<AgentJobService> _logger;
    private readonly string _jobsRootFolder;

    public AgentJobService(
        IModelInstancePool modelPool,
        AppConfiguration appConfig,
        IClusterPeerClient clusterPeerClient,
        ILogger<AgentJobService> logger,
        IAgentRunRepository? runRepository = null,
        IUtilityToolsService? utilityTools = null,
        IExternalToolsService? externalTools = null)
    {
        _modelPool = modelPool ?? throw new ArgumentNullException(nameof(modelPool));
        _appConfig = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
        _clusterPeerClient = clusterPeerClient ?? throw new ArgumentNullException(nameof(clusterPeerClient));
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
    public async Task<Guid> StartJobAsync(string goalDescription, int? maxIterations, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(goalDescription);

        var peers = _appConfig.Cluster.Peers;

        // Skip every cluster check entirely when there's nothing to forward to, or when this
        // node still has room — a single-node deployment (no peers configured) pays zero
        // overhead beyond this one property read.
        if (peers.Count > 0 && _modelPool.AvailableCount == 0)
        {
            foreach (var peer in peers)
            {
                var capacity = await _clusterPeerClient.GetAvailableCapacityAsync(peer, cancellationToken);
                if (capacity is null or <= 0)
                    continue;

                var peerJobId = await _clusterPeerClient.ForwardJobAsync(peer, goalDescription, maxIterations, cancellationToken);
                if (peerJobId is null)
                    continue;

                var forwardedJobId = Guid.NewGuid();
                _jobs[forwardedJobId] = new RemoteJobEntry(peer, peerJobId.Value);
                _logger.LogInformation(
                    "Forwarded job {JobId} to peer {Peer} (peer's own id {PeerJobId})",
                    forwardedJobId, peer.Name, peerJobId);
                return forwardedJobId;
            }

            _logger.LogInformation("Local capacity saturated and no peer had room — running job locally, queued behind the local pool.");
        }

        return StartLocalJob(goalDescription, maxIterations);
    }

    private Guid StartLocalJob(string goalDescription, int? maxIterations)
    {
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
            _runRepository,
            qb64Tools: null,
            // Unattended jobs are exactly where a destructive edit costs the most: nobody is
            // watching, and the workspace is the only record of the work. The backups sit inside
            // the job's own workspace directory, so they are downloadable with the rest of it.
            backups: new WorkspaceBackupService(
                fileAgent,
                neverBackedUpNames: new[] { GoalAgentService.TranscriptFileName }));

        _jobs[jobId] = new LocalJobEntry(goalAgent, workspacePath);

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
    public async Task<AgentJobStatus?> GetStatusAsync(Guid jobId, CancellationToken cancellationToken)
    {
        if (_jobs.TryGetValue(jobId, out var entry))
        {
            return entry switch
            {
                LocalJobEntry local => BuildLocalStatus(jobId, local.Agent),
                RemoteJobEntry remote => await _clusterPeerClient.GetRemoteStatusAsync(remote.Peer, remote.PeerJobId, cancellationToken),
                _ => null
            };
        }

        // Not tracked in this process — either an unknown id, or a locally-run job whose
        // in-memory state was lost (e.g. this process restarted). Falls back to this node's own
        // persisted history, which works for a job that ran HERE (same runId bridges the two —
        // see GoalAgentService.RunAsync's runId parameter). A job this node had FORWARDED to a
        // peer before restarting can't be recovered this way: the forwarding relationship itself
        // only ever existed in memory. Accepted Phase 2 limitation, not solved here.
        return await GetPersistedStatusAsync(jobId);
    }

    private static AgentJobStatus BuildLocalStatus(Guid jobId, GoalAgentService agent) => new(
        jobId,
        agent.Phase.ToString(),
        agent.GoalDescription ?? string.Empty,
        agent.CurrentIteration,
        agent.MaxIterations,
        agent.Requirements
            .Select(r => new AgentJobRequirementStatus(r.Description, r.Status.ToString(), r.LastVerdict))
            .ToList(),
        agent.ActivityLog.ToList());

    private async Task<AgentJobStatus?> GetPersistedStatusAsync(Guid jobId)
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
    public async Task<Stream?> GetResultZipAsync(Guid jobId, CancellationToken cancellationToken)
    {
        if (!_jobs.TryGetValue(jobId, out var entry))
            return null;

        return entry switch
        {
            LocalJobEntry local => BuildLocalResultZip(local.WorkspacePath),
            RemoteJobEntry remote => await _clusterPeerClient.GetRemoteResultZipAsync(remote.Peer, remote.PeerJobId, cancellationToken),
            _ => null
        };
    }

    /// <summary>
    /// Built in memory rather than to a temp file — jobs are expected to produce at most a
    /// handful of MB of text/code, well within what's reasonable to hold in memory for the
    /// length of one response.
    /// </summary>
    private static Stream? BuildLocalResultZip(string workspacePath)
    {
        if (!Directory.Exists(workspacePath))
            return null;

        var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in Directory.GetFiles(workspacePath, "*", SearchOption.AllDirectories))
            {
                var entryName = Path.GetRelativePath(workspacePath, file);
                zip.CreateEntryFromFile(file, entryName);
            }
        }
        buffer.Position = 0;
        return buffer;
    }

    /// <inheritdoc/>
    public async Task<bool> RequestStopAsync(Guid jobId, CancellationToken cancellationToken)
    {
        if (!_jobs.TryGetValue(jobId, out var entry))
            return false;

        switch (entry)
        {
            case LocalJobEntry local:
                local.Agent.RequestStop();
                return true;
            case RemoteJobEntry remote:
                return await _clusterPeerClient.RequestRemoteStopAsync(remote.Peer, remote.PeerJobId, cancellationToken);
            default:
                return false;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<AgentJobSummary>> GetRecentJobsAsync(int count, CancellationToken cancellationToken)
    {
        if (_runRepository is null)
            return Array.Empty<AgentJobSummary>();

        var runs = await _runRepository.GetRecentRunsAsync(count);
        return runs
            .Select(r => new AgentJobSummary(r.Id, r.GoalDescription, r.Phase, r.Iterations, r.MaxIterations, r.StartedAt, r.CompletedAt))
            .ToList();
    }
}
