namespace AgentKit.Skills.Files;

/// <summary>
/// Handles file agent slash commands (/skapa, /fyll, /läs, /läs-pdf, /redigera, /lista) in the chat input.
/// Implement this service in the Services project so it can be reused across the solution.
/// <para>
/// These same commands double as the tool set for the lightweight agentic pattern used by
/// <c>IAgenticChatService</c>: <see cref="GetToolDescriptions"/> and
/// <see cref="BuildToolsSystemPrompt"/> describe them to the LLM, and
/// <see cref="TryFindAgentCommand"/> detects when the LLM itself requests one.
/// Note that <c>/läs</c> requires both a filename and an instruction
/// (<c>/läs &lt;filnamn&gt; &lt;instruktion&gt;</c>) — the command must always carry an explicit
/// task for the agent, not just raw file content used as the prompt.
/// </para>
/// <para>
/// <c>/redigera &lt;filnamn&gt; &lt;instruktion&gt;</c> follows the same two-phase pattern as
/// <c>/fyll</c>: the file is read and presented to the LLM with line numbers, the LLM replies with
/// one or more <c>&lt;REDIGERA RAD=...&gt;</c> blocks describing which lines to replace and with
/// what — or <c>&lt;REDIGERA INFOGA_EFTER=...&gt;</c> / <c>&lt;REDIGERA INFOGA_FÖRE=...&gt;</c>
/// blocks describing brand-new code (e.g. a new method) to insert without removing anything — and
/// the caller applies them via <see cref="TryExtractLineEdits"/> + <see cref="ApplyLineEditsAsync"/>.
/// </para>
/// </summary>
public interface IFileAgentService
{
    /// <summary>
    /// The base directory where agent-managed files are stored.
    /// </summary>
    string BaseDirectory { get; }

    /// <summary>
    /// Re-confines all subsequent file operations to <paramref name="baseDirectory"/>
    /// (created automatically if missing). Used to switch the active
    /// <c>IWorkspaceService</c> workspace at runtime — the LLM can only ever read, create, or
    /// edit files inside whichever directory is active at the time of the call.
    /// </summary>
    void SetBaseDirectory(string baseDirectory);

    /// <summary>
    /// Saves an uploaded file's raw bytes as <paramref name="filename"/> in
    /// <see cref="BaseDirectory"/>, overwriting any existing file with the same name. Unlike
    /// <see cref="WriteExtractedContentAsync"/> (which writes LLM-generated text), this copies
    /// <paramref name="content"/> byte-for-byte, so it is safe for binary formats such as PDF.
    /// Intended for UI file-upload controls that let the user attach a document (e.g. a PDF) to
    /// the active workspace so the LLM can subsequently read it via <c>/läs-pdf</c> or
    /// <see cref="ReadPdfFileAsync"/>.
    /// </summary>
    Task<FileAgentResult> SaveUploadedFileAsync(string filename, Stream content);

    /// <summary>
    /// Returns true if the given input starts with a recognised file agent command
    /// (/skapa, /fyll, /läs, /las, /läs-pdf, /las-pdf, /redigera, /lista).
    /// </summary>
    bool IsCommand(string input);

    /// <summary>
    /// Executes the file agent command encoded in <paramref name="input"/> and returns
    /// a <see cref="FileAgentResult"/> describing what happened.
    /// <para>
    /// For <c>/läs &lt;filnamn&gt; &lt;instruktion&gt;</c> — the instruction is required so the
    /// command carries an explicit task for the agent instead of forwarding the raw file as the
    /// entire prompt. <see cref="FileAgentResult.InjectedContext"/> contains the instruction
    /// combined with the file content, ready to forward to the LLM.
    /// </para>
    /// <para>
    /// For <c>/fyll</c> — <see cref="FileAgentResult.ResultType"/> is
    /// <see cref="FileAgentResultType.FillRequested"/>; send <see cref="FileAgentResult.LlmPrompt"/>
    /// to the LLM and pass the response to <see cref="TryExtractFileContent"/>.
    /// </para>
    /// </summary>
    Task<FileAgentResult> ExecuteAsync(string input);

    /// <summary>
    /// Reads the raw content of <paramref name="filename"/> without requiring or combining an
    /// instruction. Intended for programmatic/tool-calling access — e.g. Semantic Kernel function
    /// calling via <c>BuiltInFileTools</c> — where the calling model already supplies its own
    /// reasoning about what to do with the content. The chat-facing <c>/läs</c> slash command
    /// (see <see cref="ExecuteAsync"/>) requires an explicit instruction instead.
    /// </summary>
    Task<FileAgentResult> ReadFileRawAsync(string filename);

    /// <summary>
    /// Extracts the text content of a PDF file in <see cref="BaseDirectory"/> so the LLM can
    /// reason over it, e.g. to summarize it or decide on a next action. Pages are joined in
    /// order, each preceded by a <c>--- Page N ---</c> marker. Intended for programmatic/tool-calling
    /// access — e.g. Semantic Kernel function calling via <c>BuiltInFileTools</c> — mirroring
    /// <see cref="ReadFileRawAsync"/> for plain text files.
    /// </summary>
    Task<FileAgentResult> ReadPdfFileAsync(string filename);

