using System.Diagnostics;
using System.Globalization;
using Xunit;
using Factories;
using Factories.Extensions;

namespace OfflineAI.Tests.Factories;

/// <summary>
/// Unit tests for LlmFactory
/// Tests all public factory methods and their various configurations
/// </summary>
public class LlmFactoryTests
{
    #region Create() Tests

    [Fact]
    public void Create_ReturnsProcessStartInfo()
    {
        // Act
        var result = LlmFactory.Create();

        // Assert
        Assert.NotNull(result);
        Assert.IsType<ProcessStartInfo>(result);
    }

    [Fact]
    public void Create_SetsDefaultValues()
    {
        // Act
        var result = LlmFactory.Create();

        // Assert
        Assert.False(result.UseShellExecute, "UseShellExecute should be false");
        Assert.True(result.RedirectStandardOutput, "RedirectStandardOutput should be true");
        Assert.True(result.RedirectStandardError, "RedirectStandardError should be true");
        Assert.True(result.CreateNoWindow, "CreateNoWindow should be true");
    }

    [Fact]
    public void Create_ReturnsNewInstanceEachTime()
    {
        // Act
        var result1 = LlmFactory.Create();
        var result2 = LlmFactory.Create();

        // Assert
        Assert.NotSame(result1, result2);
    }

    [Fact]
    public void Create_InitializesWithEmptyFileName()
    {
        // Act
        var result = LlmFactory.Create();

        // Assert
        Assert.Equal(string.Empty, result.FileName);
    }

    [Fact]
    public void Create_InitializesWithEmptyArguments()
    {
        // Act
        var result = LlmFactory.Create();

        // Assert
        Assert.Equal(string.Empty, result.Arguments);
    }

    #endregion

    #region CreateForLlama() Tests

    [Fact]
    public void CreateForLlama_WithValidPaths_ReturnsConfiguredProcessStartInfo()
    {
        // Arrange
        var cliPath = @"C:\path\to\llama-cli.exe";
        var modelPath = @"C:\path\to\model.gguf";

        // Act
        var result = LlmFactory.CreateForLlama(cliPath, modelPath);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(cliPath, result.FileName);
        Assert.Contains(modelPath, result.Arguments);
    }

    [Fact]
    public void CreateForLlama_SetsModelArgument()
    {
        // Arrange
        var cliPath = @"llama-cli.exe";
        var modelPath = @"model.gguf";

        // Act
        var result = LlmFactory.CreateForLlama(cliPath, modelPath);

        // Assert
        Assert.Contains($"-m \"{modelPath}\"", result.Arguments);
    }

    [Fact]
    public void CreateForLlama_InheritsDefaultValues()
    {
        // Arrange
        var cliPath = @"llama-cli.exe";
        var modelPath = @"model.gguf";

        // Act
        var result = LlmFactory.CreateForLlama(cliPath, modelPath);

        // Assert
        Assert.False(result.UseShellExecute);
        Assert.True(result.RedirectStandardOutput);
        Assert.True(result.RedirectStandardError);
        Assert.True(result.CreateNoWindow);
    }

    [Fact]
    public void CreateForLlama_WithPathContainingSpaces_QuotesModelPath()
    {
        // Arrange
        var cliPath = @"llama-cli.exe";
        var modelPath = @"C:\path with spaces\model.gguf";

        // Act
        var result = LlmFactory.CreateForLlama(cliPath, modelPath);

        // Assert
        Assert.Contains($"-m \"{modelPath}\"", result.Arguments);
    }

    [Fact]
    public void CreateForLlama_WithEmptyCliPath_SetsEmptyFileName()
    {
        // Arrange
        var cliPath = "";
        var modelPath = @"model.gguf";

        // Act
        var result = LlmFactory.CreateForLlama(cliPath, modelPath);

        // Assert
        Assert.Equal("", result.FileName);
    }

    [Fact]
    public void CreateForLlama_WithEmptyModelPath_IncludesEmptyModelArgument()
    {
        // Arrange
        var cliPath = @"llama-cli.exe";
        var modelPath = "";

        // Act
        var result = LlmFactory.CreateForLlama(cliPath, modelPath);

        // Assert
        Assert.Contains("-m \"\"", result.Arguments);
    }

