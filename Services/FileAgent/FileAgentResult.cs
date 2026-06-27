namespace Services.FileAgent;

/// <summary>
/// Describes the outcome type of a file agent command.
/// </summary>
public enum FileAgentResultType
{
    /// <summary>
    /// Input was not a file agent command — pass through to AI normally.
    /// </summary>
    NotACommand,

    /// <summary>
    /// A new file was created successfully.
    /// </summary>
    FileCreated,

    /// <summary>
    /// An existing file was filled with new content successfully.
    /// </summary>
    FileFilled,

    /// <summary>
    /// The /fyll command was parsed; the caller should send <see cref="FileAgentResult.LlmPrompt"/>
    /// to the LLM, then call <see cref="IFileAgentService.TryExtractFileContent"/> on the response
    /// and save it with <see cref="IFileAgentService.WriteExtractedContentAsync"/>.
    /// </summary>
    FillRequested,

    /// <summary>
    /// A file was read; its content is available in <see cref="FileAgentResult.InjectedContext"/>.
    /// </summary>
    FileRead,

    /// <summary>
    /// The command was recognized but failed (invalid filename, file not found, etc.).
    /// </summary>
    Error
}

/// <summary>
/// Result returned by <see cref="IFileAgentService.ExecuteAsync"/>.
/// </summary>
public class FileAgentResult
{
    /// <summary>
    /// The type of result produced by the command.
    /// </summary>
    public FileAgentResultType ResultType { get; init; }

    /// <summary>
    /// Whether the command completed successfully.
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// A user-facing message describing what happened (success confirmation or error detail).
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// For <see cref="FileAgentResultType.FileRead"/> results, contains the file content
    /// that should be forwarded to the AI as the prompt.
    /// Null for all other result types.
    /// </summary>
    public string? InjectedContext { get; init; }

    /// <summary>
    /// For <see cref="FileAgentResultType.FillRequested"/> results, the bare filename
    /// that the extracted content should be written to.
    /// </summary>
    public string? TargetFilename { get; init; }

    /// <summary>
    /// For <see cref="FileAgentResultType.FillRequested"/> results, the structured prompt
    /// that should be sent to the LLM so it generates the file content.
    /// </summary>
    public string? LlmPrompt { get; init; }

    /// <summary>
    /// Creates a successful result with no injected context (for /skapa).
    /// </summary>
    public static FileAgentResult Success(FileAgentResultType type, string message) =>
        new() { ResultType = type, IsSuccess = true, Message = message };

    /// <summary>
    /// Creates a FillRequested result carrying the target filename and the LLM prompt.
    /// </summary>
    public static FileAgentResult FillRequest(string filename, string llmPrompt) =>
        new()
        {
            ResultType     = FileAgentResultType.FillRequested,
            IsSuccess      = true,
            Message        = $"Genererar innehåll för: {filename}",
            TargetFilename = filename,
            LlmPrompt      = llmPrompt
        };

    /// <summary>
    /// Creates a successful FileRead result containing the file content.
    /// </summary>
    public static FileAgentResult ReadSuccess(string message, string content) =>
        new() { ResultType = FileAgentResultType.FileRead, IsSuccess = true, Message = message, InjectedContext = content };

    /// <summary>
    /// Creates an error result.
    /// </summary>
    public static FileAgentResult Failure(string message) =>
        new() { ResultType = FileAgentResultType.Error, IsSuccess = false, Message = message };

    /// <summary>
    /// Creates a result indicating the input was not a file agent command.
    /// </summary>
    public static FileAgentResult NotACommand() =>
        new() { ResultType = FileAgentResultType.NotACommand, IsSuccess = false };
}
