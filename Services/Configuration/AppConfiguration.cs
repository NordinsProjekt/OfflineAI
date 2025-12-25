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
    /// Optional context size hint for llama backends
    /// </summary>
    public int ContextSize { get; set; } = 0;
}

public class GenerationSettings
{
    /// <summary>
    /// Maximum number of tokens to generate
    /// Default: 200
    /// Range: 1-2048 (model dependent)
    /// </summary>
    public int MaxTokens { get; set; } = 200;

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
