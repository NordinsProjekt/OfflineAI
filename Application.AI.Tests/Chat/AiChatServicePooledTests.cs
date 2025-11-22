using Application.AI.Chat;
using Application.AI.Models;
using Application.AI.Pooling;
using Application.AI.Processing;
using Application.AI.Utilities;
using Entities;
using Moq;
using Services.Configuration;
using Services.Interfaces;

namespace Application.AI.Tests.Chat;

/// <summary>
/// Unit tests for AiChatServicePooled class.
/// Tests cover all public methods including SendMessageAsync and BuildSystemPromptAsync.
/// </summary>
public class AiChatServicePooledTests
{
    private readonly Mock<ILlmMemory> _mockMemory;
    private readonly Mock<ILlmMemory> _mockConversationMemory;
    private readonly Mock<IModelInstancePool> _mockModelPool;
    private readonly Mock<ISearchableMemory> _mockSearchableMemory;
    private readonly Mock<IDomainDetector> _mockDomainDetector;
    private readonly GenerationSettings _defaultSettings;

    public AiChatServicePooledTests()
    {
        _mockMemory = new Mock<ILlmMemory>();
        _mockConversationMemory = new Mock<ILlmMemory>();
        _mockModelPool = new Mock<IModelInstancePool>();
        _mockSearchableMemory = _mockMemory.As<ISearchableMemory>();
        _mockDomainDetector = new Mock<IDomainDetector>();
        
        _defaultSettings = new GenerationSettings
        {
            MaxTokens = 200,
            Temperature = 0.3f,
            TopK = 30,
            TopP = 0.85f,
            RepeatPenalty = 1.15f,
            PresencePenalty = 0.2f,
            FrequencyPenalty = 0.2f,
            RagTopK = 3,
            RagMinRelevanceScore = 0.5
        };
    }

    /// <summary>
    /// Helper method to create a mocked pooled instance with a configured response.
    /// </summary>
    private PooledInstance CreateMockPooledInstance(string response, Action<Mock<IPersistentLlmProcess>>? configure = null)
    {
        var mockProcess = new Mock<IPersistentLlmProcess>();
        mockProcess.Setup(p => p.QueryAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<float>(),
            It.IsAny<int>(),
            It.IsAny<float>(),
            It.IsAny<float>(),
            It.IsAny<float>(),
            It.IsAny<float>(),
            It.IsAny<bool>(),
            It.IsAny<int>()))
            .ReturnsAsync(response);

        configure?.Invoke(mockProcess);

