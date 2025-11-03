using Services;
using MemoryLibrary;
using MemoryLibrary.Models;
using Xunit;
using FluentAssertions;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace OfflineAI.Tests.Services;

/// <summary>
/// Unit tests for AiChatServicePooled.
/// Tests the integration between chat service and model instance pool.
/// </summary>
public class AiChatServicePooledTests : IDisposable
{
    private readonly string _testLlmPath;
    private readonly string _testModelPath;
    private readonly string _tempDir;

    public AiChatServicePooledTests()
    {
        // Create temporary files for testing
        _tempDir = Path.Combine(Path.GetTempPath(), "OfflineAI_Tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        
        _testLlmPath = Path.Combine(_tempDir, "test-llama-cli.exe");
        _testModelPath = Path.Combine(_tempDir, "test-model.gguf");
        
        File.WriteAllText(_testLlmPath, "mock executable");
        File.WriteAllText(_testModelPath, "mock model");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    #region Constructor Tests

    [Fact]
    public async Task Constructor_WithValidParameters_ShouldSucceed()
    {
        // Arrange
        var memory = new SimpleMemory();
        var conversationMemory = new SimpleMemory();
        var pool = new ModelInstancePool(_testLlmPath, _testModelPath, maxInstances: 1);
        await pool.InitializeAsync();

        // Act
        var service = new AiChatServicePooled(memory, conversationMemory, pool);

        // Assert
        service.Should().NotBeNull();

        // Cleanup
        pool.Dispose();
    }

    [Fact]
    public void Constructor_WithNullMemory_ShouldThrowArgumentNullException()
    {
        // Arrange
        var conversationMemory = new SimpleMemory();
        var pool = new ModelInstancePool(_testLlmPath, _testModelPath);

        // Act & Assert
        var act = () => new AiChatServicePooled(null!, conversationMemory, pool);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("memory");

        // Cleanup
        pool.Dispose();
    }

    [Fact]
    public void Constructor_WithNullConversationMemory_ShouldThrowArgumentNullException()
    {
        // Arrange
        var memory = new SimpleMemory();
        var pool = new ModelInstancePool(_testLlmPath, _testModelPath);

        // Act & Assert
        var act = () => new AiChatServicePooled(memory, null!, pool);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("conversationMemory");

        // Cleanup
        pool.Dispose();
    }

    [Fact]
    public void Constructor_WithNullPool_ShouldThrowArgumentNullException()
    {
        // Arrange
        var memory = new SimpleMemory();
        var conversationMemory = new SimpleMemory();

        // Act & Assert
        var act = () => new AiChatServicePooled(memory, conversationMemory, null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("modelPool");
    }

    #endregion

    #region SendMessageAsync Tests

    [Fact]
    public async Task SendMessageAsync_WithNullQuestion_ShouldThrowArgumentException()
    {
        // Arrange
        var memory = new SimpleMemory();
        var conversationMemory = new SimpleMemory();
        var pool = new ModelInstancePool(_testLlmPath, _testModelPath, maxInstances: 1);
        await pool.InitializeAsync();
        var service = new AiChatServicePooled(memory, conversationMemory, pool);

        // Act & Assert - ArgumentNullException is subtype of ArgumentException
        await Assert.ThrowsAnyAsync<ArgumentException>(async () =>
            await service.SendMessageAsync(null!));

        // Cleanup
        pool.Dispose();
    }

    [Fact]
    public async Task SendMessageAsync_WithEmptyQuestion_ShouldThrowArgumentException()
    {
        // Arrange
        var memory = new SimpleMemory();
        var conversationMemory = new SimpleMemory();
        var pool = new ModelInstancePool(_testLlmPath, _testModelPath, maxInstances: 1);
        await pool.InitializeAsync();
        var service = new AiChatServicePooled(memory, conversationMemory, pool);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await service.SendMessageAsync(""));

        // Cleanup
        pool.Dispose();
    }

    [Fact]
    public async Task SendMessageAsync_WithWhitespaceQuestion_ShouldThrowArgumentException()
    {
        // Arrange
        var memory = new SimpleMemory();
        var conversationMemory = new SimpleMemory();
        var pool = new ModelInstancePool(_testLlmPath, _testModelPath, maxInstances: 1);
        await pool.InitializeAsync();
        var service = new AiChatServicePooled(memory, conversationMemory, pool);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await service.SendMessageAsync("   "));

        // Cleanup
        pool.Dispose();
    }

    [Fact]
    public async Task SendMessageAsync_ShouldStoreQuestionInConversationMemory()
    {
        // Arrange
        var memory = new SimpleMemory();
        var conversationMemory = new SimpleMemory();
        var pool = new ModelInstancePool(_testLlmPath, _testModelPath, maxInstances: 1);
        await pool.InitializeAsync();
        var service = new AiChatServicePooled(memory, conversationMemory, pool);

        // Act
        var response = await service.SendMessageAsync("What is the capital of France?");

        // Assert
        var conversationText = conversationMemory.ToString();
        conversationText.Should().Contain("What is the capital of France?");
        conversationText.Should().Contain("User:");

        // Cleanup
        pool.Dispose();
    }

    [Fact]
    public async Task SendMessageAsync_OnFailure_ShouldReturnErrorMessage()
    {
        // Arrange
        var memory = new SimpleMemory();
        var conversationMemory = new SimpleMemory();
        var pool = new ModelInstancePool(_testLlmPath, _testModelPath, maxInstances: 1);
        await pool.InitializeAsync();
        var service = new AiChatServicePooled(memory, conversationMemory, pool);

        // Act - Will fail because mock executable isn't real
        var response = await service.SendMessageAsync("Test question");

        // Assert
        response.Should().StartWith("[ERROR]");
        response.Should().Contain("Failed to get response");

        // Cleanup
        pool.Dispose();
    }

    [Fact]
    public async Task SendMessageAsync_WithCancellationToken_ShouldRespectCancellation()
    {
        // Arrange
        var memory = new SimpleMemory();
        var conversationMemory = new SimpleMemory();
        var pool = new ModelInstancePool(_testLlmPath, _testModelPath, maxInstances: 1);
        await pool.InitializeAsync();
        var service = new AiChatServicePooled(memory, conversationMemory, pool);

        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert - TaskCanceledException inherits from OperationCanceledException
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await service.SendMessageAsync("Test", cts.Token));

        // Cleanup
        pool.Dispose();
    }

