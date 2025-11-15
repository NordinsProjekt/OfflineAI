namespace AiDashboard.Models;

public class ChatMessageModel
{
    public bool IsUser { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string FormattedText { get; set; } = string.Empty;
}
