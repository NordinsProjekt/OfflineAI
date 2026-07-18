using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.AI;
using Moq;
using Application.AI.Gemma4;
using OfflineAI.Api.Controllers;
using OfflineAI.Api.Models;
using Services.Memory;
using Services.Repositories;
using Services.Workspace;
using Xunit;

namespace OfflineAI.Api.Tests.Controllers;

/// <summary>
/// Unit tests for FilesRagController. Covers the "not configured" (503) paths for PDF ingestion
/// and image queries when the optional RAG/Gemma4 services aren't registered, plus the
/// filename/existence validation that runs before either dependency is touched.
/// </summary>
public sealed class FilesRagControllerTests : IDisposable
{
    private readonly Mock<IWorkspaceService> _mockWorkspaceService;
    private readonly Mock<ILogger<FilesRagController>> _mockLogger;
    private readonly string _tempWorkspacePath;

    public FilesRagControllerTests()
    {
        _mockWorkspaceService = new Mock<IWorkspaceService>();
        _mockLogger = new Mock<ILogger<FilesRagController>>();

        _tempWorkspacePath = Path.Combine(Path.GetTempPath(), "OfflineAI.Api.Tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempWorkspacePath);

        _mockWorkspaceService
            .Setup(s => s.GetActiveWorkspace())
            .Returns(new WorkspaceInfo("Standard", _tempWorkspacePath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempWorkspacePath))
        {
            Directory.Delete(_tempWorkspacePath, recursive: true);
        }
    }

    private FilesRagController CreateController(
        VectorMemoryPersistenceService? persistenceService = null,
        IGemma4CliService? gemma4CliService = null) =>
        new(_mockWorkspaceService.Object, _mockLogger.Object, persistenceService, gemma4CliService);

    /// <summary>
    /// A real VectorMemoryPersistenceService backed by mocked repository/embedding interfaces —
    /// enough to make IngestPdf treat RAG ingestion as "configured" so its own filename/existence
    /// validation (which runs before either dependency is touched) can be exercised.
    /// </summary>
    private static VectorMemoryPersistenceService CreateConfiguredPersistenceService() =>
        new(Mock.Of<IVectorMemoryRepository>(), Mock.Of<IEmbeddingGenerator<string, Embedding<float>>>());

    [Fact]
    public async Task IngestPdf_NoPersistenceService_Returns503()
    {
        var controller = CreateController(persistenceService: null);

        var result = await controller.IngestPdf("report.pdf");

        var statusResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(503, statusResult.StatusCode);
    }

    [Fact]
    public async Task IngestPdf_NonPdfExtension_ReturnsBadRequest()
    {
        var controller = CreateController(persistenceService: CreateConfiguredPersistenceService());

        var result = await controller.IngestPdf("notes.txt");

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
        var error = Assert.IsType<ErrorResponse>(badRequestResult.Value);
        Assert.Equal(400, error.StatusCode);
    }

    [Fact]
    public async Task IngestPdf_FileNotFoundInWorkspace_ReturnsNotFound()
    {
        var controller = CreateController(persistenceService: CreateConfiguredPersistenceService());

        var result = await controller.IngestPdf("missing.pdf");

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task AskAboutImage_NoGemma4Service_Returns503()
    {
        var controller = CreateController(gemma4CliService: null);

        var result = await controller.AskAboutImage("photo.jpg", new WorkspaceImageQuestionRequest { Question = "What is this?" }, CancellationToken.None);

        var statusResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(503, statusResult.StatusCode);
    }

    [Fact]
    public async Task AskAboutImage_FileNotFound_ReturnsNotFound()
    {
        var mockGemma4 = new Mock<IGemma4CliService>();
        var controller = CreateController(gemma4CliService: mockGemma4.Object);

        var result = await controller.AskAboutImage("missing.jpg", new WorkspaceImageQuestionRequest { Question = "What is this?" }, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task AskAboutImage_Valid_ReturnsAnswerFromGemma4()
    {
        var imagePath = Path.Combine(_tempWorkspacePath, "photo.jpg");
        await File.WriteAllBytesAsync(imagePath, new byte[] { 0xFF, 0xD8, 0xFF });

        var mockGemma4 = new Mock<IGemma4CliService>();
        mockGemma4.SetupGet(g => g.ModelName).Returns("gemma-4-4b");
        mockGemma4
            .Setup(g => g.ChatWithImageAsync("What is this?", imagePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync("A photo of a cat.");

        var controller = CreateController(gemma4CliService: mockGemma4.Object);

        var result = await controller.AskAboutImage("photo.jpg", new WorkspaceImageQuestionRequest { Question = "What is this?" }, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<QueryResponse>(okResult.Value);
        Assert.Equal("A photo of a cat.", response.Answer);
        Assert.Equal("gemma-4-4b", response.Model);
        Assert.False(response.UsedRag);
    }
}
