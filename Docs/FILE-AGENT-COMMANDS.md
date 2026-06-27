# File Agent - Slash Commands for File Management

## Summary

The **File Agent** adds slash-command support to the chat interface, letting users create,
write to, and read text files without leaving the chat. Typed commands are intercepted before
the message reaches the LLM; file content read via `/läs` is forwarded to the LLM as the
actual prompt, enabling prompt-from-file workflows.

---

## Features

- `/skapa <fil>` — creates an empty text file in the agent folder
- `/fyll <fil> <innehåll>` — writes inline content to a file (creates if needed)
- `/läs <fil>` — reads the file and sends its content to the LLM as the prompt
- `/las <fil>` — ASCII fallback for `/läs` (keyboards without ä)
- **Path traversal protection** — only bare filenames are accepted; directory segments are stripped
- **Configurable base directory** — defaults to `Documents\OfflineAI\AgentFiles`, overridable in `appsettings.json`
- **Reusable service** — lives in the `Services` project (`Services.FileAgent` namespace), injectable in any project in the solution

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
    └── FileAgentService.cs     — Implementation
```

### Interface

```csharp
public interface IFileAgentService
{
    string BaseDirectory { get; }
    bool IsCommand(string input);
    Task<FileAgentResult> ExecuteAsync(string input);
}
```

### Result Model

```csharp
public enum FileAgentResultType
{
    NotACommand,   // Not a command — pass through to AI normally
    FileCreated,   // /skapa succeeded
    FileFilled,    // /fyll succeeded
    FileRead,      // /läs succeeded — InjectedContext contains file content
    Error          // Command recognised but failed
}

public class FileAgentResult
{
    public FileAgentResultType ResultType { get; init; }
    public bool IsSuccess      { get; init; }
    public string Message      { get; init; }   // User-facing confirmation or error text
    public string? InjectedContext { get; init; } // File content (FileRead only)
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
│ FileRead          │ → Dashboard.SendMessageAsync(InjectedContext)
└───────────────────┘          ↓
                        LLM responds to file content
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

### `/läs <filename>` &nbsp;/ `/las <filename>`

Reads the file and sends its content to the LLM as the prompt.

```
/läs prompt.txt
→ (file content is forwarded to the LLM)
→ AI responds to whatever is in the file
```

Use cases:
- Store long or reusable prompts in a file and load them on demand.
- Keep notes or reference material, then ask the LLM to summarise or answer questions about them.
- Build prompt templates edited outside the chat.

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
            // Forward file content to LLM
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
        // Regular LLM message
        var response = await Dashboard.SendMessageAsync(userMessage);
        // ...add AI response to chat...
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

---

## Examples

### Workflow: Prompt from File

```
/skapa analys-prompt.txt
/fyll analys-prompt.txt Analysera följande text och ge mig tre förbättringsförslag på svenska: [text här]
/läs analys-prompt.txt
→ AI responds with three improvement suggestions in Swedish
```

### Workflow: Persistent Notes

```
/skapa mina-noter.txt
/fyll mina-noter.txt Projektet ska använda Clean Architecture med fyra lager.
/läs mina-noter.txt
→ AI: "Baserat på dina noter, projektet ska använda Clean Architecture..."
```

### Workflow: ASCII keyboard (no ä key)

```
/las mina-noter.txt
→ Same as /läs mina-noter.txt
```

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

### QuickAsk extension

| File | Change |
|------|--------|
| `AiDashboard/Components/Pages/QuickAskPage.razor` | `@inject IFileAgentService`; command interception in `SendQuestion()` |

> See [QUICKASK-FILE-AGENT-AND-MAXTOKENS.md](QUICKASK-FILE-AGENT-AND-MAXTOKENS.md) for full QuickAsk details.
