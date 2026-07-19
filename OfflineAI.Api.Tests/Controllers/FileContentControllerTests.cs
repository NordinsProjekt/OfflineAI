using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using OfflineAI.Api.Controllers;
using OfflineAI.Api.Models;
using AgentKit.Skills.Files;
using Xunit;

namespace OfflineAI.Api.Tests.Controllers;

/// <summary>
/// Unit tests for FileContentController. Covers uploading workspace files and extracting text.
/// </summary>
public class FileContentControllerTests
{
    private readonly Mock<IFileAgentService> _mockFileAgentService;

    public FileContentControllerTests()
    {
        _mockFileAgentService = new Mock<IFileAgentService>();
    }

    private FileContentController CreateController() => new(_mockFileAgentService.Object);

    [Fact]
    public async Task UploadFile_EmptyFile_ReturnsBadRequest()
    {
        var controller = CreateController();
        var emptyFile = new FormFile(Stream.Null, 0, 0, "file", "empty.txt");

        var result = await controller.UploadFile(emptyFile);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
        var error = Assert.IsType<ErrorResponse>(badRequestResult.Value);
        Assert.Equal(400, error.StatusCode);
    }

    [Fact]
    public async Task UploadFile_Valid_SavesViaFileAgentAndReturnsOk()
    {
        var content = "sample pdf bytes"u8.ToArray();
        using var stream = new MemoryStream(content);
        var formFile = new FormFile(stream, 0, content.Length, "file", "report.pdf");

        _mockFileAgentService
            .Setup(s => s.SaveUploadedFileAsync("report.pdf", It.IsAny<Stream>()))
            .ReturnsAsync(FileAgentResult.Success(FileAgentResultType.FileCreated, "Saved report.pdf"));

        var controller = CreateController();

        var result = await controller.UploadFile(formFile);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<UploadFileResponse>(okResult.Value);
        Assert.Equal("report.pdf", response.Filename);
    }

    [Fact]
    public async Task GetFileText_TxtFile_UsesReadFileRawAsync()
    {
        _mockFileAgentService
            .Setup(s => s.ReadFileRawAsync("notes.txt"))
            .ReturnsAsync(FileAgentResult.ReadSuccess("ok", "file contents"));

        var controller = CreateController();

        var result = await controller.GetFileText("notes.txt");

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<FileTextResponse>(okResult.Value);
        Assert.Equal("file contents", response.Text);

        _mockFileAgentService.Verify(s => s.ReadPdfFileAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetFileText_PdfFile_UsesReadPdfFileAsync()
    {
        _mockFileAgentService
            .Setup(s => s.ReadPdfFileAsync("report.pdf"))
            .ReturnsAsync(FileAgentResult.ReadSuccess("ok", "--- Page 1 ---\ncontent"));

        var controller = CreateController();

        var result = await controller.GetFileText("report.pdf");

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<FileTextResponse>(okResult.Value);
        Assert.Contains("Page 1", response.Text);

        _mockFileAgentService.Verify(s => s.ReadFileRawAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetFileText_MissingFile_ReturnsNotFound()
    {
        _mockFileAgentService
            .Setup(s => s.ReadFileRawAsync("missing.txt"))
            .ReturnsAsync(FileAgentResult.Failure("File not found"));

        var controller = CreateController();

        var result = await controller.GetFileText("missing.txt");

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }
}
