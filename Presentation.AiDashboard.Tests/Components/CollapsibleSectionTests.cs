using AiDashboard.State;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using CollapsibleSection = AiDashboard.Components.Pages.Components.CollapsibleSection;

namespace Presentation.AiDashboard.Tests.Components;

/// <summary>
/// Tests for CollapsibleSection component to ensure UI remains consistent across features.
/// </summary>
public class CollapsibleSectionTests : TestContext
{
    private DashboardState CreateMockDashboardState()
    {
        var state = new DashboardState();
        return state;
    }

    [Fact]
    public void CollapsibleSection_Renders_WithTitle()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<CollapsibleSection>(parameters => parameters
            .Add(p => p.SectionKey, "test-section")
            .Add(p => p.Title, "Test Section"));

        // Assert
        var title = cut.Find(".oa-card-title");
        Assert.Contains("Test Section", title.TextContent);
    }

    [Fact]
    public void CollapsibleSection_Renders_WithCorrectStructure()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<CollapsibleSection>(parameters => parameters
            .Add(p => p.SectionKey, "test-section")
            .Add(p => p.Title, "Test Section"));

        // Assert
        Assert.NotNull(cut.Find(".oa-card"));
        Assert.NotNull(cut.Find(".oa-card-title"));
        Assert.NotNull(cut.Find(".oa-collapse-btn"));
    }

    [Fact]
    public void CollapsibleSection_Shows_CollapseButton()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<CollapsibleSection>(parameters => parameters
            .Add(p => p.SectionKey, "test-section")
            .Add(p => p.Title, "Test Section"));

        // Assert
        var button = cut.Find(".oa-collapse-btn");
        Assert.NotNull(button);
        Assert.Equal("button", button.GetAttribute("type"));
    }

    [Fact]
    public void CollapsibleSection_ShowsContent_WhenExpanded()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<CollapsibleSection>(parameters => parameters
            .Add(p => p.SectionKey, "model") // model section is expanded by default
            .Add(p => p.Title, "Test Section")
            .AddChildContent("<div class='test-content'>Content</div>"));

        // Assert
        var content = cut.Find(".oa-card-content");
        Assert.NotNull(content);
        Assert.Contains("Content", content.InnerHtml);
    }

    [Fact]
    public void CollapsibleSection_HidesContent_WhenCollapsed()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<CollapsibleSection>(parameters => parameters
            .Add(p => p.SectionKey, "modes") // modes section is collapsed by default
            .Add(p => p.Title, "Test Section")
            .AddChildContent("<div class='test-content'>Content</div>"));

        // Assert
        var contents = cut.FindAll(".oa-card-content");
        Assert.Empty(contents);
    }

    [Fact]
    public void CollapsibleSection_TogglesState_OnHeaderClick()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        var cut = RenderComponent<CollapsibleSection>(parameters => parameters
            .Add(p => p.SectionKey, "model") // expanded by default
            .Add(p => p.Title, "Test Section")
            .AddChildContent("<div class='test-content'>Content</div>"));

        var header = cut.Find(".oa-collapsible-header");
        
        // Verify initial state (expanded)
        Assert.NotEmpty(cut.FindAll(".oa-card-content"));

        // Act
        header.Click();

        // Assert - should be collapsed
        Assert.Empty(cut.FindAll(".oa-card-content"));
    }

    [Fact]
    public void CollapsibleSection_ShowsCorrectIcon_WhenExpanded()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<CollapsibleSection>(parameters => parameters
            .Add(p => p.SectionKey, "model") // expanded by default
            .Add(p => p.Title, "Test Section"));

        // Assert
        var button = cut.Find(".oa-collapse-btn");
        Assert.Contains("<", button.InnerHtml);
    }

    [Fact]
    public void CollapsibleSection_ShowsCorrectIcon_WhenCollapsed()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<CollapsibleSection>(parameters => parameters
            .Add(p => p.SectionKey, "modes") // collapsed by default
            .Add(p => p.Title, "Test Section"));

        // Assert
        var button = cut.Find(".oa-collapse-btn");
        Assert.Contains(">", button.InnerHtml);
    }

    [Fact]
    public void CollapsibleSection_HasCorrectAriaLabel_WhenExpanded()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<CollapsibleSection>(parameters => parameters
            .Add(p => p.SectionKey, "model") // expanded by default
            .Add(p => p.Title, "Test Section"));

        // Assert
        var button = cut.Find(".oa-collapse-btn");
        Assert.Equal("Collapse", button.GetAttribute("aria-label"));
    }

    [Fact]
    public void CollapsibleSection_HasCorrectAriaLabel_WhenCollapsed()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<CollapsibleSection>(parameters => parameters
            .Add(p => p.SectionKey, "modes") // collapsed by default
            .Add(p => p.Title, "Test Section"));

        // Assert
        var button = cut.Find(".oa-collapse-btn");
        Assert.Equal("Expand", button.GetAttribute("aria-label"));
    }

    [Fact]
    public void CollapsibleSection_AddsCollapsedClass_WhenCollapsed()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<CollapsibleSection>(parameters => parameters
            .Add(p => p.SectionKey, "modes") // collapsed by default
            .Add(p => p.Title, "Test Section"));

        // Assert
        var card = cut.Find(".oa-card");
        Assert.Contains("oa-collapsed", card.ClassName);
    }

    [Fact]
    public void CollapsibleSection_DoesNotAddCollapsedClass_WhenExpanded()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<CollapsibleSection>(parameters => parameters
            .Add(p => p.SectionKey, "model") // expanded by default
            .Add(p => p.Title, "Test Section"));

        // Assert
        var card = cut.Find(".oa-card");
        Assert.DoesNotContain("oa-collapsed", card.ClassName);
    }

    [Fact]
    public void CollapsibleSection_PersistsState_AcrossDashboard()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Create first section and toggle it
        var cut1 = RenderComponent<CollapsibleSection>(parameters => parameters
            .Add(p => p.SectionKey, "test-section")
            .Add(p => p.Title, "Test Section"));
        
        var header1 = cut1.Find(".oa-collapsible-header");
        header1.Click(); // Toggle state

        // Act - Create second section with same key
        var cut2 = RenderComponent<CollapsibleSection>(parameters => parameters
            .Add(p => p.SectionKey, "test-section")
            .Add(p => p.Title, "Test Section"));

        // Assert - Should have same collapsed state
        Assert.Equal(
            cut1.Find(".oa-card").ClassName,
            cut2.Find(".oa-card").ClassName
        );
    }

    [Fact]
    public void CollapsibleSection_HasCorrectCssClasses()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<CollapsibleSection>(parameters => parameters
            .Add(p => p.SectionKey, "test-section")
            .Add(p => p.Title, "Test Section"));

        // Assert
        Assert.NotNull(cut.Find(".oa-card"));
        Assert.NotNull(cut.Find(".oa-card-title"));
        Assert.NotNull(cut.Find(".oa-collapsible-header"));
        Assert.NotNull(cut.Find(".oa-collapse-btn"));
    }
}
