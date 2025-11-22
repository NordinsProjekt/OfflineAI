using AiDashboard.State;
using Services.Configuration;
using Xunit;
using Moq;
using Application.AI.Pooling;
using Services.Interfaces;
using DashboardChatService = AiDashboard.Services.DashboardChatService;

namespace Presentation.AiDashboard.Tests.State;

/// <summary>
/// Tests for DashboardState to ensure state management remains consistent across features.
/// </summary>
public class DashboardStateTests
{
    [Fact]
    public void DashboardState_Constructor_InitializesServices()
    {
        // Arrange & Act
        var state = new DashboardState();

        // Assert
        Assert.NotNull(state.SettingsService);
        Assert.NotNull(state.ModelService);
    }

    [Fact]
    public void DashboardState_Collapsed_DefaultsToFalse()
    {
        // Arrange & Act
        var state = new DashboardState();

        // Assert
        Assert.False(state.Collapsed);
    }

    [Fact]
    public void DashboardState_ToggleSidebar_ChangesCollapsedState()
    {
        // Arrange
        var state = new DashboardState();
        var initialState = state.Collapsed;

        // Act
        state.ToggleSidebar();

        // Assert
        Assert.NotEqual(initialState, state.Collapsed);
    }

    [Fact]
    public void DashboardState_ToggleSidebar_TriggersOnChange()
    {
        // Arrange
        var state = new DashboardState();
        var eventTriggered = false;
        state.OnChange += () => eventTriggered = true;

        // Act
        state.ToggleSidebar();

        // Assert
        Assert.True(eventTriggered);
    }

    [Fact]
    public void DashboardState_IsSectionCollapsed_ReturnsCorrectValue()
    {
        // Arrange
        var state = new DashboardState();

        // Act & Assert
        Assert.True(state.IsSectionCollapsed("modes")); // Collapsed by default
        Assert.False(state.IsSectionCollapsed("model")); // Expanded by default
    }

    [Fact]
    public void DashboardState_ToggleSection_ChangesState()
    {
        // Arrange
        var state = new DashboardState();
        var initialState = state.IsSectionCollapsed("modes");

        // Act
        state.ToggleSection("modes");

        // Assert
        Assert.NotEqual(initialState, state.IsSectionCollapsed("modes"));
    }

    [Fact]
    public void DashboardState_ToggleSection_TriggersOnChange()
    {
        // Arrange
        var state = new DashboardState();
        var eventTriggered = false;
        state.OnChange += () => eventTriggered = true;

        // Act
        state.ToggleSection("modes");

        // Assert
        Assert.True(eventTriggered);
    }

    [Fact]
    public void DashboardState_StatusMessage_CanBeSet()
    {
        // Arrange
        var state = new DashboardState();
        var testMessage = "Test status message";

        // Act
        state.StatusMessage = testMessage;

        // Assert
        Assert.Equal(testMessage, state.StatusMessage);
    }

    [Fact]
    public void DashboardState_StatusMessage_TriggersOnChange()
    {
        // Arrange
        var state = new DashboardState();
        var eventTriggered = false;
        state.OnChange += () => eventTriggered = true;

        // Act
        state.StatusMessage = "New message";

        // Assert
        Assert.True(eventTriggered);
    }

    [Fact]
    public void DashboardState_ActiveTable_DefaultsToMemoryFragments()
    {
        // Arrange & Act
        var state = new DashboardState();

        // Assert
        Assert.Equal("MemoryFragments", state.ActiveTable);
    }

    [Fact]
    public void DashboardState_ActiveTable_CanBeChanged()
    {
        // Arrange
        var state = new DashboardState();
        var newTableName = "TestTable";

        // Act
        state.ActiveTable = newTableName;

        // Assert
        Assert.Equal(newTableName, state.ActiveTable);
    }

    [Fact]
    public void DashboardState_ActiveTable_TriggersOnChange()
    {
        // Arrange
        var state = new DashboardState();
        var eventTriggered = false;
        state.OnChange += () => eventTriggered = true;

        // Act
        state.ActiveTable = "NewTable";

        // Assert
        Assert.True(eventTriggered);
    }

    [Fact]
    public void DashboardState_SettingsService_ChangesTriggersOnChange()
    {
        // Arrange
        var state = new DashboardState();
        var eventTriggered = false;
        state.OnChange += () => eventTriggered = true;

        // Act
        state.SettingsService.RagMode = !state.SettingsService.RagMode;

        // Assert
        Assert.True(eventTriggered);
    }

    [Fact]
    public void DashboardState_SetInvokeAsync_AcceptsCallback()
    {
        // Arrange
        var state = new DashboardState();
        var callbackInvoked = false;

        // Act
        state.SetInvokeAsync(action =>
        {
            action();
            callbackInvoked = true;
            return Task.CompletedTask;
        });

        state.ToggleSidebar();

        // Assert
        Assert.True(callbackInvoked);
    }

    [Fact]
    public void DashboardState_ChatService_CanBeSet()
    {
        // Arrange
        var state = new DashboardState();
        var mockMemory = new Mock<ILlmMemory>();
        var mockConversationMemory = new Mock<ILlmMemory>();
        var mockModelPool = new Mock<IModelInstancePool>();
        
        var chatService = new DashboardChatService(
            mockMemory.Object,
            mockConversationMemory.Object,
            mockModelPool.Object);

        // Act
        state.ChatService = chatService;

        // Assert
        Assert.NotNull(state.ChatService);
    }

    [Fact]
    public void DashboardState_Collapsed_DoesNotTriggerOnChange_WhenValueIsSame()
    {
        // Arrange
        var state = new DashboardState();
        state.Collapsed = false;
        
        var eventTriggered = false;
        state.OnChange += () => eventTriggered = true;

        // Act
        state.Collapsed = false; // Same value

        // Assert
        Assert.False(eventTriggered);
    }

    [Fact]
    public void DashboardState_AllSections_HaveDefaultCollapsedState()
    {
        // Arrange
        var state = new DashboardState();

        // Act & Assert - Verify all known sections have a defined state
        var sections = new[] { "modes", "generation", "rag", "model", "collection", "domains", "files", "knowledge", "table" };
        
        foreach (var section in sections)
        {
            // Should not throw and should return a boolean
            var isCollapsed = state.IsSectionCollapsed(section);
            Assert.IsType<bool>(isCollapsed);
        }
    }

    [Fact]
    public void DashboardState_ToggleSection_OnUnknownSection_DoesNotThrow()
    {
        // Arrange
        var state = new DashboardState();

        // Act & Assert - Should not throw
        state.ToggleSection("unknown-section");
    }

    [Fact]
    public void DashboardState_IsSectionCollapsed_OnUnknownSection_ReturnsFalse()
    {
        // Arrange
        var state = new DashboardState();

        // Act
        var result = state.IsSectionCollapsed("unknown-section");

        // Assert
        Assert.False(result);
    }
}
