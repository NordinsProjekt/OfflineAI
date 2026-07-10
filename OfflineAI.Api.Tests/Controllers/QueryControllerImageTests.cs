using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Application.AI.Gemma4;
using OfflineAI.Api.Controllers;
using OfflineAI.Api.Models;
using Xunit;

namespace OfflineAI.Api.Tests.Controllers;

/// <summary>
/// Unit tests for ImageQueryController's picture/image endpoint (POST api/query/image), which
/// routes one-shot multimodal questions through the Gemma 4 CLI backend.
/// </summary>
public class QueryControllerImageTests
{
    private static IFormFile CreateImageFormFile(string contentType = "image/jpeg")
    {
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF };
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "image", "photo.jpg") { Headers = new HeaderDictionary(), ContentType = contentType };
    }

    [Fact]
    public async Task QueryImage_Gemma4NotConfigured_Returns503()
    {
        var controller = new ImageQueryController(gemma4CliService: null);

        var result = await controller.QueryImage(CreateImageFormFile(), "What is in this picture?");

        var statusResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(503, statusResult.StatusCode);
    }

    [Fact]
    public async Task QueryImage_EmptyQuestion_ReturnsBadRequest()
    {
        var mockGemma4 = new Mock<IGemma4CliService>();
        var controller = new ImageQueryController(mockGemma4.Object);

        var result = await controller.QueryImage(CreateImageFormFile(), "");

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
        var error = Assert.IsType<ErrorResponse>(badRequestResult.Value);
        Assert.Equal(400, error.StatusCode);
    }

    [Fact]
    public async Task QueryImage_EmptyImage_ReturnsBadRequest()
    {
        var mockGemma4 = new Mock<IGemma4CliService>();
        var controller = new ImageQueryController(mockGemma4.Object);
        var emptyImage = new FormFile(Stream.Null, 0, 0, "image", "empty.jpg");

        var result = await controller.QueryImage(emptyImage, "What is in this picture?");

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
        var error = Assert.IsType<ErrorResponse>(badRequestResult.Value);
        Assert.Equal(400, error.StatusCode);
    }

    [Fact]
    public async Task QueryImage_Valid_ReturnsAnswerFromGemma4()
    {
        var mockGemma4 = new Mock<IGemma4CliService>();
        mockGemma4.SetupGet(g => g.ModelName).Returns("gemma-4-4b");
        mockGemma4
            .Setup(g => g.ChatWithImageBytesAsync(
                "What is in this picture?",
                It.Is<ReadOnlyMemory<byte>>(b => b.Length == 3),
                "image/jpeg",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("A red, white, and blue flag.");

        var controller = new ImageQueryController(mockGemma4.Object);

        var result = await controller.QueryImage(CreateImageFormFile(), "What is in this picture?");

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<QueryResponse>(okResult.Value);
        Assert.Equal("A red, white, and blue flag.", response.Answer);
        Assert.Equal("gemma-4-4b", response.Model);
        Assert.False(response.UsedRag);
    }
}
