namespace Services.Configuration;

/// <summary>
/// Application configuration settings for OfflineAI.
/// Can be loaded from appsettings.json, user secrets, or environment variables.
/// </summary>
public class AppConfiguration
{
    /// <summary>
    /// LLM executable settings
    /// </summary>
    public LlmSettings Llm { get; set; } = new();

    /// <summary>
    /// BERT embedding model settings
    /// </summary>
    public EmbeddingSettings Embedding { get; set; } = new();

    /// <summary>
    /// File processing folder paths
    /// </summary>
    public FolderSettings Folders { get; set; } = new();

    /// <summary>
    /// Model pool configuration
    /// </summary>
    public PoolSettings Pool { get; set; } = new();

    /// <summary>
    /// Debug and logging settings
    /// </summary>
    public DebugSettings Debug { get; set; } = new();

    /// <summary>
    /// LLM generation parameters
    /// </summary>
    public GenerationSettings Generation { get; set; } = new();

    /// <summary>
    /// Database configuration for vector memory
    /// </summary>
    public DatabaseSettings Database { get; set; } = new();

    /// <summary>
    /// Gemma 4 CLI (llama-cli subprocess) settings.
    /// Leave <see cref="Gemma4CliSettings.ModelPath"/> empty to disable.
    /// </summary>
    public Gemma4CliSettings Gemma4Cli { get; set; } = new();

    /// <summary>
    /// Named HTTP API endpoints the agent may call as a tool (see
    /// <c>AgentKit.Skills.Utility.UtilityToolsService</c>), plus the agentic tool-calling loop settings.
    /// </summary>
    public AgentToolsSettings AgentTools { get; set; } = new();

    /// <summary>
    /// Settings for headless goal-agent jobs (see <c>AgentKit.Api</c>'s job API).
    /// </summary>
    public JobsSettings Jobs { get; set; } = new();

    /// <summary>
    /// Other <c>AgentKit.Api</c> instances this node can forward jobs to when it's too busy to
    /// take one itself. Empty (the default) means no clustering — every job runs locally.
    /// </summary>
    public ClusterSettings Cluster { get; set; } = new();
}

/// <summary>
/// Peer <c>AgentKit.Api</c> nodes this instance can forward jobs to. A static list, not
/// auto-discovered — see <see cref="ClusterPeerSettings"/>.
/// </summary>
public class ClusterSettings
{
    /// <summary>
    /// Known peer nodes, in the order they're tried when this node is too busy for a new job.
    /// Empty = clustering disabled; every job runs locally regardless of load.
    /// </summary>
    public List<ClusterPeerSettings> Peers { get; set; } = new();
}

/// <summary>
/// One peer <c>AgentKit.Api</c> node this instance may forward jobs to.
/// </summary>
public class ClusterPeerSettings
{
    /// <summary>Friendly name for logging/identification, e.g. "office-pc".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Base URL of the peer, e.g. "https://192.168.1.50:7016".</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// The <em>peer's own</em> API key — what this node presents as its <c>X-API-Key</c> header
    /// when calling the peer, not this node's own key. The peer authenticates the call the same
    /// way it authenticates any other caller; clustering adds no new auth mechanism.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
}

/// <summary>
/// Settings for headless goal-agent jobs run through an HTTP job API rather than the interactive
/// dashboard. Each job gets its own subdirectory under <see cref="RootFolder"/> so concurrent
/// jobs never share a workspace.
/// </summary>
public class JobsSettings
{
    /// <summary>
    /// Root directory under which each job gets its own <c>{jobId}</c> subdirectory as its
    /// workspace. Defaults to Documents\OfflineAI\AgentJobs when left empty.
    /// </summary>
    public string RootFolder { get; set; } = string.Empty;
}

public class AgentToolsSettings
{
    /// <summary>
    /// Maximum number of internal tool-call round trips the agentic chat loop
    /// (<c>IAgenticChatService</c>) performs before it must return a final answer to the user.
    /// Default: 3.
    /// </summary>
    public int MaxToolCallRounds { get; set; } = 3;

