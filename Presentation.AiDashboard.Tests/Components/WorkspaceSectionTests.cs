using AiDashboard.State;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Moq;
using Services.Workspace;
using WorkspaceSection = AiDashboard.Components.Pages.Components.WorkspaceSection;
using CollapsibleSection = AiDashboard.Components.Pages.Components.CollapsibleSection;

namespace Presentation.AiDashboard.Tests.Components;

/// <summary>
/// Tests for <see cref="WorkspaceSection"/>: the sidebar UI that lets the user view, switch,
/// add, and remove workspaces. All agent file operations are confined to whichever workspace is
/// active, so this UI is safety-relevant — it must accurately reflect and drive
/// <see cref="IWorkspaceService"/> state through <see cref="DashboardState"/>. Uses a real
/// <see cref="WorkspaceService"/> rooted at a per-test temp directory (rather than a mock) so the
/// add/switch/remove behavior exercised through the UI matches production wiring exactly.
/// </summary>
public sealed class WorkspaceSectionTests : TestContext, IDisposable
{
    private readonly Mock<IJSRuntime> _mockJSRuntime;
    private readonly string _rootDir;

    public WorkspaceSectionTests()
    {
        _mockJSRuntime = new Mock<IJSRuntime>();
        _rootDir = Path.Combine(Path.GetTempPath(), "WorkspaceSectionTests_" + Guid.NewGuid());
    }

    public new void Dispose()
    {
        if (Directory.Exists(_rootDir))
            Directory.Delete(_rootDir, recursive: true);
        base.Dispose();
        GC.SuppressFinalize(this);
    }

    private WorkspaceService CreateWorkspaceService() =>
        new(
            Path.Combine(_rootDir, "default-workspace"),
            Path.Combine(_rootDir, "settings", "workspaces.json"));

    private IRenderedComponent<WorkspaceSection> RenderSection(IWorkspaceService workspaceService)
    {
        var dashboardState = new DashboardState();
        dashboardState.InitializeServices(null, null, null, null, workspaceService);

        Services.AddSingleton(dashboardState);
        Services.AddSingleton<IJSRuntime>(_mockJSRuntime.Object);

        return RenderComponent<WorkspaceSection>();
    }

    // ── Structure ────────────────────────────────────────────────────────

    [Fact]
    public void WorkspaceSection_Renders_CollapsibleSection_WithCorrectTitle()
    {
        var cut = RenderSection(CreateWorkspaceService());

        var section = cut.FindComponent<CollapsibleSection>();
        Assert.Equal("workspace", section.Instance.SectionKey);
        Assert.Equal("Workspace", section.Instance.Title);
    }

    [Fact]
    public void WorkspaceSection_Shows_InfoText_AboutConfinement()
    {
        var cut = RenderSection(CreateWorkspaceService());

        var infoText = cut.Find(".oa-info-text");
        Assert.Contains("active workspace folder", infoText.TextContent);
    }

    // ── Active workspace display ───────────────────────────────────────────

    [Fact]
    public void WorkspaceSection_Shows_ActiveWorkspaceFolder_WithPath()
    {
        var workspaceService = CreateWorkspaceService();
        var cut = RenderSection(workspaceService);

        var folder = cut.Find(".oa-active-collection");
        Assert.Contains(workspaceService.GetActiveWorkspace().Path, folder.TextContent);
    }

    [Fact]
    public void WorkspaceSection_Renders_WorkspaceDropdown_WithSingleSeededWorkspace()
    {
        var cut = RenderSection(CreateWorkspaceService());

        var options = cut.FindAll("select option");
        Assert.Single(options);
        Assert.Equal("Standard", options[0].TextContent);
    }

    [Fact]
    public void WorkspaceSection_DoesNotShow_RemoveButton_WhenOnlyOneWorkspace()
    {
        var cut = RenderSection(CreateWorkspaceService());

        var removeButtons = cut.FindAll("button.oa-btn-danger");
        Assert.Empty(removeButtons);
    }

    // ── Add workspace form ───────────────────────────────────────────────

    [Fact]
    public void WorkspaceSection_AddButton_Disabled_WhenBothFieldsEmpty()
    {
        var cut = RenderSection(CreateWorkspaceService());

        var button = cut.Find("button.oa-btn-block:not(.oa-btn-danger)");
        Assert.True(button.HasAttribute("disabled"));
    }

    [Fact]
    public void WorkspaceSection_AddButton_Disabled_WhenOnlyNameFilled()
    {
        var cut = RenderSection(CreateWorkspaceService());
        var nameInput = cut.FindAll("input.oa-text")[0];

        nameInput.Input("Project X");

        var button = cut.Find("button.oa-btn-block:not(.oa-btn-danger)");
        Assert.True(button.HasAttribute("disabled"));
    }

