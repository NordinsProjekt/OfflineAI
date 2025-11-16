using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using AiDashboard.State;
using AiDashboard.Models;

namespace AiDashboard.Components.Pages;

public partial class Home : IDisposable
{
    [Inject]
    private DashboardState Dashboard { get; set; } = default!;

    private string composerText = string.Empty;
    private bool isProcessing = false;
    private ElementReference messagesContainer;

    private List<ChatMessageModel> messages = new()
    {
        new ChatMessageModel
        { 
            IsUser = false, 
            Text = "Hi! I'm ready to chat. Select a collection in the Collections section to use for RAG queries.", 
            Timestamp = DateTime.Now 
        }
    };

    protected override void OnInitialized()
    {
        // Set InvokeAsync callback for thread-safe state updates
        Dashboard.SetInvokeAsync(action => InvokeAsync(action));
        
        Dashboard.OnChange += Refresh;
        
        // Format initial message
        foreach (var msg in messages)
        {
            msg.FormattedText = FormatMessage(msg.Text);
        }
    }

    private void Refresh() => InvokeAsync(StateHasChanged);

    private string FormatMessage(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Convert markdown-style bold **text** to HTML <strong>text</strong>
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
        
        // Escape other HTML to prevent injection
        text = text.Replace("<", "&lt;").Replace(">", "&gt;")
                   .Replace("<strong>", "<strong>").Replace("</strong>", "</strong>"); // But preserve our strong tags
        
        // Convert line breaks to <br> for proper rendering
        text = text.Replace("\n", "<br>");
        
        return text;
    }

    private void OnComposerTextChanged(string value)
    {
        composerText = value;
    }

    private async Task HandleKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && !e.ShiftKey)
        {
            await SendMessage();
        }
    }

    private async Task SendMessage()
    {
        if (string.IsNullOrWhiteSpace(composerText) || isProcessing) return;

        var userMessage = composerText.Trim();
        composerText = string.Empty;
        isProcessing = true;

        // Add user message
        var userMsg = new ChatMessageModel { IsUser = true, Text = userMessage };
        userMsg.FormattedText = FormatMessage(userMsg.Text);
        messages.Add(userMsg);
        StateHasChanged();

        try
        {
            // Get AI response
            var response = await Dashboard.SendMessageAsync(userMessage);

            // Add AI response
            var aiMsg = new ChatMessageModel { IsUser = false, Text = response };
            aiMsg.FormattedText = FormatMessage(aiMsg.Text);
            messages.Add(aiMsg);
        }
        catch (Exception ex)
        {
            var errorMsg = new ChatMessageModel { IsUser = false, Text = $"[ERROR] {ex.Message}" };
            errorMsg.FormattedText = FormatMessage(errorMsg.Text);
            messages.Add(errorMsg);
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    public void Dispose()
    {
        Dashboard.OnChange -= Refresh;
    }
}
