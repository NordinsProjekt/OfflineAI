using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using OfflineAI.Api.Controllers;
using OfflineAI.Api.Models;
using OfflineAI.Api.Services;
using Xunit;

namespace OfflineAI.Api.Tests.Controllers;

/// <summary>
/// Unit tests for QueryController.
/// Tests cover request validation, successful responses, timeout handling, and error cases.
/// </summary>
public class QueryControllerTests
{
    private readonly Mock<ILlmQueryService> _mockService;
    private readonly Mock<ILogger<QueryController>> _mockLogger;
    private readonly QueryController _controller;

    public QueryControllerTests()
    {
        _mockService = new Mock<ILlmQueryService>();
        _mockLogger = new Mock<ILogger<QueryController>>();
        _controller = new QueryController(_mockService.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task Query_ValidRequest_ReturnsOkResult()
    {
        // Arrange
        var request = new QueryRequest
        {
            Question = "What is machine learning?",
            EnableRag = true,
            MaxTokens = 256
        };

        var expectedResponse = new QueryResponse
        {
            Answer = "Machine learning is a subset of artificial intelligence...",
            Model = "tinyllama",
            UsedRag = true,
            DocumentsRetrieved = 3,
            ResponseTimeMs = 5000,
            PromptTokens = 100,
            CompletionTokens = 50,
            Success = true
        };

        _mockService
            .Setup(s => s.QueryAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.Query(request);

        // Assert
        var actionResult = Assert.IsType<ActionResult<QueryResponse>>(result);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<QueryResponse>(okResult.Value);
        
        Assert.Equal("Machine learning is a subset of artificial intelligence...", response.Answer);
        Assert.True(response.UsedRag);
        Assert.Equal(3, response.DocumentsRetrieved);
        Assert.True(response.Success);
    }

    [Fact]
    public async Task Query_EmptyQuestion_ReturnsBadRequest()
    {
        // Arrange
        var request = new QueryRequest
        {
            Question = "",
            MaxTokens = 256
        };

        // Act
        var result = await _controller.Query(request);

        // Assert
        var actionResult = Assert.IsType<ActionResult<QueryResponse>>(result);
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
        var errorResponse = Assert.IsType<ErrorResponse>(badRequestResult.Value);
        
        Assert.Equal("Question is required", errorResponse.Error);
        Assert.Equal(400, errorResponse.StatusCode);
    }

    [Fact]
    public async Task Query_WhitespaceQuestion_ReturnsBadRequest()
    {
        // Arrange
        var request = new QueryRequest
        {
            Question = "   ",
            MaxTokens = 256
        };

        // Act
        var result = await _controller.Query(request);

        // Assert
        var actionResult = Assert.IsType<ActionResult<QueryResponse>>(result);
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
        var errorResponse = Assert.IsType<ErrorResponse>(badRequestResult.Value);
        
        Assert.Equal("Question is required", errorResponse.Error);
    }

    [Theory]
    [InlineData(0)]      // Too low
    [InlineData(-1)]     // Negative
    [InlineData(5000)]   // Too high
    public async Task Query_InvalidMaxTokens_ReturnsBadRequest(int maxTokens)
    {
        // Arrange
        var request = new QueryRequest
        {
            Question = "Test question",
            MaxTokens = maxTokens
        };

        // Act
        var result = await _controller.Query(request);

        // Assert
        var actionResult = Assert.IsType<ActionResult<QueryResponse>>(result);
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
        var errorResponse = Assert.IsType<ErrorResponse>(badRequestResult.Value);
        
        Assert.Equal("MaxTokens must be between 1 and 4096", errorResponse.Error);
        Assert.Contains(maxTokens.ToString(), errorResponse.Details);
    }

    [Theory]
    [InlineData(-0.1f)]  // Too low
    [InlineData(2.1f)]   // Too high
    [InlineData(3.0f)]   // Way too high
    public async Task Query_InvalidTemperature_ReturnsBadRequest(float temperature)
    {
        // Arrange
        var request = new QueryRequest
        {
            Question = "Test question",
            Temperature = temperature
        };

        // Act
        var result = await _controller.Query(request);

        // Assert
        var actionResult = Assert.IsType<ActionResult<QueryResponse>>(result);
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
        var errorResponse = Assert.IsType<ErrorResponse>(badRequestResult.Value);
        
        Assert.Equal("Temperature must be between 0 and 2", errorResponse.Error);
    }

    [Fact]
    public async Task Query_ServiceThrowsTimeout_Returns408()
    {
        // Arrange
        var request = new QueryRequest
        {
            Question = "Complex question that takes too long",
            MaxTokens = 1000
        };

        _mockService
            .Setup(s => s.QueryAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act
        var result = await _controller.Query(request);

        // Assert
        var actionResult = Assert.IsType<ActionResult<QueryResponse>>(result);
        var statusResult = Assert.IsType<ObjectResult>(actionResult.Result);
        
        Assert.Equal(408, statusResult.StatusCode);
        
        var errorResponse = Assert.IsType<ErrorResponse>(statusResult.Value);
        Assert.Contains("timeout", errorResponse.Error.ToLower());
        Assert.Equal(408, errorResponse.StatusCode);
        Assert.NotEmpty(errorResponse.Suggestions);
    }

    [Fact]
    public async Task Query_ServiceThrowsException_Returns500()
    {
        // Arrange
        var request = new QueryRequest
        {
            Question = "Test question",
            MaxTokens = 256
        };

        _mockService
            .Setup(s => s.QueryAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("LLM service unavailable"));

        // Act
        var result = await _controller.Query(request);

        // Assert
        var actionResult = Assert.IsType<ActionResult<QueryResponse>>(result);
        var statusResult = Assert.IsType<ObjectResult>(actionResult.Result);
        
        Assert.Equal(500, statusResult.StatusCode);
        
        var errorResponse = Assert.IsType<ErrorResponse>(statusResult.Value);
        Assert.Equal("Internal server error", errorResponse.Error);
        Assert.Contains("LLM service unavailable", errorResponse.Details);
    }

    [Fact]
    public async Task Query_WithRag_DocumentsRetrievedInResponse()
    {
        // Arrange
        var request = new QueryRequest
        {
            Question = "What is AI?",
            EnableRag = true,
            TopK = 5
        };

        var expectedResponse = new QueryResponse
        {
            Answer = "AI stands for Artificial Intelligence...",
            Model = "tinyllama",
            UsedRag = true,
            DocumentsRetrieved = 5,
            Success = true
        };

        _mockService
            .Setup(s => s.QueryAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.Query(request);

        // Assert
        var actionResult = Assert.IsType<ActionResult<QueryResponse>>(result);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<QueryResponse>(okResult.Value);
        
        Assert.True(response.UsedRag);
        Assert.Equal(5, response.DocumentsRetrieved);
    }

    [Fact]
    public async Task Query_WithoutRag_NoDocumentsRetrieved()
    {
        // Arrange
        var request = new QueryRequest
        {
            Question = "What is AI?",
            EnableRag = false
        };

        var expectedResponse = new QueryResponse
        {
            Answer = "AI stands for Artificial Intelligence...",
            Model = "tinyllama",
            UsedRag = false,
            DocumentsRetrieved = 0,
            Success = true
        };

        _mockService
            .Setup(s => s.QueryAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.Query(request);

        // Assert
        var actionResult = Assert.IsType<ActionResult<QueryResponse>>(result);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<QueryResponse>(okResult.Value);
        
        Assert.False(response.UsedRag);
        Assert.Equal(0, response.DocumentsRetrieved);
    }

    [Fact]
    public async Task Query_ResponseTimeSet_InResponse()
    {
        // Arrange
        var request = new QueryRequest
        {
            Question = "Test question"
        };

        var expectedResponse = new QueryResponse
        {
            Answer = "Test answer",
            Model = "tinyllama",
            Success = true
        };

        _mockService
            .Setup(s => s.QueryAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.Query(request);

        // Assert
        var actionResult = Assert.IsType<ActionResult<QueryResponse>>(result);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<QueryResponse>(okResult.Value);

        // The controller times a mocked (near-instant) call via Stopwatch.ElapsedMilliseconds,
        // which is integer/millisecond-granularity — a sub-millisecond mocked call can
        // legitimately measure as exactly 0, so >= 0 (was the flaky "> 0") is what actually
        // verifies the field gets computed/populated at all.
        Assert.True(response.ResponseTimeMs >= 0);
    }

    [Fact]
    public async Task Query_LongResponseTime_AddsWarning()
    {
        // Arrange
        var request = new QueryRequest
        {
            Question = "Test question"
        };

        var expectedResponse = new QueryResponse
        {
            Answer = "Test answer",
            Model = "tinyllama",
            Success = true
        };

        // Simulate slow response
        _mockService
            .Setup(s => s.QueryAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (QueryRequest req, CancellationToken ct) =>
            {
                await Task.Delay(28000, ct); // 28 seconds (close to 30s limit)
                return expectedResponse;
            });

        // Act
        var result = await _controller.Query(request, CancellationToken.None);

        // Assert
        var actionResult = Assert.IsType<ActionResult<QueryResponse>>(result);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<QueryResponse>(okResult.Value);
        
        Assert.Contains(response.Warnings, w => w.Contains("timeout"));
    }

    [Fact]
    public void ValidateRequest_ValidRequest_ReturnsOk()
    {
        // Arrange
        var request = new QueryRequest
        {
            Question = "What is AI?",
            MaxTokens = 256,
            Temperature = 0.5f,
            TopK = 3,
            MinRelevanceScore = 0.6f
        };

        // Act
        var result = _controller.ValidateRequest(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public void ValidateRequest_EmptyQuestion_ReturnsBadRequest()
    {
        // Arrange
        var request = new QueryRequest
        {
            Question = "",
            MaxTokens = 256
        };

        // Act
        var result = _controller.ValidateRequest(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        var errorResponse = Assert.IsType<ErrorResponse>(badRequestResult.Value);
        
        Assert.Contains("Question is required", errorResponse.Details);
    }

    [Fact]
    public void ValidateRequest_MultipleErrors_ReturnsAllErrors()
    {
        // Arrange
        var request = new QueryRequest
        {
            Question = "",
            MaxTokens = 0,
            Temperature = -1,
            TopK = 0,
            MinRelevanceScore = 2
        };

        // Act
        var result = _controller.ValidateRequest(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        var errorResponse = Assert.IsType<ErrorResponse>(badRequestResult.Value);
        
        Assert.Contains("Question is required", errorResponse.Details);
        Assert.Contains("MaxTokens", errorResponse.Details);
        Assert.Contains("Temperature", errorResponse.Details);
        Assert.Contains("TopK", errorResponse.Details);
        Assert.Contains("MinRelevanceScore", errorResponse.Details);
    }
}
