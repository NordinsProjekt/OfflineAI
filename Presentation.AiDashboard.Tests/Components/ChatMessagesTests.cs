using AiDashboard.Models;
using Bunit;
using Microsoft.AspNetCore.Components;
using ChatMessages = AiDashboard.Components.Pages.Components.ChatMessages;

namespace Presentation.AiDashboard.Tests.Components;

/// <summary>
/// Tests for ChatMessages component to ensure UI remains consistent across features.
/// </summary>
public class ChatMessagesTests : TestContext
{
    [Fact]
    public void ChatMessages_Renders_EmptyState()
    {
        // Arrange & Act
        var cut = RenderComponent<ChatMessages>(parameters => parameters
            .Add(p => p.Messages, new List<ChatMessageModel>())
            .Add(p => p.IsProcessing, false));

        // Assert
        var messagesDiv = cut.Find(".oa-messages");
        Assert.NotNull(messagesDiv);
        Assert.Empty(cut.FindAll(".oa-msg"));
    }

    [Fact]
    public void ChatMessages_Renders_UserMessage()
    {
        // Arrange
        var messages = new List<ChatMessageModel>
        {
            new ChatMessageModel
            {
                IsUser = true,
                Text = "Hello, AI!",
                FormattedText = "Hello, AI!",
                Timestamp = new DateTime(2024, 1, 1, 10, 30, 0, DateTimeKind.Local)
            }
        };

        // Act
        var cut = RenderComponent<ChatMessages>(parameters => parameters
            .Add(p => p.Messages, messages)
            .Add(p => p.IsProcessing, false));

        // Assert
        var userMsg = cut.Find(".oa-message.user");
        Assert.NotNull(userMsg);

        var avatar = userMsg.QuerySelector(".oa-msg-avatar");
        Assert.NotNull(avatar);

        var content = userMsg.QuerySelector(".oa-msg-content");
        Assert.NotNull(content);
        Assert.Contains("Hello, AI!", content.TextContent);

        var meta = userMsg.QuerySelector(".oa-msg-meta");
        Assert.Contains("You", meta?.TextContent);
        Assert.Contains("10:30", meta?.TextContent);
    }

    [Fact]
    public void ChatMessages_Renders_AiMessage()
    {
        // Arrange
        var messages = new List<ChatMessageModel>
        {
            new ChatMessageModel
            {
                IsUser = false,
                Text = "Hello, human!",
                FormattedText = "Hello, human!",
                Timestamp = new DateTime(2024, 1, 1, 10, 31, 0, DateTimeKind.Local)
            }
        };

        // Act
        var cut = RenderComponent<ChatMessages>(parameters => parameters
            .Add(p => p.Messages, messages)
            .Add(p => p.IsProcessing, false));

        // Assert
        var aiMsg = cut.Find(".oa-message.ai");
        Assert.NotNull(aiMsg);

        var avatar = aiMsg.QuerySelector(".oa-msg-avatar");
        Assert.NotNull(avatar);

        var content = aiMsg.QuerySelector(".oa-msg-content");
        Assert.NotNull(content);
        Assert.Contains("Hello, human!", content.TextContent);

        var meta = aiMsg.QuerySelector(".oa-msg-meta");
        Assert.Contains("Assistant", meta?.TextContent);
        Assert.Contains("10:31", meta?.TextContent);
    }

    [Fact]
    public void ChatMessages_Renders_MultipleMessages()
    {
        // Arrange
        var messages = new List<ChatMessageModel>
        {
            new ChatMessageModel
            {
                IsUser = true,
                Text = "Question 1",
                FormattedText = "Question 1",
                Timestamp = DateTime.Now
            },
            new ChatMessageModel
            {
                IsUser = false,
                Text = "Answer 1",
                FormattedText = "Answer 1",
                Timestamp = DateTime.Now
            },
            new ChatMessageModel
            {
                IsUser = true,
                Text = "Question 2",
                FormattedText = "Question 2",
                Timestamp = DateTime.Now
            }
        };

        // Act
        var cut = RenderComponent<ChatMessages>(parameters => parameters
            .Add(p => p.Messages, messages)
            .Add(p => p.IsProcessing, false));

        // Assert
        var allMessages = cut.FindAll(".oa-message");
        Assert.Equal(3, allMessages.Count);

        var userMessages = cut.FindAll(".oa-message.user");
        Assert.Equal(2, userMessages.Count);

        var aiMessages = cut.FindAll(".oa-message.ai");
        Assert.Single(aiMessages); // Use Assert.Single for single-item collections
    }

    [Fact]
    public void ChatMessages_Shows_ProcessingIndicator_WhenProcessing()
    {
        // Arrange & Act
        var cut = RenderComponent<ChatMessages>(parameters => parameters
            .Add(p => p.Messages, new List<ChatMessageModel>())
            .Add(p => p.IsProcessing, true));

        // Assert
        var processingMsg = cut.Find(".oa-message.ai");
        Assert.NotNull(processingMsg);

        var typingIndicator = processingMsg.QuerySelector(".oa-typing-indicator");
        Assert.NotNull(typingIndicator);
        Assert.Equal(3, typingIndicator.QuerySelectorAll("span").Length);
    }

    [Fact]
    public void ChatMessages_DoesNotShow_ProcessingIndicator_WhenNotProcessing()
    {
        // Arrange & Act
        var cut = RenderComponent<ChatMessages>(parameters => parameters
            .Add(p => p.Messages, new List<ChatMessageModel>())
            .Add(p => p.IsProcessing, false));

        // Assert
        var typingIndicators = cut.FindAll(".oa-typing");
        Assert.Empty(typingIndicators);
    }

    [Fact]
    public void ChatMessages_ProcessingIndicator_HasCorrectStructure()
    {
        // Arrange & Act
        var cut = RenderComponent<ChatMessages>(parameters => parameters
            .Add(p => p.Messages, new List<ChatMessageModel>())
            .Add(p => p.IsProcessing, true));

        // Assert
        var processingMsg = cut.Find(".oa-message.ai");
        Assert.NotNull(processingMsg);

        var avatar = processingMsg.QuerySelector(".oa-msg-avatar");
        Assert.NotNull(avatar);

        var content = processingMsg.QuerySelector(".oa-msg-content");
        Assert.NotNull(content);
    }

    [Fact]
    public void ChatMessages_HasCorrectCssClasses()
    {
        // Arrange & Act
        var cut = RenderComponent<ChatMessages>(parameters => parameters
            .Add(p => p.Messages, new List<ChatMessageModel>())
            .Add(p => p.IsProcessing, false));

        // Assert
        var messagesContainer = cut.Find(".oa-messages");
        Assert.NotNull(messagesContainer);
        Assert.Contains("no-scrollbar", messagesContainer.ClassName);
    }

    [Fact]
    public void ChatMessages_RendersFormattedText_AsMarkup()
    {
        // Arrange
        var messages = new List<ChatMessageModel>
        {
            new ChatMessageModel
            {
                IsUser = false,
                Text = "Bold text",
                FormattedText = "<strong>Bold text</strong>",
                Timestamp = DateTime.Now
            }
        };

        // Act
        var cut = RenderComponent<ChatMessages>(parameters => parameters
            .Add(p => p.Messages, messages)
            .Add(p => p.IsProcessing, false));

        // Assert
        var bubble = cut.Find(".oa-msg-text");
        var strong = bubble.QuerySelector("strong");
        Assert.NotNull(strong);
        Assert.Contains("Bold text", strong.TextContent);
    }
}