    /// <summary>
    /// Maximum number of work → verify iterations the goal agent
    /// (<c>IGoalAgentService</c>) performs before giving up with requirements still unmet.
    /// The loop already exits as soon as every requirement passes verification (see
    /// <c>GoalAgentService.RunAsync</c>), so this is only a safety cap for the pathological
    /// case — a higher value lets weaker models keep retrying instead of hitting the cap
    /// while requirements are still being worked on. Default: 20.
    /// </summary>
    public int MaxGoalIterations { get; set; } = 20;

    /// <summary>
    /// Named HTTP API endpoints the LLM may call by name via the <c>call_api</c> tool. Only
    /// endpoints listed here can be invoked — the LLM cannot supply an arbitrary URL.
    /// </summary>
    public List<ApiEndpointSettings> Endpoints { get; set; } = new();

    /// <summary>
    /// Named local executables the LLM may run by slash command. Only executables listed here
    /// (typically in user secrets or appsettings) can be started — the LLM selects a tool by
    /// its command name and can never supply an arbitrary path; it only provides the argument
    /// text. Stdout is captured and fed back to the LLM as the tool result.
    /// </summary>
    public List<ExternalToolSettings> ExternalTools { get; set; } = new();

    /// <summary>
    /// QB64 (QBasic) compiler integration (see <c>AgentKit.Skills.Qb64.Qb64ToolService</c>):
    /// lets the LLM compile and run .bas files from the active workspace via
    /// <c>/qb64</c> and <c>/qb64-kompilera</c>. Leave <see cref="Qb64Settings.CompilerPath"/>
    /// empty to disable.
    /// </summary>
    public Qb64Settings Qb64 { get; set; } = new();
}

/// <summary>
/// Settings for the QB64 compiler tool (<c>AgentKit.Skills.Qb64.Qb64ToolService</c>). The LLM
/// only ever supplies a bare .bas filename that is resolved inside the active workspace — the
/// compiler path and argument shape come exclusively from this configuration.
/// </summary>
public class Qb64Settings
{
    /// <summary>
    /// Full path to qb64.exe (download from https://qb64.com/ or QB64 Phoenix Edition).
    /// Leave empty to disable the QB64 tool — the commands are then never offered to the LLM.
    /// </summary>
    public string CompilerPath { get; set; } = string.Empty;

    /// <summary>
    /// Argument template passed to the compiler. <c>{source}</c> is replaced with the full path
    /// of the .bas file and <c>{output}</c> with the full path of the .exe to produce.
    /// The default uses QB64's headless mode: <c>-x</c> compiles without opening the IDE and
    /// writes compiler output (including errors) to the console.
    /// </summary>
    public string CompilerArguments { get; set; } = "-x \"{source}\" -o \"{output}\"";

    /// <summary>
    /// Compile timeout in milliseconds. QB64 invokes a C++ backend under the hood, and the very
    /// first compile on a machine can take considerably longer than subsequent ones.
    /// Default: 180000 (3 minutes).
    /// </summary>
    public int CompileTimeoutMs { get; set; } = 180_000;

    /// <summary>
    /// Run timeout in milliseconds for the compiled program; the process tree is killed when
    /// exceeded (e.g. a program stuck waiting for keyboard input). Output captured up to that
    /// point is still returned to the LLM. Default: 30000.
    /// </summary>
    public int RunTimeoutMs { get; set; } = 30_000;

    /// <summary>Maximum characters of compiler/program output returned to the LLM. Default: 4000.</summary>
    public int MaxOutputLength { get; set; } = 4000;
}

/// <summary>
/// Describes one named, pre-configured local executable the agent is allowed to run.
/// The LLM invokes it as <c>/&lt;Command&gt; &lt;argument&gt;</c> and receives the process's
/// stdout as the tool result. Mirrors the whitelist principle of
/// <see cref="ApiEndpointSettings"/>: paths come only from configuration, never from the LLM.
/// </summary>
public class ExternalToolSettings
{
    /// <summary>
    /// Slash-command name (without the leading '/') the LLM uses to invoke this tool,
    /// e.g. "väder" → the LLM writes "/väder Stockholm".
    /// </summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>Full path to the executable, e.g. "d:\\tools\\weather.exe".</summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>
    /// Description shown to the LLM of what the tool does and what input it expects.
    /// This is the LLM's only documentation for the tool — describe the parameters here.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Optional usage hint appended to the command signature in the tool list shown to the
    /// LLM, e.g. "&lt;ort&gt;" renders as "/väder &lt;ort&gt;". Empty for tools without input.
    /// </summary>
    public string Usage { get; set; } = string.Empty;

