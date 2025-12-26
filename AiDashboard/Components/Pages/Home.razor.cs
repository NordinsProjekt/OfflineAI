using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using AiDashboard.State;
using AiDashboard.Models;
using AiDashboard.Services.Interfaces;

namespace AiDashboard.Components.Pages;

public partial class Home : IDisposable
{
    [Inject] private DashboardState Dashboard { get; set; } = default!;
    [Inject] private ILlmResponseFormatterService Formatter { get; set; } = default!;

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
            msg.FormattedText = FormatMessage(msg.Text, msg.IsUser);
        }
    }

    private void Refresh() => InvokeAsync(StateHasChanged);

    private string FormatMessage(string text, bool isUser)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // For AI messages, use the full formatter with syntax highlighting
        if (!isUser)
        {
            // The formatter handles everything: code blocks, line breaks, HTML encoding
            return Formatter.FormatResponse(text);
        }

        // For user messages, simple formatting
        // Convert markdown-style bold **text** to HTML <strong>text</strong>
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
        
        // Escape HTML to prevent injection
        text = System.Net.WebUtility.HtmlEncode(text);
        
        // Restore the strong tags we just added
        text = text.Replace("&lt;strong&gt;", "<strong>").Replace("&lt;/strong&gt;", "</strong>");
        
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
        userMsg.FormattedText = FormatMessage(userMsg.Text, isUser: true);
        messages.Add(userMsg);
        StateHasChanged();

        try
        {
            // Get AI response
            var response = await Dashboard.SendMessageAsync(userMessage);

            // Add AI response - formatter handles all formatting
            var aiMsg = new ChatMessageModel { IsUser = false, Text = response };
            aiMsg.FormattedText = FormatMessage(aiMsg.Text, isUser: false);
            messages.Add(aiMsg);
        }
        catch (Exception ex)
        {
            var errorMsg = new ChatMessageModel { IsUser = false, Text = $"[ERROR] {ex.Message}" };
            errorMsg.FormattedText = FormatMessage(errorMsg.Text, isUser: false);
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