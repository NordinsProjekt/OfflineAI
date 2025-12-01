using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using AiDashboard.State;
using DomainsSection = AiDashboard.Components.Pages.Components.DomainsSection;
using CollapsibleSection = AiDashboard.Components.Pages.Components.CollapsibleSection;

namespace Presentation.AiDashboard.Tests.Components;

/// <summary>
/// Tests for DomainsSection component to ensure UI remains consistent across features.
/// </summary>
public class DomainsSectionTests : TestContext
{
    private DashboardState CreateMockDashboardState()
    {
        var state = new DashboardState();
        // Expand the domains section for testing
        if (state.IsSectionCollapsed("domains"))
        {
            state.ToggleSection("domains");
        }
        return state;
    }

    [Fact]
    public void DomainsSection_Renders_WithCorrectStructure()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);
        var navMan = Services.GetRequiredService<NavigationManager>();

        // Act
        var cut = RenderComponent<DomainsSection>();

        // Assert
        var section = cut.FindComponent<CollapsibleSection>();
        Assert.NotNull(section);
    }

    [Fact]
    public void DomainsSection_Renders_CollapsibleSection_WithCorrectTitle()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<DomainsSection>();

        // Assert
        var section = cut.FindComponent<CollapsibleSection>();
        Assert.Equal("domains", section.Instance.SectionKey);
        Assert.Equal("Knowledge Domains", section.Instance.Title);
    }

    [Fact]
    public void DomainsSection_Displays_InfoText()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<DomainsSection>();

        // Assert
        var infoText = cut.Find(".oa-info-text");
        Assert.Contains("Manage knowledge domains", infoText.TextContent);
        Assert.Contains("search keywords", infoText.TextContent);
    }

    [Fact]
    public void DomainsSection_Renders_ManageDomainsButton()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<DomainsSection>();

        // Assert
        var button = cut.Find("button.oa-btn.oa-btn-primary.oa-btn-block");
        Assert.NotNull(button);
        Assert.Contains("Manage Domains", button.TextContent);
    }

    [Fact]
    public void DomainsSection_Displays_Note()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<DomainsSection>();

        // Assert
        var note = cut.Find(".oa-note");
        Assert.Contains("Add domains to filter RAG queries", note.TextContent);
    }

    [Fact]
    public void DomainsSection_NavigatesToDomains_OnButtonClick()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);
        var navMan = Services.GetRequiredService<NavigationManager>();
        var cut = RenderComponent<DomainsSection>();
        var button = cut.Find("button.oa-btn.oa-btn-primary");

        // Act
        button.Click();

        // Assert
        Assert.EndsWith("/domains", navMan.Uri);
    }

    [Fact]
    public void DomainsSection_HasCorrectCssClasses()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<DomainsSection>();

        // Assert
        Assert.NotNull(cut.Find(".oa-info-text"));
        Assert.NotNull(cut.Find("button.oa-btn"));
        Assert.NotNull(cut.Find("button.oa-btn-primary"));
        Assert.NotNull(cut.Find("button.oa-btn-block"));
        Assert.NotNull(cut.Find(".oa-note"));
    }
}