    /// <summary>
    /// Optional arguments always passed to the executable, before any text the LLM supplies.
    /// Useful for interpreter-hosted tools, e.g. ExecutablePath "python.exe" with
    /// FixedArguments "d:\\scripts\\tool.py".
    /// </summary>
    public string FixedArguments { get; set; } = string.Empty;

    /// <summary>Per-run timeout in milliseconds; the process is killed when exceeded. Default: 30000.</summary>
    public int TimeoutMs { get; set; } = 30_000;

    /// <summary>Maximum characters of process output returned to the LLM. Default: 4000.</summary>
    public int MaxOutputLength { get; set; } = 4000;
}

/// <summary>
/// Describes one named, pre-configured HTTP endpoint the agent is allowed to call.
/// The LLM selects an endpoint by <see cref="Name"/> and can never specify a raw URL —
/// this keeps outbound calls limited to destinations the user has explicitly configured.
/// </summary>
public class ApiEndpointSettings
{
    /// <summary>Unique name the LLM uses to select this endpoint, e.g. "weather".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Short description shown to the LLM to help it decide when to use this endpoint.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Full request URL. May contain <c>{param}</c> placeholders filled in from the LLM's arguments.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>HTTP method to use. Default: "GET".</summary>
    public string Method { get; set; } = "GET";

    /// <summary>Optional static request headers (e.g. API keys) sent with every call to this endpoint.</summary>
    public Dictionary<string, string> Headers { get; set; } = new();

    /// <summary>Per-request timeout in milliseconds. Default: 15000 (15 seconds).</summary>
    public int TimeoutMs { get; set; } = 15_000;

    /// <summary>Maximum characters of the response body returned to the LLM. Default: 4000.</summary>
    public int MaxResponseLength { get; set; } = 4000;
}

public class LlmSettings
{
    /// <summary>
    /// Path to llama-cli.exe
    /// Example: "d:\tinyllama\llama-cli.exe"
    /// </summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>
    /// Path to the GGUF model file
    /// Example: "d:\tinyllama\tinyllama-1.1b-chat-v1.0.Q5_K_M.gguf"
    /// </summary>
    public string ModelPath { get; set; } = string.Empty;

    /// <summary>
    /// Friendly model name for display purposes
    /// Example: "mistral-7b-instruct-v0.2.Q5_K_M"
    /// </summary>
    public string? ModelName { get; set; }

    /// <summary>
    /// Optional model family/type label (e.g., "Mistral", "Llama3")
    /// </summary>
    public string? ModelType { get; set; }

    /// <summary>
    /// Optional hint whether to use GPU (parsed but not required by runtime logic)
    /// </summary>
    public bool UseGpu { get; set; } = false;

    /// <summary>
    /// Optional GPU layers hint for llama backends
    /// </summary>
    public int GpuLayers { get; set; } = 0;

    /// <summary>
    /// Device for llama backends to offload to, e.g. "CUDA0" (llama.cpp <c>--device</c>).
    /// Discover the names with <c>llama-completion --list-devices</c> on the target machine.
    /// <para>
    /// Leave empty on single-GPU machines. On a multi-GPU machine this is effectively
    /// required: llama.cpp otherwise splits the model across every visible card, including
    /// ones far too small to hold their share.
    /// </para>
    /// </summary>
    public string Device { get; set; } = string.Empty;

    /// <summary>
    /// Optional context size hint for llama backends
    /// </summary>
    public int ContextSize { get; set; } = 0;
}

public class GenerationSettings
{
    /// <summary>
    /// Maximum number of tokens to generate
    /// Default: 2048
    /// Range: 1-2048 (model dependent)
    /// </summary>
    public int MaxTokens { get; set; } = 2048;

