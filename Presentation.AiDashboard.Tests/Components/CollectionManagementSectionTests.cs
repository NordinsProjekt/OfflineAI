using AiDashboard.State;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Moq;
using CollectionManagementSection = AiDashboard.Components.Pages.Components.CollectionManagementSection;
using CollapsibleSection = AiDashboard.Components.Pages.Components.CollapsibleSection;

namespace Presentation.AiDashboard.Tests.Components;

/// <summary>
/// Tests for CollectionManagementSection component to ensure UI remains consistent across features.
/// </summary>
public class CollectionManagementSectionTests : TestContext
{
    private readonly Mock<IJSRuntime> _mockJSRuntime;

    public CollectionManagementSectionTests()
    {
        _mockJSRuntime = new Mock<IJSRuntime>();
    }

    private DashboardState CreateMockDashboardState()
    {
        var state = new DashboardState();
        // Expand the collection section for testing
        if (state.IsSectionCollapsed("collection"))
        {
            state.ToggleSection("collection");
        }
        return state;
    }

    [Fact]
    public void CollectionManagementSection_Renders_WithCorrectStructure()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);
        Services.AddSingleton<IJSRuntime>(_mockJSRuntime.Object);

        // Act
        var cut = RenderComponent<CollectionManagementSection>();

        // Assert
        var section = cut.FindComponent<CollapsibleSection>();
        Assert.NotNull(section);
    }

    [Fact]
    public void CollectionManagementSection_Renders_CollapsibleSection_WithCorrectTitle()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);
        Services.AddSingleton<IJSRuntime>(_mockJSRuntime.Object);

        // Act
        var cut = RenderComponent<CollectionManagementSection>();

        // Assert
        var section = cut.FindComponent<CollapsibleSection>();
        Assert.Equal("collection", section.Instance.SectionKey);
        Assert.Equal("Collections", section.Instance.Title);
    }

    [Fact]
    public void CollectionManagementSection_Shows_InfoText_WhenNoCollections()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);
        Services.AddSingleton<IJSRuntime>(_mockJSRuntime.Object);

        // Act
        var cut = RenderComponent<CollectionManagementSection>();

        // Assert
        var infoText = cut.Find(".oa-info-text");
        Assert.Contains("No collections yet", infoText.TextContent);
    }

    [Fact]
    public void CollectionManagementSection_Renders_NewCollectionField()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);
        Services.AddSingleton<IJSRuntime>(_mockJSRuntime.Object);

        // Act
        var cut = RenderComponent<CollectionManagementSection>();

        // Assert
        var input = cut.Find("input[type='text'].oa-text");
        Assert.Equal("Enter new collection name...", input.GetAttribute("placeholder"));
        
        var button = cut.Find("button.oa-btn.oa-btn-block");
        Assert.Contains("Create & Select", button.TextContent);
    }

    [Fact]
    public void CollectionManagementSection_CreateButton_Disabled_WhenInputEmpty()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);
        Services.AddSingleton<IJSRuntime>(_mockJSRuntime.Object);

        // Act
        var cut = RenderComponent<CollectionManagementSection>();

        // Assert
        var button = cut.Find("button.oa-btn.oa-btn-block");
        Assert.NotNull(button.GetAttribute("disabled"));
    }

    [Fact]
    public void CollectionManagementSection_CreateButton_Enabled_WhenInputHasText()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);
        Services.AddSingleton<IJSRuntime>(_mockJSRuntime.Object);

        var cut = RenderComponent<CollectionManagementSection>();
        var input = cut.Find("input[type='text'].oa-text");

        // Act
        input.Input("test-collection");

        // Assert
        var button = cut.Find("button.oa-btn.oa-btn-block");
        Assert.Null(button.GetAttribute("disabled"));
    }

    [Fact]
    public void CollectionManagementSection_DoesNotShow_ActiveCollection_Initially()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);
        Services.AddSingleton<IJSRuntime>(_mockJSRuntime.Object);

        // Act
        var cut = RenderComponent<CollectionManagementSection>();

        // Assert
        var activeCollections = cut.FindAll(".oa-active-collection");
        Assert.Empty(activeCollections);
    }

    [Fact]
    public void CollectionManagementSection_DoesNotShow_DeleteButton_Initially()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);
        Services.AddSingleton<IJSRuntime>(_mockJSRuntime.Object);

        // Act
        var cut = RenderComponent<CollectionManagementSection>();

        // Assert
        var deleteButtons = cut.FindAll("button.oa-btn-danger");
        Assert.Empty(deleteButtons);
    }

    [Fact]
    public void CollectionManagementSection_HasCorrectCssClasses()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);
        Services.AddSingleton<IJSRuntime>(_mockJSRuntime.Object);

        // Act
        var cut = RenderComponent<CollectionManagementSection>();

        // Assert
        Assert.NotEmpty(cut.FindAll(".oa-field"));
        Assert.NotNull(cut.Find("input.oa-text"));
        Assert.NotNull(cut.Find("button.oa-btn"));
    }
}
