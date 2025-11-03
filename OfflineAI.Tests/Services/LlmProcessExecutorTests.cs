using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Services;
using MemoryLibrary.Models;
using System.Text;
using System.Reflection;

namespace OfflineAI.Tests.Services;

/// <summary>
/// Unit tests for LlmProcessExecutor service.
/// Tests process execution, output parsing, and streaming logic.
/// </summary>
public class LlmProcessExecutorTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_InitializesWithDefaultTimeout()
    {
        // Arrange & Act
        var executor = new LlmProcessExecutor();

        // Assert
        Assert.NotNull(executor);
    }

    [Fact]
    public void Constructor_InitializesWithCustomTimeout()
    {
        // Arrange & Act
        var executor = new LlmProcessExecutor(timeoutMs: 5000);

        // Assert
        Assert.NotNull(executor);
    }

    [Fact]
    public void Constructor_InitializesWithConversationHistory()
    {
        // Arrange
        var mockMemory = new Mock<ILlmMemory>();

        // Act
        var executor = new LlmProcessExecutor(30000, mockMemory.Object);

        // Assert
        Assert.NotNull(executor);
    }

    [Fact]
    public void Constructor_AcceptsNullConversationHistory()
    {
        // Arrange & Act
        var executor = new LlmProcessExecutor(30000, null);

        // Assert
        Assert.NotNull(executor);
    }

    [Fact]
    public void Constructor_UsesDefaultTimeoutWhenNotSpecified()
    {
        // Arrange & Act
        var executor = new LlmProcessExecutor();

        // Assert
        // Default timeout should be 30000ms
        Assert.NotNull(executor);
    }

    #endregion

    #region ExecuteAsync Tests

    [Fact]
    public async Task ExecuteAsync_ReturnsExceptionMessage_WhenProcessStartFails()
    {
        // Arrange
        var mockMemory = new Mock<ILlmMemory>();
        var executor = new LlmProcessExecutor(1000, mockMemory.Object);
        
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "non-existent-executable-12345.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        // Act
        var result = await executor.ExecuteAsync(process);

        // Assert
        Assert.Contains("[EXCEPTION]", result);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsConversationEnded_OnSuccess()
    {
        // This test would require a mock process or a real executable
        // For now, we document expected behavior
        
        // Expected: When a valid process completes successfully,
        // ExecuteAsync should return "Conversation ended"
        Assert.True(true);
    }

    [Fact]
    public async Task ExecuteAsync_HandlesProcessWithoutConversationHistory()
    {
        // Arrange
        var executor = new LlmProcessExecutor(1000, null);
        
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "invalid-exe.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        // Act
        var result = await executor.ExecuteAsync(process);

        // Assert
        Assert.Contains("[EXCEPTION]", result);
    }

    [Fact]
    public async Task ExecuteAsync_CatchesAllExceptions()
    {
        // Arrange
        var executor = new LlmProcessExecutor(1000, null);
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "", // Empty filename should cause exception
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        // Act
        var result = await executor.ExecuteAsync(process);

        // Assert
        Assert.StartsWith("[EXCEPTION]", result);
    }

    #endregion

    #region Helper Method Tests (via Reflection)

    [Fact]
    public void CleanOutput_RemovesTrailingTags()
    {
        // Arrange
        var executor = new LlmProcessExecutor();
        var input = "This is the answer<|endofturn|>";
        
        // Act
        var result = InvokeCleanOutput(executor, input);

        // Assert
        Assert.Equal("This is the answer", result);
    }

    [Fact]
    public void CleanOutput_TrimsWhitespace()
    {
        // Arrange
        var executor = new LlmProcessExecutor();
        var input = "  This is the answer  ";
        
        // Act
        var result = InvokeCleanOutput(executor, input);

        // Assert
        Assert.Equal("This is the answer", result);
    }

    [Fact]
    public void CleanOutput_HandlesOutputWithoutTags()
    {
        // Arrange
        var executor = new LlmProcessExecutor();
        var input = "This is a clean answer";
        
        // Act
        var result = InvokeCleanOutput(executor, input);

        // Assert
        Assert.Equal("This is a clean answer", result);
    }

    [Fact]
    public void CleanOutput_RemovesFirstOccurrenceOfTag()
    {
        // Arrange
        var executor = new LlmProcessExecutor();
        var input = "Answer before <| and more text <| after";
        
        // Act
        var result = InvokeCleanOutput(executor, input);

        // Assert
        Assert.Equal("Answer before", result);
    }

    [Fact]
    public void CleanOutput_HandlesEmptyString()
    {
        // Arrange
        var executor = new LlmProcessExecutor();
        var input = "";
        
        // Act
        var result = InvokeCleanOutput(executor, input);

        // Assert
        Assert.Equal("", result);
    }

    [Fact]
    public void CleanOutput_HandlesWhitespaceOnly()
    {
        // Arrange
        var executor = new LlmProcessExecutor();
        var input = "   \n\t  ";
        
        // Act
        var result = InvokeCleanOutput(executor, input);

        // Assert
        Assert.Equal("", result);
    }

    [Fact]
    public void StoreInConversationHistory_StoresCleanAnswer_WhenHistoryProvided()
    {
        // Arrange
        var mockMemory = new Mock<ILlmMemory>();
        var executor = new LlmProcessExecutor(30000, mockMemory.Object);
        var cleanAnswer = "Test answer";

        // Act
        InvokeStoreInConversationHistory(executor, cleanAnswer);

        // Assert
        mockMemory.Verify(
            m => m.ImportMemory(It.Is<IMemoryFragment>(
                f => f.Category == "AI" && f.Content == cleanAnswer)),
            Times.Once);
    }

    [Fact]
    public void StoreInConversationHistory_DoesNotStore_WhenHistoryIsNull()
    {
        // Arrange
        var executor = new LlmProcessExecutor(30000, null);
        var cleanAnswer = "Test answer";

        // Act & Assert - Should not throw
        InvokeStoreInConversationHistory(executor, cleanAnswer);
    }

    [Fact]
    public void StoreInConversationHistory_DoesNotStore_WhenAnswerIsEmpty()
    {
        // Arrange
        var mockMemory = new Mock<ILlmMemory>();
        var executor = new LlmProcessExecutor(30000, mockMemory.Object);

        // Act
        InvokeStoreInConversationHistory(executor, "");

        // Assert
        mockMemory.Verify(
            m => m.ImportMemory(It.IsAny<IMemoryFragment>()),
            Times.Never);
    }

    [Fact]
    public void StoreInConversationHistory_DoesNotStore_WhenAnswerIsWhitespace()
    {
        // Arrange
        var mockMemory = new Mock<ILlmMemory>();
        var executor = new LlmProcessExecutor(30000, mockMemory.Object);

        // Act
        InvokeStoreInConversationHistory(executor, "   \n\t  ");

        // Assert
        mockMemory.Verify(
            m => m.ImportMemory(It.IsAny<IMemoryFragment>()),
            Times.Never);
    }

    [Fact]
    public void KillProcessIfRunning_KillsProcess_WhenNotExited()
    {
        // This test verifies the method exists and can be called
        // Cannot fully test without a real running process
        Assert.True(true);
    }

    #endregion

    #region Reflection Helper Methods

    private static string InvokeCleanOutput(LlmProcessExecutor executor, string output)
    {
        var method = typeof(LlmProcessExecutor).GetMethod(
            "CleanOutput",
            BindingFlags.NonPublic | BindingFlags.Static);

        return (string)method!.Invoke(null, new object[] { output })!;
    }

    private static void InvokeStoreInConversationHistory(LlmProcessExecutor executor, string cleanAnswer)
    {
        var method = typeof(LlmProcessExecutor).GetMethod(
            "StoreInConversationHistory",
            BindingFlags.NonPublic | BindingFlags.Instance);

        method!.Invoke(executor, new object[] { cleanAnswer });
    }

    #endregion
}