using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Services;
using MemoryLibrary.Models;

namespace OfflineAI.Tests.Services;

/// <summary>
/// Integration tests for AiChatService that test end-to-end scenarios
/// These tests may require more setup and may run slower than unit tests
/// </summary>
public class AiChatServiceIntegrationTests
{
    [Fact]
    public async Task SendMessageStreamAsync_WithSimpleMemory_BuildsCorrectPrompt()
    {
        // Arrange
        var memory = new SimpleTestMemory();
        memory.ImportMemory(new MemoryFragment("Rules", "Roll two dice to attack."));
        memory.ImportMemory(new MemoryFragment("Movement", "Move up to 3 spaces."));

        var conversationMemory = new SimpleTestMemory();

        var service = new TestableAiChatService(
            memory,
            conversationMemory,
            "invalid-exe-for-testing",
            "invalid-model",
            1000);

        // Act
        var prompt = await service.BuildSystemPromptAsyncPublic("How do I attack?");

        // Assert
        Assert.Contains("Roll two dice to attack", prompt);
        Assert.Contains("Move up to 3 spaces", prompt);
    }

    [Fact]
    public async Task SendMessageStreamAsync_WithVectorMemory_PerformsSemanticSearch()
    {
        // Arrange
        var embeddingService = new LocalLlmEmbeddingService("mock", "mock", 384);
        var vectorMemory = new VectorMemory(embeddingService, "test");

        vectorMemory.ImportMemory(new MemoryFragment("Combat", "Roll dice when attacking enemies."));
        vectorMemory.ImportMemory(new MemoryFragment("Movement", "Players move through rooms."));
        vectorMemory.ImportMemory(new MemoryFragment("Items", "Collect treasure cards."));

        var conversationMemory = new SimpleTestMemory();

        var service = new TestableAiChatService(
            vectorMemory,
            conversationMemory,
            "invalid-exe",
            "invalid-model",
            1000);

        // Act
        var prompt = await service.BuildSystemPromptAsyncPublic("How do I fight?");

        // Assert
        // Should include relevance scores from vector search
        Assert.Contains("Relevance:", prompt);
        // Should prioritize combat-related content
        Assert.Contains("attacking", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendMessageStreamAsync_MaintainsConversationHistory()
    {
        // Arrange
        var memory = new SimpleTestMemory();
        memory.ImportMemory(new MemoryFragment("Rules", "Game rules here."));

        var conversationMemory = new SimpleTestMemory();

        var service = new TestableAiChatService(
            memory,
            conversationMemory,
            "invalid-exe",
            "invalid-model",
            1000);

        // Act - First question
        try
        {
            await service.SendMessageStreamAsync("What are the rules?");
        }
        catch
        {
            // Expected to fail without real LLM
        }

        // Assert - Question stored in conversation history
        Assert.Contains("User: What are the rules?", conversationMemory.ToString());

        // Act - Second question
        var prompt = await service.BuildSystemPromptAsyncPublic("Can you elaborate?");

        // Assert - Previous question included in context
        Assert.Contains("Recent conversation:", prompt);
        Assert.Contains("What are the rules?", prompt);
    }

    [Fact]
    public async Task SendMessageStreamAsync_HandlesEmptyMemory()
    {
        // Arrange
        var memory = new SimpleTestMemory();
        var conversationMemory = new SimpleTestMemory();

        var service = new TestableAiChatService(
            memory,
            conversationMemory,
            "invalid-exe",
            "invalid-model",
            1000);

        // Act
        var prompt = await service.BuildSystemPromptAsyncPublic("test question");

        // Assert
        Assert.Contains("You are a helpful AI assistant", prompt);
        Assert.Contains("Context:", prompt);
        Assert.DoesNotContain("Recent conversation:", prompt);
    }

    [Fact]
    public async Task BuildSystemPrompt_VectorSearchWithNoResults_FallsBackToToString()
    {
        // Arrange
        var embeddingService = new LocalLlmEmbeddingService("mock", "mock", 384);
        var vectorMemory = new VectorMemory(embeddingService, "test");

        vectorMemory.ImportMemory(new MemoryFragment("Unrelated", "Gardening tips."));

        var conversationMemory = new SimpleTestMemory();

        var service = new TestableAiChatService(
            vectorMemory,
            conversationMemory,
            "invalid-exe",
            "invalid-model",
            1000);

        // Act
        var prompt = await service.BuildSystemPromptAsyncPublic("space exploration");

        // Assert
        // With very low relevance threshold (0.1), we should still get results
        Assert.Contains("Context:", prompt);
    }

    [Fact]
    public async Task SendMessageStreamAsync_WithMultipleQuestions_BuildsConversationContext()
    {
        // Arrange
        var memory = new SimpleTestMemory();
        memory.ImportMemory(new MemoryFragment("Rules", "Game rules."));

        var conversationMemory = new SimpleTestMemory();

        var service = new TestableAiChatService(
            memory,
            conversationMemory,
            "invalid-exe",
            "invalid-model",
            1000);

        // Act - Simulate multiple exchanges
        conversationMemory.ImportMemory(new MemoryFragment("User", "Question 1"));
        conversationMemory.ImportMemory(new MemoryFragment("AI", "Answer 1"));
        conversationMemory.ImportMemory(new MemoryFragment("User", "Question 2"));
        conversationMemory.ImportMemory(new MemoryFragment("AI", "Answer 2"));

        var prompt = await service.BuildSystemPromptAsyncPublic("Question 3");

        // Assert
        Assert.Contains("Recent conversation:", prompt);
        Assert.Contains("Question 1", prompt);
        Assert.Contains("Answer 1", prompt);
        Assert.Contains("Question 2", prompt);
        Assert.Contains("Answer 2", prompt);
    }

    [Fact]
    public async Task BuildSystemPrompt_WithVectorMemory_RespectsTopKParameter()
    {
        // Arrange
        var embeddingService = new LocalLlmEmbeddingService("mock", "mock", 384);
        var vectorMemory = new VectorMemory(embeddingService, "test");

        // Add more than 5 fragments
        for (int i = 0; i < 10; i++)
        {
            vectorMemory.ImportMemory(new MemoryFragment($"Rule {i}", $"Content about dice rolling {i}."));
        }

        var conversationMemory = new SimpleTestMemory();

        var service = new TestableAiChatService(
            vectorMemory,
            conversationMemory,
            "invalid-exe",
            "invalid-model",
            1000);

        // Act
        var prompt = await service.BuildSystemPromptAsyncPublic("dice rolling");

        // Assert
        // The code uses topK: 5, so we should get at most 5 results
        var relevanceCount = CountOccurrences(prompt, "Relevance:");
        Assert.True(relevanceCount <= 5, $"Expected at most 5 results, got {relevanceCount}");
    }

    [Fact]
    public async Task BuildSystemPrompt_WithVectorMemory_RespectsMinRelevanceScore()
    {
        // Arrange
        var embeddingService = new LocalLlmEmbeddingService("mock", "mock", 384);
        var vectorMemory = new VectorMemory(embeddingService, "test");

        vectorMemory.ImportMemory(new MemoryFragment("Topic A", "Very relevant: dice rolling combat"));
        vectorMemory.ImportMemory(new MemoryFragment("Topic B", "Completely unrelated: gardening tips"));

        var conversationMemory = new SimpleTestMemory();

        var service = new TestableAiChatService(
            vectorMemory,
            conversationMemory,
            "invalid-exe",
            "invalid-model",
            1000);

        // Act
        var prompt = await service.BuildSystemPromptAsyncPublic("dice rolling");

        // Assert
        // Should include at least the relevant fragment
        Assert.Contains("combat", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendMessageStreamAsync_WithSpecialCharacters_PreservesContent()
    {
        // Arrange
        var memory = new SimpleTestMemory();
        memory.ImportMemory(new MemoryFragment("Rules", "Use \"quotes\" and <tags> in content."));

        var conversationMemory = new SimpleTestMemory();

        var service = new TestableAiChatService(
            memory,
            conversationMemory,
            "invalid-exe",
            "invalid-model",
            1000);

        // Act
        var prompt = await service.BuildSystemPromptAsyncPublic("test");

        // Assert
        Assert.Contains("\"quotes\"", prompt);
        Assert.Contains("<tags>", prompt);
    }

    [Fact]
    public async Task SendMessageStreamAsync_WithUnicodeCharacters_PreservesContent()
    {
        // Arrange
        var memory = new SimpleTestMemory();
        memory.ImportMemory(new MemoryFragment("Rules", "International: café, naïve, ???"));

        var conversationMemory = new SimpleTestMemory();

        var service = new TestableAiChatService(
            memory,
            conversationMemory,
            "invalid-exe",
            "invalid-model",
            1000);

        // Act
        var prompt = await service.BuildSystemPromptAsyncPublic("test");

        // Assert
        Assert.Contains("café", prompt);
        Assert.Contains("naïve", prompt);
        Assert.Contains("???", prompt);
    }

    [Fact]
    public async Task BuildSystemPrompt_StructureIsCorrect()
    {
        // Arrange
        var memory = new SimpleTestMemory();
        memory.ImportMemory(new MemoryFragment("Rules", "Memory content"));

        var conversationMemory = new SimpleTestMemory();
        conversationMemory.ImportMemory(new MemoryFragment("User", "Previous question"));

        var service = new TestableAiChatService(
            memory,
            conversationMemory,
            "invalid-exe",
            "invalid-model",
            1000);

        // Act
        var prompt = await service.BuildSystemPromptAsyncPublic("current question");

        // Assert - Verify the prompt structure
        var lines = prompt.Split('\n');
        
        // Should have base prompt
        Assert.Contains(lines, l => l.Contains("You are a helpful AI assistant"));
        
        // Should have context section
        Assert.Contains(lines, l => l.Contains("Context:"));
        
        // Should have conversation history section
        Assert.Contains(lines, l => l.Contains("Recent conversation:"));
    }

    [Fact]
    public void Constructor_AllowsCustomTimeout()
    {
        // Arrange & Act
        var memory = new SimpleTestMemory();
        var conversationMemory = new SimpleTestMemory();

        var service1 = new AiChatService(memory, conversationMemory, "exe", "model", 5000);
        var service2 = new AiChatService(memory, conversationMemory, "exe", "model", 60000);

        // Assert
        Assert.NotNull(service1);
        Assert.NotNull(service2);
    }

    [Fact]
    public async Task SendMessageStreamAsync_WithEmptyConversationMemory_DoesNotAddExtraNewlines()
    {
        // Arrange
        var memory = new SimpleTestMemory();
        memory.ImportMemory(new MemoryFragment("Rules", "Content"));

        var conversationMemory = new SimpleTestMemory();

        var service = new TestableAiChatService(
            memory,
            conversationMemory,
            "invalid-exe",
            "invalid-model",
            1000);

        // Act
        var prompt = await service.BuildSystemPromptAsyncPublic("test");

        // Assert - Should not have excessive newlines
        Assert.DoesNotContain("\n\n\n\n", prompt);
    }

    private int CountOccurrences(string text, string substring)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(substring, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += substring.Length;
        }
        return count;
    }
}

/// <summary>
/// Simple test implementation of ILlmMemory for testing purposes
/// </summary>
public class SimpleTestMemory : ILlmMemory
{
    private readonly List<IMemoryFragment> _fragments = new();

    public void ImportMemory(IMemoryFragment section)
    {
        _fragments.Add(section);
    }

    public override string ToString()
    {
        if (_fragments.Count == 0)
            return string.Empty;

        return string.Join(Environment.NewLine, _fragments.Select(f => f.ToString()));
    }

    public int Count => _fragments.Count;
}
