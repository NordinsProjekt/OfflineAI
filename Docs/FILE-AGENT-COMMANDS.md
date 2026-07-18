# File Agent - Slash Commands for File Management

## Summary

The **File Agent** adds slash-command support to the chat interface, letting users create,
write to, read, and edit text files without leaving the chat. Typed commands are intercepted
before the message reaches the LLM; `/läs` reads a file and combines its content with an
explicit instruction supplied in the command, so the agent always receives a clear task rather
than just raw file content used as the prompt. `/redigera` extends this further: it shows the
LLM the file with line numbers and asks it to reply with precise line-range replacements — or,
when asked to add brand-new code such as a new method, with insertion blocks that splice content
in at a chosen line without touching anything else — which the service then applies
automatically, appending new code within the correct namespace/class instead of overwriting it.
No full-file rewrite is required for either kind of change.

---

## Features

- `/skapa <fil>` — creates an empty text file in the agent folder
- `/fyll <fil> <innehåll>` — writes inline content to a file (creates if needed)
- `/läs <fil> <instruktion>` — reads the file and sends it to the LLM together with an explicit instruction
- `/las <fil> <instruktion>` — ASCII fallback for `/läs` (keyboards without ä)
- `/redigera <fil> <instruktion>` — shows the file with line numbers, asks the LLM which line ranges to replace and with what (or where to insert brand-new code, e.g. a new method, without removing anything), then applies those edits to the file automatically
- `/lista` — lists every file currently stored in the agent folder
- **Path traversal protection** — only bare filenames are accepted; directory segments are stripped
- **Configurable base directory** — defaults to `Documents\OfflineAI\AgentFiles`, overridable in `appsettings.json`
- **Workspace-confined** — the base directory is actually the currently active **workspace**, a user-selectable, runtime-switchable folder; the agent can never read/write outside of it. See [WORKSPACE-CONFINEMENT.md](WORKSPACE-CONFINEMENT.md)
- **Reusable service** — lives in the `Services` project (`Services.FileAgent` namespace), injectable in any project in the solution
- **Agentic tool-calling** — the same commands double as a tool dictionary the LLM can invoke itself; see [AGENTIC-CHAT-TOOL-CALLING.md](AGENTIC-CHAT-TOOL-CALLING.md)

### Page support

| Page | Supported |
|------|-----------|
| Home (Dashboard) — `Home.razor.cs` | ✅ |
| QuickAsk — `QuickAskPage.razor` | ✅ |

---

## Architecture

### Service Layer (`Services` project)

```
Services/
└── FileAgent/
    ├── FileAgentResult.cs      — Result model + FileAgentResultType enum
    ├── IFileAgentService.cs    — Interface (DI contract)
    ├── FileAgentService.cs     — Implementation
    └── LineEdit.cs             — Line-range replacement/insertion model used by /redigera
```

### Interface

```csharp
public interface IFileAgentService
{
    string BaseDirectory { get; }
    void SetBaseDirectory(string baseDirectory);
    bool IsCommand(string input);
    Task<FileAgentResult> ExecuteAsync(string input);
    Task<FileAgentResult> ReadFileRawAsync(string filename);
    bool TryExtractFileContent(string llmResponse, out string content);
    Task WriteExtractedContentAsync(string filename, string content);
    string StripFileMarkers(string llmResponse);
    bool TryExtractLineEdits(string llmResponse, out IReadOnlyList<LineEdit> edits);
    Task<FileAgentResult> ApplyLineEditsAsync(string filename, IReadOnlyList<LineEdit> edits);
    string StripEditMarkers(string llmResponse);
    IReadOnlyDictionary<string, string> GetToolDescriptions();
    string BuildToolsSystemPrompt();
    bool TryFindAgentCommand(string llmResponse, out string command);
}
```

> `SetBaseDirectory(string)` lets the active base directory change at runtime without recreating
> the service — this is how switching the active **workspace** re-confines every subsequent file
> operation to the newly-selected directory. See
> [WORKSPACE-CONFINEMENT.md](WORKSPACE-CONFINEMENT.md) for the full workspace model.

