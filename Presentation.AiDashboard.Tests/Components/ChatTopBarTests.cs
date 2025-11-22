using AiDashboard.State;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Services.Configuration;
using ChatTopBar = AiDashboard.Components.Pages.Components.ChatTopBar;
using System.Globalization;

namespace Presentation.AiDashboard.Tests.Components;

/// <summary>
/// Tests for ChatTopBar component to ensure UI remains consistent across features.
/// </summary>
public class ChatTopBarTests : TestContext
{
    private DashboardState CreateMockDashboardState()
    {
        var state = new DashboardState();
        return state;
    }

    [Fact]
    public void ChatTopBar_Renders_WithCorrectStructure()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<ChatTopBar>();

        // Assert - Check for key elements without exact text matching
        Assert.NotNull(cut.Find(".oa-topbar"));
        Assert.NotNull(cut.Find(".oa-badges"));
        Assert.Equal(4, cut.FindAll(".oa-badge").Count);
        
        // Verify each badge type exists
        Assert.NotNull(cut.Find(".oa-badge.green")); // RAG badge
        Assert.NotNull(cut.Find(".oa-badge.blue"));  // Model badge
        Assert.NotNull(cut.Find(".oa-badge.purple")); // Temperature badge
        var gpuBadge = cut.FindAll(".oa-badge").Last(); // GPU badge (last one)
        Assert.True(gpuBadge.ClassList.Contains("oa-badge"));
    }

    [Fact]
    public void ChatTopBar_Displays_RagModeOn_WhenEnabled()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        dashboardState.SettingsService.RagMode = true;
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<ChatTopBar>();

        // Assert
        var ragBadge = cut.Find(".oa-badge.green");
        Assert.Contains("RAG: ON", ragBadge.TextContent);
    }

    [Fact]
    public void ChatTopBar_Displays_RagModeOff_WhenDisabled()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        dashboardState.SettingsService.RagMode = false;
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<ChatTopBar>();

        // Assert
        var ragBadge = cut.Find(".oa-badge.green");
        Assert.Contains("RAG: OFF", ragBadge.TextContent);
    }

    [Fact]
    public void ChatTopBar_Displays_CurrentModel()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<ChatTopBar>();

        // Assert
        var modelBadge = cut.Find(".oa-badge.blue");
        Assert.Contains("Model:", modelBadge.TextContent);
    }

    [Fact]
    public void ChatTopBar_Displays_Temperature()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        dashboardState.SettingsService.Temperature = 0.7;
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<ChatTopBar>();

        // Assert
        var tempBadge = cut.Find(".oa-badge.purple");
        // Use Contains "Temp:" and check the badge contains a number, accounting for culture
        Assert.Contains("Temp:", tempBadge.TextContent);
        var tempText = tempBadge.TextContent.Replace("Temp:", "").Trim();
        // Parse using current culture or invariant
        Assert.True(double.TryParse(tempText, NumberStyles.Float, CultureInfo.CurrentCulture, out var temp) ||
                    double.TryParse(tempText, NumberStyles.Float, CultureInfo.InvariantCulture, out temp));
        Assert.Equal(0.7, temp, 0.01); // Allow small tolerance
    }

    [Fact]
    public void ChatTopBar_Displays_GpuOn_WhenEnabled()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        dashboardState.SettingsService.UseGpu = true;
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<ChatTopBar>();

        // Assert
        var badges = cut.FindAll(".oa-badge");
        var gpuBadge = badges.FirstOrDefault(b => b.TextContent.Contains("GPU:"));
        Assert.NotNull(gpuBadge);
        Assert.Contains("GPU: ON (34)", gpuBadge.TextContent);
        Assert.True(gpuBadge.ClassList.Contains("orange"));
    }

    [Fact]
    public void ChatTopBar_Displays_GpuOff_WhenDisabled()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        dashboardState.SettingsService.UseGpu = false;
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<ChatTopBar>();

        // Assert
        var badges = cut.FindAll(".oa-badge");
        var gpuBadge = badges.FirstOrDefault(b => b.TextContent.Contains("GPU:"));
        Assert.NotNull(gpuBadge);
        Assert.Contains("GPU: OFF (0)", gpuBadge.TextContent);
        Assert.True(gpuBadge.ClassList.Contains("gray"));
    }

    [Fact]
    public void ChatTopBar_Updates_WhenStateChanges()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        dashboardState.SettingsService.RagMode = false;
        Services.AddSingleton(dashboardState);

        var cut = RenderComponent<ChatTopBar>();
        
        // Verify initial state
        var ragBadge = cut.Find(".oa-badge.green");
        Assert.Contains("RAG: OFF", ragBadge.TextContent);

        // Act
        dashboardState.SettingsService.RagMode = true;

        // Assert
        ragBadge = cut.Find(".oa-badge.green");
        Assert.Contains("RAG: ON", ragBadge.TextContent);
    }

    [Fact]
    public void ChatTopBar_HasCorrectCssClasses()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<ChatTopBar>();

        // Assert
        Assert.NotNull(cut.Find(".oa-topbar"));
        Assert.NotNull(cut.Find(".oa-badges"));
        Assert.Equal(4, cut.FindAll(".oa-badge").Count);
    }
}
