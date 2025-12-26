using Bunit;
using Xunit;
using AiDashboard.Components.Shared;

namespace Presentation.AiDashboard.Tests.Components;

/// <summary>
/// Browser-based UI tests for FileDropZone component.
/// These tests verify visual styling and user interactions.
/// </summary>
public class FileDropZoneBunitTests : TestContext
{
    [Fact]
    public void FileDropZone_DragOver_AddsCorrectClass()
    {
        // Arrange
        var cut = RenderComponent<FileDropZone>();
        var dropzone = cut.Find(".file-dropzone");

        // Act
        dropzone.DragEnter();

        // Assert
        Assert.Contains("drag-over", cut.Find(".file-dropzone").ClassName);
    }

    [Fact]
    public void FileDropZone_DragLeave_RemovesClass()
    {
        // Arrange
        var cut = RenderComponent<FileDropZone>();
        var dropzone = cut.Find(".file-dropzone");
        
        // Act
        dropzone.DragEnter();
        Assert.Contains("drag-over", cut.Find(".file-dropzone").ClassName);
        
        dropzone.DragLeave();

        // Assert
        Assert.DoesNotContain("drag-over", cut.Find(".file-dropzone").ClassName);
    }

    [Fact]
    public void FileDropZone_Drop_RemovesDragOverClass()
    {
        // Arrange
        var cut = RenderComponent<FileDropZone>();
        var dropzone = cut.Find(".file-dropzone");

        // Act
        dropzone.DragEnter();
        Assert.Contains("drag-over", cut.Find(".file-dropzone").ClassName);
        
        dropzone.Drop();

        // Assert
        Assert.DoesNotContain("drag-over", cut.Find(".file-dropzone").ClassName);
    }

    [Fact]
    public void FileDropZone_DarkMode_HasDarkThemeClass()
    {
        // Arrange & Act
        var cut = RenderComponent<FileDropZone>(parameters => parameters
            .Add(p => p.DarkMode, true));

        // Assert
        var dropzone = cut.Find(".file-dropzone");
        Assert.Contains("theme-dark", dropzone.ClassName);
    }

    [Fact]
    public void FileDropZone_LightMode_HasLightThemeClass()
    {
        // Arrange & Act
        var cut = RenderComponent<FileDropZone>(parameters => parameters
            .Add(p => p.DarkMode, false));

        // Assert
        var dropzone = cut.Find(".file-dropzone");
        Assert.Contains("theme-light", dropzone.ClassName);
    }

    [Fact]
    public void FileDropZone_CustomParameters_AllRenderCorrectly()
    {
        // Arrange
        var cut = RenderComponent<FileDropZone>(parameters => parameters
            .Add(p => p.Title, "Custom Title")
            .Add(p => p.PromptText, "Custom Prompt")
            .Add(p => p.Icon, "[CUSTOM]")
            .Add(p => p.HintText, "Custom Hint")
            .Add(p => p.DarkMode, false));

        // Assert
        Assert.Equal("Custom Title", cut.Find(".dropzone-title").TextContent);
        Assert.Equal("Custom Prompt", cut.Find(".dropzone-prompt").TextContent);
        Assert.Equal("[CUSTOM]", cut.Find(".dropzone-icon").TextContent);
        Assert.Equal("Custom Hint", cut.Find(".dropzone-hint").TextContent);
        Assert.Contains("theme-light", cut.Find(".file-dropzone").ClassName);
    }

    [Fact]
    public void FileDropZone_BrowseButton_RendersWithCorrectText()
    {
        // Arrange & Act
        var cut = RenderComponent<FileDropZone>();

        // Assert
        var button = cut.Find(".dropzone-browse-btn");
        Assert.Equal("Browse Files", button.TextContent);
    }

    [Fact]
    public void FileDropZone_InputElement_HasCorrectType()
    {
        // Arrange & Act
        var cut = RenderComponent<FileDropZone>();

        // Assert
        var input = cut.Find("input.dropzone-input");
        Assert.Equal("file", input.GetAttribute("type"));
    }

    [Fact]
    public void FileDropZone_MultipleInstances_HaveUniqueIds()
    {
        // Arrange & Act
        var cut1 = RenderComponent<FileDropZone>();
        var cut2 = RenderComponent<FileDropZone>();
        var cut3 = RenderComponent<FileDropZone>();

        // Assert
        var id1 = cut1.Find("input[type='file']").GetAttribute("id");
        var id2 = cut2.Find("input[type='file']").GetAttribute("id");
        var id3 = cut3.Find("input[type='file']").GetAttribute("id");

        Assert.NotEqual(id1, id2);
        Assert.NotEqual(id2, id3);
        Assert.NotEqual(id1, id3);
    }

    [Theory]
    [InlineData(".pdf", ".pdf")]
    [InlineData(".txt,.docx", ".txt,.docx")]
    [InlineData(".jpg,.png,.gif", ".jpg,.png,.gif")]
    public void FileDropZone_AcceptedTypes_SetsCorrectAttribute(string acceptedTypes, string expected)
    {
        // Arrange & Act
        var cut = RenderComponent<FileDropZone>(parameters => parameters
            .Add(p => p.AcceptedTypes, acceptedTypes));

        // Assert
        var input = cut.Find("input[type='file']");
        Assert.Equal(expected, input.GetAttribute("accept"));
    }

    [Fact]
    public void FileDropZone_InitialState_NoErrorDisplayed()
    {
        // Arrange & Act
        var cut = RenderComponent<FileDropZone>();

        // Assert
        var errors = cut.FindAll(".dropzone-error");
        Assert.Empty(errors);
    }

    [Fact]
    public void FileDropZone_RendersAllRequiredElements()
    {
        // Arrange & Act
        var cut = RenderComponent<FileDropZone>();

        // Assert - All critical elements must exist
        Assert.NotNull(cut.Find(".file-dropzone"));
        Assert.NotNull(cut.Find(".dropzone-content"));
        Assert.NotNull(cut.Find(".dropzone-icon"));
        Assert.NotNull(cut.Find(".dropzone-title"));
        Assert.NotNull(cut.Find(".dropzone-prompt"));
        Assert.NotNull(cut.Find(".dropzone-input"));
        Assert.NotNull(cut.Find(".dropzone-browse-btn"));
        Assert.NotNull(cut.Find(".dropzone-hint"));
    }
}