> `TryExtractLineEdits`, `ApplyLineEditsAsync`, and `StripEditMarkers` support the `/redigera`
> line-editing workflow: the LLM is shown the file with line numbers and replies with one or
> more `<REDIGERA RAD=...>` blocks (replace existing lines) and/or `<REDIGERA INFOGA_EFTER=...>` /
> `<REDIGERA INFOGA_FÖRE=...>` blocks (insert brand-new code, e.g. a new method, without removing
> anything). All blocks are parsed, validated (range + overlap checks), and applied to the file.

> `ReadFileRawAsync`, `GetToolDescriptions`, `BuildToolsSystemPrompt`, and `TryFindAgentCommand`
> exist to support the agentic tool-calling pattern described in
> [AGENTIC-CHAT-TOOL-CALLING.md](AGENTIC-CHAT-TOOL-CALLING.md); `ReadFileRawAsync` in particular
> reads a file **without** requiring an instruction, for programmatic callers (e.g. Semantic
> Kernel's `read_file` tool in `BuiltInFileTools.cs`) that supply their own reasoning.

### Result Model

```csharp
public enum FileAgentResultType
{
    NotACommand,    // Not a command — pass through to AI normally
    FileCreated,    // /skapa succeeded
    FileFilled,     // /fyll succeeded
    FillRequested,  // /fyll parsed — LlmPrompt must be sent to the LLM first
    EditRequested,  // /redigera parsed — LlmPrompt must be sent to the LLM first
    FileRead,       // /läs succeeded — InjectedContext contains instruction + file content
    FileEdited,     // /redigera succeeded — line replacements and/or insertions were applied to the file
    FilesListed,    // /lista succeeded — InjectedContext contains the comma-separated file listing
    Error           // Command recognised but failed
}

public class FileAgentResult
{
    public FileAgentResultType ResultType { get; init; }
    public bool IsSuccess      { get; init; }
    public string Message      { get; init; }   // User-facing confirmation or error text
    public string? InjectedContext { get; init; } // Instruction + file content (FileRead) or listing (FilesListed)
}
```

### Message Flow

```
User types message
        ↓
IFileAgentService.IsCommand(input)
        ↓ yes                         ↓ no
ExecuteAsync(input)           Dashboard.SendMessageAsync(input)
        ↓                                     ↓
┌───────────────────┐              LLM generates response
│ FileCreated/      │
│ FileFilled/Error  │ → show system message in chat
│                   │
│ FileRead          │ → Dashboard.SendMessageAsync(InjectedContext) // instruction + file content
└───────────────────┘          ↓
                        LLM responds per the instruction, using the file content as context
```

---

## Commands Reference

### `/skapa <filename>`

Creates an empty text file.

```
/skapa notes.txt
→ ✓ Fil skapad: notes.txt
```

- The file is always created (or overwritten as empty) even if it already exists.
- Only the filename is required; no path separators allowed.

---

### `/fyll <filename> <content>`

Writes `<content>` to the named file. The file is created if it does not exist.

```
/fyll notes.txt Kom ihåg att beställa kaffe imorgon.
→ ✓ Fil uppdaterad: notes.txt
```

- Everything after the filename (and one space) becomes the file content verbatim.
- Multi-word content is supported; the entire remainder of the line is written.
- Previous content is **overwritten**, not appended.

---

### `/läs <filename> <instruktion>` &nbsp;/ `/las <filename> <instruktion>`

Reads the file and sends its content to the LLM **together with an explicit instruction** —
the command always carries a task for the agent; it is never just raw file content used as
the prompt.

```
/läs prompt.txt Sammanfatta innehållet i tre punkter.
→ (file content + instruction are forwarded to the LLM)
→ AI summarises the file content per the instruction
```

- The first word after the filename starts the instruction; everything after it (the entire
  remainder of the line) is the instruction text.
- An instruction is required — `/läs <filename>` alone returns an error asking for one.

Use cases:
- Store long or reusable reference material in a file, then ask the LLM to act on it with a
  fresh instruction each time.
- Keep notes, then ask the LLM to summarise, translate, or answer questions about them.
- Reuse the same file with different instructions across multiple requests.

---

### `/redigera <filename> <instruktion>`

Reads the file with line numbers and asks the LLM to reply with either precise line-range
replacements, or insertion blocks that add brand-new content (e.g. a new method) without removing
anything, or both mixed in the same reply. This follows the same two-phase pattern as `/fyll`:
the first call returns `FileAgentResultType.EditRequested` with an `LlmPrompt` to send to the LLM,
then the caller extracts and applies the edits from the LLM's reply.

```
/redigera notes.txt Rätta stavfelet på rad 2.
→ (file content with line numbers + instruction are forwarded to the LLM)
→ LLM replies with one or more <REDIGERA RAD=...> blocks
→ ✓ Fil redigerad: notes.txt (rad 2)
```

There are two kinds of block the LLM can reply with.

**1) Replace existing lines** — use when correcting/changing text that already exists:

