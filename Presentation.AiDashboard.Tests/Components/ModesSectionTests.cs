using AiDashboard.State;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using ModesSection = AiDashboard.Components.Pages.Components.ModesSection;
using CollapsibleSection = AiDashboard.Components.Pages.Components.CollapsibleSection;

namespace Presentation.AiDashboard.Tests.Components;

/// <summary>
/// Tests for ModesSection component to ensure UI remains consistent across features.
/// </summary>
public class ModesSectionTests : TestContext
{
    private DashboardState CreateMockDashboardState()
    {
        var state = new DashboardState();
        // Expand the modes section for testing
        if (state.IsSectionCollapsed("modes"))
        {
            state.ToggleSection("modes");
        }
        return state;
    }

    [Fact]
    public void ModesSection_Renders_WithCorrectStructure()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<ModesSection>();

        // Assert
        var section = cut.FindComponent<CollapsibleSection>();
        Assert.NotNull(section);
    }

    [Fact]
    public void ModesSection_Renders_AllSwitches()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<ModesSection>();

        // Assert
        var switches = cut.FindAll(".oa-switch-row");
        Assert.Equal(4, switches.Count);
        
        var checkboxes = cut.FindAll("input[type='checkbox']");
        Assert.Equal(4, checkboxes.Count);
    }

    [Fact]
    public void ModesSection_RagMode_TogglesCorrectly()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        dashboardState.SettingsService.RagMode = false;
        Services.AddSingleton(dashboardState);

        var cut = RenderComponent<ModesSection>();
        var switches = cut.FindAll("input[type='checkbox']");
        var ragSwitch = switches[0]; // First switch is RAG Mode

        // Act
        ragSwitch.Change(true);

        // Assert
        Assert.True(dashboardState.SettingsService.RagMode);
    }

    [Fact]
    public void ModesSection_PerformanceMetrics_TogglesCorrectly()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        dashboardState.SettingsService.PerformanceMetrics = false;
        Services.AddSingleton(dashboardState);

        var cut = RenderComponent<ModesSection>();
        var switches = cut.FindAll("input[type='checkbox']");
        var perfSwitch = switches[1]; // Second switch is Performance Metrics

        // Act
        perfSwitch.Change(true);

        // Assert
        Assert.True(dashboardState.SettingsService.PerformanceMetrics);
    }

    [Fact]
    public void ModesSection_DebugMode_TogglesCorrectly()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        dashboardState.SettingsService.DebugMode = false;
        Services.AddSingleton(dashboardState);

        var cut = RenderComponent<ModesSection>();
        var switches = cut.FindAll("input[type='checkbox']");
        var debugSwitch = switches[2]; // Third switch is Debug Mode

        // Act
        debugSwitch.Change(true);

        // Assert
        Assert.True(dashboardState.SettingsService.DebugMode);
    }

    [Fact]
    public void ModesSection_UseGpu_TogglesCorrectly()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        dashboardState.SettingsService.UseGpu = false;
        Services.AddSingleton(dashboardState);

        var cut = RenderComponent<ModesSection>();
        var switches = cut.FindAll("input[type='checkbox']");
        var gpuSwitch = switches[3]; // Fourth switch is Use GPU

        // Act
        gpuSwitch.Change(true);

        // Assert
        Assert.True(dashboardState.SettingsService.UseGpu);
    }

    [Fact]
    public void ModesSection_InitialState_ReflectsSettings()
    {
        // Arrange - Set specific values to test
        var dashboardState = CreateMockDashboardState();
        dashboardState.SettingsService.RagMode = false;
        dashboardState.SettingsService.PerformanceMetrics = true;
        dashboardState.SettingsService.DebugMode = true;
        dashboardState.SettingsService.UseGpu = false;
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<ModesSection>();

        // Assert
        var switches = cut.FindAll("input[type='checkbox']");
        Assert.False(switches[0].HasAttribute("checked")); // RAG Mode OFF
        Assert.True(switches[1].HasAttribute("checked"));  // Performance Metrics ON
        Assert.True(switches[2].HasAttribute("checked"));  // Debug Mode ON
        Assert.False(switches[3].HasAttribute("checked")); // Use GPU OFF
    }

    [Fact]
    public void ModesSection_DefaultState_ReflectsDefaultSettings()
    {
        // Arrange - Use default values (RAG ON, GPU ON, others OFF)
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<ModesSection>();

        // Assert - Verify defaults
        var switches = cut.FindAll("input[type='checkbox']");
        Assert.True(switches[0].HasAttribute("checked"));  // RAG Mode ON (default)
        Assert.False(switches[1].HasAttribute("checked")); // Performance Metrics OFF (default)
        Assert.False(switches[2].HasAttribute("checked")); // Debug Mode OFF (default)
        Assert.True(switches[3].HasAttribute("checked"));  // Use GPU ON (default)
    }

    [Fact]
    public void ModesSection_HasCorrectLabels()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<ModesSection>();

        // Assert
        var labels = cut.FindAll(".oa-switch-row");
        Assert.Contains("RAG Mode", labels[0].TextContent);
        Assert.Contains("Performance Metrics", labels[1].TextContent);
        Assert.Contains("Debug Mode", labels[2].TextContent);
        Assert.Contains("Use GPU (34 layers)", labels[3].TextContent);
    }

    [Fact]
    public void ModesSection_HasCorrectCssClasses()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<ModesSection>();

        // Assert
        Assert.Equal(4, cut.FindAll(".oa-switch-row").Count);
        Assert.Equal(4, cut.FindAll(".oa-switch").Count);
    }
}
