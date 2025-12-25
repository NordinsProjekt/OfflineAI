namespace AiDashboard.Services.Interfaces;

/// <summary>
/// Service for formatting LLM responses with proper code block formatting.
/// Detects code blocks in various languages and formats them with indentation and line breaks.
/// </summary>
public interface ILlmResponseFormatterService
{
    /// <summary>
    /// Formats an LLM response by detecting and formatting code blocks.
    /// </summary>
    /// <param name="response">Raw LLM response text</param>
    /// <returns>Formatted response with properly indented code blocks</returns>
    string FormatResponse(string response);
    
    /// <summary>
    /// Detects if the text contains code blocks.
    /// </summary>
    /// <param name="text">Text to check</param>
    /// <returns>True if code blocks are detected</returns>
    bool ContainsCodeBlocks(string text);
    
    /// <summary>
    /// Extracts all code blocks from the text.
    /// </summary>
    /// <param name="text">Text containing code blocks</param>
    /// <returns>List of detected code blocks with their language</returns>
    List<CodeBlock> ExtractCodeBlocks(string text);
}

/// <summary>
/// Represents a code block with language and content.
/// </summary>
public class CodeBlock
{
    /// <summary>
    /// Programming language (e.g., "csharp", "python", "javascript")
    /// </summary>
    public string Language { get; set; } = string.Empty;
    
    /// <summary>
    /// Raw code content
    /// </summary>
    public string RawCode { get; set; } = string.Empty;
    
    /// <summary>
    /// Formatted code with proper indentation and line breaks
    /// </summary>
    public string FormattedCode { get; set; } = string.Empty;
    
    /// <summary>
    /// Start position in original text
    /// </summary>
    public int StartIndex { get; set; }
    
    /// <summary>
    /// End position in original text
    /// </summary>
    public int EndIndex { get; set; }
}
