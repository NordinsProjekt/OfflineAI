using AiDashboard.State;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using GenerationSettingsSection = AiDashboard.Components.Pages.Components.GenerationSettingsSection;
using CollapsibleSection = AiDashboard.Components.Pages.Components.CollapsibleSection;
using System.Globalization;

namespace Presentation.AiDashboard.Tests.Components;

/// <summary>
/// Tests for GenerationSettingsSection component to ensure UI remains consistent across features.
/// </summary>
public class GenerationSettingsSectionTests : TestContext
{
    private DashboardState CreateMockDashboardState()
    {
        var state = new DashboardState();
        // Expand sections by default for testing
        state.ToggleSection("generation"); // Make it expanded
        state.ToggleSection("rag"); // Make it expanded
        return state;
    }

    private double ParseNumber(string text)
    {
        // Try current culture first, then invariant
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var result))
            return result;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
            return result;
        throw new FormatException($"Could not parse '{text}' as a number");
    }

    [Fact]
    public void GenerationSettingsSection_Renders_WithCorrectStructure()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<GenerationSettingsSection>();

        // Assert
        var sections = cut.FindComponents<CollapsibleSection>();
        Assert.Equal(2, sections.Count); // Generation and RAG Settings
    }

    [Fact]
    public void GenerationSettingsSection_Renders_AllGenerationFields()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<GenerationSettingsSection>();

        // Assert - Should have 10 fields total (3 direct + 4 in grids + 1 + 2 RAG)
        var fields = cut.FindAll(".oa-field");
        Assert.True(fields.Count >= 10, $"Expected at least 10 fields, but found {fields.Count}");
    }

    [Fact]
    public void GenerationSettingsSection_Temperature_DisplaysCorrectValue()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        dashboardState.SettingsService.Temperature = 0.7;
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<GenerationSettingsSection>();

        // Assert
        var inputs = cut.FindAll("input[type='number']");
        var temperatureInput = inputs.FirstOrDefault(i => 
            i.GetAttribute("step") == "0.1" && 
            i.GetAttribute("min") == "0" && 
            i.GetAttribute("max") == "2");
        
        Assert.NotNull(temperatureInput);
        var value = ParseNumber(temperatureInput.GetAttribute("value") ?? "0");
        Assert.Equal(0.7, value, 0.01);
    }

    [Fact]
    public void GenerationSettingsSection_MaxTokens_DisplaysCorrectValue()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        dashboardState.SettingsService.MaxTokens = 512;
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<GenerationSettingsSection>();

        // Assert
        var inputs = cut.FindAll("input[type='number']");
        var maxTokensInput = inputs.FirstOrDefault(i => 
            i.GetAttribute("min") == "1" && 
            i.GetAttribute("max") == "4096");
        
        Assert.NotNull(maxTokensInput);
        Assert.Equal("512", maxTokensInput.GetAttribute("value"));
    }

    [Fact]
    public void GenerationSettingsSection_TimeoutSeconds_DisplaysCorrectValue()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        dashboardState.SettingsService.TimeoutSeconds = 120;
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<GenerationSettingsSection>();

        // Assert
        var inputs = cut.FindAll("input[type='number']");
        var timeoutInput = inputs.FirstOrDefault(i => 
            i.GetAttribute("min") == "10" && 
            i.GetAttribute("max") == "300");
        
        Assert.NotNull(timeoutInput);
        Assert.Equal("120", timeoutInput.GetAttribute("value"));
    }

    [Fact]
    public void GenerationSettingsSection_TopK_DisplaysCorrectValue()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        dashboardState.SettingsService.TopK = 40;
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<GenerationSettingsSection>();

        // Assert
        var inputs = cut.FindAll("input[type='number']");
        var topKInput = inputs.FirstOrDefault(i => 
            i.GetAttribute("min") == "1" && 
            i.GetAttribute("max") == "100");
        
        Assert.NotNull(topKInput);
        Assert.Equal("40", topKInput.GetAttribute("value"));
    }

    [Fact]
    public void GenerationSettingsSection_TopP_DisplaysCorrectValue()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        dashboardState.SettingsService.TopP = 0.95;
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<GenerationSettingsSection>();

        // Assert
        var inputs = cut.FindAll("input[type='number']");
        var topPInput = inputs.FirstOrDefault(i => 
            i.GetAttribute("step") == "0.01" && 
            i.GetAttribute("min") == "0" && 
            i.GetAttribute("max") == "1");
        
        Assert.NotNull(topPInput);
        var value = ParseNumber(topPInput.GetAttribute("value") ?? "0");
        Assert.Equal(0.95, value, 0.01);
    }

    [Fact]
    public void GenerationSettingsSection_RepeatPenalty_DisplaysCorrectValue()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        dashboardState.SettingsService.RepeatPenalty = 1.1;
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<GenerationSettingsSection>();

        // Assert
        var inputs = cut.FindAll("input[type='number']");
        // Find by looking for inputs with the right attributes
        var repeatPenaltyInputs = inputs.Where(i => 
            i.GetAttribute("step") == "0.01" && 
            i.GetAttribute("min") == "0" && 
            i.GetAttribute("max") == "2").ToList();
        
        // Should be 3 inputs with these attributes (Repeat, Presence, Frequency)
        Assert.NotEmpty(repeatPenaltyInputs);
        // First one should be Repeat Penalty
        var value = ParseNumber(repeatPenaltyInputs[0].GetAttribute("value") ?? "0");
        Assert.Equal(1.1, value, 0.01);
    }

    [Fact]
    public void GenerationSettingsSection_PresencePenalty_DisplaysCorrectValue()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        dashboardState.SettingsService.PresencePenalty = 0.5;
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<GenerationSettingsSection>();

        // Assert
        var inputs = cut.FindAll("input[type='number']");
        var penaltyInputs = inputs.Where(i => 
            i.GetAttribute("step") == "0.01" && 
            i.GetAttribute("min") == "0" && 
            i.GetAttribute("max") == "2").ToList();
        
        // Should have at least 2 elements (Repeat, Presence)
        Assert.True(penaltyInputs.Count >= 2, $"Expected at least 2 penalty inputs, found {penaltyInputs.Count}");
        // Second one should be Presence Penalty
        var value = ParseNumber(penaltyInputs[1].GetAttribute("value") ?? "0");
        Assert.Equal(0.5, value, 0.01);
    }

    [Fact]
    public void GenerationSettingsSection_FrequencyPenalty_DisplaysCorrectValue()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        dashboardState.SettingsService.FrequencyPenalty = 0.3;
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<GenerationSettingsSection>();

        // Assert
        var inputs = cut.FindAll("input[type='number']");
        var penaltyInputs = inputs.Where(i => 
            i.GetAttribute("step") == "0.01" && 
            i.GetAttribute("min") == "0" && 
            i.GetAttribute("max") == "2").ToList();
        
        // Should have 3 elements (Repeat, Presence, Frequency)
        Assert.True(penaltyInputs.Count >= 3, $"Expected at least 3 penalty inputs, found {penaltyInputs.Count}");
        // Third one should be Frequency Penalty
        var value = ParseNumber(penaltyInputs[2].GetAttribute("value") ?? "0");
        Assert.Equal(0.3, value, 0.01);
    }

    [Fact]
    public void GenerationSettingsSection_RagTopK_DisplaysCorrectValue()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        dashboardState.SettingsService.RagTopK = 3;
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<GenerationSettingsSection>();

        // Assert
        var inputs = cut.FindAll("input[type='number']");
        var ragTopKInput = inputs.FirstOrDefault(i => 
            i.GetAttribute("min") == "1" && 
            i.GetAttribute("max") == "5");
        
        Assert.NotNull(ragTopKInput);
        Assert.Equal("3", ragTopKInput.GetAttribute("value"));
    }

    [Fact]
    public void GenerationSettingsSection_RagMinRelevanceScore_DisplaysCorrectValue()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        dashboardState.SettingsService.RagMinRelevanceScore = 0.5;
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<GenerationSettingsSection>();

        // Assert
        var inputs = cut.FindAll("input[type='number']");
        var ragMinScoreInput = inputs.FirstOrDefault(i => 
            i.GetAttribute("step") == "0.05" && 
            i.GetAttribute("min") == "0.3" && 
            i.GetAttribute("max") == "0.8");
        
        Assert.NotNull(ragMinScoreInput);
        var value = ParseNumber(ragMinScoreInput.GetAttribute("value") ?? "0");
        Assert.Equal(0.5, value, 0.01);
    }

    [Fact]
    public void GenerationSettingsSection_Temperature_CanBeChanged()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        dashboardState.SettingsService.Temperature = 0.7;
        Services.AddSingleton(dashboardState);

        var cut = RenderComponent<GenerationSettingsSection>();
        var inputs = cut.FindAll("input[type='number']");
        var temperatureInput = inputs.FirstOrDefault(i => 
            i.GetAttribute("step") == "0.1" && 
            i.GetAttribute("min") == "0" && 
            i.GetAttribute("max") == "2");

        // Act
        temperatureInput?.Input("1.2");

        // Assert
        Assert.Equal(1.2, dashboardState.SettingsService.Temperature, 0.01);
    }

    [Fact]
    public void GenerationSettingsSection_HasCorrectLabels()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<GenerationSettingsSection>();

        // Assert
        var labels = cut.FindAll("label");
        var labelTexts = labels.Select(l => l.TextContent.Trim()).ToList();
        
        Assert.Contains("Temperature", labelTexts);
        Assert.Contains("Max Tokens", labelTexts);
        Assert.Contains("Timeout (seconds)", labelTexts);
        Assert.Contains("Top-K", labelTexts);
        Assert.Contains("Top-P", labelTexts);
        Assert.Contains("Repeat Penalty", labelTexts);
        Assert.Contains("Presence Penalty", labelTexts);
        Assert.Contains("Frequency Penalty", labelTexts);
        Assert.Contains("Top-K Chunks", labelTexts);
        Assert.Contains("Min Relevance", labelTexts);
    }

    [Fact]
    public void GenerationSettingsSection_HasHelpText_ForRagSettings()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<GenerationSettingsSection>();

        // Assert
        var hints = cut.FindAll(".oa-hint");
        Assert.Equal(2, hints.Count);
        Assert.Contains("Number of knowledge chunks to use (1-5)", hints[0].TextContent);
        Assert.Contains("Minimum similarity score", hints[1].TextContent);
    }

    [Fact]
    public void GenerationSettingsSection_HasCorrectCssClasses()
    {
        // Arrange
        var dashboardState = CreateMockDashboardState();
        Services.AddSingleton(dashboardState);

        // Act
        var cut = RenderComponent<GenerationSettingsSection>();

        // Assert
        Assert.NotEmpty(cut.FindAll(".oa-field"));
        Assert.NotEmpty(cut.FindAll(".oa-text"));
        Assert.Equal(2, cut.FindAll(".oa-grid2").Count); // Two grids: Top-K/Top-P, Repeat/Presence
    }
}