    [Fact]
    public void WorkspaceSection_AddButton_Enabled_WhenBothFieldsFilled()
    {
        var cut = RenderSection(CreateWorkspaceService());

        FillNewWorkspaceForm(cut, "Project X", Path.Combine(_rootDir, "project-x"));

        var button = cut.Find("button.oa-btn-block:not(.oa-btn-danger)");
        Assert.False(button.HasAttribute("disabled"));
    }

    [Fact]
    public async Task WorkspaceSection_AddWorkspace_AddsNewWorkspaceAndSelectsItAsActive()
    {
        var workspaceService = CreateWorkspaceService();
        var cut = RenderSection(workspaceService);
        var newPath = Path.Combine(_rootDir, "project-x");

        FillNewWorkspaceForm(cut, "Project X", newPath);
        await cut.InvokeAsync(() => cut.Find("button.oa-btn-block:not(.oa-btn-danger)").Click());

        Assert.Equal("Project X", workspaceService.GetActiveWorkspace().Name);
        var options = cut.FindAll("select option");
        Assert.Equal(2, options.Count);
    }

    // ── Remove workspace flow ─────────────────────────────────────────────

    [Fact]
    public async Task WorkspaceSection_AfterAddingSecondWorkspace_ShowsRemoveButton()
    {
        var cut = RenderSection(CreateWorkspaceService());

        FillNewWorkspaceForm(cut, "Project X", Path.Combine(_rootDir, "project-x"));
        await cut.InvokeAsync(() => cut.Find("button.oa-btn-block:not(.oa-btn-danger)").Click());

        var removeButton = cut.Find("button.oa-btn-danger");
        Assert.Contains("Remove Active Workspace", removeButton.TextContent);
    }

    [Fact]
    public async Task WorkspaceSection_ClickRemoveActiveWorkspace_ShowsConfirmationWithWorkspaceName()
    {
        var cut = RenderSection(CreateWorkspaceService());
        FillNewWorkspaceForm(cut, "Project X", Path.Combine(_rootDir, "project-x"));
        await cut.InvokeAsync(() => cut.Find("button.oa-btn-block:not(.oa-btn-danger)").Click());

        await cut.InvokeAsync(() => cut.Find("button.oa-btn-danger").Click());

        var confirmation = cut.Find(".delete-confirmation");
        Assert.Contains("Project X", confirmation.TextContent);
    }

    [Fact]
    public async Task WorkspaceSection_CancelRemove_HidesConfirmationAndKeepsWorkspace()
    {
        var workspaceService = CreateWorkspaceService();
        var cut = RenderSection(workspaceService);
        FillNewWorkspaceForm(cut, "Project X", Path.Combine(_rootDir, "project-x"));
        await cut.InvokeAsync(() => cut.Find("button.oa-btn-block:not(.oa-btn-danger)").Click());
        await cut.InvokeAsync(() => cut.Find("button.oa-btn-danger").Click());

        var cancelButton = cut.FindAll("button").First(b => b.TextContent.Contains("Cancel"));
        await cut.InvokeAsync(() => cancelButton.Click());

        Assert.Empty(cut.FindAll(".delete-confirmation"));
        Assert.Equal("Project X", workspaceService.GetActiveWorkspace().Name);
    }

    [Fact]
    public async Task WorkspaceSection_ConfirmRemove_RemovesWorkspaceAndFallsBackToStandard()
    {
        var workspaceService = CreateWorkspaceService();
        var cut = RenderSection(workspaceService);
        FillNewWorkspaceForm(cut, "Project X", Path.Combine(_rootDir, "project-x"));
        await cut.InvokeAsync(() => cut.Find("button.oa-btn-block:not(.oa-btn-danger)").Click());
        await cut.InvokeAsync(() => cut.Find("button.oa-btn-danger").Click());

        var confirmButton = cut.FindAll("button").First(b => b.TextContent.Contains("Confirm Remove"));
        await cut.InvokeAsync(() => confirmButton.Click());

        Assert.Equal("Standard", workspaceService.GetActiveWorkspace().Name);
        var options = cut.FindAll("select option");
        Assert.Single(options);
    }

    /// <summary>
    /// Fills the "new workspace" name and path inputs, re-querying the path input fresh from the
    /// render tree after the name input's change triggers a re-render (its <c>oninput</c> handler
    /// updates the button's <c>disabled</c> binding) — reusing an element reference captured
    /// before that re-render would raise <c>UnknownEventHandlerIdException</c>.
    /// </summary>
    private static void FillNewWorkspaceForm(IRenderedComponent<WorkspaceSection> cut, string name, string path)
    {
        cut.FindAll("input.oa-text")[0].Input(name);
        cut.FindAll("input.oa-text")[1].Input(path);
    }
}