    /// <summary>
    /// Temperature for sampling (higher = more creative, lower = more focused)
    /// Default: 0.3
    /// Range: 0.0-2.0
    /// </summary>
    public float Temperature { get; set; } = 0.3f;

    /// <summary>
    /// Top-k sampling parameter (limits vocabulary choices)
    /// Default: 30
    /// </summary>
    public int TopK { get; set; } = 30;

    /// <summary>
    /// Top-p (nucleus) sampling parameter
    /// Default: 0.85
    /// Range: 0.0-1.0
    /// </summary>
    public float TopP { get; set; } = 0.85f;

    /// <summary>
    /// Repeat penalty (discourages repetition)
    /// Default: 1.15
    /// Range: 1.0-2.0
    /// </summary>
    public float RepeatPenalty { get; set; } = 1.15f;

    /// <summary>
    /// Presence penalty (reduces adding new concepts)
    /// Default: 0.2
    /// Range: 0.0-1.0
    /// </summary>
    public float PresencePenalty { get; set; } = 0.2f;

    /// <summary>
    /// Frequency penalty (discourages repeating patterns)
    /// Default: 0.2
    /// Range: 0.0-1.0
    /// </summary>
    public float FrequencyPenalty { get; set; } = 0.2f;

    /// <summary>
    /// Number of relevant chunks to retrieve for RAG
    /// Default: 3
    /// Range: 1-5 (optimal for context window management)
    /// </summary>
    public int RagTopK { get; set; } = 3;

    /// <summary>
    /// Minimum relevance score for RAG chunks (cosine similarity)
    /// Default: 0.5
    /// Range: 0.3-0.8 (lower = more results, higher = stricter filtering)
    /// </summary>
    public double RagMinRelevanceScore { get; set; } = 0.5;
}

public class EmbeddingSettings
{
    /// <summary>
    /// Path to the ONNX BERT model
    /// Example: "d:\tinyllama\models\all-mpnet-base-v2\onnx\model.onnx"
    /// </summary>
    public string ModelPath { get; set; } = string.Empty;

    /// <summary>
    /// Path to the BERT vocabulary file (vocab.txt)
    /// Example: "d:\tinyllama\models\all-mpnet-base-v2\vocab.txt"
    /// Required for proper tokenization with real BERT vocabulary
    /// </summary>
    public string VocabPath { get; set; } = string.Empty;

    /// <summary>
    /// Embedding dimension (384 for MiniLM, 768 for MPNet)
    /// </summary>
    public int Dimension { get; set; } = 768;
}

public class FolderSettings
{
    /// <summary>
    /// Folder to watch for new knowledge files
    /// Example: "d:\tinyllama\inbox"
    /// </summary>
    public string InboxFolder { get; set; } = string.Empty;

    /// <summary>
    /// Folder to archive processed files
    /// Example: "d:\tinyllama\archive"
    /// </summary>
    public string ArchiveFolder { get; set; } = string.Empty;

    /// <summary>
    /// Base directory used by the file agent (/skapa, /fyll, /läs commands).
    /// Defaults to Documents\OfflineAI\AgentFiles when left empty.
    /// </summary>
    public string AgentFilesFolder { get; set; } = string.Empty;

    /// <summary>
    /// Root directory that every workspace must live inside. Workspaces created via the API or
    /// dashboard are rejected if their resolved path falls outside this root, so the file agent
    /// can never be pointed at an arbitrary location on disk (see the WorkspaceService
    /// confinement check). When left empty, the root defaults to the parent directory of
    /// <see cref="AgentFilesFolder"/> (i.e. Documents\OfflineAI when AgentFilesFolder is unset).
    /// Widen this only if you deliberately want workspaces outside that tree.
    /// </summary>
    public string WorkspaceRoot { get; set; } = string.Empty;
}

public class PoolSettings
{
    /// <summary>
    /// Maximum number of model instances to keep in memory
    /// Default: 3 (supports 3-10 concurrent users)
    /// </summary>
    public int MaxInstances { get; set; } = 3;