    /// <summary>
    /// Tries to extract a file content block from an LLM response: the <c>&lt;FILE&gt;</c> /
    /// <c>&lt;ENDFILE&gt;</c> markers when present, otherwise a Markdown code fence (```), which
    /// models frequently use instead. Returns <c>true</c> and sets <paramref name="content"/> when
    /// content is found.
    /// </summary>
    bool TryExtractFileContent(string llmResponse, out string content);

    /// <summary>
    /// If <paramref name="command"/> is a <c>/fyll</c> or <c>/skapa</c> command naming a valid
    /// in-workspace file, returns that filename as the target for an inline-content write. This
    /// supports the common model pattern of issuing the command and putting the file body directly
    /// in the same message (e.g. in a code fence) instead of the describe-then-generate round.
    /// Returns <c>false</c> for any other command, a missing filename, or an invalid filename.
    /// </summary>
    bool TryGetInlineWriteTarget(string command, out string filename);

    /// <summary>
    /// Writes <paramref name="content"/> to <paramref name="filename"/> inside
    /// <see cref="BaseDirectory"/>. Path-traversal safe.
    /// </summary>
    Task WriteExtractedContentAsync(string filename, string content);

    /// <summary>
    /// Returns the LLM response with the raw marker lines (<c>&lt;FILE&gt;</c> and
    /// <c>&lt;ENDFILE&gt;</c>) removed so the content displays cleanly in chat.
    /// </summary>
    string StripFileMarkers(string llmResponse);

    /// <summary>
    /// Tries to extract one or more line-edit blocks from an LLM response produced after a
    /// <c>/redigera</c> request. Two kinds of blocks are recognised:
    /// <list type="bullet">
    ///   <item><c>&lt;REDIGERA RAD=N&gt;...&lt;/REDIGERA&gt;</c> (or <c>RAD=start-slut</c>, e.g.
    ///     <c>RAD=5-7</c>) replaces the given 1-based inclusive line range with new content.</item>
    ///   <item><c>&lt;REDIGERA INFOGA_EFTER=N&gt;...&lt;/REDIGERA&gt;</c> or
    ///     <c>&lt;REDIGERA INFOGA_FÖRE=N&gt;...&lt;/REDIGERA&gt;</c> (ASCII fallback
    ///     <c>INFOGA_FORE</c>) inserts new content — e.g. a brand-new method — immediately after
    ///     or before line <c>N</c> without removing any existing lines.</item>
    /// </list>
    /// Returns <c>true</c> and sets <paramref name="edits"/> when at least one valid block is
    /// found; unmatched or malformed text outside the blocks is ignored.
    /// </summary>
    bool TryExtractLineEdits(string llmResponse, out IReadOnlyList<LineEdit> edits);

    /// <summary>
    /// Applies <paramref name="edits"/> (replacements and/or insertions) to <paramref name="filename"/>
    /// inside <see cref="BaseDirectory"/>. All edits are validated against the file's current line
    /// count and checked for overlaps before anything is written; if any edit is invalid the
    /// file is left unchanged and the returned result describes the problem.
    /// </summary>
    Task<FileAgentResult> ApplyLineEditsAsync(string filename, IReadOnlyList<LineEdit> edits);

    /// <summary>
    /// Returns the LLM response with any <c>&lt;REDIGERA&gt;</c> line-edit blocks removed, so
    /// remaining explanatory text (if any) displays cleanly in chat.
    /// </summary>
    string StripEditMarkers(string llmResponse);

    /// <summary>
    /// Returns a dictionary describing each available slash-command tool: key is the exact
    /// command signature (e.g. <c>"/läs &lt;filnamn&gt; &lt;instruktion&gt;"</c>), value is a
    /// natural-language description of what the tool does. Used to tell the LLM which tools it
    /// may invoke.
    /// </summary>
    IReadOnlyDictionary<string, string> GetToolDescriptions();

    /// <summary>
    /// Builds the instructional preamble ("start message") that tells the LLM about the
    /// available tools from <see cref="GetToolDescriptions"/> and how to invoke them
    /// (by writing the exact slash command on its own line in its reply).
    /// </summary>
    string BuildToolsSystemPrompt();

    /// <summary>
    /// Scans <paramref name="llmResponse"/> line by line — using plain string search via
    /// <see cref="IsCommand"/> — for a known agent slash command the LLM wants to invoke.
    /// Returns <c>true</c> and sets <paramref name="command"/> to the exact command line
    /// (ready to pass to <see cref="ExecuteAsync"/>) when one is found.
    /// <para>
    /// Falls back to recognising a command quoted inline (straight or curly quotes) anywhere in
    /// the response for models that narrate their intent instead of writing the command alone on
    /// its own line, e.g. <c>I will use the "/läs-pdf report.pdf Sammanfatta innehållet" command.</c>
    /// </para>
    /// </summary>
    bool TryFindAgentCommand(string llmResponse, out string command);
}

