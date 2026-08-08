using AgentKit.Skills.Files;
using FluentAssertions;
using Services.Workspace;

namespace Services.Tests.Workspace;

/// <summary>
/// Unit tests for <see cref="WorkspaceBackupService"/>: the safety net that lets a destructive
/// agent edit be undone. The agent's own <c>/fyll</c> replaces a whole file, so the value of these
/// backups is entirely in what they contain and whether restoring puts it back — and in never
/// throwing on the write path, since they are taken in the middle of a run.
/// </summary>
public sealed class WorkspaceBackupServiceTests : IDisposable
{
    private readonly string _workspace;
    private readonly IFileAgentService _fileAgent;

    public WorkspaceBackupServiceTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "WorkspaceBackupTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_workspace);
        _fileAgent = new FileAgentService(_workspace);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
            Directory.Delete(_workspace, recursive: true);
    }

    private WorkspaceBackupService CreateSut(int retainCount = WorkspaceBackupService.DefaultRetainCount) =>
        new(_fileAgent, neverBackedUpNames: new[] { "agentlogg.txt" }, retainCount);

    private void WriteFile(string name, string content) =>
        File.WriteAllText(Path.Combine(_workspace, name), content);

    [Fact]
    public void Create_EmptyWorkspace_ReturnsNullAndWritesNothing()
    {
        var sut = CreateSut();

        sut.Create("iteration-1").Should().BeNull();
        sut.GetBackups().Should().BeEmpty();
    }

    [Fact]
    public void Create_CopiesEditableFiles()
    {
        WriteFile("calc.bas", "PRINT 1");
        WriteFile("readme.txt", "hej");
        var sut = CreateSut();

        var backup = sut.Create("iteration-1");

        backup.Should().NotBeNull();
        backup!.FileCount.Should().Be(2);
        backup.Label.Should().Be("iteration-1");
        sut.GetBackups().Should().ContainSingle().Which.Id.Should().Be(backup.Id);
    }

    [Fact]
    public void Create_SkipsExcludedNamesAndBinaryFormats()
    {
        WriteFile("calc.bas", "PRINT 1");
        WriteFile("agentlogg.txt", "the run's own transcript");
        File.WriteAllBytes(Path.Combine(_workspace, "manual.pdf"), new byte[] { 1, 2, 3 });
        var sut = CreateSut();

        var backup = sut.Create("iteration-1")!;

        backup.FileCount.Should().Be(1, "only the agent's own editable output is worth copying");
        Directory.GetFiles(Path.Combine(_workspace, sut.BackupFolderName, backup.Id))
            .Select(Path.GetFileName).Should().Equal("calc.bas");
    }

    [Fact]
    public void Create_DoesNotBackUpTheBackupsThemselves()
    {
        WriteFile("calc.bas", "PRINT 1");
        var sut = CreateSut();

        sut.Create("iteration-1");
        var second = sut.Create("iteration-2")!;

        // The backup folder is a subdirectory, and only the workspace root is enumerated —
        // otherwise every backup would contain all the previous ones.
        second.FileCount.Should().Be(1);
    }

    [Fact]
    public void Restore_PutsBackTheOverwrittenContent()
    {
        WriteFile("calc.bas", "the good version");
        var sut = CreateSut();
        var backup = sut.Create("iteration-1")!;

        WriteFile("calc.bas", "wiped by /fyll");

        var restored = sut.Restore(backup.Id);

        restored.Should().Be(1);
        File.ReadAllText(Path.Combine(_workspace, "calc.bas")).Should().Be("the good version");
    }

    [Fact]
    public void Restore_LeavesFilesCreatedAfterTheBackupAlone()
    {
        WriteFile("calc.bas", "v1");
        var sut = CreateSut();
        var backup = sut.Create("iteration-1")!;

        WriteFile("notes.txt", "written later, by hand");
        sut.Restore(backup.Id);

        // Restoring undoes a bad edit; it must not double as a delete of everything else.
        File.Exists(Path.Combine(_workspace, "notes.txt")).Should().BeTrue();
    }

    [Fact]
    public void Restore_UnknownBackup_Throws()
    {
        var sut = CreateSut();

        var act = () => sut.Restore("20200101-000000-000_nope");

        act.Should().Throw<DirectoryNotFoundException>();
    }

    [Fact]
    public void Restore_IdWithPathTraversal_StaysInsideTheBackupFolder()
    {
        WriteFile("calc.bas", "v1");
        var sut = CreateSut();
        sut.Create("iteration-1");

        var act = () => sut.Restore(@"..\..\Windows");

        act.Should().Throw<DirectoryNotFoundException>();
    }

    [Fact]
    public void GetBackups_ReturnsNewestFirst()
    {
        WriteFile("calc.bas", "v1");
        var sut = CreateSut();

        var first = sut.Create("iteration-1")!;
        var second = sut.Create("iteration-2")!;

        sut.GetBackups().Select(b => b.Id).Should().Equal(second.Id, first.Id);
    }

    [Fact]
    public void Create_PrunesOldBackupsBeyondTheRetentionCount()
    {
        WriteFile("calc.bas", "v1");
        var sut = CreateSut(retainCount: 2);

        sut.Create("iteration-1");
        sut.Create("iteration-2");
        sut.Create("iteration-3");

        var remaining = sut.GetBackups();
        remaining.Should().HaveCount(2);
        remaining.Select(b => b.Label).Should().Equal("iteration-3", "iteration-2");
    }

    [Fact]
    public void GetBackups_IgnoresFoldersItDidNotCreate()
    {
        WriteFile("calc.bas", "v1");
        var sut = CreateSut();
        sut.Create("iteration-1");
        Directory.CreateDirectory(Path.Combine(_workspace, sut.BackupFolderName, "handmade-folder"));

        sut.GetBackups().Should().ContainSingle();
    }
}
