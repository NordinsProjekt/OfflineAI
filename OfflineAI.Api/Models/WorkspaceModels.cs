namespace OfflineAI.Api.Models;

/// <summary>
/// A workspace directory the file agent can be confined to.
/// </summary>
public class WorkspaceResponse
{
    public required string Name { get; set; }
    public required string Path { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>
/// Request body for creating a new workspace.
/// </summary>
public class CreateWorkspaceRequest
{
    /// <summary>Friendly, unique name for the workspace.</summary>
    public required string Name { get; set; }

    /// <summary>Absolute directory path the workspace is rooted at (created if missing).</summary>
    public required string Path { get; set; }
}

/// <summary>
/// Request body for switching the active workspace.
/// </summary>
public class SetActiveWorkspaceRequest
{
    /// <summary>Name of the workspace to activate.</summary>
    public required string Name { get; set; }
}