    [Fact]
    public void CreateForLlama_ReturnsNewInstanceEachTime()
    {
        // Arrange
        var cliPath = @"llama-cli.exe";
        var modelPath = @"model.gguf";

        // Act
        var result1 = LlmFactory.CreateForLlama(cliPath, modelPath);
        var result2 = LlmFactory.CreateForLlama(cliPath, modelPath);

        // Assert
        Assert.NotSame(result1, result2);
    }

    [Fact]
    public void CreateForLlama_CanBeChainedWithAdditionalExtensions()
    {
        // Arrange
        var cliPath = @"llama-cli.exe";
        var modelPath = @"model.gguf";
        var prompt = "Test prompt";

        // Act
        var result = LlmFactory.CreateForLlama(cliPath, modelPath)
            .SetPrompt(prompt);

        // Assert
        Assert.Contains($"-m \"{modelPath}\"", result.Arguments);
        Assert.Contains($"-p \"{prompt}\"", result.Arguments);
    }

    #endregion

    #region CreateForBoardGame() Tests

    [Fact]
    public void CreateForBoardGame_WithDefaultParameters_ReturnsConfiguredProcessStartInfo()
    {
        // Arrange
        var cliPath = @"llama-cli.exe";
        var modelPath = @"model.gguf";

        // Act
        var result = LlmFactory.CreateForBoardGame(cliPath, modelPath);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(cliPath, result.FileName);
        Assert.Contains($"-m \"{modelPath}\"", result.Arguments);
    }

    [Fact]
    public void CreateForBoardGame_WithDefaultParameters_UsesDefaultMaxTokens()
    {
        // Arrange
        var cliPath = @"llama-cli.exe";
        var modelPath = @"model.gguf";

        // Act
        var result = LlmFactory.CreateForBoardGame(cliPath, modelPath);

        // Assert
        Assert.Contains("-n 200", result.Arguments);
    }

    [Fact]
    public void CreateForBoardGame_WithDefaultParameters_UsesDefaultTemperature()
    {
        // Arrange
        var cliPath = @"llama-cli.exe";
        var modelPath = @"model.gguf";

        // Act
        var result = LlmFactory.CreateForBoardGame(cliPath, modelPath);

        // Assert
        // Check for temperature, accounting for culture-specific decimal separators
        Assert.True(
            result.Arguments.Contains("--temp 0.4") || result.Arguments.Contains("--temp 0,4"),
            $"Expected '--temp 0.4' or '--temp 0,4' but got: {result.Arguments}");
    }

    [Fact]
    public void CreateForBoardGame_WithCustomMaxTokens_SetsCorrectValue()
    {
        // Arrange
        var cliPath = @"llama-cli.exe";
        var modelPath = @"model.gguf";
        var maxTokens = 500;

        // Act
        var result = LlmFactory.CreateForBoardGame(cliPath, modelPath, maxTokens: maxTokens);

        // Assert
        Assert.Contains($"-n {maxTokens}", result.Arguments);
    }

    [Fact]
    public void CreateForBoardGame_WithCustomTemperature_SetsCorrectValue()
    {
        // Arrange
        var cliPath = @"llama-cli.exe";
        var modelPath = @"model.gguf";
        var temperature = 0.7f;

        // Act
        var result = LlmFactory.CreateForBoardGame(cliPath, modelPath, temperature: temperature);

        // Assert
        // Check for temperature, accounting for culture-specific decimal separators
        Assert.True(
            result.Arguments.Contains("--temp 0.7") || result.Arguments.Contains("--temp 0,7"),
            $"Expected '--temp 0.7' or '--temp 0,7' but got: {result.Arguments}");
    }

    [Fact]
    public void CreateForBoardGame_WithBothCustomParameters_SetsCorrectValues()
    {
        // Arrange
        var cliPath = @"llama-cli.exe";
        var modelPath = @"model.gguf";
        var maxTokens = 300;
        var temperature = 0.5f;

        // Act
        var result = LlmFactory.CreateForBoardGame(
            cliPath, 
            modelPath, 
            maxTokens: maxTokens, 
            temperature: temperature);

        // Assert
        Assert.Contains($"-n {maxTokens}", result.Arguments);
        // Check for temperature, accounting for culture-specific decimal separators
        Assert.True(
            result.Arguments.Contains("--temp 0.5") || result.Arguments.Contains("--temp 0,5"),
            $"Expected '--temp 0.5' or '--temp 0,5' but got: {result.Arguments}");
    }