    /// <summary>
    /// Timeout in milliseconds for model operations
    /// Default: 300000 (5 minutes - 300 seconds)
    /// </summary>
    public int TimeoutMs { get; set; } = 300000;
}

public class DebugSettings
{
    /// <summary>
    /// Enable debug mode (shows system prompts, debug commands)
    /// Default: false (production mode)
    /// </summary>
    public bool EnableDebugMode { get; set; } = false;

    /// <summary>
    /// Enable RAG mode (uses semantic search with vector memory)
    /// When false, directly talks to the LLM without context retrieval
    /// Default: true (RAG enabled)
    /// </summary>
    public bool EnableRagMode { get; set; } = true;

    /// <summary>
    /// Show performance metrics (tokens/sec, timing)
    /// Default: false
    /// </summary>
    public bool ShowPerformanceMetrics { get; set; } = false;

    /// <summary>
    /// Collection name for vector memory
    /// Default: "game-rules-mpnet"
    /// </summary>
    public string CollectionName { get; set; } = "game-rules-mpnet";
}

/// <summary>
/// Configuration for the local Gemma 4 CLI service (llama-cli subprocess).
/// </summary>
public class Gemma4CliSettings
{
    /// <summary>
    /// Path to llama-cli.exe. Falls back to <see cref="LlmSettings.ExecutablePath"/> when empty.
    /// </summary>
    public string LlamaCliPath { get; set; } = string.Empty;

    /// <summary>
    /// Path to the Gemma 4 GGUF model file.
    /// Leave empty to disable the Gemma 4 CLI service.
    /// </summary>
    public string ModelPath { get; set; } = string.Empty;

    /// <summary>Number of GPU layers to offload (0 = CPU only).</summary>
    public int GpuLayers { get; set; } = 0;

    /// <summary>
    /// Device to offload to (e.g. "CUDA0"). Required on multi-GPU machines — see
    /// <see cref="LlmSettings.Device"/>. Falls back to <see cref="LlmSettings.Device"/>
    /// when empty and the Gemma 4 service is running off the Llm section.
    /// </summary>
    public string Device { get; set; } = string.Empty;

    /// <summary>Context window size in tokens (default: 4096).</summary>
    public int ContextSize { get; set; } = 4096;

    /// <summary>Maximum tokens to generate per request (default: 2048).</summary>
    public int MaxTokens { get; set; } = 2048;

    /// <summary>Sampling temperature (default: 0.7).</summary>
    public float Temperature { get; set; } = 0.7f;

    /// <summary>Top-p nucleus sampling (default: 0.9).</summary>
    public float TopP { get; set; } = 0.9f;

    /// <summary>Top-k sampling (default: 40).</summary>
    public int TopK { get; set; } = 40;

    /// <summary>Per-request timeout in milliseconds (default: 120 000 = 2 min).</summary>
    public int TimeoutMs { get; set; } = 120_000;

    /// <summary>
    /// If no new stdout output is produced for this many milliseconds, treat generation as
    /// complete (default: 10 000 = 10 s). Increase substantially for large/partially
    /// CPU-offloaded models, where model loading and prompt processing can pause output for
    /// longer than the default before the first token appears.
    /// </summary>
    public int PauseTimeoutMs { get; set; } = 10_000;

    /// <summary>Maximum tool-call iterations per request (default: 3).</summary>
    public int MaxToolCallIterations { get; set; } = 3;
}

public class DatabaseSettings
{
    /// <summary>
    /// Connection string for the database
    /// Example: "Host=myserver;Database=mydb;Username=myuser;Password=mypass"
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Type of the database (e.g., "PostgreSQL", "SQLServer")
    /// </summary>
    public string DatabaseType { get; set; } = string.Empty;

    /// <summary>
    /// Optional schema name (for databases that supportschemas)
    /// Example: "public"
    /// </summary>
    public string Schema { get; set; } = string.Empty;

    /// <summary>
    /// Active table/collection name for vector memory
    /// Example: "MemoryFragments", "game-rules-mpnet"
    /// </summary>
    public string ActiveTableName { get; set; } = "MemoryFragments";
}
