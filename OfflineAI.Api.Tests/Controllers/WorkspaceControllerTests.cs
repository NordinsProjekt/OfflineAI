using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using OfflineAI.Api.Controllers;
using OfflineAI.Api.Models;
using Services.Workspace;
using Xunit;

namespace OfflineAI.Api.Tests.Controllers;

/// <summary>
/// Unit tests for WorkspaceController. Covers listing, creating, activating, and removing
/// workspaces via a mocked IWorkspaceService.
/// </summary>
public class WorkspaceControllerTests
{
    private readonly Mock<IWorkspaceService> _mockService;
    private readonly Mock<ILogger<WorkspaceController>> _mockLogger;
    private readonly WorkspaceController _controller;

    public WorkspaceControllerTests()
    {
        _mockService = new Mock<IWorkspaceService>();
        _mockLogger = new Mock<ILogger<WorkspaceController>>();
        _controller = new WorkspaceController(_mockService.Object, _mockLogger.Object);
    }

    [Fact]
    public void GetWorkspaces_MarksActiveWorkspace()
    {
        _mockService.Setup(s => s.GetWorkspaces()).Returns(new List<WorkspaceInfo>
        {
            new("Standard", @"C:\workspaces\standard"),
            new("Project", @"C:\workspaces\project")
        });
        _mockService.Setup(s => s.GetActiveWorkspace()).Returns(new WorkspaceInfo("Project", @"C:\workspaces\project"));

        var result = _controller.GetWorkspaces();

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var workspaces = Assert.IsType<List<WorkspaceResponse>>(okResult.Value);

        Assert.Equal(2, workspaces.Count);
        Assert.False(workspaces.Single(w => w.Name == "Standard").IsActive);
        Assert.True(workspaces.Single(w => w.Name == "Project").IsActive);
    }

    [Fact]
    public void GetActiveWorkspace_ReturnsActiveWorkspace()
    {
        _mockService.Setup(s => s.GetActiveWorkspace()).Returns(new WorkspaceInfo("Standard", @"C:\workspaces\standard"));

        var result = _controller.GetActiveWorkspace();

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var workspace = Assert.IsType<WorkspaceResponse>(okResult.Value);

        Assert.Equal("Standard", workspace.Name);
        Assert.True(workspace.IsActive);
    }

    [Fact]
    public async Task CreateWorkspace_Valid_ReturnsCreated()
    {
        var request = new CreateWorkspaceRequest { Name = "Project", Path = @"C:\workspaces\project" };
        _mockService
            .Setup(s => s.AddWorkspaceAsync(request.Name, request.Path))
            .ReturnsAsync(new WorkspaceInfo(request.Name, request.Path));

        var result = await _controller.CreateWorkspace(request);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        var workspace = Assert.IsType<WorkspaceResponse>(createdResult.Value);
        Assert.Equal("Project", workspace.Name);
    }

    [Fact]
    public async Task CreateWorkspace_DuplicateName_ReturnsBadRequest()
    {
        var request = new CreateWorkspaceRequest { Name = "Standard", Path = @"C:\workspaces\standard2" };
        _mockService
            .Setup(s => s.AddWorkspaceAsync(request.Name, request.Path))
            .ThrowsAsync(new InvalidOperationException("A workspace with that name already exists."));

        var result = await _controller.CreateWorkspace(request);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
        var error = Assert.IsType<ErrorResponse>(badRequestResult.Value);
        Assert.Equal(400, error.StatusCode);
    }

    [Fact]
    public async Task SetActiveWorkspace_Valid_ReturnsOk()
    {
        var request = new SetActiveWorkspaceRequest { Name = "Project" };
        _mockService.Setup(s => s.SetActiveWorkspaceAsync(request.Name)).Returns(Task.CompletedTask);
        _mockService.Setup(s => s.GetActiveWorkspace()).Returns(new WorkspaceInfo("Project", @"C:\workspaces\project"));

        var result = await _controller.SetActiveWorkspace(request);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var workspace = Assert.IsType<WorkspaceResponse>(okResult.Value);
        Assert.Equal("Project", workspace.Name);
        Assert.True(workspace.IsActive);
    }

    [Fact]
    public async Task SetActiveWorkspace_UnknownName_ReturnsBadRequest()
    {
        var request = new SetActiveWorkspaceRequest { Name = "DoesNotExist" };
        _mockService
            .Setup(s => s.SetActiveWorkspaceAsync(request.Name))
            .ThrowsAsync(new InvalidOperationException("No workspace found with that name."));

        var result = await _controller.SetActiveWorkspace(request);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
        var error = Assert.IsType<ErrorResponse>(badRequestResult.Value);
        Assert.Equal(400, error.StatusCode);
    }

    [Fact]
    public async Task RemoveWorkspace_ReturnsNoContent()
    {
        _mockService.Setup(s => s.RemoveWorkspaceAsync("Project")).Returns(Task.CompletedTask);

        var result = await _controller.RemoveWorkspace("Project");

        Assert.IsType<NoContentResult>(result);
        _mockService.Verify(s => s.RemoveWorkspaceAsync("Project"), Times.Once);
    }
}