    [Fact]
    public void CreateForBoardGame_SetsBoardGameSamplingParameters()
    {
        // Arrange
        var cliPath = @"llama-cli.exe";
        var modelPath = @"model.gguf";

        // Act
        var result = LlmFactory.CreateForBoardGame(cliPath, modelPath);

        // Assert
        // Check for board game-specific sampling parameters
        Assert.Contains("--top-k", result.Arguments);
        Assert.Contains("--top-p", result.Arguments);
        Assert.Contains("--repeat-penalty", result.Arguments);
        Assert.Contains("--repeat-last-n", result.Arguments);
    }

    [Fact]
    public void CreateForBoardGame_InheritsDefaultValues()
    {
        // Arrange
        var cliPath = @"llama-cli.exe";
        var modelPath = @"model.gguf";

        // Act
        var result = LlmFactory.CreateForBoardGame(cliPath, modelPath);

        // Assert
        Assert.False(result.UseShellExecute);
        Assert.True(result.RedirectStandardOutput);
        Assert.True(result.RedirectStandardError);
        Assert.True(result.CreateNoWindow);
    }

    [Fact]
    public void CreateForBoardGame_ReturnsNewInstanceEachTime()
    {
        // Arrange
        var cliPath = @"llama-cli.exe";
        var modelPath = @"model.gguf";

        // Act
        var result1 = LlmFactory.CreateForBoardGame(cliPath, modelPath);
        var result2 = LlmFactory.CreateForBoardGame(cliPath, modelPath);

        // Assert
        Assert.NotSame(result1, result2);
    }

    [Fact]
    public void CreateForBoardGame_WithZeroMaxTokens_SetsZeroValue()
    {
        // Arrange
        var cliPath = @"llama-cli.exe";
        var modelPath = @"model.gguf";
        var maxTokens = 0;

        // Act
        var result = LlmFactory.CreateForBoardGame(cliPath, modelPath, maxTokens: maxTokens);

        // Assert
        Assert.Contains("-n 0", result.Arguments);
    }

    [Fact]
    public void CreateForBoardGame_WithZeroTemperature_SetsZeroValue()
    {
        // Arrange
        var cliPath = @"llama-cli.exe";
        var modelPath = @"model.gguf";
        var temperature = 0.0f;

        // Act
        var result = LlmFactory.CreateForBoardGame(cliPath, modelPath, temperature: temperature);

        // Assert
        // Check for temperature, accounting for culture-specific decimal separators
        Assert.True(
            result.Arguments.Contains("--temp 0.0") || result.Arguments.Contains("--temp 0,0"),
            $"Expected '--temp 0.0' or '--temp 0,0' but got: {result.Arguments}");
    }

    [Fact]
    public void CreateForBoardGame_WithHighTemperature_SetsCorrectValue()
    {
        // Arrange
        var cliPath = @"llama-cli.exe";
        var modelPath = @"model.gguf";
        var temperature = 2.0f;

        // Act
        var result = LlmFactory.CreateForBoardGame(cliPath, modelPath, temperature: temperature);

        // Assert
        // Check for temperature, accounting for culture-specific decimal separators
        Assert.True(
            result.Arguments.Contains("--temp 2.0") || result.Arguments.Contains("--temp 2,0"),
            $"Expected '--temp 2.0' or '--temp 2,0' but got: {result.Arguments}");
    }

    [Fact]
    public void CreateForBoardGame_WithLargeMaxTokens_SetsCorrectValue()
    {
        // Arrange
        var cliPath = @"llama-cli.exe";
        var modelPath = @"model.gguf";
        var maxTokens = 4096;

        // Act
        var result = LlmFactory.CreateForBoardGame(cliPath, modelPath, maxTokens: maxTokens);

        // Assert
        Assert.Contains($"-n {maxTokens}", result.Arguments);
    }

    [Fact]
    public void CreateForBoardGame_WithNegativeMaxTokens_SetsNegativeValue()
    {
        // Arrange
        var cliPath = @"llama-cli.exe";
        var modelPath = @"model.gguf";
        var maxTokens = -1;

        // Act
        var result = LlmFactory.CreateForBoardGame(cliPath, modelPath, maxTokens: maxTokens);

        // Assert
        Assert.Contains("-n -1", result.Arguments);
    }

