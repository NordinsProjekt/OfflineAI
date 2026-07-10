using Microsoft.AspNetCore.Mvc;
using Moq;
using OfflineAI.Api.Controllers;
using OfflineAI.Api.Models;
using Services.Workspace;
using Xunit;

namespace OfflineAI.Api.Tests.Controllers;

/// <summary>
/// Unit tests for FilesController. Covers listing workspace files. Uploading/reading file
/// content is covered by <see cref="FileContentControllerTests"/>; PDF ingestion and image
/// queries are covered by <see cref="FilesRagControllerTests"/>.
/// </summary>
public sealed class FilesControllerTests : IDisposable
{
    private readonly Mock<IWorkspaceService> _mockWorkspaceService;
    private readonly string _tempWorkspacePath;

    public FilesControllerTests()
    {
        _mockWorkspaceService = new Mock<IWorkspaceService>();

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

    private FilesController CreateController() => new(_mockWorkspaceService.Object);

    [Fact]
    public void ListFiles_ReturnsFilesFromActiveWorkspace()
    {
        File.WriteAllText(Path.Combine(_tempWorkspacePath, "notes.txt"), "hello");
        File.WriteAllBytes(Path.Combine(_tempWorkspacePath, "diagram.png"), new byte[] { 1, 2, 3 });

        var controller = CreateController();

        var result = controller.ListFiles();

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var files = Assert.IsType<List<WorkspaceFileInfo>>(okResult.Value);

        Assert.Equal(2, files.Count);
        Assert.Contains(files, f => f.Name == "notes.txt");
        Assert.Contains(files, f => f.Name == "diagram.png" && f.SizeBytes == 3);
    }
}
