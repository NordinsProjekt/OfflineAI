namespace Services.Workspace;

/// <summary>
/// Describes a single user-defined workspace: a friendly name paired with the absolute
/// directory that the file agent (<see cref="Services.FileAgent.IFileAgentService"/>) is
/// confined to while that workspace is active. All file creation, reading, and editing
/// performed by the LLM — whether via slash commands or Semantic Kernel tool calling — is
/// restricted to this directory; the LLM can never read or write outside of it.
/// </summary>
/// <param name="Name">Friendly, user-chosen name shown in the workspace selector.</param>
/// <param name="Path">Absolute directory path this workspace is rooted at.</param>
public sealed record WorkspaceInfo(string Name, string Path);
