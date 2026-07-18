namespace Services.Workspace;

/// <summary>
/// Manages the set of user-defined <see cref="WorkspaceInfo"/> entries and which one is
/// currently active. Persists to a JSON file so the selection and the workspace list survive
/// app restarts.
/// <para>
/// The active workspace's <see cref="WorkspaceInfo.Path"/> is the single directory the file
/// agent (<c>IFileAgentService</c>) is confined to — switching the active workspace
/// re-confines every subsequent file operation (slash commands and Semantic Kernel file tools
/// alike) to the new directory. This is the mechanism that guarantees the LLM "never leaves
/// its directory" while still letting the user choose which directory that is.
/// </para>
/// </summary>
public interface IWorkspaceService
{
    /// <summary>
    /// Raised whenever the active workspace changes (selection, or the active workspace being
    /// removed). Subscribers (e.g. the file agent) should re-confine themselves to
    /// <see cref="GetActiveWorkspace"/>'s path.
    /// </summary>
    event Action<WorkspaceInfo>? ActiveWorkspaceChanged;

    /// <summary>Returns all known workspaces, in the order they were added.</summary>
    IReadOnlyList<WorkspaceInfo> GetWorkspaces();

    /// <summary>Returns the currently active workspace.</summary>
    WorkspaceInfo GetActiveWorkspace();

    /// <summary>
    /// Adds a new workspace with the given name and root directory (created if missing) and
    /// persists the updated list. Does not change the active workspace.
    /// </summary>
    Task<WorkspaceInfo> AddWorkspaceAsync(string name, string path);

    /// <summary>
    /// Removes the workspace with the given name. If it was the active workspace, the first
    /// remaining workspace becomes active (a default workspace is created if none remain).
    /// </summary>
    Task RemoveWorkspaceAsync(string name);

    /// <summary>
    /// Sets the workspace with the given name as active, persists the selection, and raises
    /// <see cref="ActiveWorkspaceChanged"/>.
    /// </summary>
    Task SetActiveWorkspaceAsync(string name);
}
