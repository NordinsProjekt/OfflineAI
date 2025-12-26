using System;
using System.Text.RegularExpressions;

namespace Services.QuickAsk;

/// <summary>
/// Service for managing QuickAsk conversations.
/// Handles message formatting, processing, and conversation management.
/// </summary>
public class QuickAskService : IQuickAskService
{
    /// <summary>
    /// Formats a message for display, applying appropriate formatting based on whether it's a user or AI message.
    /// </summary>
    public string FormatMessage(string text, bool isUser)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        if (!isUser)
        {
            // For AI messages, formatting will be handled by the caller with LlmResponseFormatterService
            // This service focuses on business logic, not presentation dependencies
            return text;
        }

        // For user messages, simple formatting
        // Convert markdown-style bold **text** to HTML <strong>text</strong>
        text = Regex.Replace(text, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
        
        // Escape HTML to prevent injection
        text = System.Net.WebUtility.HtmlEncode(text);
        
        // Restore the strong tags we just added
        text = text.Replace("&lt;strong&gt;", "<strong>").Replace("&lt;/strong&gt;", "</strong>");
        
        // Convert line breaks to <br> for proper rendering
        text = text.Replace("\n", "<br>");

        return text;
    }

    /// <summary>
    /// Creates a user message with formatted text.
    /// </summary>
    public QuickAskMessage CreateUserMessage(string text)
    {
        var message = new QuickAskMessage
        {
            IsUser = true,
            Text = text,
            Timestamp = DateTime.Now
        };
        message.FormattedText = FormatMessage(message.Text, isUser: true);
        return message;
    }

    /// <summary>
    /// Creates an AI response message with formatted text and performance metrics.
    /// </summary>
    public QuickAskMessage CreateAiMessage(string text, DateTime startTime)
    {
        // Calculate tokens per second (rough estimate based on response length)
        var elapsed = (DateTime.Now - startTime).TotalSeconds;
        var estimatedTokens = text.Length / 4; // Rough estimate: 1 token per 4 characters
        var tokensPerSecond = elapsed > 0 ? estimatedTokens / elapsed : 0;

        var message = new QuickAskMessage
        {
            IsUser = false,
            Text = text,
            Timestamp = DateTime.Now,
            TokensPerSecond = tokensPerSecond,
            FormattedText = FormatMessage(text, isUser: false) // Will be replaced by caller with proper formatting
        };
        return message;
    }

    /// <summary>
    /// Creates an error message.
    /// </summary>
    public QuickAskMessage CreateErrorMessage(string errorMessage)
    {
        var message = new QuickAskMessage
        {
            IsUser = false,
            Text = $"Error: {errorMessage}",
            Timestamp = DateTime.Now
        };
        message.FormattedText = FormatMessage(message.Text, isUser: false);
        return message;
    }

    /// <summary>
    /// Formats a model filename for display, removing extension and intelligently truncating if needed.
    /// </summary>
    public string FormatModelName(string modelFileName)
    {
        if (string.IsNullOrEmpty(modelFileName))
            return modelFileName;

        // Remove .gguf extension
        var name = modelFileName.Replace(".gguf", "", StringComparison.OrdinalIgnoreCase);
        
        // If name is too long, truncate intelligently
        if (name.Length > 30)
        {
            // Try to keep the important parts (model name and quantization)
            // Example: "Mistral-14b-Merge-Base-Q5_K_M" -> "Mistral-14b...Q5_K_M"
            var parts = name.Split('-', '_');
            if (parts.Length > 3)
            {
                // Keep first 2 parts and last part
                var shortened = $"{parts[0]}-{parts[1]}...{parts[^1]}";
                if (shortened.Length < 30)
                    return shortened;
            }
            
            // Fallback: simple truncation
            return name.Substring(0, 27) + "...";
        }

        return name;
    }
}
