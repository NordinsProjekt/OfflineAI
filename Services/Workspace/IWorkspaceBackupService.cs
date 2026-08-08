namespace Services.Workspace;

/// <summary>
/// Point-in-time copies of the active workspace's editable files, taken before the goal agent
/// starts a work step.
/// <para>
/// The agent's own tools are destructive by design — <c>/fyll</c> replaces a whole file — and a
/// model that reaches for the wrong one can undo an hour of good work in a single command. A
/// backup per work step turns that from "the run is ruined" into "restore and carry on".
/// </para>
/// </summary>
public interface IWorkspaceBackupService
{
    /// <summary>
    /// Name of the subdirectory holding the backups inside the workspace. It is a subdirectory on
    /// purpose: the file agent only ever resolves bare filenames in the workspace root, and both
    /// the <c>/lista</c> tool and the agent's workspace snapshot enumerate that root
    /// non-recursively — so the model can neither see the backups nor write to them.
    /// </summary>
    string BackupFolderName { get; }

    /// <summary>
    /// Copies the workspace's editable files into a new backup. Returns null when there was
    /// nothing worth copying, or when the copy failed — a backup is insurance, and failing to
    /// take one must never interrupt a run.
    /// </summary>
    /// <param name="label">Short human-readable context, e.g. "iteration-3". Sanitised for use in
    /// the folder name.</param>
    WorkspaceBackupInfo? Create(string label);

    /// <summary>Existing backups of the active workspace, newest first.</summary>
    IReadOnlyList<WorkspaceBackupInfo> GetBackups();

    /// <summary>
    /// Copies every file in the named backup back into the workspace, overwriting what is there.
    /// Files created after the backup was taken are left alone: deleting a user's later work to
    /// make the workspace match a snapshot exactly would be a second destructive act, which is
    /// the very thing this feature exists to undo.
    /// </summary>
    /// <returns>The number of files restored.</returns>
    /// <exception cref="DirectoryNotFoundException">The backup no longer exists.</exception>
    int Restore(string backupId);
}
