# Workspace Confinement — Multi-Workspace File Safety

## Summary

The LLM must never read, write, or edit files outside of a single, user-chosen directory. This
is enforced by a **workspace** model: the user can define one or more named workspaces (a friendly
name paired with an absolute folder path), pick which one is *active*, and every file operation the
agent performs — whether a typed slash command, an LLM-initiated tool call via the
[agentic chat loop](AGENTIC-CHAT-TOOL-CALLING.md), or Semantic Kernel tool calling for Gemma 4 — is
confined to that single active workspace directory. Switching workspaces re-confines the agent
instantly; nothing the LLM does can widen that boundary.

This complements the existing per-call path-traversal protection described in
[FILE-AGENT-COMMANDS.md](FILE-AGENT-COMMANDS.md#security): workspaces control **which** directory
is the boundary, while `FileAgentService` continues to guarantee the LLM can't escape **whatever**
that boundary currently is.

---

## Why workspaces?

Previously the file agent's base directory was fixed for the lifetime of the app (read once from
`AppConfiguration.Folders.AgentFilesFolder` at startup). That meant:
- Only one project/folder could be used per app session.
- Changing folders required editing configuration and restarting the app.

Workspaces make the active directory a **first-class, user-visible, runtime-switchable setting**,
surfaced directly in the Dashboard sidebar (see [UI](#ui-workspace-sidebar-section) below), while
keeping the underlying safety guarantee identical: exactly one directory is ever writable/readable
by the agent at any given time.

---

## Architecture

```
Services/
└── Workspace/
    ├── WorkspaceInfo.cs        — record: friendly Name + absolute Path
    ├── IWorkspaceService.cs    — contract: list/add/remove/select workspaces
    └── WorkspaceService.cs     — JSON-persisted implementation, default-workspace seeding
```

### Model

```csharp
public sealed record WorkspaceInfo(string Name, string Path);
```

### Contract

```csharp
public interface IWorkspaceService
{
    event Action<WorkspaceInfo>? ActiveWorkspaceChanged;

    IReadOnlyList<WorkspaceInfo> GetWorkspaces();
    WorkspaceInfo GetActiveWorkspace();
    Task<WorkspaceInfo> AddWorkspaceAsync(string name, string path);
    Task RemoveWorkspaceAsync(string name);
    Task SetActiveWorkspaceAsync(string name);
}
```

- **`ActiveWorkspaceChanged`** — raised whenever the active workspace changes (explicit selection,
  or removal of the currently-active workspace falling back to another one). The file agent
  subscribes to this event to re-confine itself immediately (see [Wiring](#wiring-di) below).
- **`AddWorkspaceAsync`** — creates the directory if it doesn't exist yet, persists the new entry,
  but does **not** switch to it automatically (the caller does that separately, see the UI flow).
  Throws `InvalidOperationException` if the name is already taken (case-insensitive).
- **`RemoveWorkspaceAsync`** — removes a workspace by name. If it was active, the first remaining
  workspace becomes active; if it was the *last* workspace, a fresh default `"Standard"` workspace
  is recreated so there is always at least one workspace to fall back to.
- **`SetActiveWorkspaceAsync`** — switches the active workspace and raises `ActiveWorkspaceChanged`.
  Throws `InvalidOperationException` for an unknown name.

### Persistence

`WorkspaceService` persists the workspace list and active selection as JSON, by default at:

```
%AppData%\OfflineAI\workspaces.json
```

```json
{
  "ActiveWorkspaceName": "Project X",
  "Workspaces": [
    { "Name": "Standard", "Path": "C:\\Users\\you\\Documents\\OfflineAI\\AgentFiles" },
    { "Name": "Project X", "Path": "C:\\Projects\\ProjectX" }
  ]
}
```

- **First run** (no settings file yet): a single `"Standard"` workspace is seeded from the
  caller-supplied default path (see [Wiring](#wiring-di)) so existing installs keep working exactly
  as before workspaces existed — no migration step or user action is required.
- **Corrupt/unreadable settings file**: falls back to re-seeding the default `"Standard"` workspace
  rather than failing to start.
- Every known workspace's directory is created automatically (`Directory.CreateDirectory`) on load
  and on add, so switching to a freshly-added workspace never fails because the folder is missing.
- All list/selection state is protected by an internal lock, so concurrent add/remove/switch calls
  from the UI are safe.

---

## How confinement is enforced (`FileAgentService`)

```csharp
public interface IFileAgentService
{
    string BaseDirectory { get; }
    void SetBaseDirectory(string baseDirectory);
    // ... IsCommand, ExecuteAsync, ReadFileRawAsync, etc. (see FILE-AGENT-COMMANDS.md)
}
```

- **`BaseDirectory`** is the single directory every file operation is restricted to. It is settable
  at runtime via **`SetBaseDirectory(string)`**, which re-resolves and re-creates the target
  directory (`Path.GetFullPath` + `Directory.CreateDirectory`) and updates `BaseDirectory` — no
  service recreation or app restart needed.
- **`GetSafePath(filename)`** (private) is unaffected by which directory is active: it always
  strips any directory component from the given filename (`Path.GetFileName`), resolves it inside
  the *current* `BaseDirectory`, and rejects the result if it doesn't start with `BaseDirectory`
  (defends against path traversal, e.g. `../../secret.txt`, and absolute paths like
  `C:\Windows\...`). Every file command (`/skapa`, `/fyll`, `/läs`, `/redigera`, `/lista`) and every
  Semantic Kernel file tool (`create_file`, `read_file`, `write_file`, `edit_file_lines`,
  `insert_file_lines`, `list_files`) goes through this same check.
- Because `SetBaseDirectory` and `GetSafePath` are the *only* ways `FileAgentService` resolves a
  path, re-confining the service to a new workspace is simply a matter of calling
  `SetBaseDirectory(newWorkspace.Path)` — every subsequent call (regardless of caller) is
  automatically restricted to the new directory.

---

## Wiring (DI)

Registered in `AiDashboard/Program.cs`:

```csharp
// Register the workspace service: manages the list of user-selectable workspace
// directories (persisted in %AppData%\OfflineAI\workspaces.json) and the active
// selection. The file agent is always confined to whichever workspace is active.
builder.Services.AddSingleton<IWorkspaceService>(_ =>
{
    var defaultAgentDir = !string.IsNullOrWhiteSpace(appConfig.Folders.AgentFilesFolder)
        ? appConfig.Folders.AgentFilesFolder
        : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "OfflineAI", "AgentFiles");
    return new WorkspaceService(defaultAgentDir);
});

// Register file agent service for /skapa, /fyll, /läs chat commands. Rooted at the
// active workspace directory; SetBaseDirectory(...) re-confines it whenever the user
// switches workspaces, so the LLM can never read/write outside the selected directory.
builder.Services.AddSingleton<IFileAgentService>(sp =>
{
    var workspaceService = sp.GetRequiredService<IWorkspaceService>();
    var fileAgent = new FileAgentService(workspaceService.GetActiveWorkspace().Path);
    workspaceService.ActiveWorkspaceChanged += workspace => fileAgent.SetBaseDirectory(workspace.Path);
    return fileAgent;
});
```

Both are Singletons: there is exactly one `IWorkspaceService` and one `IFileAgentService` for the
app's lifetime, and the subscription wires them together permanently — any future
`SetActiveWorkspaceAsync` call anywhere in the app (currently only from the workspace sidebar UI)
re-confines the *same* `FileAgentService` instance used by every chat page, QuickAsk, and Semantic
Kernel tool call.

`DashboardState.InitializeServices(...)` also takes an optional `IWorkspaceService?` so the Blazor
UI layer can read/drive workspace state and show a status message on every switch:

```csharp
if (workspaceService != null)
{
    WorkspaceService = workspaceService;
    WorkspaceService.ActiveWorkspaceChanged += workspace =>
    {
        StatusMessage = $"[INFO] Arbetsyta bytt till \"{workspace.Name}\" ({workspace.Path})";
        NotifyStateChanged();
    };
}
```

---

## UI: Workspace sidebar section

`AiDashboard/Components/Pages/Components/WorkspaceSection.razor` is inserted into the sidebar
(`Sidebar.razor`, after `FilesSection`) and gives the user full control without touching
configuration files:

- An info banner stating the confinement guarantee in plain language.
- A dropdown listing every known workspace by name; selecting a different one calls
  `Dashboard.SetActiveWorkspaceAsync(name)`.
- The active workspace's absolute folder path, so the user can always see exactly where the agent
  is confined.
- Fields to add a new workspace by name + folder path, which calls `Dashboard.AddWorkspaceAsync(...)`
  followed by `Dashboard.SetActiveWorkspaceAsync(...)` to switch to it immediately.
- A "Remove Active Workspace" action (only shown when more than one workspace exists) with an
  explicit confirm/cancel step before calling `Dashboard.RemoveWorkspaceAsync(...)`. Removing a
  workspace only forgets it from the list — the folder and its contents on disk are **not** deleted.

`DashboardState` exposes the following helpers consumed by this component:

| Method | Purpose |
|---|---|
| `GetWorkspaces()` | All known workspaces, for the dropdown. |
| `GetActiveWorkspace()` | The active workspace, for the path display and dropdown selection. |
| `SetActiveWorkspaceAsync(name)` | Switches the active workspace. |
| `AddWorkspaceAsync(name, path)` | Adds a new workspace. |
| `RemoveWorkspaceAsync(name)` | Removes a workspace by name. |

Covered by `Presentation.AiDashboard.Tests/Components/WorkspaceSectionTests.cs` (bUnit), using a
real `WorkspaceService` rooted at a per-test temp directory rather than a mock, so the add/switch/
remove behavior exercised through the UI matches production wiring exactly.

---

## Guarantees against escaping the workspace

| Concern | Mitigation |
|---|---|
| LLM tries to read/write a file outside the active workspace | `GetSafePath` strips directory components and rejects any resolved path outside `BaseDirectory` — see [How confinement is enforced](#how-confinement-is-enforced-fileagentservice). |
| LLM tries path traversal (`../../secret.txt`) or an absolute path (`C:\Windows\...`) | Same `GetSafePath` check; only the bare filename is ever used, see [FILE-AGENT-COMMANDS.md § Security](FILE-AGENT-COMMANDS.md#security). |
| LLM tries to call an arbitrary external API/URL | Not possible — `/api` (and the `call_api` Semantic Kernel tool) only accept a **named endpoint** pre-configured in `AppConfiguration.AgentTools.Endpoints`; the LLM can never supply a raw URL. See [AGENTIC-CHAT-TOOL-CALLING.md § Utility tools](AGENTIC-CHAT-TOOL-CALLING.md#utility-tools-tid-datum-api). |
| LLM tries to shell out (PowerShell/JS) to edit files outside the workspace | The app never executes arbitrary shell commands or scripts on the LLM's behalf. All file mutation goes exclusively through `IFileAgentService`'s slash commands / Semantic Kernel functions, which are always bound to the single active `BaseDirectory`. There is no code path that lets an LLM response trigger process execution, PowerShell, or script generation that touches the filesystem directly. |
| User wants to work in a different folder | Explicit, user-driven `SetActiveWorkspaceAsync` via the sidebar UI — never triggered by the LLM itself. |
| Removing the last workspace | `RemoveWorkspaceAsync` always leaves at least one workspace (`"Standard"`) so the agent is never left without a valid, existing base directory. |

---

## Tests

`Services.Tests/Workspace/WorkspaceServiceTests.cs` covers:
- Constructor guards (null/empty default path).
- Default-workspace seeding on first run and recovery from a corrupt settings file.
- `AddWorkspaceAsync` success, duplicate-name rejection, and directory creation.
- `RemoveWorkspaceAsync` fallback-to-first-remaining and last-workspace-recreates-default behavior.
- `SetActiveWorkspaceAsync` success, unknown-name rejection, and `ActiveWorkspaceChanged` raising.
- JSON persistence round-trips (state survives re-loading a new `WorkspaceService` instance
  pointed at the same settings file).

`Presentation.AiDashboard.Tests/Components/WorkspaceSectionTests.cs` covers the sidebar UI
end-to-end against a real `WorkspaceService` (dropdown rendering, add/switch/remove flows,
confirmation step, and disabled/enabled button states).

---

## Related Docs

- [FILE-AGENT-COMMANDS.md](FILE-AGENT-COMMANDS.md) — the slash commands confined by the active workspace, and per-call path-traversal protection
- [AGENTIC-CHAT-TOOL-CALLING.md](AGENTIC-CHAT-TOOL-CALLING.md) — the tool-calling loop (file + utility tools) that operates within the active workspace
