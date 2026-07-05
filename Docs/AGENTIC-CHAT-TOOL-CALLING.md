# Agentic Chat — Lightweight Tool-Calling for QuickAsk & Dashboard

## Summary

Both the **Dashboard chat** (`Home.razor.cs`) and **QuickAsk** (`QuickAskPage.razor`) send
regular (non-slash-command) messages through a lightweight, text-based agentic pattern: the LLM
is told about the available [File Agent](FILE-AGENT-COMMANDS.md) slash commands **and** the
built-in utility commands (current time/date, config-driven API calls — see below) as a single
tool dictionary, and if its reply requests one, the app executes it and feeds the result back so
the LLM can produce a final, tool-informed answer — all without the user typing a command
themselves. The entire tool-call loop stays internal to the service: only short, human-readable
status lines (via an `onToolStatus` callback) and the final answer are meant to reach the user, so
the raw priming prompts and intermediate LLM replies never leak into the chat transcript.

This is implemented by `IAgenticChatService` / `AgenticChatService` (drives the loop) together with
`IFileAgentService` (file commands) and `IUtilityToolsService` (time/date/API commands), all in
the `Services` project (`Services.AgentTools` / `Services.FileAgent` namespaces). It is a
**separate, simpler mechanism** than the JSON/Semantic-Kernel structured tool calling used by the
Gemma 4 CLI backend (see [gemma4-cli-feature.md](gemma4-cli-feature.md#tool-calling)). It works
with *any* backend that can only produce plain text — the pooled "Classic" backend and the
Gemma 4 CLI backend alike — because it never depends on structured JSON output.

---

## Why two tool-calling mechanisms?

| | Agentic chat (this doc) | Semantic Kernel tool calling |
|---|---|---|
| Service | `IAgenticChatService` | `IGemma4AgentService.ChatWithToolsAsync` + `IAgentToolRegistry` |
| Tool detection | Plain string search for a known slash command in the plain-text reply | Model returns structured JSON tool-call array |
| Backend requirement | Any backend that returns text (Classic pooled subprocess, Gemma 4 CLI) | Requires a model capable of Semantic Kernel function calling |
| Tool set | `IFileAgentService` slash commands (`/skapa`, `/fyll`, `/läs`, `/redigera`, `/lista`) + `IUtilityToolsService` commands (`/tid`, `/datum`, `/api <slutpunkt> <instruktion>`) | `BuiltInFileTools` `[KernelFunction]` methods (`create_file`, `read_file`, `read_pdf`, `write_file`, `edit_file_lines`, `insert_file_lines`, `list_files`) + `BuiltInUtilityTools` (`get_current_time`, `get_current_date`, `call_api`) |
| Where used | Regular chat messages in Dashboard + QuickAsk | Wherever `ChatWithToolsAsync` is explicitly called with a kernel |

Both mechanisms are backed by the same underlying file/utility operations (`IFileAgentService`,
`IUtilityToolsService`), just exposed through different calling conventions.

---

## Architecture

```
Services/
├── AgentTools/
│   ├── IAgenticChatService.cs   — ToolInvocation, AgenticChatResult, SendWithToolsAsync contract
│   ├── AgenticChatService.cs    — Implementation: prime → detect → execute → feed back loop
│   ├── IUtilityToolsService.cs  — UtilityToolResult, /tid, /datum, /api contract
│   ├── UtilityToolsService.cs   — Implementation: current time/date + named HTTP API calls
│   └── BuiltInUtilityTools.cs   — Semantic Kernel [KernelFunction] wrapper around IUtilityToolsService
└── FileAgent/
    ├── IFileAgentService.cs     — File-agent slash command contract (see FILE-AGENT-COMMANDS.md)
    └── FileAgentService.cs     — File-agent implementation, workspace-confined via SetBaseDirectory
```

### Interface

```csharp
public sealed record ToolInvocation(string Command, string ResultSummary);

public sealed record AgenticChatResult(string FinalResponse, IReadOnlyList<ToolInvocation> ToolInvocations);

public interface IAgenticChatService
{
    Task<AgenticChatResult> SendWithToolsAsync(
        string userMessage,
        Func<string, Task<string>> sendToLlm,
        CancellationToken cancellationToken = default,
        Action<string>? onToolStatus = null);
}
```

- **`userMessage`** — the user's original question.
- **`sendToLlm`** — a backend-agnostic delegate that sends a prompt to whichever LLM backend is
  currently active and returns its raw text reply. Callers pass `Dashboard.SendActiveAsync` (Home)
  or `Dashboard.SendQuickAskActiveAsync` (QuickAsk), so the same service works regardless of the
  selected `LlmBackend` (`Classic` or `Gemma4Cli`).
- **`onToolStatus`** — optional callback invoked with a short status line (e.g.
  `"🔧 Kör: /api väder ..."`) immediately *before* each internal tool call executes, so the caller
  can surface live progress (both pages wire this to `DashboardState.StatusMessage`) without
  exposing the raw tool-loop prompts/replies to the user. The loop itself always stays internal —
  only these short status strings and the final answer are meant to reach the user.
- **`AgenticChatResult.FinalResponse`** — the answer to display to the user.
- **`AgenticChatResult.ToolInvocations`** — an ordered log of every tool call made along the way,
  each with the exact command used and a short result summary, so the UI can show
  `🔧 Used /läs notes.txt ... — ✓ Fil läst: notes.txt`-style status messages before the final answer.

---

## The tool dictionary sent to the LLM

`IFileAgentService.GetToolDescriptions()` returns the tool set as a dictionary (command signature
→ natural-language description):

```csharp
IReadOnlyDictionary<string, string> GetToolDescriptions() => new Dictionary<string, string>
{
    ["/läs <filnamn> <instruktion>"] = "Läser innehållet i en fil i agentkatalogen och skickar det tillsammans med instruktionen till dig, t.ex. \"/läs text.txt Sammanfatta innehållet.\"",
    ["/skapa <filnamn>"]             = "Skapar en ny, tom fil med angivet namn i agentkatalogen.",
    ["/fyll <filnamn> <beskrivning>"] = "Genererar innehåll utifrån beskrivningen och sparar det i filen.",
    ["/redigera <filnamn> <instruktion>"] = "Läser en fil med radnummer, ber dig ange exakt vilka rader som ska ersättas och med vad (eller var ny kod, t.ex. en ny funktion, ska infogas utan att skriva över något), och uppdaterar sedan filen automatiskt utifrån ditt svar.",
    ["/lista"]                        = "Listar alla filer som just nu finns i agentkatalogen."
};
```

`IFileAgentService.BuildToolsSystemPrompt()` wraps this dictionary in an instructional preamble
(Swedish, matching the rest of the in-chat file-agent UX) that tells the model:
- it has access to these tools,
- to write the exact command on its own line if it needs one,
- and to write nothing else on that line so it can be reliably detected,
- otherwise, to just answer directly in plain text.

### Utility tool descriptions are appended to the same prompt

When an `IUtilityToolsService` is configured (it is, in `AiDashboard`), `AgenticChatService`
appends its tool descriptions to the same prompt in the identical `"- {command} : {description}"`
bullet format, so the LLM sees **one unified tool list** regardless of which service ultimately
executes the command:

```csharp
IReadOnlyDictionary<string, string> GetToolDescriptions() => new Dictionary<string, string>
{
    ["/tid"] = "Returnerar aktuellt klockslag.",
    ["/datum"] = "Returnerar dagens datum.",
    ["/api <slutpunkt> <instruktion>"] = "Anropar ett förkonfigurerat API-slutpunkt (t.ex. \"väder\") och kombinerar svaret med din instruktion."
};
```

`/api` only ever accepts a **named endpoint** (e.g. `väder`) that must already exist in
`AppConfiguration.AgentTools.Endpoints` — the LLM can never supply an arbitrary URL. See
[Utility tools: /tid, /datum, /api](#utility-tools-tid-datum-api) below for the full contract.

---

## The round-trip loop (`AgenticChatService.SendWithToolsAsync`)

```
userMessage
    ↓
BuildToolsSystemPrompt() + utility tool descriptions + "Fråga: {userMessage}"
    ── "start message" ──▶ sendToLlm
    ↓
response = LLM reply
    ↓
┌──────────────────────────── loop (max MaxToolCallRounds rounds, default 3) ───────────────────┐
│ TryFindAgentCommand(response, out command)?             (file-agent commands checked FIRST) │
│   yes → ExecuteAsync(command)                                                              │
│           ├─ FillRequested (/fyll) → sendToLlm(LlmPrompt) to generate content,             │
│           │    TryExtractFileContent + WriteExtractedContentAsync, then                   │
│           │    sendToLlm("Tool result: ...") for confirmation + original answer           │
│           ├─ EditRequested (/redigera) → sendToLlm(LlmPrompt) to get line edits,          │
│           │    TryExtractLineEdits + ApplyLineEditsAsync, then                            │
│           │    sendToLlm("Tool result: ...") for confirmation + original answer           │
│           └─ otherwise (/skapa, /läs, /lista, errors)                                     │
│                → sendToLlm("Verktygsresultat för \"{command}\": {result}\n\n" +           │
│                             "Använd informationen ovan för att besvara: {userMessage}")     │
│   no  → else check TryFindCommand on IUtilityToolsService (/tid, /datum, /api)              │
│           yes → onToolStatus?.Invoke("🔧 Kör: {command}"); ExecuteAsync(command)             │
│                 → sendToLlm("Verktygsresultat för \"{command}\": {result}\n\n" + ...)      │
│   no (neither matched) → break, response is the final answer                                │
│         response = new LLM reply; ToolInvocations.Add(...); loop again                      │
└─────────────────────────────────────────────────────────────────────────────────┘
    ↓
return AgenticChatResult(response, invocations)
```

Key details:
- **Detection is plain string search**, not JSON parsing: `TryFindAgentCommand` splits the LLM
  reply into lines and returns the first line that `IsCommand` recognises as a known slash
  command. This keeps the mechanism trivial to reason about and backend-agnostic.
- **File-agent commands are checked before utility commands** on every round: if a line matches
  both (which shouldn't normally happen since command prefixes don't overlap), the file agent
  wins. `IUtilityToolsService` is optional — when `null` (no utility service configured), only
  file-agent commands are detected/executed at all.
- **`onToolStatus` fires immediately before *every* tool execution**, file-agent or utility alike,
  with the exact command string (e.g. `"🔧 Kör: /api väder Hur är vädret idag?"`). Both `Home` and
  `QuickAskPage` wire this straight to `Dashboard.StatusMessage` so the sidebar/status area shows
  live progress while the loop runs, without exposing internal prompts.
- **`/läs` inside the loop still requires an instruction** (`/läs <filnamn> <instruktion>`) — the
  LLM is taught the exact signature via the tool dictionary, so it naturally includes one when
  it requests the tool itself. See the `/läs` command reference in
  [FILE-AGENT-COMMANDS.md](FILE-AGENT-COMMANDS.md) for why an instruction is mandatory.
- **`/fyll` needs a second round-trip** because the model itself must generate the file content
  (wrapped in `<FILE>` / `<ENDFILE>` markers) before anything can be saved — this mirrors the
  direct slash-command `/fyll` flow used elsewhere in the app.
- **`/redigera` also needs a second round-trip**: the model is shown the file with line numbers
  and must reply with one or more `<REDIGERA RAD=...>` blocks (replace) and/or
  `<REDIGERA INFOGA_EFTER=...>` / `<REDIGERA INFOGA_FÖRE=...>` blocks (insert brand-new code, e.g.
  a new method, without removing anything — see the `/redigera` command reference in
  [FILE-AGENT-COMMANDS.md](FILE-AGENT-COMMANDS.md)) before `ApplyLineEditsAsync` validates and
  applies the edits — same two-phase shape as `/fyll`, just producing targeted line
  replacements/insertions instead of a full file rewrite.
- **`/api` (utility) does not need a second round-trip**: the endpoint is called synchronously
  inside `ExecuteAsync`/`CallNamedApiAsync`, and its (possibly truncated) response text is fed
  straight back to the LLM as tool result context, same shape as the "otherwise" file-agent branch.
- **`MaxToolCallRounds` (default 3, configurable via `AppConfiguration.AgentTools.MaxToolCallRounds`)**
  caps the loop so a confused model can't request tools indefinitely instead of answering; after
  the cap, whatever the LLM last returned is used as the final answer.
- Every tool call — successful or not — is appended to `ToolInvocations` so the UI can show what
  happened, even for `/fyll` failures (e.g. missing `<FILE>` markers) or `/api` failures (e.g.
  unknown endpoint name, timeout).

---

## Utility tools: `/tid`, `/datum`, `/api`

`IUtilityToolsService` / `UtilityToolsService` (`Services/AgentTools/`) implement three built-in
commands that mirror the shape of `IFileAgentService` (command detection + execution + tool
descriptions) so `AgenticChatService` can drive both services through the same
prime → detect → execute → feed-back loop:

| Command | Description |
|---|---|
| `/tid` | Returns the current local time. |
| `/datum` | Returns today's date. |
| `/api <slutpunkt> <instruktion>` | Calls a **named, pre-configured** HTTP endpoint and combines its response with the given instruction, ready to forward to the LLM. |

```csharp
public sealed record UtilityToolResult(bool IsSuccess, string Message, string? InjectedContext = null);

public interface IUtilityToolsService
{
    bool IsCommand(string input);
    Task<UtilityToolResult> ExecuteAsync(string input);
    Task<UtilityToolResult> CallNamedApiAsync(string endpointName, string instruction = "");
    IReadOnlyList<string> GetApiEndpointNames();
    IReadOnlyDictionary<string, string> GetToolDescriptions();
    bool TryFindCommand(string llmResponse, out string command);
}
```

### `/api` is endpoint-name-only — never an arbitrary URL

The LLM can only ever select an endpoint **by name** (e.g. `väder`); it can never supply a raw
URL. Each endpoint is fully defined ahead of time in configuration
(`AppConfiguration.AgentTools.Endpoints`), including its actual URL, HTTP method, headers,
timeout, and max response length:

```json
"AgentTools": {
  "MaxToolCallRounds": 3,
  "Endpoints": [
    {
      "Name": "väder",
      "Description": "Hämtar aktuellt väder.",
      "Url": "https://api.example.com/weather?q={input}",
      "Method": "GET",
      "Headers": { "X-Api-Key": "..." },
      "TimeoutMs": 10000,
      "MaxResponseLength": 4000
    }
  ]
}
```

- `{input}` in `Url` is substituted with the instruction text supplied after the endpoint name.
- Requests are made through a named `HttpClient` (`"AgentApiTools"`, registered via
  `AddHttpClient("AgentApiTools")`), never a client the LLM controls.
- Responses longer than `MaxResponseLength` are truncated before being handed back to the LLM.
- Calling an endpoint name that isn't configured, or a request that times out, returns
  `UtilityToolResult.Failure(...)` with a user-facing message — the loop still feeds that failure
  message back to the LLM so it can respond gracefully instead of the tool call silently vanishing.

### Semantic Kernel exposure

For Gemma 4 CLI's structured tool calling, `BuiltInUtilityTools` wraps the same
`IUtilityToolsService` as `[KernelFunction]` methods (`get_current_time`, `get_current_date`,
`call_api`), so both tool-calling mechanisms share one implementation and one safety boundary
for outbound HTTP calls.

---

## Wiring into the chat pages

Both pages first check `IFileAgentService.IsCommand(input)` for a **user-typed** slash command
(handled exactly as described in [FILE-AGENT-COMMANDS.md](FILE-AGENT-COMMANDS.md)); only when the
message is *not* a direct command does it fall through to the agentic pattern:

```csharp
// Home.razor.cs / QuickAskPage.razor — regular question branch
var agentResult = await AgenticChat.SendWithToolsAsync(
    userMessage,
    Dashboard.SendActiveAsync,
    onToolStatus: status => Dashboard.StatusMessage = status);

foreach (var invocation in agentResult.ToolInvocations)
{
    // Show a "🔧 Used /läs notes.txt ... — ✓ Fil läst: notes.txt" status message
}

// Show agentResult.FinalResponse as the AI's answer
```

The `onToolStatus` callback fires once per tool call, right before it runs, letting
`DashboardState.StatusMessage` reflect live progress (e.g. `"🔧 Kör: /tid"`) in the UI while the
internal loop is still executing — the user never sees the priming prompt or intermediate replies,
only these short status lines followed by the final answer.

| Page | Delegate passed to `SendWithToolsAsync` |
|------|------------------------------------------|
| Home (Dashboard) — `Home.razor.cs` | `Dashboard.SendActiveAsync` |
| QuickAsk — `QuickAskPage.razor` | `Dashboard.SendQuickAskActiveAsync` |

Both delegates route to whichever backend is currently selected (`Classic` or `Gemma4Cli`) via
`DashboardState`, so the agentic pattern works transparently regardless of backend.

---

## Example

```
User: "Vad står det i mina-noter.txt? Sammanfatta det kort."

→ Start message primes the LLM with the tool dictionary + the question.
→ LLM replies:
    /läs mina-noter.txt Sammanfatta innehållet kort.
→ App detects the command, executes it, gets back:
    "Instruktion: Sammanfatta innehållet kort.

     Filens innehåll (mina-noter.txt):
     Projektet ska använda Clean Architecture med fyra lager."
→ App sends that back to the LLM with instructions to answer the original question.
→ LLM: "Enligt dina anteckningar ska projektet använda Clean Architecture med fyra lager."

UI shows:
    🔧 Used /läs mina-noter.txt Sammanfatta innehållet kort. — ✓ Fil läst: mina-noter.txt
    Enligt dina anteckningar ska projektet använda Clean Architecture med fyra lager.
```

---

## Registration (Dependency Injection)

All wired up as **Singletons** in `AiDashboard/Program.cs`:

```csharp
// The file agent is rooted at the active workspace directory; SetBaseDirectory(...)
// re-confines it whenever the user switches workspaces, so the LLM can never read/write
// outside the selected directory (see WORKSPACE-CONFINEMENT.md).
builder.Services.AddSingleton<IFileAgentService>(sp =>
{
    var workspaceService = sp.GetRequiredService<IWorkspaceService>();
    var fileAgent = new FileAgentService(workspaceService.GetActiveWorkspace().Path);
    workspaceService.ActiveWorkspaceChanged += workspace => fileAgent.SetBaseDirectory(workspace.Path);
    return fileAgent;
});

// Utility tools (/tid, /datum, /api) resolve API endpoints only from
// AppConfiguration.AgentTools.Endpoints — the LLM can never supply an arbitrary URL.
builder.Services.AddHttpClient("AgentApiTools");
builder.Services.AddSingleton<IUtilityToolsService, UtilityToolsService>();

// The agentic chat loop combines both tool sources and caps round trips via
// AppConfiguration.AgentTools.MaxToolCallRounds.
builder.Services.AddSingleton<IAgenticChatService>(sp =>
    new AgenticChatService(
        sp.GetRequiredService<IFileAgentService>(),
        sp.GetRequiredService<IUtilityToolsService>(),
        appConfig.AgentTools.MaxToolCallRounds));
```

`IUtilityToolsService` is an **optional** constructor argument on `AgenticChatService` — passing
`null` (or omitting it) falls back to file-agent-only behavior, which is exactly what the existing
unit tests exercise for the original, simpler flow.

---

## Files Involved

| File | Role |
|------|------|
| `Services/AgentTools/IAgenticChatService.cs` | `ToolInvocation`, `AgenticChatResult`, `SendWithToolsAsync` contract (incl. `onToolStatus`) |
| `Services/AgentTools/AgenticChatService.cs` | Prime/detect/execute/feed-back loop implementation, file + utility command dispatch |
| `Services/AgentTools/IUtilityToolsService.cs` | `UtilityToolResult`, `/tid`/`/datum`/`/api` contract |
| `Services/AgentTools/UtilityToolsService.cs` | Time/date + named-endpoint HTTP API implementation |
| `Services/AgentTools/BuiltInUtilityTools.cs` | Semantic Kernel `[KernelFunction]` wrapper around `IUtilityToolsService` |
| `Services/FileAgent/IFileAgentService.cs` | `GetToolDescriptions`, `BuildToolsSystemPrompt`, `TryFindAgentCommand`, `SetBaseDirectory` — the tool dictionary, detection, and workspace-confinement helpers |
| `Services/FileAgent/FileAgentService.cs` | Tool dictionary contents, command execution, and runtime base-directory switching |
| `Services/Workspace/IWorkspaceService.cs` / `WorkspaceService.cs` | Persisted, user-selectable workspaces that confine the file agent (see [WORKSPACE-CONFINEMENT.md](WORKSPACE-CONFINEMENT.md)) |
| `Services/Configuration/AppConfiguration.cs` | `AgentToolsSettings` (`MaxToolCallRounds`, `Endpoints`) consumed by both services |
| `AiDashboard/Program.cs` | DI registration for workspace, file-agent, utility-tools, and agentic-chat services |
| `AiDashboard/State/DashboardState.cs` | Exposes workspace actions and receives `StatusMessage` updates from `onToolStatus` |
| `AiDashboard/Components/Pages/Home.razor.cs` | Wires regular dashboard chat messages to `AgenticChat.SendWithToolsAsync` with `onToolStatus` |
| `AiDashboard/Components/Pages/QuickAskPage.razor` | Wires regular QuickAsk questions to `AgenticChat.SendWithToolsAsync` with `onToolStatus` |

## Related Docs

- [FILE-AGENT-COMMANDS.md](FILE-AGENT-COMMANDS.md) — the underlying slash commands and their direct (non-agentic) usage
- [WORKSPACE-CONFINEMENT.md](WORKSPACE-CONFINEMENT.md) — how workspaces confine file-agent access and how to add/switch them
- [QUICKASK-FILE-AGENT-AND-MAXTOKENS.md](QUICKASK-FILE-AGENT-AND-MAXTOKENS.md) — QuickAsk-specific file agent details
- [gemma4-cli-feature.md](gemma4-cli-feature.md#tool-calling) — the separate JSON/Semantic-Kernel tool-calling mechanism for Gemma 4 CLI
