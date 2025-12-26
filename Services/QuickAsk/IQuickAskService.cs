namespace Services.QuickAsk;

/// <summary>
/// Service for managing QuickAsk conversations.
/// Handles message formatting, processing, and conversation management.
/// </summary>
public interface IQuickAskService
{
    /// <summary>
    /// Formats a message for display, applying appropriate formatting based on whether it's a user or AI message.
    /// </summary>
    /// <param name="text">The raw message text.</param>
    /// <param name="isUser">True if this is a user message, false if it's an AI message.</param>
    /// <returns>HTML-formatted text ready for display.</returns>
    string FormatMessage(string text, bool isUser);
    
    /// <summary>
    /// Creates a user message with formatted text.
    /// </summary>
    /// <param name="text">The user's message text.</param>
    /// <returns>A QuickAskMessage ready to be added to the conversation.</returns>
    QuickAskMessage CreateUserMessage(string text);
    
    /// <summary>
    /// Creates an AI response message with formatted text and performance metrics.
    /// </summary>
    /// <param name="text">The AI's response text.</param>
    /// <param name="startTime">When the request was initiated.</param>
    /// <returns>A QuickAskMessage with calculated tokens per second.</returns>
    QuickAskMessage CreateAiMessage(string text, DateTime startTime);
    
    /// <summary>
    /// Creates an error message.
    /// </summary>
    /// <param name="errorMessage">The error message text.</param>
    /// <returns>A QuickAskMessage formatted as an error.</returns>
    QuickAskMessage CreateErrorMessage(string errorMessage);
    
    /// <summary>
    /// Formats a model filename for display, removing extension and intelligently truncating if needed.
    /// </summary>
    /// <param name="modelFileName">The full model filename.</param>
    /// <returns>A shortened, user-friendly model name.</returns>
    string FormatModelName(string modelFileName);
}
