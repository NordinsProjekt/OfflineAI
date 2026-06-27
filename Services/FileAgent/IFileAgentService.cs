namespace Services.FileAgent;

/// <summary>
/// Handles file agent slash commands (/skapa, /fyll, /läs) in the chat input.
/// Implement this service in the Services project so it can be reused across the solution.
/// </summary>
public interface IFileAgentService
{
    /// <summary>
    /// The base directory where agent-managed files are stored.
    /// </summary>
    string BaseDirectory { get; }

    /// <summary>
    /// Returns true if the given input starts with a recognised file agent command
    /// (/skapa, /fyll, /läs, /las).
    /// </summary>
    bool IsCommand(string input);

    /// <summary>
    /// Executes the file agent command encoded in <paramref name="input"/> and returns
    /// a <see cref="FileAgentResult"/> describing what happened.
    /// <para>
    /// For <c>/läs</c> — <see cref="FileAgentResult.InjectedContext"/> contains file content
    /// to forward to the LLM as the prompt.
    /// </para>
    /// <para>
    /// For <c>/fyll</c> — <see cref="FileAgentResult.ResultType"/> is
    /// <see cref="FileAgentResultType.FillRequested"/>; send <see cref="FileAgentResult.LlmPrompt"/>
    /// to the LLM and pass the response to <see cref="TryExtractFileContent"/>.
    /// </para>
    /// </summary>
    Task<FileAgentResult> ExecuteAsync(string input);

    /// <summary>
    /// Tries to extract the file content block delimited by <c>&lt;&lt;&lt;FIL&gt;&gt;&gt;</c> /
    /// <c>&lt;&lt;&lt;SLUT&gt;&gt;&gt;</c> from an LLM response.
    /// Returns <c>true</c> and sets <paramref name="content"/> when the block is found.
    /// </summary>
    bool TryExtractFileContent(string llmResponse, out string content);

    /// <summary>
    /// Writes <paramref name="content"/> to <paramref name="filename"/> inside
    /// <see cref="BaseDirectory"/>. Path-traversal safe.
    /// </summary>
    Task WriteExtractedContentAsync(string filename, string content);

    /// <summary>
    /// Returns the LLM response with the raw marker lines (<c>&lt;&lt;&lt;FIL&gt;&gt;&gt;</c>
    /// and <c>&lt;&lt;&lt;SLUT&gt;&gt;&gt;</c>) removed so the content displays cleanly in chat.
    /// </summary>
    string StripFileMarkers(string llmResponse);
}

