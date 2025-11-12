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
    /// Default: 45000 (45 seconds)
    /// </summary>
    public int TimeoutMs { get; set; } = 45000;
}

public class DebugSettings
{
    /// <summary>
    /// Enable debug mode (shows system prompts, debug commands)
    /// Default: false (production mode)
    /// </summary>
    public bool EnableDebugMode { get; set; } = false;

    /// <summary>
    /// Collection name for vector memory
    /// Default: "game-rules-mpnet"
    /// </summary>
    public string CollectionName { get; set; } = "game-rules-mpnet";
}
