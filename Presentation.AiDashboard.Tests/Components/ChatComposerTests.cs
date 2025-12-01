using Bunit;
using Microsoft.AspNetCore.Components.Web;
using ChatComposer = AiDashboard.Components.Pages.Components.ChatComposer;

namespace Presentation.AiDashboard.Tests.Components;

/// <summary>
/// Tests for ChatComposer component to ensure UI remains consistent across features.
/// </summary>
public class ChatComposerTests : TestContext
{
    [Fact]
    public void ChatComposer_Renders_WithCorrectStructure()
    {
        // Arrange & Act
        var cut = RenderComponent<ChatComposer>(parameters => parameters
            .Add(p => p.ComposerText, string.Empty)
            .Add(p => p.IsProcessing, false));

        // Assert
        var composer = cut.Find(".oa-composer");
        Assert.NotNull(composer);
        
        var textarea = cut.Find(".oa-composer-input");
        Assert.NotNull(textarea);
        Assert.Equal("textarea", textarea.TagName.ToLower());
        
        var button = cut.Find(".oa-send-btn");
        Assert.NotNull(button);
        Assert.Equal("button", button.TagName.ToLower());
    }

    [Fact]
    public void ChatComposer_Textarea_HasCorrectAttributes()
    {
        // Arrange & Act
        var cut = RenderComponent<ChatComposer>(parameters => parameters
            .Add(p => p.ComposerText, string.Empty)
            .Add(p => p.IsProcessing, false));

        // Assert
        var textarea = cut.Find(".oa-composer-input");
        Assert.Equal("2", textarea.GetAttribute("rows"));
        Assert.Equal("Type your message...", textarea.GetAttribute("placeholder"));
        Assert.Null(textarea.GetAttribute("disabled"));
    }

    [Fact]
    public void ChatComposer_Button_HasCorrectAttributes()
    {
        // Arrange & Act
        var cut = RenderComponent<ChatComposer>(parameters => parameters
            .Add(p => p.ComposerText, string.Empty)
            .Add(p => p.IsProcessing, false));

        // Assert
        var button = cut.Find(".oa-send-btn");
        Assert.Equal("button", button.GetAttribute("type"));
        Assert.Equal("Send message", button.GetAttribute("title"));
        Assert.Equal("Send message", button.GetAttribute("aria-label"));
        Assert.Null(button.GetAttribute("disabled"));
    }

    [Fact]
    public void ChatComposer_DisplaysComposerText()
    {
        // Arrange
        var testText = "Hello, this is a test message";

        // Act
        var cut = RenderComponent<ChatComposer>(parameters => parameters
            .Add(p => p.ComposerText, testText)
            .Add(p => p.IsProcessing, false));

        // Assert
        var textarea = cut.Find(".oa-composer-input");
        Assert.Equal(testText, textarea.GetAttribute("value"));
    }

    [Fact]
    public void ChatComposer_DisablesControls_WhenProcessing()
    {
        // Arrange & Act
        var cut = RenderComponent<ChatComposer>(parameters => parameters
            .Add(p => p.ComposerText, string.Empty)
            .Add(p => p.IsProcessing, true));

        // Assert
        var textarea = cut.Find(".oa-composer-input");
        Assert.NotNull(textarea.GetAttribute("disabled"));
        
        var button = cut.Find(".oa-send-btn");
        Assert.NotNull(button.GetAttribute("disabled"));
    }

    [Fact]
    public void ChatComposer_EnablesControls_WhenNotProcessing()
    {
        // Arrange & Act
        var cut = RenderComponent<ChatComposer>(parameters => parameters
            .Add(p => p.ComposerText, string.Empty)
            .Add(p => p.IsProcessing, false));

        // Assert
        var textarea = cut.Find(".oa-composer-input");
        Assert.Null(textarea.GetAttribute("disabled"));
        
        var button = cut.Find(".oa-send-btn");
        Assert.Null(button.GetAttribute("disabled"));
    }

    [Fact]
    public void ChatComposer_InvokesCallback_OnTextInput()
    {
        // Arrange
        string? capturedText = null;
        var cut = RenderComponent<ChatComposer>(parameters => parameters
            .Add(p => p.ComposerText, string.Empty)
            .Add(p => p.IsProcessing, false)
            .Add(p => p.OnComposerTextChanged, text => capturedText = text));

        var textarea = cut.Find(".oa-composer-input");

        // Act
        textarea.Input("Test input");

        // Assert
        Assert.Equal("Test input", capturedText);
    }

    [Fact]
    public void ChatComposer_InvokesCallback_OnSendButtonClick()
    {
        // Arrange
        var callbackInvoked = false;
        var cut = RenderComponent<ChatComposer>(parameters => parameters
            .Add(p => p.ComposerText, string.Empty)
            .Add(p => p.IsProcessing, false)
            .Add(p => p.OnSendMessage, () => callbackInvoked = true));

        var button = cut.Find(".oa-send-btn");

        // Act
        button.Click();

        // Assert
        Assert.True(callbackInvoked);
    }

    [Fact]
    public void ChatComposer_InvokesCallback_OnKeyDown()
    {
        // Arrange
        KeyboardEventArgs? capturedEvent = null;
        var cut = RenderComponent<ChatComposer>(parameters => parameters
            .Add(p => p.ComposerText, string.Empty)
            .Add(p => p.IsProcessing, false)
            .Add(p => p.OnKeyDown, args => capturedEvent = args));

        var textarea = cut.Find(".oa-composer-input");

        // Act
        textarea.KeyDown("Enter");

        // Assert
        Assert.NotNull(capturedEvent);
        Assert.Equal("Enter", capturedEvent.Key);
    }

    [Fact]
    public void ChatComposer_HasCorrectCssClasses()
    {
        // Arrange & Act
        var cut = RenderComponent<ChatComposer>(parameters => parameters
            .Add(p => p.ComposerText, string.Empty)
            .Add(p => p.IsProcessing, false));

        // Assert
        Assert.NotNull(cut.Find(".oa-composer"));
        Assert.NotNull(cut.Find(".oa-composer-input"));
        Assert.NotNull(cut.Find(".oa-send-btn"));
    }

    [Fact]
    public void ChatComposer_Button_IsDisabled_WhenProcessing()
    {
        // Arrange
        var cut = RenderComponent<ChatComposer>(parameters => parameters
            .Add(p => p.ComposerText, string.Empty)
            .Add(p => p.IsProcessing, true));

        // Assert - Just verify the button has the disabled attribute
        // bUnit's click() will still fire events on disabled buttons,
        // but in real browsers, disabled buttons don't fire click events
        var button = cut.Find(".oa-send-btn");
        Assert.NotNull(button.GetAttribute("disabled"));
    }
}