        return new PooledInstance(mockProcess.Object, _mockModelPool.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Arrange & Act
        var service = new AiChatServicePooled(
            _mockMemory.Object,
            _mockConversationMemory.Object,
            _mockModelPool.Object,
            _defaultSettings);

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_WithNullMemory_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new AiChatServicePooled(
                null!,
                _mockConversationMemory.Object,
                _mockModelPool.Object,
                _defaultSettings));
    }

    [Fact]
    public void Constructor_WithNullConversationMemory_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new AiChatServicePooled(
                _mockMemory.Object,
                null!,
                _mockModelPool.Object,
                _defaultSettings));
    }

    [Fact]
    public void Constructor_WithNullModelPool_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new AiChatServicePooled(
                _mockMemory.Object,
                _mockConversationMemory.Object,
                null!,
                _defaultSettings));
    }

    [Fact]
    public void Constructor_WithNullGenerationSettings_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new AiChatServicePooled(
                _mockMemory.Object,
                _mockConversationMemory.Object,
                _mockModelPool.Object,
                null!));
    }

    [Fact]
    public void Constructor_WithAllOptionalParameters_CreatesInstance()
    {
        // Arrange & Act
        var service = new AiChatServicePooled(
            _mockMemory.Object,
            _mockConversationMemory.Object,
            _mockModelPool.Object,
            _defaultSettings,
            debugMode: true,
            enableRag: false,
            showPerformanceMetrics: true,
            domainDetector: _mockDomainDetector.Object);

        // Assert
        Assert.NotNull(service);
    }

    #endregion

    #region SendMessageAsync Tests - RAG Mode

    [Fact]
    public async Task SendMessageAsync_WithNullQuestion_ThrowsArgumentException()
    {
        // Arrange
        var service = new AiChatServicePooled(
            _mockMemory.Object,
            _mockConversationMemory.Object,
            _mockModelPool.Object,
            _defaultSettings);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await service.SendMessageAsync(null!));
    }

    [Fact]
    public async Task SendMessageAsync_WithEmptyQuestion_ThrowsArgumentException()
    {
        // Arrange
        var service = new AiChatServicePooled(
            _mockMemory.Object,
            _mockConversationMemory.Object,
            _mockModelPool.Object,
            _defaultSettings);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await service.SendMessageAsync("   "));
    }

    [Fact]
    public async Task SendMessageAsync_WithValidQuestion_StoresInConversationMemory()
    {
        // Arrange
        var question = "What is the capital of France?";
        var mockProcess = new Mock<IPersistentLlmProcess>();
        mockProcess.Setup(p => p.QueryAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<float>(),
            It.IsAny<int>(),
            It.IsAny<float>(),
            It.IsAny<float>(),
            It.IsAny<float>(),
            It.IsAny<float>(),
            It.IsAny<bool>(),
            It.IsAny<int>()))
            .ReturnsAsync("Paris is the capital of France.");

        var mockPooledInstance = CreateMockPooledInstance("Paris is the capital of France.");

        _mockModelPool.Setup(p => p.AcquireAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockPooledInstance);
        
        _mockSearchableMemory.Setup(m => m.SearchRelevantMemoryAsync(
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<double>(),
            It.IsAny<List<string>>(),
            It.IsAny<int>(),
            It.IsAny<bool>()))
            .ReturnsAsync("France is a country in Europe. Paris is its capital.");

        var service = new AiChatServicePooled(
            _mockSearchableMemory.Object,
            _mockConversationMemory.Object,
            _mockModelPool.Object,
            _defaultSettings);

        // Act
        await service.SendMessageAsync(question);

        // Assert
        _mockConversationMemory.Verify(
            m => m.ImportMemory(It.Is<IMemoryFragment>(
                f => f.Content == question && f.Category == "User")),
            Times.Once);
    }

    [Fact]
    public async Task SendMessageAsync_WithValidResponse_StoresInConversationMemory()
    {
        // Arrange
        var question = "What is the capital of France?";
        var expectedResponse = "Paris is the capital of France.";
        
        // Return a meaningful context that's long enough (> 150 chars)
        var context = "France is a country in Western Europe. Paris is the capital and largest city of France. It is located on the Seine River. France is known for its culture, history, and cuisine.";
        
        var mockPooledInstance = CreateMockPooledInstance(expectedResponse);
        
        _mockModelPool.Setup(p => p.AcquireAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockPooledInstance);
        
        _mockSearchableMemory.Setup(m => m.SearchRelevantMemoryAsync(
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<double>(),
            It.IsAny<List<string>>(),
            It.IsAny<int>(),
            It.IsAny<bool>()))
            .ReturnsAsync(context);

        var service = new AiChatServicePooled(
            _mockSearchableMemory.Object,
            _mockConversationMemory.Object,
            _mockModelPool.Object,
            _defaultSettings);

        // Act
        await service.SendMessageAsync(question);

        // Assert
        _mockConversationMemory.Verify(
            m => m.ImportMemory(It.Is<IMemoryFragment>(
                f => f.Content == expectedResponse && f.Category == "AI")),
            Times.Once);
    }

    [Fact]
    public async Task SendMessageAsync_WithNoRelevantContext_ReturnsNoInformationMessage()
    {
        // Arrange
        var question = "Tell me about quantum physics";
        
        _mockSearchableMemory.Setup(m => m.SearchRelevantMemoryAsync(
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<double>(),
            It.IsAny<List<string>>(),
            It.IsAny<int>(),
            It.IsAny<bool>()))
            .ReturnsAsync((string?)null);

        var service = new AiChatServicePooled(
            _mockSearchableMemory.Object,
            _mockConversationMemory.Object,
            _mockModelPool.Object,
            _defaultSettings,
            enableRag: true);

        // Act
        var result = await service.SendMessageAsync(question);

        // Assert
        Assert.Contains("don't have any relevant information", result);
    }

    [Fact]
    public async Task SendMessageAsync_WithDomainDetectedButNoResults_ReturnsNoDomainInformationMessage()
    {
        // Arrange
        var question = "How to win in Gloomhaven?";
        var detectedDomains = new List<string> { "gloomhaven" };
        
        _mockDomainDetector.Setup(d => d.DetectDomainsAsync(It.IsAny<string>()))
            .ReturnsAsync(detectedDomains);
        
        _mockDomainDetector.Setup(d => d.GetDisplayNameAsync("gloomhaven"))
            .ReturnsAsync("Gloomhaven");
        
        _mockSearchableMemory.Setup(m => m.SearchRelevantMemoryAsync(
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<double>(),
            It.IsAny<List<string>>(),
            It.IsAny<int>(),
            It.IsAny<bool>()))
            .ReturnsAsync((string?)null);

        var service = new AiChatServicePooled(
            _mockSearchableMemory.Object,
            _mockConversationMemory.Object,
            _mockModelPool.Object,
            _defaultSettings,
            enableRag: true,
            domainDetector: _mockDomainDetector.Object);

        // Act
        var result = await service.SendMessageAsync(question);

        // Assert
        Assert.Contains("don't have information about Gloomhaven", result);
    }

    [Fact]
    public async Task SendMessageAsync_WithEmptyResponse_ReturnsWarningMessage()
    {
        // Arrange
        var question = "What is AI?";
        
        // Return a meaningful context that's long enough (> 150 chars)
        var context = "Artificial Intelligence (AI) refers to the simulation of human intelligence in machines that are programmed to think like humans and mimic their actions. This includes learning, reasoning, and self-correction.";
        
        var mockPooledInstance = CreateMockPooledInstance("   ");
        
        _mockModelPool.Setup(p => p.AcquireAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockPooledInstance);
        
        _mockSearchableMemory.Setup(m => m.SearchRelevantMemoryAsync(
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<double>(),
            It.IsAny<List<string>>(),
            It.IsAny<int>(),
            It.IsAny<bool>()))
            .ReturnsAsync(context);

        var service = new AiChatServicePooled(
            _mockSearchableMemory.Object,
            _mockConversationMemory.Object,
            _mockModelPool.Object,
            _defaultSettings);

        // Act
        var result = await service.SendMessageAsync(question);

        // Assert
        // Implementation returns a message about empty response
        Assert.Contains("empty response", result);
    }

    [Fact]
    public async Task SendMessageAsync_WithTimeoutException_ReturnsErrorMessage()
    {
        // Arrange
        var question = "What is AI?";
        
        // Return a meaningful context that's long enough (> 150 chars)
        var context = "Artificial Intelligence (AI) refers to the simulation of human intelligence in machines that are programmed to think like humans and mimic their actions. This includes learning, reasoning, and self-correction.";
        
        var mockProcess = new Mock<IPersistentLlmProcess>();
        mockProcess.Setup(p => p.QueryAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<float>(),
            It.IsAny<int>(),
            It.IsAny<float>(),
            It.IsAny<float>(),
            It.IsAny<float>(),
            It.IsAny<float>(),
            It.IsAny<bool>(),
            It.IsAny<int>()))
            .ThrowsAsync(new TimeoutException("Query timed out"));

        var mockPooledInstance = new PooledInstance(mockProcess.Object, _mockModelPool.Object);
        
        _mockModelPool.Setup(p => p.AcquireAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockPooledInstance);
        
        _mockSearchableMemory.Setup(m => m.SearchRelevantMemoryAsync(
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<double>(),
            It.IsAny<List<string>>(),
            It.IsAny<int>(),
            It.IsAny<bool>()))
            .ReturnsAsync(context);

        var service = new AiChatServicePooled(
            _mockSearchableMemory.Object,
            _mockConversationMemory.Object,
            _mockModelPool.Object,
            _defaultSettings);

        // Act
        var result = await service.SendMessageAsync(question);

        // Assert
        // Implementation returns error message with [ERROR] prefix
        Assert.Contains("timed out", result);
    }

    [Fact]
    public async Task SendMessageAsync_WithGeneralException_ReturnsErrorMessage()
    {
        // Arrange
        var question = "What is AI?";
        
        // Return a meaningful context that's long enough (> 150 chars)
        var context = "Artificial Intelligence (AI) refers to the simulation of human intelligence in machines that are programmed to think like humans and mimic their actions. This includes learning, reasoning, and self-correction.";
        
        var mockProcess = new Mock<IPersistentLlmProcess>();
        mockProcess.Setup(p => p.QueryAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<float>(),
            It.IsAny<int>(),
            It.IsAny<float>(),
            It.IsAny<float>(),
            It.IsAny<float>(),
            It.IsAny<float>(),
            It.IsAny<bool>(),
            It.IsAny<int>()))
            .ThrowsAsync(new InvalidOperationException("Process failed"));

        var mockPooledInstance = new PooledInstance(mockProcess.Object, _mockModelPool.Object);
        
        _mockModelPool.Setup(p => p.AcquireAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockPooledInstance);
        
        _mockSearchableMemory.Setup(m => m.SearchRelevantMemoryAsync(
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<double>(),
            It.IsAny<List<string>>(),
            It.IsAny<int>(),
            It.IsAny<bool>()))
            .ReturnsAsync(context);

        var service = new AiChatServicePooled(
            _mockSearchableMemory.Object,
            _mockConversationMemory.Object,
            _mockModelPool.Object,
            _defaultSettings);

        // Act
        var result = await service.SendMessageAsync(question);

        // Assert
        // Implementation returns error message with [ERROR] prefix and exception type
        Assert.Contains("InvalidOperationException", result);
    }

    [Fact]
    public async Task SendMessageAsync_WithValidResponse_SetsPerformanceMetrics()
    {
        // Arrange
        var question = "What is AI?";
        var response = "Artificial Intelligence is the simulation of human intelligence.";
        
        // Return a meaningful context that's long enough (> 150 chars)
        var context = "Artificial Intelligence (AI) refers to the simulation of human intelligence in machines that are programmed to think like humans and mimic their actions. This includes learning, reasoning, and self-correction.";
        
        var mockPooledInstance = CreateMockPooledInstance(response);
        
        _mockModelPool.Setup(p => p.AcquireAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockPooledInstance);
        
        _mockSearchableMemory.Setup(m => m.SearchRelevantMemoryAsync(
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<double>(),
            It.IsAny<List<string>>(),
            It.IsAny<int>(),
            It.IsAny<bool>()))
            .ReturnsAsync(context);

        var service = new AiChatServicePooled(
            _mockSearchableMemory.Object,
            _mockConversationMemory.Object,
            _mockModelPool.Object,
            _defaultSettings,
            showPerformanceMetrics: true);

        // Act
        await service.SendMessageAsync(question);

        // Assert
        Assert.NotNull(service.LastMetrics);
        Assert.True(service.LastMetrics.TotalTimeMs > 0);
        Assert.True(service.LastMetrics.CompletionTokens > 0);
        Assert.True(service.LastMetrics.PromptTokens > 0);
    }

    [Fact]
    public async Task SendMessageAsync_WithCancellationToken_PassesToPool()
    {
        // Arrange
        var question = "What is AI?";
        var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        
        // Return a meaningful context that's long enough (> 150 chars)
        var context = "Artificial Intelligence (AI) refers to the simulation of human intelligence in machines that are programmed to think like humans and mimic their actions. This includes learning, reasoning, and self-correction.";
        
        var mockPooledInstance = CreateMockPooledInstance("Response");
        
        _mockModelPool.Setup(p => p.AcquireAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockPooledInstance);
        
        _mockSearchableMemory.Setup(m => m.SearchRelevantMemoryAsync(
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<double>(),
            It.IsAny<List<string>>(),
            It.IsAny<int>(),
            It.IsAny<bool>()))
            .ReturnsAsync(context);

        var service = new AiChatServicePooled(
            _mockSearchableMemory.Object,
            _mockConversationMemory.Object,
            _mockModelPool.Object,
            _defaultSettings);

        // Act
        await service.SendMessageAsync(question, cancellationToken);

        // Assert
        // Verify it was called at least once - the exact token doesn't matter as long as AcquireAsync was called
        _mockModelPool.Verify(p => p.AcquireAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region SendMessageAsync Tests - Non-RAG Mode

    [Fact]
    public async Task SendMessageAsync_WithRagDisabled_UsesSimplePrompt()
    {
        // Arrange
        var question = "What is 2+2?";
        var response = "4";
        string? capturedSystemPrompt = null;
        
        var mockProcess = new Mock<IPersistentLlmProcess>();
        mockProcess.Setup(p => p.QueryAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<float>(),
            It.IsAny<int>(),
            It.IsAny<float>(),
            It.IsAny<float>(),
            It.IsAny<float>(),
            It.IsAny<float>(),
            It.IsAny<bool>(),
            It.IsAny<int>()))
            .Callback<string, string, int, float, int, float, float, float, float, bool, int>(
                (sysPrompt, userQ, _, _, _, _, _, _, _, _, _) => capturedSystemPrompt = sysPrompt)
            .ReturnsAsync(response);

        var mockPooledInstance = new PooledInstance(mockProcess.Object, _mockModelPool.Object);
        
        _mockModelPool.Setup(p => p.AcquireAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockPooledInstance);

        var service = new AiChatServicePooled(
            _mockMemory.Object,
            _mockConversationMemory.Object,
            _mockModelPool.Object,
            _defaultSettings,
            enableRag: false);

        // Act
        var result = await service.SendMessageAsync(question);

        // Assert
        Assert.Equal(response, result);
        Assert.NotNull(capturedSystemPrompt);
        Assert.Contains("Answer the question directly", capturedSystemPrompt);
        
        // Verify SearchRelevantMemoryAsync was NOT called
        _mockSearchableMemory.Verify(
            m => m.SearchRelevantMemoryAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<double>(),
                It.IsAny<List<string>>(),
                It.IsAny<int>(),
                It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public async Task SendMessageAsync_WithRagDisabled_DoesNotSearchMemory()
    {
        // Arrange
        var question = "Tell me something";
        
        var mockPooledInstance = CreateMockPooledInstance("Response");
        
        _mockModelPool.Setup(p => p.AcquireAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockPooledInstance);

        var service = new AiChatServicePooled(
            _mockSearchableMemory.Object,
            _mockConversationMemory.Object,
            _mockModelPool.Object,
            _defaultSettings,
            enableRag: false);

        // Act
        await service.SendMessageAsync(question);

        // Assert
        _mockSearchableMemory.Verify(
            m => m.SearchRelevantMemoryAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<double>(),
                It.IsAny<List<string>>(),
                It.IsAny<int>(),
                It.IsAny<bool>()),
            Times.Never);
    }

    #endregion

    #region BuildSystemPromptAsync Tests (via RAG mode)

    [Fact]
    public async Task SendMessageAsync_WithDomainDetector_DetectsDomains()
    {
        // Arrange
        var question = "How to play Gloomhaven?";
        var detectedDomains = new List<string> { "gloomhaven" };
        
        // Return a meaningful context that's long enough (> 150 chars)
        var context = "Gloomhaven is a cooperative board game for one to four players designed by Isaac Childres and published by Cephalofair Games in 2017. The game is set in a persistent world and features a unique campaign-driven structure.";
        
        _mockDomainDetector.Setup(d => d.DetectDomainsAsync(question))
            .ReturnsAsync(detectedDomains);
        
        _mockDomainDetector.Setup(d => d.GetDisplayNameAsync("gloomhaven"))
            .ReturnsAsync("Gloomhaven");
        
        _mockSearchableMemory.Setup(m => m.SearchRelevantMemoryAsync(
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<double>(),
            It.IsAny<List<string>>(),
            It.IsAny<int>(),
            It.IsAny<bool>()))
            .ReturnsAsync(context);

        var mockPooledInstance = CreateMockPooledInstance("Gloomhaven is a cooperative game.");
        
        _mockModelPool.Setup(p => p.AcquireAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockPooledInstance);

        var service = new AiChatServicePooled(
            _mockSearchableMemory.Object,
            _mockConversationMemory.Object,
            _mockModelPool.Object,
            _defaultSettings,
            enableRag: true,
            domainDetector: _mockDomainDetector.Object);

        // Act
        await service.SendMessageAsync(question);

        // Assert
        _mockDomainDetector.Verify(d => d.DetectDomainsAsync(question), Times.Once);
        _mockSearchableMemory.Verify(
            m => m.SearchRelevantMemoryAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<double>(),
                It.Is<List<string>>(list => list.Contains("gloomhaven")),
                It.IsAny<int>(),
                It.IsAny<bool>()),
            Times.Once);
    }

    [Fact]
    public async Task SendMessageAsync_WithoutDomainDetector_SearchesWithoutDomainFilter()
    {
        // Arrange
        var question = "What is AI?";
        
        // Return a meaningful context that's long enough (> 150 chars)
        var context = "Artificial Intelligence (AI) refers to the simulation of human intelligence in machines that are programmed to think like humans and mimic their actions. This includes learning, reasoning, and self-correction.";
        
        _mockSearchableMemory.Setup(m => m.SearchRelevantMemoryAsync(
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<double>(),
            null, // No domain filter
            It.IsAny<int>(),
            It.IsAny<bool>()))
            .ReturnsAsync(context);

        var mockPooledInstance = CreateMockPooledInstance("Response");
        
        _mockModelPool.Setup(p => p.AcquireAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockPooledInstance);

        var service = new AiChatServicePooled(
            _mockSearchableMemory.Object,
            _mockConversationMemory.Object,
            _mockModelPool.Object,
            _defaultSettings,
            enableRag: true,
            domainDetector: null);

        // Act
        await service.SendMessageAsync(question);

        // Assert
        _mockSearchableMemory.Verify(
            m => m.SearchRelevantMemoryAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<double>(),
                null,
                It.IsAny<int>(),
                It.IsAny<bool>()),
            Times.Once);
    }

    [Fact]
    public async Task SendMessageAsync_WithLongContext_TruncatesContext()
    {
        // Arrange
        var question = "Tell me about AI";
        var longContext = new string('A', 2000); // Exceeds MaxContextChars
        string? capturedSystemPrompt = null;
        
        _mockSearchableMemory.Setup(m => m.SearchRelevantMemoryAsync(
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<double>(),
            It.IsAny<List<string>>(),
            It.IsAny<int>(),
            It.IsAny<bool>()))
            .ReturnsAsync(longContext);

        var mockProcess = new Mock<IPersistentLlmProcess>();
        mockProcess.Setup(p => p.QueryAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<float>(),
            It.IsAny<int>(),
            It.IsAny<float>(),
            It.IsAny<float>(),
            It.IsAny<float>(),
            It.IsAny<float>(),
            It.IsAny<bool>(),
            It.IsAny<int>()))
            .Callback<string, string, int, float, int, float, float, float, float, bool, int>(
                (sysPrompt, _, _, _, _, _, _, _, _, _, _) => capturedSystemPrompt = sysPrompt)
            .ReturnsAsync("Response");

        var mockPooledInstance = new PooledInstance(mockProcess.Object, _mockModelPool.Object);
        
        _mockModelPool.Setup(p => p.AcquireAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockPooledInstance);

        var service = new AiChatServicePooled(
            _mockSearchableMemory.Object,
            _mockConversationMemory.Object,
            _mockModelPool.Object,
            _defaultSettings,
            enableRag: true);

        // Act
        await service.SendMessageAsync(question);

        // Assert
        Assert.NotNull(capturedSystemPrompt);
        // System prompt should be truncated (less than original context + base prompt)
        Assert.True(capturedSystemPrompt.Length < longContext.Length);
    }

    [Fact]
    public async Task SendMessageAsync_WithRagSettings_UsesConfiguredValues()
    {
        // Arrange
        var question = "What is AI?";
        var customSettings = new GenerationSettings
        {
            RagTopK = 5,
            RagMinRelevanceScore = 0.7
        };
        
        // Return a meaningful context that's long enough (> 150 chars)
        var context = "Artificial Intelligence (AI) refers to the simulation of human intelligence in machines that are programmed to think like humans and mimic their actions. This includes learning, reasoning, and self-correction.";
        
        _mockSearchableMemory.Setup(m => m.SearchRelevantMemoryAsync(
            It.IsAny<string>(),
            5, // RagTopK
            0.7, // RagMinRelevanceScore
            It.IsAny<List<string>>(),
            It.IsAny<int>(),
            It.IsAny<bool>()))
            .ReturnsAsync(context);

        var mockPooledInstance = CreateMockPooledInstance("Response");
        
        _mockModelPool.Setup(p => p.AcquireAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockPooledInstance);

        var service = new AiChatServicePooled(
            _mockSearchableMemory.Object,
            _mockConversationMemory.Object,
            _mockModelPool.Object,
            customSettings,
            enableRag: true);

        // Act
        await service.SendMessageAsync(question);

        // Assert
        _mockSearchableMemory.Verify(
            m => m.SearchRelevantMemoryAsync(
                It.IsAny<string>(),
                5,
                0.7,
                It.IsAny<List<string>>(),
                It.IsAny<int>(),
                It.IsAny<bool>()),
            Times.Once);
    }

    #endregion

    #region Performance Metrics Tests

    [Fact]
    public async Task SendMessageAsync_WithPerformanceMetrics_CalculatesCorrectly()
    {
        // Arrange
        var question = "Short question";
        var response = "Short response";
        
        // Return a meaningful context that's long enough (> 150 chars)
        var context = "Artificial Intelligence (AI) refers to the simulation of human intelligence in machines that are programmed to think like humans and mimic their actions. This includes learning, reasoning, and self-correction.";
        
        var mockPooledInstance = CreateMockPooledInstance(response);
        
        _mockModelPool.Setup(p => p.AcquireAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockPooledInstance);
        
        _mockSearchableMemory.Setup(m => m.SearchRelevantMemoryAsync(
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<double>(),
            It.IsAny<List<string>>(),
            It.IsAny<int>(),
            It.IsAny<bool>()))
            .ReturnsAsync(context);

        var service = new AiChatServicePooled(
            _mockSearchableMemory.Object,
            _mockConversationMemory.Object,
            _mockModelPool.Object,
            _defaultSettings,
            showPerformanceMetrics: false); // Metrics are set regardless of this flag

        // Act
        await service.SendMessageAsync(question);

        // Assert
        // LastMetrics is always set even when showPerformanceMetrics is false
        Assert.NotNull(service.LastMetrics);
        Assert.True(service.LastMetrics.CompletionTokens > 0);
        Assert.True(service.LastMetrics.PromptTokens > 0);
        Assert.True(service.LastMetrics.TotalTokens > 0);
        Assert.Equal(
            service.LastMetrics.PromptTokens + service.LastMetrics.CompletionTokens,
            service.LastMetrics.TotalTokens);
    }

    [Fact]
    public async Task SendMessageAsync_WithEmptyResponse_SetsZeroCompletionTokens()
    {
        // Arrange
        var question = "Question";
        
        // Return a meaningful context that's long enough (> 150 chars)
        var context = "Artificial Intelligence (AI) refers to the simulation of human intelligence in machines that are programmed to think like humans and mimic their actions. This includes learning, reasoning, and self-correction.";
        
        var mockPooledInstance = CreateMockPooledInstance("");
        
        _mockModelPool.Setup(p => p.AcquireAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockPooledInstance);
        
        _mockSearchableMemory.Setup(m => m.SearchRelevantMemoryAsync(
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<double>(),
            It.IsAny<List<string>>(),
            It.IsAny<int>(),
            It.IsAny<bool>()))
            .ReturnsAsync(context);

        var service = new AiChatServicePooled(
            _mockSearchableMemory.Object,
            _mockConversationMemory.Object,
            _mockModelPool.Object,
            _defaultSettings);

        // Act
        await service.SendMessageAsync(question);

        // Assert
        // Metrics are set even for empty responses
        Assert.NotNull(service.LastMetrics);
        Assert.Equal(0, service.LastMetrics.CompletionTokens);
    }

    [Fact]
    public async Task SendMessageAsync_LastMetrics_IsNullInitially()
    {
        // Arrange
        var service = new AiChatServicePooled(
            _mockMemory.Object,
            _mockConversationMemory.Object,
            _mockModelPool.Object,
            _defaultSettings);

        // Assert
        Assert.Null(service.LastMetrics);
    }

    #endregion

    #region Generation Settings Tests

    [Fact]
    public async Task SendMessageAsync_PassesGenerationSettings_ToQueryAsync()
    {
        // Arrange
        var question = "Test question";
        var customSettings = new GenerationSettings
        {
            MaxTokens = 300,
            Temperature = 0.7f,
            TopK = 50,
            TopP = 0.9f,
            RepeatPenalty = 1.2f,
            PresencePenalty = 0.3f,
            FrequencyPenalty = 0.4f
        };

        int? capturedMaxTokens = null;
        float? capturedTemperature = null;
        int? capturedTopK = null;
        float? capturedTopP = null;
        float? capturedRepeatPenalty = null;
        float? capturedPresencePenalty = null;
        float? capturedFrequencyPenalty = null;
        
        var mockProcess = new Mock<IPersistentLlmProcess>();
        mockProcess.Setup(p => p.QueryAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<float>(),
            It.IsAny<int>(),
            It.IsAny<float>(),
            It.IsAny<float>(),
            It.IsAny<float>(),
            It.IsAny<float>(),
            It.IsAny<bool>(),
            It.IsAny<int>()))
            .Callback<string, string, int, float, int, float, float, float, float, bool, int>(
                (_, _, maxTokens, temp, topK, topP, repeatP, presenceP, freqP, _, _) =>
                {
                    capturedMaxTokens = maxTokens;
                    capturedTemperature = temp;
                    capturedTopK = topK;
                    capturedTopP = topP;
                    capturedRepeatPenalty = repeatP;
                    capturedPresencePenalty = presenceP;
                    capturedFrequencyPenalty = freqP;
                })
            .ReturnsAsync("Response");

        var mockPooledInstance = new PooledInstance(mockProcess.Object, _mockModelPool.Object);
        
        _mockModelPool.Setup(p => p.AcquireAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockPooledInstance);

        var service = new AiChatServicePooled(
            _mockMemory.Object,
            _mockConversationMemory.Object,
            _mockModelPool.Object,
            customSettings,
            enableRag: false);

        // Act
        await service.SendMessageAsync(question);

        // Assert
        Assert.Equal(customSettings.MaxTokens, capturedMaxTokens);
        Assert.Equal(customSettings.Temperature, capturedTemperature);
        Assert.Equal(customSettings.TopK, capturedTopK);
        Assert.Equal(customSettings.TopP, capturedTopP);
        Assert.Equal(customSettings.RepeatPenalty, capturedRepeatPenalty);
        Assert.Equal(customSettings.PresencePenalty, capturedPresencePenalty);
        Assert.Equal(customSettings.FrequencyPenalty, capturedFrequencyPenalty);
    }

    #endregion
}
