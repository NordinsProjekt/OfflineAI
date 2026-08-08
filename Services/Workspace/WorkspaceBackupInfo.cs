namespace Services.Workspace;

/// <summary>One stored backup of the workspace's editable files.</summary>
/// <param name="Id">
/// Folder name of the backup inside the backup directory — also the handle passed to
/// <see cref="IWorkspaceBackupService.Restore"/>.
/// </param>
/// <param name="Label">What the backup was taken before, e.g. "iteration-3".</param>
/// <param name="CreatedAt">Local time the backup was taken (derived from the folder name).</param>
/// <param name="FileCount">Number of files stored in it.</param>
/// <param name="TotalBytes">Combined size of those files.</param>
public sealed record WorkspaceBackupInfo(
    string Id,
    string Label,
    DateTime CreatedAt,
    int FileCount,
    long TotalBytes);