```
<REDIGERA RAD=2>
Rättad rad två
</REDIGERA>
```

Or, for a contiguous range of lines replaced with the same new content:

```
<REDIGERA RAD=5-7>
Ersättningstext för rad 5 till 7
</REDIGERA>
```

**2) Insert brand-new code** — use when the LLM is asked to *add* something new (e.g. write a new
function), so the existing code is never overwritten or deleted:

```
<REDIGERA INFOGA_EFTER=42>
public void NyMetod()
{
    // ...
}
</REDIGERA>
```

`INFOGA_EFTER=N` splices the block in immediately **after** line `N` (use `INFOGA_EFTER=0` to
insert at the very top of the file). `INFOGA_FÖRE=N` (ASCII fallback: `INFOGA_FORE=N`) splices it
in immediately **before** line `N` instead — handy for inserting right before a class/namespace's
closing `}` so a new method lands last inside the correct block, or before a `using` statement to
add another one above it. The prompt tells the LLM to look at the numbered file content's brace
structure (`{` / `}`) to pick an anchor line that keeps the new code inside the right
namespace/class, so appended code is never dropped outside its intended scope.

- Multiple blocks — of either kind, in any combination — may appear in a single reply; each is
  applied independently.
- All edits are validated against the file's current line count and checked for overlaps before
  anything is written — if any edit is invalid, the file is left completely unchanged and an
  error message explains why. An insertion counts as overlapping only if it lands strictly inside
  a replaced range; one anchored exactly at the boundary of a replacement (immediately before or
  after it) is allowed.
- Edits are applied from the bottom of the file upward internally, so line numbers given by the
  LLM (based on the original numbered content) remain valid even when a replacement spans a
  different number of lines than the range it replaces, or when insertions add new lines.
- Multiple insertions anchored at the exact same splice point are merged into one, applied in the
  order the LLM wrote them, so there's no ambiguity about which goes first.
- An instruction is required — `/redigera <filename>` alone returns an error asking for one.

Use cases:
- Fix a typo or small section without regenerating and resending the entire file.
- Apply a targeted correction the LLM identified after being asked to review a file.
- Make repeated small edits to a long file cheaply, since only the relevant lines are rewritten.
- Ask the LLM to write a brand-new function/method and have it appended inside the correct
  class or namespace, without risking existing code being overwritten.

---

### `/lista`

Lists every file currently stored in the agent folder.

```
/lista
→ ✓ Filer: notes.txt, prompt.txt, analys-prompt.txt
```

- Takes no arguments.
- Returns `"Inga filer finns i agentkatalogen."` when the folder is empty.
- The listing is also placed in `InjectedContext`, so if this command is invoked by the LLM
  itself during agentic tool-calling (see below), it can be fed back for the model to reason
  about which file to read next.

---

## Configuration

### Default Folder

When `AgentFilesFolder` is empty (the default), files are stored in:

```
%USERPROFILE%\Documents\OfflineAI\AgentFiles
```

The directory is created automatically on first use.

### Custom Folder via `appsettings.json`

