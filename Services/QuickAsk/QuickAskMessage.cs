using System;

namespace Services.QuickAsk;

/// <summary>
/// Represents a message in a QuickAsk conversation.
/// </summary>
public class QuickAskMessage
{
    /// <summary>
    /// Indicates whether this message is from the user (true) or AI (false).
    /// </summary>
    public bool IsUser { get; set; }
    
    /// <summary>
    /// The raw text content of the message.
    /// </summary>
    public string Text { get; set; } = "";
    
    /// <summary>
    /// The HTML-formatted text ready for display.
    /// </summary>
    public string FormattedText { get; set; } = "";
    
    /// <summary>
    /// When the message was created.
    /// </summary>
    public DateTime? Timestamp { get; set; }
    
    /// <summary>
    /// Performance metric: estimated tokens per second for AI responses.
    /// </summary>
    public double? TokensPerSecond { get; set; }
}