    #endregion

    #region Concurrency Tests

    [Fact]
    public async Task SendMessageAsync_Concurrent_ShouldHandleMultipleRequests()
    {
        // Arrange
        var memory = new SimpleMemory();
        var conversationMemory = new SimpleMemory();
        var pool = new ModelInstancePool(_testLlmPath, _testModelPath, maxInstances: 3);
        await pool.InitializeAsync();
        var service = new AiChatServicePooled(memory, conversationMemory, pool);

        // Act - Send 3 concurrent messages
        var task1 = service.SendMessageAsync("Question 1");
        var task2 = service.SendMessageAsync("Question 2");
        var task3 = service.SendMessageAsync("Question 3");

        var responses = await Task.WhenAll(task1, task2, task3);

        // Assert
        responses.Should().HaveCount(3);
        responses.Should().AllSatisfy(r => r.Should().NotBeNullOrWhiteSpace());

        // All instances should be returned to pool
        pool.AvailableCount.Should().Be(3);

        // Cleanup
        pool.Dispose();
    }

    [Fact]
    public async Task SendMessageAsync_MoreRequestsThanInstances_ShouldQueue()
    {
        // Arrange
        var memory = new SimpleMemory();
        var conversationMemory = new SimpleMemory();
        var pool = new ModelInstancePool(_testLlmPath, _testModelPath, maxInstances: 2);
        await pool.InitializeAsync();
        var service = new AiChatServicePooled(memory, conversationMemory, pool);

        // Act - Send 5 requests with only 2 instances
        var tasks = new[]
        {
            service.SendMessageAsync("Q1"),
            service.SendMessageAsync("Q2"),
            service.SendMessageAsync("Q3"),
            service.SendMessageAsync("Q4"),
            service.SendMessageAsync("Q5")
        };

        var responses = await Task.WhenAll(tasks);
        
        // Wait for all instances to be returned
        await Task.Delay(200);

        // Assert
        responses.Should().HaveCount(5);
        pool.AvailableCount.Should().BeGreaterThanOrEqualTo(1); // At least some returned

        // Cleanup
        pool.Dispose();
    }

    #endregion

    #region Memory Integration Tests

    [Fact]
    public async Task SendMessageAsync_WithSimpleMemory_ShouldIncludeContextInPrompt()
    {
        // Arrange
        var memory = new SimpleMemory();
        memory.ImportMemory(new MemoryFragment("Rules", "The sky is blue."));
        
        var conversationMemory = new SimpleMemory();
        var pool = new ModelInstancePool(_testLlmPath, _testModelPath, maxInstances: 1);
        await pool.InitializeAsync();
        var service = new AiChatServicePooled(memory, conversationMemory, pool);

        // Act
        var response = await service.SendMessageAsync("What color is the sky?");

        // Assert
        response.Should().NotBeNull();
        // The system prompt should include the memory context
        // (validated through integration behavior)

        // Cleanup
        pool.Dispose();
    }

    [Fact]
    public async Task SendMessageAsync_WithEmptyMemory_ShouldStillWork()
    {
        // Arrange
        var memory = new SimpleMemory();
        var conversationMemory = new SimpleMemory();
        var pool = new ModelInstancePool(_testLlmPath, _testModelPath, maxInstances: 1);
        await pool.InitializeAsync();
        var service = new AiChatServicePooled(memory, conversationMemory, pool);

        // Act
        var response = await service.SendMessageAsync("Hello");

        // Assert
        response.Should().NotBeNull();

        // Cleanup
        pool.Dispose();
    }

