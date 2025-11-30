using AiDashboard.State;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Sidebar = AiDashboard.Components.Pages.Components.Sidebar;
using ModesSection = AiDashboard.Components.Pages.Components.ModesSection;
using GenerationSettingsSection = AiDashboard.Components.Pages.Components.GenerationSettingsSection;

namespace Presentation.AiDashboard.Tests.Components;

/// <summary>
/// Tests for Sidebar component to ensure UI remains consistent across features.
/// </summary>
public class SidebarTests : TestContext
{
    private DashboardState CreateMockDashboardState()
    {
        var state = new DashboardState();
        return state;
    }

    [Fact]
    public void Sidebar_Renders_WithCorrectStructure()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<Sidebar>();

        // Assert
        Assert.NotNull(cut.Find(".oa-sidebar"));
        Assert.NotNull(cut.Find(".oa-sidebar-header"));
        Assert.NotNull(cut.Find(".oa-app-title"));
        Assert.NotNull(cut.Find(".oa-subtitle"));
    }

    [Fact]
    public void Sidebar_DisplaysAppTitle()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<Sidebar>();

        // Assert
        var appTitle = cut.Find(".oa-app-title");
        Assert.Equal("OfflineAI", appTitle.TextContent);
    }

    [Fact]
    public void Sidebar_DisplaysSubtitle()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<Sidebar>();

        // Assert
        var subtitle = cut.Find(".oa-subtitle");
        Assert.Equal("Control Panel", subtitle.TextContent);
    }

    [Fact]
    public void Sidebar_HasCollapseButton()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<Sidebar>();

        // Assert
        var button = cut.Find(".oa-icon-btn");
        Assert.NotNull(button);
        Assert.Equal("button", button.GetAttribute("type"));
        Assert.Equal("Collapse", button.GetAttribute("title"));
        Assert.Equal("Collapse sidebar", button.GetAttribute("aria-label"));
    }

    [Fact]
    public void Sidebar_CollapseButton_CallsToggleSidebar()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        dashboardState.Collapsed = false;
        Services.AddSingleton(dashboardState);

        var cut = RenderComponent<Sidebar>();
        var button = cut.Find(".oa-icon-btn");

        // Act
        button.Click();

        // Assert
        Assert.True(dashboardState.Collapsed);
    }

    [Fact]
    public void Sidebar_RendersAllSections()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<Sidebar>();

        // Assert
        Assert.NotNull(cut.FindComponent<ModesSection>());
        Assert.NotNull(cut.FindComponent<GenerationSettingsSection>());
    }

    [Fact]
    public void Sidebar_DisplaysFooterNote()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<Sidebar>();

        // Assert
        var footer = cut.Find(".oa-footer-note");
        // The footer contains a bullet "•" and text "Connected to LLM backend" with whitespace
        Assert.Contains("Connected to LLM backend", footer.TextContent);
    }

    [Fact]
    public void Sidebar_HasCorrectCssClasses()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<Sidebar>();

        // Assert
        var aside = cut.Find("aside");
        Assert.Contains("oa-sidebar", aside.ClassName);
    }
}
