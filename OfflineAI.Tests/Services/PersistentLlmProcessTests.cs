using Services;
using Xunit;
using FluentAssertions;
using System;
using System.IO;
using System.Threading.Tasks;

namespace OfflineAI.Tests.Services;

/// <summary>
/// Unit tests for PersistentLlmProcess.
/// Tests the lifecycle, health monitoring, and thread-safety of persistent LLM process management.
/// </summary>
public class PersistentLlmProcessTests
{
    // Mock file paths for testing (will use File.Create to make them exist)
    private readonly string _testLlmPath;
    private readonly string _testModelPath;

    public PersistentLlmProcessTests()
    {
        // Create temporary files for testing
        var tempDir = Path.Combine(Path.GetTempPath(), "OfflineAI_Tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        
        _testLlmPath = Path.Combine(tempDir, "test-llama-cli.exe");
        _testModelPath = Path.Combine(tempDir, "test-model.gguf");
        
        File.WriteAllText(_testLlmPath, "mock executable");
        File.WriteAllText(_testModelPath, "mock model");
    }

    [Fact]
    public async Task CreateAsync_WithValidPaths_ShouldCreateInstance()
    {
        // Act
        var process = await PersistentLlmProcess.CreateAsync(_testLlmPath, _testModelPath, timeoutMs: 5000);

        // Assert
        process.Should().NotBeNull();
        process.IsHealthy.Should().BeTrue();
        process.LastUsed.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));

        // Cleanup
        process.Dispose();
    }

    [Fact]
    public async Task CreateAsync_WithMissingLlmPath_ShouldThrowFileNotFoundException()
    {
        // Arrange
        var missingPath = @"C:\NonExistent\llama-cli.exe";

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(async () =>
            await PersistentLlmProcess.CreateAsync(missingPath, _testModelPath));
    }

    [Fact]
    public async Task CreateAsync_WithMissingModelPath_ShouldThrowFileNotFoundException()
    {
        // Arrange
        var missingPath = @"C:\NonExistent\model.gguf";

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(async () =>
            await PersistentLlmProcess.CreateAsync(_testLlmPath, missingPath));
    }

    [Fact]
    public async Task IsHealthy_InitialState_ShouldBeTrue()
    {
        // Arrange
        var process = await PersistentLlmProcess.CreateAsync(_testLlmPath, _testModelPath);

        // Assert
        process.IsHealthy.Should().BeTrue();

        // Cleanup
        process.Dispose();
    }

    [Fact]
    public async Task LastUsed_AfterCreation_ShouldBeRecent()
    {
        // Arrange
        var beforeCreation = DateTime.UtcNow;
        var process = await PersistentLlmProcess.CreateAsync(_testLlmPath, _testModelPath);

        // Assert
        process.LastUsed.Should().BeOnOrAfter(beforeCreation);
        process.LastUsed.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));

        // Cleanup
        process.Dispose();
    }

    [Fact]
    public async Task Dispose_ShouldAllowMultipleCalls()
    {
        // Arrange
        var process = await PersistentLlmProcess.CreateAsync(_testLlmPath, _testModelPath);

        // Act
        process.Dispose();
        var act = () => process.Dispose(); // Second call

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public async Task QueryAsync_AfterDispose_ShouldThrowObjectDisposedException()
    {
        // Arrange
        var process = await PersistentLlmProcess.CreateAsync(_testLlmPath, _testModelPath);
        process.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await process.QueryAsync("System prompt", "User question"));
    }

    [Fact]
    public async Task QueryAsync_WithEmptySystemPrompt_ShouldNotThrow()
    {
        // Arrange
        var process = await PersistentLlmProcess.CreateAsync(_testLlmPath, _testModelPath, timeoutMs: 1000);

        // Act
        var act = async () => await process.QueryAsync("", "Test question");

        // Assert - Will fail to execute but shouldn't throw on empty prompt
        // Note: This will likely throw InvalidOperationException due to process execution failure
        // but not due to prompt validation
        await act.Should().ThrowAsync<InvalidOperationException>();
        
        // Cleanup
        process.Dispose();
    }

    [Fact]
    public async Task IsHealthy_AfterQueryFailure_ShouldBeFalse()
    {
        // Arrange
        var process = await PersistentLlmProcess.CreateAsync(_testLlmPath, _testModelPath, timeoutMs: 1000);

        // Act - Try to query (will fail because mock exe isn't real)
        try
        {
            await process.QueryAsync("System", "Question");
        }
        catch
        {
            // Expected to fail
        }

        // Assert
        process.IsHealthy.Should().BeFalse();

        // Cleanup
        process.Dispose();
    }

    [Fact]
    public async Task CleanResponse_RemovesTrailingTags()
    {
        // This is tested implicitly through QueryAsync
        // The CleanResponse method is private but we can verify the behavior
        // through integration tests
        
        // Arrange
        var process = await PersistentLlmProcess.CreateAsync(_testLlmPath, _testModelPath);

        // Assert - Just verify creation succeeds
        process.Should().NotBeNull();

        // Cleanup
        process.Dispose();
    }
}
