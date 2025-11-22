using AiDashboard.Models;
using AiDashboard.State;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using ChatArea = AiDashboard.Components.Pages.Components.ChatArea;
using ChatTopBar = AiDashboard.Components.Pages.Components.ChatTopBar;
using ChatMessages = AiDashboard.Components.Pages.Components.ChatMessages;
using ChatComposer = AiDashboard.Components.Pages.Components.ChatComposer;

namespace Presentation.AiDashboard.Tests.Components;

/// <summary>
/// Tests for ChatArea component to ensure UI remains consistent across features.
/// </summary>
public class ChatAreaTests : TestContext
{
    private DashboardState CreateMockDashboardState()
    {
        var state = new DashboardState();
        return state;
    }

    [Fact]
    public void ChatArea_Renders_WithCorrectStructure()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<ChatArea>(parameters => parameters
            .Add(p => p.Messages, new List<ChatMessageModel>())
            .Add(p => p.IsProcessing, false)
            .Add(p => p.ComposerText, string.Empty));

        // Assert
        Assert.NotNull(cut.Find(".oa-chat"));
        Assert.NotNull(cut.FindComponent<ChatTopBar>());
        Assert.NotNull(cut.FindComponent<ChatMessages>());
        Assert.NotNull(cut.FindComponent<ChatComposer>());
    }

    [Fact]
    public void ChatArea_ShowsExpandButton_WhenSidebarCollapsed()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        dashboardState.Collapsed = true;
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<ChatArea>(parameters => parameters
            .Add(p => p.Messages, new List<ChatMessageModel>())
            .Add(p => p.IsProcessing, false)
            .Add(p => p.ComposerText, string.Empty));

        // Assert
        var expandButton = cut.Find(".oa-expand");
        Assert.NotNull(expandButton);
        Assert.Equal("button", expandButton.GetAttribute("type"));
        Assert.Equal("Expand", expandButton.GetAttribute("title"));
        Assert.Equal("Expand sidebar", expandButton.GetAttribute("aria-label"));
    }

    [Fact]
    public void ChatArea_HidesExpandButton_WhenSidebarExpanded()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        dashboardState.Collapsed = false;
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<ChatArea>(parameters => parameters
            .Add(p => p.Messages, new List<ChatMessageModel>())
            .Add(p => p.IsProcessing, false)
            .Add(p => p.ComposerText, string.Empty));

        // Assert
        var expandButtons = cut.FindAll(".oa-expand");
        Assert.Empty(expandButtons);
    }

    [Fact]
    public void ChatArea_ExpandButton_CallsToggleSidebar()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        dashboardState.Collapsed = true;
        Services.AddSingleton(dashboardState);

        var cut = RenderComponent<ChatArea>(parameters => parameters
            .Add(p => p.Messages, new List<ChatMessageModel>())
            .Add(p => p.IsProcessing, false)
            .Add(p => p.ComposerText, string.Empty));

        var expandButton = cut.Find(".oa-expand");

        // Act
        expandButton.Click();

        // Assert
        Assert.False(dashboardState.Collapsed);
    }

    [Fact]
    public void ChatArea_PassesMessages_ToChatMessages()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        var messages = new List<ChatMessageModel>
        {
            new ChatMessageModel
            {
                IsUser = true,
                Text = "Test message",
                FormattedText = "Test message",
                Timestamp = DateTime.Now
            }
        };

        // Act
        var cut = RenderComponent<ChatArea>(parameters => parameters
            .Add(p => p.Messages, messages)
            .Add(p => p.IsProcessing, false)
            .Add(p => p.ComposerText, string.Empty));

        // Assert
        var chatMessages = cut.FindComponent<ChatMessages>();
        Assert.NotNull(chatMessages);
        Assert.Single(chatMessages.Instance.Messages);
        Assert.Equal("Test message", chatMessages.Instance.Messages[0].Text);
    }

    [Fact]
    public void ChatArea_PassesIsProcessing_ToComponents()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<ChatArea>(parameters => parameters
            .Add(p => p.Messages, new List<ChatMessageModel>())
            .Add(p => p.IsProcessing, true)
            .Add(p => p.ComposerText, string.Empty));

        // Assert
        var chatMessages = cut.FindComponent<ChatMessages>();
        Assert.True(chatMessages.Instance.IsProcessing);

        var chatComposer = cut.FindComponent<ChatComposer>();
        Assert.True(chatComposer.Instance.IsProcessing);
    }

    [Fact]
    public void ChatArea_PassesComposerText_ToChatComposer()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        var composerText = "Draft message";

        // Act
        var cut = RenderComponent<ChatArea>(parameters => parameters
            .Add(p => p.Messages, new List<ChatMessageModel>())
            .Add(p => p.IsProcessing, false)
            .Add(p => p.ComposerText, composerText));

        // Assert
        var chatComposer = cut.FindComponent<ChatComposer>();
        Assert.Equal(composerText, chatComposer.Instance.ComposerText);
    }

    [Fact]
    public void ChatArea_ComposerTextChanged_InvokesCallback()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        string? capturedText = null;

        var cut = RenderComponent<ChatArea>(parameters => parameters
            .Add(p => p.Messages, new List<ChatMessageModel>())
            .Add(p => p.IsProcessing, false)
            .Add(p => p.ComposerText, string.Empty)
            .Add(p => p.OnComposerTextChanged, text => capturedText = text));

        var chatComposer = cut.FindComponent<ChatComposer>();
        var textarea = chatComposer.Find(".oa-input");

        // Act
        textarea.Input("New text");

        // Assert
        Assert.Equal("New text", capturedText);
    }

    [Fact]
    public void ChatArea_SendMessage_InvokesCallback()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        var callbackInvoked = false;

        var cut = RenderComponent<ChatArea>(parameters => parameters
            .Add(p => p.Messages, new List<ChatMessageModel>())
            .Add(p => p.IsProcessing, false)
            .Add(p => p.ComposerText, "Test message")
            .Add(p => p.OnSendMessage, () => callbackInvoked = true));

        var chatComposer = cut.FindComponent<ChatComposer>();
        var sendButton = chatComposer.Find(".oa-send");

        // Act
        sendButton.Click();

        // Assert
        Assert.True(callbackInvoked);
    }

    [Fact]
    public void ChatArea_HasCorrectCssClass()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<ChatArea>(parameters => parameters
            .Add(p => p.Messages, new List<ChatMessageModel>())
            .Add(p => p.IsProcessing, false)
            .Add(p => p.ComposerText, string.Empty));

        // Assert
        var mainElement = cut.Find("main");
        Assert.Contains("oa-chat", mainElement.ClassName);
    }
}
