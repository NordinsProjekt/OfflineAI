using AiDashboard.State;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using FilesSection = AiDashboard.Components.Pages.Components.FilesSection;
using CollapsibleSection = AiDashboard.Components.Pages.Components.CollapsibleSection;

namespace Presentation.AiDashboard.Tests.Components;

/// <summary>
/// Tests for FilesSection component to ensure UI remains consistent across features.
/// </summary>
public class FilesSectionTests : TestContext
{
    private DashboardState CreateMockDashboardState()
    {
        var state = new DashboardState();
        // Expand the files section for testing
        if (state.IsSectionCollapsed("files"))
        {
            state.ToggleSection("files");
        }
        return state;
    }

    [Fact]
    public void FilesSection_Renders_WithCorrectStructure()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<FilesSection>();

        // Assert
        var section = cut.FindComponent<CollapsibleSection>();
        Assert.NotNull(section);
    }

    [Fact]
    public void FilesSection_Renders_CollapsibleSection_WithCorrectTitle()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<FilesSection>();

        // Assert
        var section = cut.FindComponent<CollapsibleSection>();
        Assert.Equal("files", section.Instance.SectionKey);
        Assert.Equal("Files", section.Instance.Title);
    }

    [Fact]
    public void FilesSection_Displays_InboxField()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<FilesSection>();

        // Assert
        var labels = cut.FindAll("label");
        Assert.Contains(labels, l => l.TextContent == "Inbox");
        
        var inputs = cut.FindAll("input[type='text'].oa-text[readonly]");
        Assert.NotEmpty(inputs);
    }

    [Fact]
    public void FilesSection_Shows_NotConfigured_WhenInboxServiceNull()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<FilesSection>();

        // Assert
        var inputs = cut.FindAll("input[type='text'].oa-text[readonly]");
        var inboxInput = inputs.FirstOrDefault(i => i.GetAttribute("value") == "Not configured");
        Assert.NotNull(inboxInput);
    }

    [Fact]
    public void FilesSection_Displays_StatusField()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<FilesSection>();

        // Assert
        var labels = cut.FindAll("label");
        Assert.Contains(labels, l => l.TextContent == "Status");
        
        var inputs = cut.FindAll("input[type='text'].oa-text[readonly]");
        var statusInput = inputs.FirstOrDefault(i => i.GetAttribute("value") == "Ready");
        Assert.NotNull(statusInput);
    }

    [Fact]
    public void FilesSection_Renders_ReloadInboxButton()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<FilesSection>();

        // Assert
        var buttons = cut.FindAll("button.oa-btn.oa-btn-secondary.oa-btn-block");
        Assert.Contains(buttons, b => b.TextContent.Contains("Reload Inbox"));
    }

    [Fact]
    public void FilesSection_Renders_PdfToTxtButton()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<FilesSection>();

        // Assert
        var buttons = cut.FindAll("button.oa-btn.oa-btn-secondary.oa-btn-block");
        Assert.Contains(buttons, b => b.TextContent.Contains("PDF to TXT"));
    }

    [Fact]
    public void FilesSection_Buttons_NotDisabled_WhenNotProcessing()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<FilesSection>();

        // Assert
        var buttons = cut.FindAll("button.oa-btn.oa-btn-secondary.oa-btn-block");
        foreach (var button in buttons)
        {
            Assert.Null(button.GetAttribute("disabled"));
        }
    }

    [Fact]
    public void FilesSection_HasCorrectCssClasses()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<FilesSection>();

        // Assert
        Assert.NotEmpty(cut.FindAll(".oa-field"));
        Assert.NotEmpty(cut.FindAll("input.oa-text"));
        Assert.NotEmpty(cut.FindAll("button.oa-btn"));
        Assert.NotEmpty(cut.FindAll("button.oa-btn-secondary"));
        Assert.NotEmpty(cut.FindAll("button.oa-btn-block"));
    }

    [Fact]
    public void FilesSection_HasTwoButtons()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<FilesSection>();

        // Assert
        var buttons = cut.FindAll("button.oa-btn.oa-btn-secondary.oa-btn-block");
        Assert.Equal(2, buttons.Count);
    }

    [Fact]
    public void FilesSection_HasTwoFields()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<FilesSection>();

        // Assert
        var fields = cut.FindAll(".oa-field");
        Assert.Equal(2, fields.Count);
    }
}
