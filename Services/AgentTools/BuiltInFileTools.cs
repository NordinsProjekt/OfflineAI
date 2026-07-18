using System.ComponentModel;
using Microsoft.SemanticKernel;
using Services.FileAgent;

namespace Services.AgentTools;

/// <summary>
/// Semantic Kernel plugin that exposes <see cref="IFileAgentService"/> file operations
/// as <c>[KernelFunction]</c> methods so Gemma 4 can call them automatically during
/// a tool-calling session.
/// <para>
/// Register with the kernel before calling
/// <c>IGemma4AgentService.ChatWithToolsAsync</c>:
/// <code>
/// var kernel = gemma4Service.CreateKernel();
/// kernel.Plugins.AddFromObject(new BuiltInFileTools(fileAgent), "files");
/// string answer = await gemma4Service.ChatWithToolsAsync(userMessage, kernel);
/// </code>
/// Gemma 4 will then request these tools when needed, and Semantic Kernel invokes
/// them automatically, feeding the results back to the model.
/// </para>
/// </summary>
public sealed class BuiltInFileTools(IFileAgentService fileAgent)
{
    private readonly IFileAgentService _fileAgent =
        fileAgent ?? throw new ArgumentNullException(nameof(fileAgent));

    /// <summary>Creates an empty file in the agent directory.</summary>
    [KernelFunction("create_file")]
    [Description("Creates an empty file in the agent directory.")]
    public async Task<string> CreateFileAsync(
        [Description("Bare filename to create (no directory path), e.g. notes.txt")]
        string filename)
    {
        var result = await _fileAgent.ExecuteAsync($"/skapa {filename}");
        return result.Message;
    }

    /// <summary>Reads the full text content of a file from the agent directory.</summary>
    [KernelFunction("read_file")]
    [Description("Reads the full text content of a file from the agent directory.")]
    public async Task<string> ReadFileAsync(
        [Description("Bare filename to read, e.g. notes.txt")]
        string filename)
    {
        var result = await _fileAgent.ReadFileRawAsync(filename);
        return result.InjectedContext ?? result.Message;
    }

    /// <summary>Extracts the text content of a PDF file from the agent directory.</summary>
    [KernelFunction("read_pdf")]
    [Description("Extracts the text content of a PDF file in the agent directory, so it can be summarized or used to decide on a next action.")]
    public async Task<string> ReadPdfAsync(
        [Description("Bare PDF filename to read, e.g. report.pdf")]
        string filename)
    {
        var result = await _fileAgent.ReadPdfFileAsync(filename);
        return result.InjectedContext ?? result.Message;
    }

    /// <summary>
    /// Writes text content to a file in the agent directory,
    /// replacing any existing content.
    /// </summary>
    [KernelFunction("write_file")]
    [Description("Writes text content to a named file in the agent directory, overwriting any existing content.")]
    public async Task<string> WriteFileAsync(
        [Description("Bare filename to write, e.g. notes.txt")]
        string filename,
        [Description("The full text content to write to the file")]
        string content)
    {
        await _fileAgent.WriteExtractedContentAsync(filename, content);
        return $"✓ Content written to {filename}";
    }

    /// <summary>
    /// Replaces a contiguous range of lines in a file with new content.
    /// </summary>
    [KernelFunction("edit_file_lines")]
    [Description("Replaces a contiguous range of lines (1-based, inclusive) in a file with new content. Use read_file first to see the current line numbers.")]
    public async Task<string> EditFileLinesAsync(
        [Description("Bare filename to edit, e.g. notes.txt")]
        string filename,
        [Description("First line number (1-based) to replace")]
        int startLine,
        [Description("Last line number (1-based, inclusive) to replace; same as startLine for a single-line edit")]
        int endLine,
        [Description("The new text that should replace the given line range")]
        string newContent)
    {
        var edit = new LineEdit(startLine, endLine, newContent);
        var result = await _fileAgent.ApplyLineEditsAsync(filename, new[] { edit });
        return result.Message;
    }

    /// <summary>
    /// Inserts new content into a file without removing any existing lines. Use this to add
    /// brand-new code, such as a new method, rather than edit_file_lines (which overwrites).
    /// </summary>
    [KernelFunction("insert_file_lines")]
    [Description("Inserts new content (e.g. a brand-new method) into a file relative to an existing line, without removing anything. Use read_file first to see the current line numbers and pick an anchor line that keeps the new code inside the correct class/namespace.")]
    public async Task<string> InsertFileLinesAsync(
        [Description("Bare filename to edit, e.g. notes.txt")]
        string filename,
        [Description("The 1-based anchor line number the new content is inserted relative to")]
        int anchorLine,
        [Description("If true, insert immediately after anchorLine (use 0 to insert at the very top of the file); if false, insert immediately before anchorLine (use lineCount + 1 to append at the end of the file)")]
        bool insertAfter,
        [Description("The new text to insert, e.g. a complete new method")]
        string newContent)
    {
        var edit = insertAfter
            ? LineEdit.InsertAfterLine(anchorLine, newContent)
            : LineEdit.InsertBeforeLine(anchorLine, newContent);
        var result = await _fileAgent.ApplyLineEditsAsync(filename, new[] { edit });
        return result.Message;
    }

    /// <summary>Lists all files currently stored in the agent directory.</summary>
    [KernelFunction("list_files")]
    [Description("Lists all files currently stored in the agent directory.")]
    public string ListFiles()
    {
        var files = Directory
            .GetFiles(_fileAgent.BaseDirectory)
            .Select(Path.GetFileName)
            .Where(f => f is not null)
            .ToArray();

        return files.Length == 0
            ? "No files in the agent directory."
            : string.Join(", ", files);
    }
}
