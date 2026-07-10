using FluentAssertions;
using Services.Workspace;

namespace Services.Tests.Workspace;

/// <summary>
/// Unit tests for <see cref="WorkspaceService"/>: the JSON-backed persistence of the user's
/// workspace list and active selection. The active workspace's path is the single directory the
/// file agent is confined to, so correct seeding, switching, and persistence here are what
/// guarantee the LLM can never leave the directory the user selected.
/// </summary>
public sealed class WorkspaceServiceTests : IDisposable
{
    private readonly string _rootDir;
    private readonly string _defaultWorkspacePath;
    private readonly string _settingsFilePath;

    public WorkspaceServiceTests()
    {
        _rootDir = Path.Combine(Path.GetTempPath(), "WorkspaceServiceTests_" + Guid.NewGuid());
        _defaultWorkspacePath = Path.Combine(_rootDir, "default-workspace");
        _settingsFilePath = Path.Combine(_rootDir, "settings", "workspaces.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDir))
            Directory.Delete(_rootDir, recursive: true);
    }

    private WorkspaceService CreateSut() => new(_defaultWorkspacePath, _settingsFilePath);

    // ── Constructor guards ──────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_NullOrWhitespaceDefaultPath_Throws(string? defaultPath)
    {
        var act = () => new WorkspaceService(defaultPath!, _settingsFilePath);

        act.Should().Throw<ArgumentNullException>();
    }

    // ── First-run seeding ─────────────────────────────────────────────────

    [Fact]
    public void Constructor_NoSettingsFile_SeedsDefaultStandardWorkspace()
    {
        var sut = CreateSut();

        var workspaces = sut.GetWorkspaces();
        workspaces.Should().ContainSingle();
        workspaces[0].Name.Should().Be("Standard");
        workspaces[0].Path.Should().Be(Path.GetFullPath(_defaultWorkspacePath));
        sut.GetActiveWorkspace().Should().BeEquivalentTo(workspaces[0]);
        Directory.Exists(_defaultWorkspacePath).Should().BeTrue();
    }

    [Fact]
    public void Constructor_NoSettingsFile_PersistsSeededWorkspaceToDisk()
    {
        _ = CreateSut();

        File.Exists(_settingsFilePath).Should().BeTrue();
        File.ReadAllText(_settingsFilePath).Should().Contain("Standard");
    }

    // ── AddWorkspaceAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task AddWorkspaceAsync_NewWorkspace_AddsWithoutChangingActiveAndCreatesDirectory()
    {
        var sut = CreateSut();
        var newPath = Path.Combine(_rootDir, "project-x");

        var added = await sut.AddWorkspaceAsync("Project X", newPath);

        added.Name.Should().Be("Project X");
        added.Path.Should().Be(Path.GetFullPath(newPath));
        sut.GetWorkspaces().Should().HaveCount(2);
        sut.GetActiveWorkspace().Name.Should().Be("Standard");
        Directory.Exists(newPath).Should().BeTrue();
    }

    [Fact]
    public async Task AddWorkspaceAsync_DuplicateName_ThrowsInvalidOperationException()
    {
        var sut = CreateSut();

        var act = async () => await sut.AddWorkspaceAsync("Standard", Path.Combine(_rootDir, "other"));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddWorkspaceAsync_InvalidName_ThrowsArgumentException(string? name)
    {
        var sut = CreateSut();

        var act = async () => await sut.AddWorkspaceAsync(name!, Path.Combine(_rootDir, "other"));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddWorkspaceAsync_InvalidPath_ThrowsArgumentException(string? path)
    {
        var sut = CreateSut();

        var act = async () => await sut.AddWorkspaceAsync("Name", path!);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── SetActiveWorkspaceAsync ───────────────────────────────────────────

    [Fact]
    public async Task SetActiveWorkspaceAsync_ExistingWorkspace_ChangesActiveAndRaisesEvent()
    {
        var sut = CreateSut();
        await sut.AddWorkspaceAsync("Project X", Path.Combine(_rootDir, "project-x"));

        WorkspaceInfo? raised = null;
        sut.ActiveWorkspaceChanged += w => raised = w;

        await sut.SetActiveWorkspaceAsync("Project X");

        sut.GetActiveWorkspace().Name.Should().Be("Project X");
        raised.Should().NotBeNull();
        raised!.Name.Should().Be("Project X");
    }

    [Fact]
    public async Task SetActiveWorkspaceAsync_UnknownName_ThrowsInvalidOperationException()
    {
        var sut = CreateSut();

        var act = async () => await sut.SetActiveWorkspaceAsync("Does Not Exist");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ── RemoveWorkspaceAsync ──────────────────────────────────────────────

    [Fact]
    public async Task RemoveWorkspaceAsync_NonActiveWorkspace_RemovesWithoutChangingActive()
    {
        var sut = CreateSut();
        await sut.AddWorkspaceAsync("Project X", Path.Combine(_rootDir, "project-x"));

        await sut.RemoveWorkspaceAsync("Project X");

        sut.GetWorkspaces().Should().ContainSingle();
        sut.GetActiveWorkspace().Name.Should().Be("Standard");
    }

    [Fact]
    public async Task RemoveWorkspaceAsync_ActiveWorkspace_SwitchesToRemainingAndRaisesEvent()
    {
        var sut = CreateSut();
        await sut.AddWorkspaceAsync("Project X", Path.Combine(_rootDir, "project-x"));
        await sut.SetActiveWorkspaceAsync("Project X");

        WorkspaceInfo? raised = null;
        sut.ActiveWorkspaceChanged += w => raised = w;

        await sut.RemoveWorkspaceAsync("Project X");

        sut.GetActiveWorkspace().Name.Should().Be("Standard");
        raised.Should().NotBeNull();
        raised!.Name.Should().Be("Standard");
    }

    [Fact]
    public async Task RemoveWorkspaceAsync_LastWorkspace_RecreatesDefaultWorkspace()
    {
        var sut = CreateSut();

        await sut.RemoveWorkspaceAsync("Standard");

        var workspaces = sut.GetWorkspaces();
        workspaces.Should().ContainSingle();
        workspaces[0].Name.Should().Be("Standard");
        sut.GetActiveWorkspace().Name.Should().Be("Standard");
    }

    [Fact]
    public async Task RemoveWorkspaceAsync_UnknownName_DoesNothing()
    {
        var sut = CreateSut();

        await sut.RemoveWorkspaceAsync("Does Not Exist");

        sut.GetWorkspaces().Should().ContainSingle();
    }

    // ── Persistence round trip ────────────────────────────────────────────

    [Fact]
    public async Task NewInstance_WithSameSettingsFile_LoadsPersistedWorkspacesAndActiveSelection()
    {
        var first = CreateSut();
        await first.AddWorkspaceAsync("Project X", Path.Combine(_rootDir, "project-x"));
        await first.SetActiveWorkspaceAsync("Project X");

        var second = CreateSut();

        second.GetWorkspaces().Should().HaveCount(2);
        second.GetActiveWorkspace().Name.Should().Be("Project X");
    }
}