```json
{
  "AppConfiguration": {
    "Folders": {
      "AgentFilesFolder": "D:/mina-filer/agent"
    }
  }
}
```

Or via User Secrets:

```bash
dotnet user-secrets set "AppConfiguration:Folders:AgentFilesFolder" "D:/mina-filer/agent"
```

---

## Registration (Dependency Injection)

Registered as a **Singleton** in `AiDashboard/Program.cs`:

```csharp
builder.Services.AddSingleton<IFileAgentService>(_ =>
{
    var agentDir = !string.IsNullOrWhiteSpace(appConfig.Folders.AgentFilesFolder)
        ? appConfig.Folders.AgentFilesFolder
        : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "OfflineAI", "AgentFiles");
    return new FileAgentService(agentDir);
});
```

### Reusing in Another Project

```csharp
// In any other project's DI setup:
services.AddSingleton<IFileAgentService>(
    _ => new FileAgentService("C:/my-agent-folder"));
```

---

## Integration in Home.razor.cs

`IFileAgentService` is injected into the page and commands are intercepted in `SendMessage()`:

```csharp
[Inject] private IFileAgentService FileAgent { get; set; } = default!;

private async Task SendMessage()
{
    // ...add user message to chat history...

    if (FileAgent.IsCommand(userMessage))
    {
        var result = await FileAgent.ExecuteAsync(userMessage);

        if (result.ResultType == FileAgentResultType.FileRead && result.IsSuccess
            && result.InjectedContext is not null)
        {
            // Forward instruction + file content to LLM
            var response = await Dashboard.SendMessageAsync(result.InjectedContext);
            // ...add AI response to chat...
        }
        else
        {
            // Show confirmation or error as system message
            // ...add result.Message to chat...
        }
    }
    else
    {
        // Regular message — let the LLM decide (agentic pattern) whether it needs a file tool.
        // AgenticChat primes it with the tool dictionary, detects any slash command in the
        // reply via string search, executes it, and feeds the result back for a final answer.
        // See AGENTIC-CHAT-TOOL-CALLING.md for the full flow.
        var agentResult = await AgenticChat.SendWithToolsAsync(userMessage, Dashboard.SendActiveAsync);
        // ...add agentResult.ToolInvocations as status messages, then agentResult.FinalResponse...
    }
}
```

---

## Security

| Threat | Mitigation |
|--------|-----------|
| Path traversal (`../secret.txt`) | `Path.GetFileName()` strips all directory components |
| Escaping the base directory | `Path.GetFullPath()` result is verified to start with `BaseDirectory` |
| Arbitrary absolute paths (`C:\Windows\...`) | Only the bare filename is used; absolute paths are reduced to their filename part |
| Working outside the user's chosen folder | The base directory is the active **workspace** — a user-selectable, persisted, runtime-switchable directory (see [WORKSPACE-CONFINEMENT.md](WORKSPACE-CONFINEMENT.md)); only the user can change it, never the LLM |

---

## Examples

### Workflow: Prompt from File

```
/skapa analys-prompt.txt
/fyll analys-prompt.txt Analysera följande text och ge mig tre förbättringsförslag på svenska: [text här]
/läs analys-prompt.txt Använd instruktionerna i filen och svara på svenska.
→ AI responds with three improvement suggestions in Swedish
```

### Workflow: Persistent Notes

```
/skapa mina-noter.txt
/fyll mina-noter.txt Projektet ska använda Clean Architecture med fyra lager.
/läs mina-noter.txt Påminn mig vad projektet ska använda för arkitektur.
→ AI: "Baserat på dina noter, projektet ska använda Clean Architecture..."
```

### Workflow: ASCII keyboard (no ä key)

```
/las mina-noter.txt Påminn mig vad projektet ska använda för arkitektur.
→ Same as /läs mina-noter.txt Påminn mig vad projektet ska använda för arkitektur.
```

### Workflow: Discover files before reading

```
/lista
→ ✓ Filer: analys-prompt.txt, mina-noter.txt
/läs mina-noter.txt Sammanfatta de tre viktigaste punkterna.
→ AI summarises mina-noter.txt
```