    [Fact]
    public void CreateForBoardGame_CanBeChainedWithAdditionalExtensions()
    {
        // Arrange
        var cliPath = @"llama-cli.exe";
        var modelPath = @"model.gguf";
        var prompt = "What are the rules?";

        // Act
        var result = LlmFactory.CreateForBoardGame(cliPath, modelPath)
            .SetPrompt(prompt);

        // Assert
        Assert.Contains($"-p \"{prompt}\"", result.Arguments);
    }

    #endregion

    #region Integration and Edge Case Tests

    [Fact]
    public void AllFactoryMethods_ProduceValidProcessStartInfo()
    {
        // Arrange
        var cliPath = @"llama-cli.exe";
        var modelPath = @"model.gguf";

        // Act
        var create = LlmFactory.Create();
        var createForLlama = LlmFactory.CreateForLlama(cliPath, modelPath);
        var createForBoardGame = LlmFactory.CreateForBoardGame(cliPath, modelPath);

        // Assert
        Assert.NotNull(create);
        Assert.NotNull(createForLlama);
        Assert.NotNull(createForBoardGame);
        Assert.IsType<ProcessStartInfo>(create);
        Assert.IsType<ProcessStartInfo>(createForLlama);
        Assert.IsType<ProcessStartInfo>(createForBoardGame);
    }

    [Fact]
    public void CreateForBoardGame_ContainsAllExpectedArguments()
    {
        // Arrange
        var cliPath = @"llama-cli.exe";
        var modelPath = @"model.gguf";
        var maxTokens = 200;
        var temperature = 0.4f;

        // Act
        var result = LlmFactory.CreateForBoardGame(
            cliPath, 
            modelPath, 
            maxTokens: maxTokens, 
            temperature: temperature);

        // Assert
        var args = result.Arguments;
        Assert.Contains($"-m \"{modelPath}\"", args);
        Assert.Contains($"-n {maxTokens}", args);
        Assert.Contains("--temp", args);
        Assert.Contains("--top-k", args);
        Assert.Contains("--top-p", args);
        Assert.Contains("--repeat-penalty", args);
        Assert.Contains("--repeat-last-n", args);
    }

    [Fact]
    public void CreateForLlama_WithSpecialCharactersInPath_HandlesCorrectly()
    {
        // Arrange
        var cliPath = @"C:\Program Files (x86)\llama-cli.exe";
        var modelPath = @"C:\Models\my-model@v1.0.gguf";

        // Act
        var result = LlmFactory.CreateForLlama(cliPath, modelPath);

        // Assert
        Assert.Equal(cliPath, result.FileName);
        Assert.Contains(modelPath, result.Arguments);
    }

    [Fact]
    public void CreateForBoardGame_WithDecimalTemperature_FormatsCorrectly()
    {
        // Arrange
        var cliPath = @"llama-cli.exe";
        var modelPath = @"model.gguf";
        var temperature = 0.3333f;

        // Act
        var result = LlmFactory.CreateForBoardGame(cliPath, modelPath, temperature: temperature);

        // Assert
        // Temperature should be formatted with 1 decimal place (either 0.3 or 0,3 depending on culture)
        Assert.True(
            result.Arguments.Contains("--temp 0.3") || result.Arguments.Contains("--temp 0,3"),
            $"Expected temperature to be formatted with 1 decimal place, but got: {result.Arguments}");
    }

    [Theory]
    [InlineData(50, 0.1f)]
    [InlineData(100, 0.3f)]
    [InlineData(200, 0.4f)]
    [InlineData(500, 0.7f)]
    [InlineData(1000, 1.0f)]
    public void CreateForBoardGame_WithVariousParameters_ConfiguresCorrectly(int maxTokens, float temperature)
    {
        // Arrange
        var cliPath = @"llama-cli.exe";
        var modelPath = @"model.gguf";

        // Act
        var result = LlmFactory.CreateForBoardGame(
            cliPath, 
            modelPath, 
            maxTokens: maxTokens, 
            temperature: temperature);

        // Assert
        Assert.Contains($"-n {maxTokens}", result.Arguments);
        Assert.Contains("--temp", result.Arguments);
    }

    [Fact]
    public void FactoryMethods_AreFluentAndChainable()
    {
        // Arrange
        var cliPath = @"llama-cli.exe";
        var modelPath = @"model.gguf";
        var systemPrompt = "You are a board game assistant.";
        var userPrompt = "Explain the setup phase.";

        // Act
        var result = LlmFactory.CreateForBoardGame(cliPath, modelPath, maxTokens: 300, temperature: 0.5f)
            .SetLlmContext(systemPrompt)
            .SetPrompt(userPrompt);

        // Assert
        Assert.NotNull(result);
        Assert.Contains(systemPrompt, result.Arguments);
        Assert.Contains(userPrompt, result.Arguments);
    }

