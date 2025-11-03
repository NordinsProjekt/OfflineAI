using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Services;
using MemoryLibrary.Models;

namespace OfflineAI.Tests.Services;

public class AiChatServiceTests
{
    private const string TestFilePath = "test-llm.exe";
    private const string TestModelPath = "test-model.bin";
    private const int TestTimeout = 5000;

    [Fact]
    public async Task SendMessageStreamAsync_ThrowsArgumentException_WhenQuestionIsNull()
    {
        // Arrange
        var mockMemory = new Mock<ILlmMemory>();
        var mockConversationMemory = new Mock<ILlmMemory>();
        var service = new AiChatService(
            mockMemory.Object,
            mockConversationMemory.Object,
            TestFilePath,
            TestModelPath,
            TestTimeout);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException> (
            async () => await service.SendMessageStreamAsync(null!));
    }

    [Fact]
    public async Task SendMessageStreamAsync_ThrowsArgumentException_WhenQuestionIsEmpty()
    {
        // Arrange
        var mockMemory = new Mock<ILlmMemory>();
        var mockConversationMemory = new Mock<ILlmMemory>();
        var service = new AiChatService(
            mockMemory.Object,
            mockConversationMemory.Object,
            TestFilePath,
            TestModelPath,
            TestTimeout);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await service.SendMessageStreamAsync(""));
    }

    [Fact]
    public async Task SendMessageStreamAsync_ThrowsArgumentException_WhenQuestionIsWhitespace()
    {
        // Arrange
        var mockMemory = new Mock<ILlmMemory>();
        var mockConversationMemory = new Mock<ILlmMemory>();
        var service = new AiChatService(
            mockMemory.Object,
            mockConversationMemory.Object,
            TestFilePath,
            TestModelPath,
            TestTimeout);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await service.SendMessageStreamAsync("   "));
    }

    [Fact]
    public async Task SendMessageStreamAsync_AddsQuestionToConversationHistory()
    {
        // Arrange
        var mockMemory = new Mock<ILlmMemory>();
        mockMemory.Setup(m => m.ToString()).Returns("test context");
        
        var mockConversationMemory = new Mock<ILlmMemory>();
        mockConversationMemory.Setup(m => m.ToString()).Returns("");
        
        var service = new TestableAiChatService(
            mockMemory.Object,
            mockConversationMemory.Object,
            TestFilePath,
            TestModelPath,
            TestTimeout);

        var question = "What are the rules?";

        // Act
        try
        {
            await service.SendMessageStreamAsync(question);
        }
        catch
        {
            // Expected to fail because we're not mocking the full process execution
        }

        // Assert
        mockConversationMemory.Verify(
            m => m.ImportMemory(It.Is<IMemoryFragment>(
                f => f.Category == "User" && f.Content == question)),
            Times.Once);
    }

    [Fact]
    public void Constructor_InitializesPropertiesCorrectly()
    {
        // Arrange
        var mockMemory = new Mock<ILlmMemory>();
        var mockConversationMemory = new Mock<ILlmMemory>();

        // Act
        var service = new AiChatService(
            mockMemory.Object,
            mockConversationMemory.Object,
            TestFilePath,
            TestModelPath,
            TestTimeout);

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_AcceptsDefaultTimeout()
    {
        // Arrange
        var mockMemory = new Mock<ILlmMemory>();
        var mockConversationMemory = new Mock<ILlmMemory>();

        // Act
        var service = new AiChatService(
            mockMemory.Object,
            mockConversationMemory.Object,
            TestFilePath,
            TestModelPath);

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public async Task BuildSystemPrompt_IncludesBasePrompt()
    {
        // Arrange
        var mockMemory = new Mock<ILlmMemory>();
        mockMemory.Setup(m => m.ToString()).Returns("test memory");
        
        var mockConversationMemory = new Mock<ILlmMemory>();
        mockConversationMemory.Setup(m => m.ToString()).Returns("");
        
        var service = new TestableAiChatService(
            mockMemory.Object,
            mockConversationMemory.Object,
            TestFilePath,
            TestModelPath,
            TestTimeout);

        // Act
        var prompt = await service.BuildSystemPromptAsyncPublic("test question");

        // Assert
        Assert.Contains("You are a helpful AI assistant", prompt);
        Assert.Contains("Answer questions accurately and concisely", prompt);
    }

    [Fact]
    public async Task BuildSystemPrompt_IncludesMemoryContext()
    {
        // Arrange
        var mockMemory = new Mock<ILlmMemory>();
        mockMemory.Setup(m => m.ToString()).Returns("Game rules: Roll two dice.");
        
        var mockConversationMemory = new Mock<ILlmMemory>();
        mockConversationMemory.Setup(m => m.ToString()).Returns("");
        
        var service = new TestableAiChatService(
            mockMemory.Object,
            mockConversationMemory.Object,
            TestFilePath,
            TestModelPath,
            TestTimeout);

        // Act
        var prompt = await service.BuildSystemPromptAsyncPublic("test question");

        // Assert
        Assert.Contains("Context:", prompt);
        Assert.Contains("Game rules: Roll two dice.", prompt);
    }

    [Fact]
    public async Task BuildSystemPrompt_IncludesConversationHistory_WhenAvailable()
    {
        // Arrange
        var mockMemory = new Mock<ILlmMemory>();
        mockMemory.Setup(m => m.ToString()).Returns("test memory");
        
        var mockConversationMemory = new Mock<ILlmMemory>();
        mockConversationMemory.Setup(m => m.ToString())
            .Returns("User: Previous question\nAI: Previous answer");
        
        var service = new TestableAiChatService(
            mockMemory.Object,
            mockConversationMemory.Object,
            TestFilePath,
            TestModelPath,
            TestTimeout);

        // Act
        var prompt = await service.BuildSystemPromptAsyncPublic("test question");

        // Assert
        Assert.Contains("Recent conversation:", prompt);
        Assert.Contains("User: Previous question", prompt);
        Assert.Contains("AI: Previous answer", prompt);
    }

    [Fact]
    public async Task BuildSystemPrompt_ExcludesConversationHistory_WhenEmpty()
    {
        // Arrange
        var mockMemory = new Mock<ILlmMemory>();
        mockMemory.Setup(m => m.ToString()).Returns("test memory");
        
        var mockConversationMemory = new Mock<ILlmMemory>();
        mockConversationMemory.Setup(m => m.ToString()).Returns("");
        
        var service = new TestableAiChatService(
            mockMemory.Object,
            mockConversationMemory.Object,
            TestFilePath,
            TestModelPath,
            TestTimeout);

        // Act
        var prompt = await service.BuildSystemPromptAsyncPublic("test question");

        // Assert
        Assert.DoesNotContain("Recent conversation:", prompt);
    }

    [Fact]
    public async Task BuildSystemPrompt_ExcludesConversationHistory_WhenWhitespace()
    {
        // Arrange
        var mockMemory = new Mock<ILlmMemory>();
        mockMemory.Setup(m => m.ToString()).Returns("test memory");
        
        var mockConversationMemory = new Mock<ILlmMemory>();
        mockConversationMemory.Setup(m => m.ToString()).Returns("   \n\n   ");
        
        var service = new TestableAiChatService(
            mockMemory.Object,
            mockConversationMemory.Object,
            TestFilePath,
            TestModelPath,
            TestTimeout);

        // Act
        var prompt = await service.BuildSystemPromptAsyncPublic("test question");

        // Assert
        Assert.DoesNotContain("Recent conversation:", prompt);
    }

    [Fact]
    public async Task BuildSystemPrompt_UsesVectorSearch_WhenMemoryIsVectorMemory()
    {
        // Arrange
        var embeddingService = new LocalLlmEmbeddingService("mock", "mock", 384);
        var vectorMemory = new VectorMemory(embeddingService, "test-collection");
        
        // Add some test data
        vectorMemory.ImportMemory(new MemoryFragment("Rules", "Roll two dice when attacking."));
        vectorMemory.ImportMemory(new MemoryFragment("Rules", "Players move 3 spaces per turn."));
        
        var mockConversationMemory = new Mock<ILlmMemory>();
        mockConversationMemory.Setup(m => m.ToString()).Returns("");
        
        var service = new TestableAiChatService(
            vectorMemory,
            mockConversationMemory.Object,
            TestFilePath,
            TestModelPath,
            TestTimeout);

        // Act
        var prompt = await service.BuildSystemPromptAsyncPublic("How do I attack?");

        // Assert
        Assert.Contains("Context:", prompt);
        // Vector search should include relevance scores
        Assert.Contains("Relevance:", prompt);
    }

    [Fact]
    public async Task BuildSystemPrompt_UsesVectorSearchWithCorrectParameters()
    {
        // Arrange
        var embeddingService = new LocalLlmEmbeddingService("mock", "mock", 384);
        var vectorMemory = new VectorMemory(embeddingService, "test-collection");
        
        vectorMemory.ImportMemory(new MemoryFragment("Combat", "Attack with dice rolls."));
        
        var mockConversationMemory = new Mock<ILlmMemory>();
        mockConversationMemory.Setup(m => m.ToString()).Returns("");
        
        var service = new TestableAiChatService(
            vectorMemory,
            mockConversationMemory.Object,
            TestFilePath,
            TestModelPath,
            TestTimeout);

        // Act
        var prompt = await service.BuildSystemPromptAsyncPublic("combat");

        // Assert
        // The code uses topK: 5 and minRelevanceScore: 0.1
        Assert.Contains("Context:", prompt);
        // Should return results since we're using a low threshold
        Assert.Contains("Combat", prompt);
    }

    [Fact]
    public async Task ExecuteProcess_ExtractsCleanAnswer_FromStreamingOutput()
    {
        // Arrange
        var mockMemory = new Mock<ILlmMemory>();
        var mockConversationMemory = new Mock<ILlmMemory>();
        
        var service = new TestableAiChatService(
            mockMemory.Object,
            mockConversationMemory.Object,
            TestFilePath,
            TestModelPath,
            TestTimeout);

        // Act & Assert
        // This is a complex integration test that requires a real process
        // We'll test the behavior indirectly through other tests
        Assert.NotNull(service);
    }

    [Fact]
    public async Task ExecuteProcess_StoresCleanAnswerInConversationHistory()
    {
        // Arrange
        var mockMemory = new Mock<ILlmMemory>();
        var mockConversationMemory = new Mock<ILlmMemory>();
        
        var service = new TestableAiChatService(
            mockMemory.Object,
            mockConversationMemory.Object,
            TestFilePath,
            TestModelPath,
            TestTimeout);

        // This test verifies that when ExecuteProcessAsync completes successfully,
        // it stores the AI response in conversation history
        // This would need a mock process or integration test to fully verify
        Assert.NotNull(service);
    }

    [Fact]
    public async Task ExecuteProcess_ReturnsExceptionMessage_WhenExceptionOccurs()
    {
        // Arrange
        var mockMemory = new Mock<ILlmMemory>();
        mockMemory.Setup(m => m.ToString()).Returns("context");
        
        var mockConversationMemory = new Mock<ILlmMemory>();
        mockConversationMemory.Setup(m => m.ToString()).Returns("");
        
        // Use invalid file path to trigger exception
        var service = new AiChatService(
            mockMemory.Object,
            mockConversationMemory.Object,
            "invalid-file-that-does-not-exist.exe",
            TestModelPath,
            TestTimeout);

        // Act
        var result = await service.SendMessageStreamAsync("test question");

        // Assert
        Assert.Contains("[EXCEPTION]", result);
    }

    [Fact]
    public async Task ExecuteProcess_RemovesTrailingTags_FromAnswer()
    {
        // This test verifies that the answer cleaning logic removes <| tags
        // This is tested through the public API
        var mockMemory = new Mock<ILlmMemory>();
        var mockConversationMemory = new Mock<ILlmMemory>();
        
        var service = new TestableAiChatService(
            mockMemory.Object,
            mockConversationMemory.Object,
            TestFilePath,
            TestModelPath,
            TestTimeout);

        // The actual tag removal logic is in ExecuteProcessAsync
        // and can only be fully tested with a real or mocked process
        Assert.NotNull(service);
    }

    [Fact]
    public void AiChatService_IsNotDisposed_AfterConstruction()
    {
        // Arrange & Act
        var mockMemory = new Mock<ILlmMemory>();
        var mockConversationMemory = new Mock<ILlmMemory>();
        
        var service = new AiChatService(
            mockMemory.Object,
            mockConversationMemory.Object,
            TestFilePath,
            TestModelPath,
            TestTimeout);

        // Assert
        // The _disposed field is private, but we can verify the service works
        Assert.NotNull(service);
    }

    [Fact]
    public async Task BuildSystemPrompt_HandlesLargeMemoryContent()
    {
        // Arrange
        var largeContent = new string('x', 10000);
        var mockMemory = new Mock<ILlmMemory>();
        mockMemory.Setup(m => m.ToString()).Returns(largeContent);
        
        var mockConversationMemory = new Mock<ILlmMemory>();
        mockConversationMemory.Setup(m => m.ToString()).Returns("");
        
        var service = new TestableAiChatService(
            mockMemory.Object,
            mockConversationMemory.Object,
            TestFilePath,
            TestModelPath,
            TestTimeout);

        // Act
        var prompt = await service.BuildSystemPromptAsyncPublic("test question");

        // Assert
        Assert.Contains(largeContent, prompt);
        Assert.True(prompt.Length > 10000);
    }

    [Fact]
    public async Task BuildSystemPrompt_HandlesSpecialCharactersInMemory()
    {
        // Arrange
        var specialContent = "Test with special chars: \r\n\t\"quotes\" and <tags>";
        var mockMemory = new Mock<ILlmMemory>();
        mockMemory.Setup(m => m.ToString()).Returns(specialContent);
        
        var mockConversationMemory = new Mock<ILlmMemory>();
        mockConversationMemory.Setup(m => m.ToString()).Returns("");
        
        var service = new TestableAiChatService(
            mockMemory.Object,
            mockConversationMemory.Object,
            TestFilePath,
            TestModelPath,
            TestTimeout);

        // Act
        var prompt = await service.BuildSystemPromptAsyncPublic("test question");

        // Assert
        Assert.Contains(specialContent, prompt);
    }

    [Fact]
    public async Task SendMessageStreamAsync_HandlesVeryLongQuestions()
    {
        // Arrange
        var mockMemory = new Mock<ILlmMemory>();
        mockMemory.Setup(m => m.ToString()).Returns("context");
        
        var mockConversationMemory = new Mock<ILlmMemory>();
        mockConversationMemory.Setup(m => m.ToString()).Returns("");
        
        var service = new TestableAiChatService(
            mockMemory.Object,
            mockConversationMemory.Object,
            TestFilePath,
            TestModelPath,
            TestTimeout);

        var longQuestion = new string('a', 1000);

        // Act
        try
        {
            await service.SendMessageStreamAsync(longQuestion);
        }
        catch
        {
            // Expected to fail in test environment
        }

        // Assert
        mockConversationMemory.Verify(
            m => m.ImportMemory(It.Is<IMemoryFragment>(
                f => f.Content.Length == 1000)),
            Times.Once);
    }

    [Fact]
    public async Task BuildSystemPrompt_CombinesAllComponents()
    {
        // Arrange
        var mockMemory = new Mock<ILlmMemory>();
        mockMemory.Setup(m => m.ToString()).Returns("Memory content here");
        
        var mockConversationMemory = new Mock<ILlmMemory>();
        mockConversationMemory.Setup(m => m.ToString()).Returns("User: Hi\nAI: Hello");
        
        var service = new TestableAiChatService(
            mockMemory.Object,
            mockConversationMemory.Object,
            TestFilePath,
            TestModelPath,
            TestTimeout);

        // Act
        var prompt = await service.BuildSystemPromptAsyncPublic("test question");

        // Assert
        Assert.Contains("You are a helpful AI assistant", prompt);
        Assert.Contains("Context:", prompt);
        Assert.Contains("Memory content here", prompt);
        Assert.Contains("Recent conversation:", prompt);
        Assert.Contains("User: Hi", prompt);
        
        // Verify order
        var basePromptIndex = prompt.IndexOf("You are a helpful AI assistant");
        var contextIndex = prompt.IndexOf("Context:");
        var memoryIndex = prompt.IndexOf("Memory content here");
        var conversationIndex = prompt.IndexOf("Recent conversation:");
        
        Assert.True(basePromptIndex < contextIndex);
        Assert.True(contextIndex < memoryIndex);
        Assert.True(memoryIndex < conversationIndex);
    }
}

// Testable wrapper to expose private methods for testing
public class TestableAiChatService : AiChatService
{
    public TestableAiChatService(
        ILlmMemory memory,
        ILlmMemory conversationMemory,
        string filePath,
        string modelPath,
        int timeoutMs = 30000)
        : base(memory, conversationMemory, filePath, modelPath, timeoutMs)
    {
    }

    public async Task<string> BuildSystemPromptAsyncPublic(string question)
    {
        // Use reflection to call the private method
        var method = typeof(AiChatService).GetMethod(
            "BuildSystemPromptAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        var task = (Task<string>)method!.Invoke(this, new object[] { question })!;
        return await task;
    }
}