---

## Agentic Tool-Calling

These same slash commands double as a **tool dictionary** that the LLM can invoke itself —
without the user typing a command — while answering a regular chat question. For example, if
you ask "What's in mina-noter.txt?", the LLM can request `/läs mina-noter.txt Summarize this.`
on its own, get the result, and answer using it. Likewise, it can request
`/redigera mina-noter.txt Fix the typo on line 2.` to have specific lines corrected automatically,
or `/redigera fileservice.cs Lägg till en ny metod som validerar filnamn.` to have a brand-new
method inserted at the correct spot inside the right class, without overwriting existing code.

This pattern is implemented by `IAgenticChatService` and wired into both the Dashboard chat and
QuickAsk. See **[AGENTIC-CHAT-TOOL-CALLING.md](AGENTIC-CHAT-TOOL-CALLING.md)** for the full
design, prompt format, and round-trip flow.

---

## Files Changed

### Initial implementation (Dashboard / Home)

| File | Change |
|------|--------|
| `Services/FileAgent/FileAgentResult.cs` | New — result model and enum |
| `Services/FileAgent/IFileAgentService.cs` | New — service interface |
| `Services/FileAgent/FileAgentService.cs` | New — implementation |
| `Services/Configuration/AppConfiguration.cs` | Added `AgentFilesFolder` to `FolderSettings` |
| `AiDashboard/appsettings.json` | Added `AgentFilesFolder` key under `Folders` |
| `AiDashboard/Program.cs` | Singleton registration of `IFileAgentService` |
| `AiDashboard/Components/Pages/Home.razor.cs` | Injected service; command interception in `SendMessage()` |

### `/redigera` command

| File | Change |
|------|--------|
| `Services/FileAgent/LineEdit.cs` | New — line-range replacement model (`StartLine`, `EndLine`, `NewContent`) |
| `Services/FileAgent/FileAgentResult.cs` | Added `EditRequested` and `FileEdited` result types and `EditRequest()` factory |
| `Services/FileAgent/IFileAgentService.cs` | Added `TryExtractLineEdits`, `ApplyLineEditsAsync`, `StripEditMarkers` |
| `Services/FileAgent/FileAgentService.cs` | Added `/redigera` parsing, numbered-content prompt, block extraction, validation, and bottom-up apply logic |
| `Services/AgentTools/AgenticChatService.cs` | Added `EditRequested` round-trip branch mirroring `FillRequested` |
| `Services/AgentTools/BuiltInFileTools.cs` | Added `edit_file_lines` `[KernelFunction]` reusing `ApplyLineEditsAsync` |
| `AiDashboard/Components/Pages/Home.razor.cs` | Added `EditRequested` branch in `SendMessage()` |
| `AiDashboard/Components/Pages/QuickAskPage.razor` | Added `EditRequested` branch in `SendQuestion()` |
| `Services.Tests/FileAgent/FileAgentServiceTests.cs` | New — unit tests for line-edit extraction and application |

### `/redigera` insertion support (add new code, e.g. new methods)

| File | Change |
|------|--------|
| `Services/FileAgent/LineEdit.cs` | Added `LineEditKind` (`Replace`, `InsertAfter`, `InsertBefore`), a `Kind` property, and `InsertAfterLine()` / `InsertBeforeLine()` factories |
| `Services/FileAgent/FileAgentService.cs` | `EditBlockRegex` now also matches `INFOGA_EFTER=N` / `INFOGA_FÖRE=N` (ASCII `INFOGA_FORE`); `TryExtractLineEdits` parses insertion blocks; `ApplyLineEditsAsync` rewritten to normalize replacements/insertions into ranges, merge coincident insertions, validate overlaps, and apply bottom-up; `/redigera` prompt now explains when to insert vs. replace and how to pick an anchor line inside the correct class/namespace |
| `Services/FileAgent/IFileAgentService.cs` | Updated XML docs for `TryExtractLineEdits` / `ApplyLineEditsAsync` to describe insertion blocks |
| `Services/AgentTools/BuiltInFileTools.cs` | Added `insert_file_lines` `[KernelFunction]` using `LineEdit.InsertAfterLine`/`InsertBeforeLine` |
| `Services.Tests/FileAgent/FileAgentServiceTests.cs` | Added insertion parsing/application tests (top/end of file, multi-line content, merged same-anchor insertions, boundary-adjacent vs. strictly-inside overlap rules) |