    [Fact]
    public void CreateForBoardGame_BuildsValidProcessStartInfo_ForExecution()
    {
        // Arrange
        var cliPath = @"llama-cli.exe";
        var modelPath = @"model.gguf";

        // Act
        var result = LlmFactory.CreateForBoardGame(cliPath, modelPath);

        // Assert - Check all properties needed for process execution
        Assert.Equal(cliPath, result.FileName);
        Assert.NotEmpty(result.Arguments);
        Assert.False(result.UseShellExecute);
        Assert.True(result.RedirectStandardOutput);
        Assert.True(result.RedirectStandardError);
        Assert.True(result.CreateNoWindow);
    }

    [Fact]
    public void Create_CanBeExtendedManually()
    {
        // Arrange & Act
        var result = LlmFactory.Create();
        result.FileName = "custom-cli.exe";
        result.Arguments = "--custom-arg";

        // Assert
        Assert.Equal("custom-cli.exe", result.FileName);
        Assert.Equal("--custom-arg", result.Arguments);
    }

    [Fact]
    public void CreateForLlama_PathsWithBackslashes_PreservesCorrectly()
    {
        // Arrange
        var cliPath = @"C:\tools\llama\llama-cli.exe";
        var modelPath = @"D:\models\llama\model.gguf";

        // Act
        var result = LlmFactory.CreateForLlama(cliPath, modelPath);

        // Assert
        Assert.Equal(cliPath, result.FileName);
        Assert.Contains(modelPath, result.Arguments);
    }

    [Fact]
    public void CreateForLlama_PathsWithForwardSlashes_PreservesCorrectly()
    {
        // Arrange
        var cliPath = "~/tools/llama/llama-cli";
        var modelPath = "~/models/llama/model.gguf";

        // Act
        var result = LlmFactory.CreateForLlama(cliPath, modelPath);

        // Assert
        Assert.Equal(cliPath, result.FileName);
        Assert.Contains(modelPath, result.Arguments);
    }

    #endregion

    #region Documentation and Use Case Tests

    [Fact]
    public void CreateForBoardGame_UseCaseExample_ConfiguresForQuestionAnswering()
    {
        // This test demonstrates the intended use case from the documentation
        // Arrange
        var cliPath = @"d:\tinyllama\llama-cli.exe";
        var modelPath = @"d:\tinyllama\tinyllama-1.1b-chat-v1.0.Q5_K_M.gguf";

        // Act
        var result = LlmFactory.CreateForBoardGame(
            cliPath, 
            modelPath, 
            maxTokens: 200, 
            temperature: 0.4f);

        // Assert
        Assert.Equal(cliPath, result.FileName);
        Assert.Contains(modelPath, result.Arguments);
        Assert.Contains("-n 200", result.Arguments);
        Assert.Contains("--temp", result.Arguments);
        // Verify optimized sampling parameters for board games
        Assert.Contains("--repeat-penalty", result.Arguments);
        Assert.Contains("--repeat-last-n", result.Arguments);
    }

    [Fact]
    public void CreateForLlama_UseCaseExample_BasicLlamaConfiguration()
    {
        // This test demonstrates basic Llama CLI configuration
        // Arrange
        var cliPath = @"llama-cli.exe";
        var modelPath = @"model.gguf";

        // Act
        var result = LlmFactory.CreateForLlama(cliPath, modelPath);

        // Assert - Provides a foundation for further configuration
        Assert.NotEmpty(result.FileName);
        Assert.Contains("-m", result.Arguments);
        Assert.True(result.RedirectStandardOutput);
        Assert.True(result.RedirectStandardError);
    }

    [Fact]
    public void Create_UseCaseExample_CustomConfiguration()
    {
        // This test demonstrates starting from scratch with defaults
        // Arrange & Act
        var result = LlmFactory.Create();

        // Assert - Provides a clean slate with process defaults
        Assert.Empty(result.FileName);
        Assert.Empty(result.Arguments);
        Assert.True(result.RedirectStandardOutput);
        Assert.True(result.RedirectStandardError);
        Assert.False(result.UseShellExecute);
    }

    #endregion
}