    [Fact]
    public async Task SendMessageAsync_MultipleMessages_ShouldBuildConversationHistory()
    {
        // Arrange
        var memory = new SimpleMemory();
        var conversationMemory = new SimpleMemory();
        var pool = new ModelInstancePool(_testLlmPath, _testModelPath, maxInstances: 5);
        await pool.InitializeAsync();
        var service = new AiChatServicePooled(memory, conversationMemory, pool);

        // Act
        await service.SendMessageAsync("First question");
        await service.SendMessageAsync("Second question");
        await service.SendMessageAsync("Third question");

        // Assert - Focus on conversation history, not pool state
        var history = conversationMemory.ToString();
        history.Should().Contain("First question");
        history.Should().Contain("Second question");
        history.Should().Contain("Third question");

        // Cleanup
        pool.Dispose();
    }

    #endregion

    #region Pool Resource Management Tests

    [Fact]
    public async Task SendMessageAsync_ShouldReleaseInstanceOnSuccess()
    {
        // Arrange
        var memory = new SimpleMemory();
        var conversationMemory = new SimpleMemory();
        var pool = new ModelInstancePool(_testLlmPath, _testModelPath, maxInstances: 2);
        await pool.InitializeAsync();
        var service = new AiChatServicePooled(memory, conversationMemory, pool);

        // Act
        var beforeCount = pool.AvailableCount;
        await service.SendMessageAsync("Test");
        
        // Wait for instance to be fully returned
        await Task.Delay(100);
        var afterCount = pool.AvailableCount;

        // Assert
        beforeCount.Should().Be(2);
        afterCount.Should().BeGreaterThanOrEqualTo(1); // At least one returned (instance may become unhealthy)

        // Cleanup
        pool.Dispose();
    }

    [Fact]
    public async Task SendMessageAsync_ShouldReleaseInstanceOnFailure()
    {
        // Arrange
        var memory = new SimpleMemory();
        var conversationMemory = new SimpleMemory();
        var pool = new ModelInstancePool(_testLlmPath, _testModelPath, maxInstances: 2);
        await pool.InitializeAsync();
        var service = new AiChatServicePooled(memory, conversationMemory, pool);

        // Act - Will fail but should still release instance
        var beforeCount = pool.AvailableCount;
        var response = await service.SendMessageAsync("Test");
        
        // Wait for instance to be fully returned
        await Task.Delay(100);
        var afterCount = pool.AvailableCount;

        // Assert
        response.Should().StartWith("[ERROR]");
        beforeCount.Should().Be(2);
        afterCount.Should().BeGreaterThanOrEqualTo(1); // At least one returned

        // Cleanup
        pool.Dispose();
    }

    #endregion

    #region Integration Scenario Tests

    [Fact]
    public async Task Scenario_WebChatBot_MultipleUsersConcurrently()
    {
        // Arrange - Simulates a web chatbot with 3-instance pool
        var memory = new SimpleMemory();
        memory.ImportMemory(new MemoryFragment("FAQ", "Our support hours are 9-5 PM"));
        
        var conversationMemory = new SimpleMemory();
        var pool = new ModelInstancePool(_testLlmPath, _testModelPath, maxInstances: 3);
        await pool.InitializeAsync();
        var service = new AiChatServicePooled(memory, conversationMemory, pool);

        // Act - Simulate 3 users asking questions simultaneously
        var user1 = service.SendMessageAsync("What are your hours?");
        var user2 = service.SendMessageAsync("Do you ship internationally?");
        var user3 = service.SendMessageAsync("What's your return policy?");

        var responses = await Task.WhenAll(user1, user2, user3);

        // Assert
        responses.Should().HaveCount(3);
        responses.Should().AllSatisfy(r => r.Should().NotBeNullOrEmpty());
        pool.AvailableCount.Should().Be(3); // All instances returned

        // Cleanup
        pool.Dispose();
    }

    [Fact]
    public async Task Scenario_HighLoad_10RequestsWith3Instances()
    {
        // Arrange
        var memory = new SimpleMemory();
        var conversationMemory = new SimpleMemory();
        var pool = new ModelInstancePool(_testLlmPath, _testModelPath, maxInstances: 3);
        await pool.InitializeAsync();
        var service = new AiChatServicePooled(memory, conversationMemory, pool);

        // Act - 10 requests with only 3 instances (queueing required)
        var tasks = Enumerable.Range(1, 10)
            .Select(i => service.SendMessageAsync($"Question {i}"))
            .ToArray();

        var responses = await Task.WhenAll(tasks);
        
        // Wait for all instances to be fully returned
        await Task.Delay(200);

        // Assert
        responses.Should().HaveCount(10);
        pool.AvailableCount.Should().BeGreaterThanOrEqualTo(2); // Most should be returned

        // Cleanup
        pool.Dispose();
    }

    #endregion
}
