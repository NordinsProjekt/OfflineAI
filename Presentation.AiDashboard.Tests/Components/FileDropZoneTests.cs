using Bunit;
using Xunit;
using AiDashboard.Components.Shared;

namespace Presentation.AiDashboard.Tests.Components;

/// <summary>
/// Unit tests for FileDropZone component.
/// These tests enforce consistent styling and behavior.
/// </summary>
public class FileDropZoneTests : TestContext
{
    [Fact]
    public void FileDropZone_RendersWithDefaultParameters()
    {
        // Arrange & Act
        var cut = RenderComponent<FileDropZone>();

        // Assert
        Assert.NotNull(cut.Find(".file-dropzone"));
        Assert.NotNull(cut.Find(".dropzone-title"));
        Assert.NotNull(cut.Find(".dropzone-icon"));
    }

    [Fact]
    public void FileDropZone_DarkMode_AppliesCorrectThemeClass()
    {
        // Arrange & Act
        var cut = RenderComponent<FileDropZone>(parameters => parameters
            .Add(p => p.DarkMode, true));

        // Assert
        Assert.Contains("theme-dark", cut.Find(".file-dropzone").ClassName);
        Assert.DoesNotContain("theme-light", cut.Find(".file-dropzone").ClassName);
    }

    [Fact]
    public void FileDropZone_LightMode_AppliesCorrectThemeClass()
    {
        // Arrange & Act
        var cut = RenderComponent<FileDropZone>(parameters => parameters
            .Add(p => p.DarkMode, false));

        // Assert
        Assert.Contains("theme-light", cut.Find(".file-dropzone").ClassName);
        Assert.DoesNotContain("theme-dark", cut.Find(".file-dropzone").ClassName);
    }

    [Fact]
    public void FileDropZone_CustomTitle_DisplaysCorrectly()
    {
        // Arrange
        var customTitle = "Upload Your Document";

        // Act
        var cut = RenderComponent<FileDropZone>(parameters => parameters
            .Add(p => p.Title, customTitle));

        // Assert
        var title = cut.Find(".dropzone-title");
        Assert.Equal(customTitle, title.TextContent);
    }

    [Fact]
    public void FileDropZone_CustomPromptText_DisplaysCorrectly()
    {
        // Arrange
        var customPrompt = "Drag files here or click";

        // Act
        var cut = RenderComponent<FileDropZone>(parameters => parameters
            .Add(p => p.PromptText, customPrompt));

        // Assert
        var prompt = cut.Find(".dropzone-prompt");
        Assert.Equal(customPrompt, prompt.TextContent);
    }

    [Fact]
    public void FileDropZone_CustomIcon_DisplaysCorrectly()
    {
        // Arrange
        var customIcon = "[DOC]";

        // Act
        var cut = RenderComponent<FileDropZone>(parameters => parameters
            .Add(p => p.Icon, customIcon));

        // Assert
        var icon = cut.Find(".dropzone-icon");
        Assert.Equal(customIcon, icon.TextContent);
    }

    [Fact]
    public void FileDropZone_CustomHintText_DisplaysCorrectly()
    {
        // Arrange
        var customHint = "Only PDF files allowed";

        // Act
        var cut = RenderComponent<FileDropZone>(parameters => parameters
            .Add(p => p.HintText, customHint));

        // Assert
        var hint = cut.Find(".dropzone-hint");
        Assert.Equal(customHint, hint.TextContent);
    }

    [Fact]
    public void FileDropZone_NoHintText_DoesNotRenderHint()
    {
        // Arrange & Act
        var cut = RenderComponent<FileDropZone>(parameters => parameters
            .Add(p => p.HintText, ""));

        // Assert
        Assert.Empty(cut.FindAll(".dropzone-hint"));
    }

    [Fact]
    public void FileDropZone_AcceptedTypes_SetsInputAcceptAttribute()
    {
        // Arrange
        var acceptedTypes = ".pdf,.doc";

        // Act
        var cut = RenderComponent<FileDropZone>(parameters => parameters
            .Add(p => p.AcceptedTypes, acceptedTypes));

        // Assert
        var input = cut.Find("input[type='file']");
        Assert.Equal(acceptedTypes, input.GetAttribute("accept"));
    }

    [Fact]
    public void FileDropZone_AllowMultiple_SetsInputMultipleAttribute()
    {
        // Arrange & Act
        var cut = RenderComponent<FileDropZone>(parameters => parameters
            .Add(p => p.AllowMultiple, true));

        // Assert
        var input = cut.Find("input[type='file']");
        Assert.NotNull(input.GetAttribute("multiple"));
    }