### `/lista` command

| File | Change |
|------|--------|
| `Services/FileAgent/FileAgentResult.cs` | Added `FilesListed` result type and `ListSuccess()` factory |
| `Services/FileAgent/IFileAgentService.cs` | Documented `/lista` in `IsCommand` |
| `Services/FileAgent/FileAgentService.cs` | Added `

### Agentic tool-calling + instruction-required `/läs`

| File | Change |
|------|--------|
| `Services/FileAgent/FileAgentService.cs` | `/läs` now requires `<filnamn> <instruktion>`; added `ReadFileRawAsync`, `GetToolDescriptions()`, `BuildToolsSystemPrompt()`, `TryFindAgentCommand()` |
| `Services/FileAgent/IFileAgentService.cs` | Declared the new tool-calling helper methods |
| `Services/AgentTools/IAgenticChatService.cs` | New — `ToolInvocation`, `AgenticChatResult`, `SendWithToolsAsync` contract |
| `Services/AgentTools/AgenticChatService.cs` | New — implements the prime/detect/execute/feed-back loop |
| `Services/AgentTools/BuiltInFileTools.cs` | `read_file` now calls `ReadFileRawAsync` (no instruction needed for SK tool calling) |
| `AiDashboard/Program.cs` | Singleton registration of `IAgenticChatService` |
| `AiDashboard/Components/Pages/Home.razor.cs` | Regular (non-slash-command) messages routed through `AgenticChat.SendWithToolsAsync` |
| `AiDashboard/Components/Pages/QuickAskPage.razor` | Regular questions routed through `AgenticChat.SendWithToolsAsync` |

### QuickAsk extension

| File | Change |
|------|--------|
| `AiDashboard/Components/Pages/QuickAskPage.razor` | `@inject IFileAgentService`; command interception in `SendQuestion()` |

> See [QUICKASK-FILE-AGENT-AND-MAXTOKENS.md](QUICKASK-FILE-AGENT-AND-MAXTOKENS.md) for full QuickAsk details.

### Multi-workspace confinement

| File | Change |
|------|--------|
| `Services/Workspace/WorkspaceInfo.cs` | New — workspace model (`Name`, `Path`) |
| `Services/Workspace/IWorkspaceService.cs` | New — workspace list/add/remove/select contract |
| `Services/Workspace/WorkspaceService.cs` | New — JSON-persisted implementation, default-workspace seeding |
| `Services/FileAgent/FileAgentService.cs` | `BaseDirectory` made mutable; added `SetBaseDirectory(string)` to re-confine at runtime |
| `Services/FileAgent/IFileAgentService.cs` | Declared `SetBaseDirectory(string)` |
| `AiDashboard/Program.cs` | Singleton registration of `IWorkspaceService`; `IFileAgentService` now rooted at the active workspace and re-confined via `ActiveWorkspaceChanged` |
| `AiDashboard/State/DashboardState.cs` | Exposes workspace state/actions; shows a status message on every workspace switch |
| `AiDashboard/Components/Pages/Components/WorkspaceSection.razor` | New — sidebar UI for viewing, switching, adding, and removing workspaces |
| `AiDashboard/Components/Pages/Components/Sidebar.razor` | Added `<WorkspaceSection />` |
| `Services.Tests/Workspace/WorkspaceServiceTests.cs` | New — unit tests for workspace persistence and selection |
| `Presentation.AiDashboard.Tests/Components/WorkspaceSectionTests.cs` | New — bUnit tests for the workspace sidebar UI |

> See **[WORKSPACE-CONFINEMENT.md](WORKSPACE-CONFINEMENT.md)** for the full workspace model, persistence format, and safety guarantees.
