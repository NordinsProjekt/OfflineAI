# Agentic Chat — Lightweight Tool-Calling for QuickAsk & Dashboard

## Summary

Both the **Dashboard chat** (`Home.razor.cs`) and **QuickAsk** (`QuickAskPage.razor`) send
regular (non-slash-command) messages through a lightweight, text-based agentic pattern: the LLM
is told about the available [File Agent](FILE-AGENT-COMMANDS.md) slash commands as a tool
dictionary, and if its reply requests one, the app executes it and feeds the result back so the
LLM can produce a final, tool-informed answer — all without the user typing a command
themselves.

This is implemented by `IAgenticChatService` / `AgenticChatService` in the `Services` project
(`Services.AgentTools` namespace), and is a **separate, simpler mechanism** than the JSON/
Semantic-Kernel structured tool calling used by the Gemma 4 CLI backend
(see [gemma4-cli-feature.md](gemma4-cli-feature.md#tool-calling)). It works with *any* backend
that can only produce plain text — the pooled "Classic" backend and the Gemma 4 CLI backend
alike — because it never depends on structured JSON output.

---

## Why two tool-calling mechanisms?

| | Agentic chat (this doc) | Semantic Kernel tool calling |
|---|---|---|
| Service | `IAgenticChatService` | `IGemma4AgentService.ChatWithToolsAsync` + `IAgentToolRegistry` |
| Tool detection | Plain string search for a known slash command in the plain-text reply | Model returns structured JSON tool-call array |
| Backend requirement | Any backend that returns text (Classic pooled subprocess, Gemma 4 CLI) | Requires a model capable of Semantic Kernel function calling |
| Tool set | `IFileAgentService` slash commands (`/skapa`, `/fyll`, `/läs`, `/redigera`, `/lista`) | `BuiltInFileTools` `[KernelFunction]` methods (`create_file`, `read_file`, `write_file`, `edit_file_lines`, `insert_file_lines`, `list_files`) |
| Where used | Regular chat messages in Dashboard + QuickAsk | Wherever `ChatWithToolsAsync` is explicitly called with a kernel |

Both mechanisms are backed by the same underlying file operations (`IFileAgentService`), just
exposed through different calling conventions.

---

## Architecture

```
Services/
└── AgentTools/
    ├── IAgenticChatService.cs   — ToolInvocation, AgenticChatResult, SendWithToolsAsync contract
    └── AgenticChatService.cs    — Implementation: prime → detect → execute → feed back loop
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
        CancellationToken cancellationToken = default);
}
```

- **`userMessage`** — the user's original question.
- **`sendToLlm`** — a backend-agnostic delegate that sends a prompt to whichever LLM backend is
  currently active and returns its raw text reply. Callers pass `Dashboard.SendActiveAsync` (Home)
  or `Dashboard.SendQuickAskActiveAsync` (QuickAsk), so the same service works regardless of the
  selected `LlmBackend` (`Classic` or `Gemma4Cli`).
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

---

## The round-trip loop (`AgenticChatService.SendWithToolsAsync`)

```
userMessage
    ↓
BuildToolsSystemPrompt() + "Fråga: {userMessage}"   ── "start message" ──▶ sendToLlm
    ↓
response = LLM reply
    ↓
┌─────────────────────────────── loop (max 3 rounds) ───────────────────────────────┐
│ TryFindAgentCommand(response, out command)?                                       │
│   no  → break, response is the final answer                                       │
│   yes → ExecuteAsync(command)                                                     │
│           ├─ FillRequested (/fyll) → sendToLlm(LlmPrompt) to generate content,     │
│           │    TryExtractFileContent + WriteExtractedContentAsync, then           │
│           │    sendToLlm("Tool result: ...") for confirmation + original answer   │
│           ├─ EditRequested (/redigera) → sendToLlm(LlmPrompt) to get line edits,  │
│           │    TryExtractLineEdits + ApplyLineEditsAsync, then                    │
│           │    sendToLlm("Tool result: ...") for confirmation + original answer   │
│           └─ otherwise (/skapa, /läs, /lista, errors)                             │
│                → sendToLlm("Verktygsresultat för \"{command}\": {result}\n\n" +   │
│                             "Använd informationen ovan för att besvara: {userMessage}") │
│         response = new LLM reply; ToolInvocations.Add(...); loop again           │
└─────────────────────────────────────────────────────────────────────────────────┘
    ↓
return AgenticChatResult(response, invocations)
```

Key details:
- **Detection is plain string search**, not JSON parsing: `TryFindAgentCommand` splits the LLM
  reply into lines and returns the first line that `IsCommand` recognises as a known slash
  command. This keeps the mechanism trivial to reason about and backend-agnostic.
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
- **`MaxToolCallRounds = 3`** caps the loop so a confused model can't request tools indefinitely
  instead of answering; after the cap, whatever the LLM last returned is used as the final answer.
- Every tool call — successful or not — is appended to `ToolInvocations` so the UI can show what
  happened, even for `/fyll` failures (e.g. missing `<FILE>` markers).

---

## Wiring into the chat pages

Both pages first check `IFileAgentService.IsCommand(input)` for a **user-typed** slash command
(handled exactly as described in [FILE-AGENT-COMMANDS.md](FILE-AGENT-COMMANDS.md)); only when the
message is *not* a direct command does it fall through to the agentic pattern:

```csharp
// Home.razor.cs / QuickAskPage.razor — regular question branch
var agentResult = await AgenticChat.SendWithToolsAsync(userMessage, Dashboard.SendActiveAsync);

foreach (var invocation in agentResult.ToolInvocations)
{
    // Show a "🔧 Used /läs notes.txt ... — ✓ Fil läst: notes.txt" status message
}

// Show agentResult.FinalResponse as the AI's answer
```

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

Registered as a **Singleton** in `AiDashboard/Program.cs`, depending only on `IFileAgentService`:

```csharp
// Register the lightweight, text-based agentic chat service used by QuickAsk and the
// Dashboard chat: tells the LLM about the IFileAgentService slash commands and executes
// any it requests, feeding the result back for a final answer.
builder.Services.AddSingleton<IAgenticChatService, AgenticChatService>();
```

---

## Files Involved

| File | Role |
|------|------|
| `Services/AgentTools/IAgenticChatService.cs` | `ToolInvocation`, `AgenticChatResult`, `SendWithToolsAsync` contract |
| `Services/AgentTools/AgenticChatService.cs` | Prime/detect/execute/feed-back loop implementation |
| `Services/FileAgent/IFileAgentService.cs` | `GetToolDescriptions`, `BuildToolsSystemPrompt`, `TryFindAgentCommand` — the tool dictionary and detection helpers |
| `Services/FileAgent/FileAgentService.cs` | Tool dictionary contents and command execution |
| `AiDashboard/Program.cs` | DI registration |
| `AiDashboard/Components/Pages/Home.razor.cs` | Wires regular dashboard chat messages to `AgenticChat.SendWithToolsAsync` |
| `AiDashboard/Components/Pages/QuickAskPage.razor` | Wires regular QuickAsk questions to `AgenticChat.SendWithToolsAsync` |

## Related Docs

- [FILE-AGENT-COMMANDS.md](FILE-AGENT-COMMANDS.md) — the underlying slash commands and their direct (non-agentic) usage
- [QUICKASK-FILE-AGENT-AND-MAXTOKENS.md](QUICKASK-FILE-AGENT-AND-MAXTOKENS.md) — QuickAsk-specific file agent details
- [gemma4-cli-feature.md](gemma4-cli-feature.md#tool-calling) — the separate JSON/Semantic-Kernel tool-calling mechanism for Gemma 4 CLI