    [Fact]
    public void FileDropZone_SingleFileMode_DoesNotSetMultipleAttribute()
    {
        // Arrange & Act
        var cut = RenderComponent<FileDropZone>(parameters => parameters
            .Add(p => p.AllowMultiple, false));

        // Assert
        var input = cut.Find("input[type='file']");
        Assert.Null(input.GetAttribute("multiple"));
    }

    [Fact]
    public void FileDropZone_HasRequiredCssClasses()
    {
        // Arrange & Act
        var cut = RenderComponent<FileDropZone>();

        // Assert - Verify critical CSS classes exist
        Assert.NotNull(cut.Find(".file-dropzone"));
        Assert.NotNull(cut.Find(".dropzone-content"));
        Assert.NotNull(cut.Find(".dropzone-icon"));
        Assert.NotNull(cut.Find(".dropzone-title"));
        Assert.NotNull(cut.Find(".dropzone-prompt"));
        Assert.NotNull(cut.Find(".dropzone-input"));
        Assert.NotNull(cut.Find(".dropzone-browse-btn"));
    }

    [Fact]
    public void FileDropZone_BrowseButton_IsClickable()
    {
        // Arrange
        var cut = RenderComponent<FileDropZone>();

        // Act
        var browseButton = cut.Find(".dropzone-browse-btn");

        // Assert
        Assert.NotNull(browseButton);
        Assert.Equal("Browse Files", browseButton.TextContent);
        Assert.Equal("label", browseButton.TagName.ToLower());
    }

    [Fact]
    public void FileDropZone_InputHasUniqueId()
    {
        // Arrange & Act
        var cut1 = RenderComponent<FileDropZone>();
        var cut2 = RenderComponent<FileDropZone>();

        // Assert
        var input1 = cut1.Find("input[type='file']");
        var input2 = cut2.Find("input[type='file']");
        
        var id1 = input1.GetAttribute("id");
        var id2 = input2.GetAttribute("id");
        
        Assert.NotNull(id1);
        Assert.NotNull(id2);
        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void FileDropZone_LabelForAttribute_MatchesInputId()
    {
        // Arrange & Act
        var cut = RenderComponent<FileDropZone>();

        // Assert
        var input = cut.Find("input[type='file']");
        var label = cut.Find("label.dropzone-browse-btn");
        
        var inputId = input.GetAttribute("id");
        var labelFor = label.GetAttribute("for");
        
        Assert.Equal(inputId, labelFor);
    }

    [Fact]
    public void FileDropZone_NoError_ErrorMessageNotDisplayed()
    {
        // Arrange & Act
        var cut = RenderComponent<FileDropZone>();

        // Assert
        Assert.Empty(cut.FindAll(".dropzone-error"));
    }

    [Theory]
    [InlineData(true, "theme-dark")]
    [InlineData(false, "theme-light")]
    public void FileDropZone_ThemeClass_MatchesParameter(bool darkMode, string expectedClass)
    {
        // Arrange & Act
        var cut = RenderComponent<FileDropZone>(parameters => parameters
            .Add(p => p.DarkMode, darkMode));

        // Assert
        Assert.Contains(expectedClass, cut.Find(".file-dropzone").ClassName);
    }

    [Fact]
    public void FileDropZone_Structure_MatchesExpectedHierarchy()
    {
        // Arrange & Act
        var cut = RenderComponent<FileDropZone>();

        // Assert - Verify DOM structure
        var dropzone = cut.Find(".file-dropzone");
        var content = dropzone.QuerySelector(".dropzone-content");
        
        Assert.NotNull(content);
        Assert.NotNull(content.QuerySelector(".dropzone-icon"));
        Assert.NotNull(content.QuerySelector(".dropzone-title"));
        Assert.NotNull(content.QuerySelector(".dropzone-prompt"));
        Assert.NotNull(content.QuerySelector(".dropzone-input"));
        Assert.NotNull(content.QuerySelector(".dropzone-browse-btn"));
    }

    [Fact]
    public void FileDropZone_FileInput_IsHidden()
    {
        // Arrange & Act
        var cut = RenderComponent<FileDropZone>();

        // Assert
        var input = cut.Find("input[type='file']");
        Assert.Contains("dropzone-input", input.ClassName);
        // The CSS class .dropzone-input should have display: none
    }
}
