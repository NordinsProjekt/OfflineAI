using AiDashboard.State;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using ModelControlsSection = AiDashboard.Components.Pages.Components.ModelControlsSection;
using CollapsibleSection = AiDashboard.Components.Pages.Components.CollapsibleSection;

namespace Presentation.AiDashboard.Tests.Components;

/// <summary>
/// Tests for ModelControlsSection component to ensure UI remains consistent across features.
/// </summary>
public class ModelControlsSectionTests : TestContext
{
    private DashboardState CreateMockDashboardState()
    {
        var state = new DashboardState();
        // The "model" section is expanded by default, but let's ensure it
        if (state.IsSectionCollapsed("model"))
        {
            state.ToggleSection("model");
        }
        return state;
    }

    [Fact]
    public void ModelControlsSection_Renders_WithCorrectStructure()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<ModelControlsSection>();

        // Assert
        var section = cut.FindComponent<CollapsibleSection>();
        Assert.NotNull(section);
    }

    [Fact]
    public void ModelControlsSection_Renders_CollapsibleSection_WithCorrectTitle()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<ModelControlsSection>();

        // Assert
        var section = cut.FindComponent<CollapsibleSection>();
        Assert.Equal("model", section.Instance.SectionKey);
        Assert.Equal("Model", section.Instance.Title);
    }

    [Fact]
    public void ModelControlsSection_Renders_ModelSelectField()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<ModelControlsSection>();

        // Assert
        var label = cut.Find("label");
        Assert.Equal("Current Model", label.TextContent);
        
        var select = cut.Find("select.oa-text");
        Assert.NotNull(select);
    }

    [Fact]
    public void ModelControlsSection_Displays_AvailableModels()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<ModelControlsSection>();

        // Assert
        var options = cut.FindAll("option");
        Assert.NotEmpty(options);
        Assert.All(options, opt => Assert.NotNull(opt.TextContent));
    }

    [Fact]
    public void ModelControlsSection_Displays_NoteAboutAutoSwitch()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<ModelControlsSection>();

        // Assert
        var note = cut.Find(".oa-note");
        Assert.Contains("Model switches automatically", note.TextContent);
    }

    [Fact]
    public void ModelControlsSection_SelectsCurrentModel()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        var currentModel = dashboardState.ModelService.CurrentModel;
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<ModelControlsSection>();

        // Assert
        var select = cut.Find("select.oa-text");
        Assert.Equal(currentModel, select.GetAttribute("value"));
    }

    [Fact]
    public void ModelControlsSection_ChangesModel_OnSelectChange()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        var cut = RenderComponent<ModelControlsSection>();
        var select = cut.Find("select.oa-text");
        var options = cut.FindAll("option");
        
        // Find a different model to switch to
        var differentModel = options
            .Select(o => o.TextContent)
            .FirstOrDefault(m => m != dashboardState.ModelService.CurrentModel);

        if (differentModel != null)
        {
            // Act
            select.Change(differentModel);

            // Assert
            Assert.Equal(differentModel, dashboardState.ModelService.CurrentModel);
        }
        else
        {
            // If there's only one model, just verify the structure is correct
            Assert.Single(options);
        }
    }

    [Fact]
    public void ModelControlsSection_HasCorrectCssClasses()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<ModelControlsSection>();

        // Assert
        Assert.NotNull(cut.Find(".oa-field"));
        Assert.NotNull(cut.Find("select.oa-text"));
        Assert.NotNull(cut.Find(".oa-note"));
    }

    [Fact]
    public void ModelControlsSection_HasOneField()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<ModelControlsSection>();

        // Assert
        var fields = cut.FindAll(".oa-field");
        Assert.Single(fields);
    }
}
