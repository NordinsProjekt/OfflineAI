# AgentKit

Tool-calling agent building blocks for driving a local LLM through file edits and other
side-effecting actions from plain-text tool commands (`/fyll`, `/redigera`, `/qb64`, ...),
without relying on a model's native function-calling support.

Preview package — not yet published anywhere, API may still change.

## What's in it

- **`ToolLoop.IAgenticChatService`** — runs the request/response/tool-execute loop: sends a
  prompt, parses any tool command(s) out of the reply, executes them, and feeds the result back
  to the model, up to a configurable round cap.
- **`Skills.Files`** — `IFileAgentService`: create, read, and line-based edit text files in a
  workspace directory, plus PDF text extraction (via `UglyToad.PdfPig`).
- **`Skills.Qb64`** — `IQb64ToolService`: compile and run QB64 (QBasic) `.bas` programs.
- **`Skills.Utility`** — `IUtilityToolsService`: named HTTP API calls the model can invoke by
  name from a configured allowlist.
- **`Skills.External`** — `IExternalToolsService`: run named local executables from a configured
  allowlist, capturing stdout back to the model.

## Usage

Every skill has an interface + one concrete implementation you construct directly — there's no
DI container or factory built into the package, so wire it into whatever container (or none)
your host uses.

### Minimal setup: file editing only

```csharp
using AgentKit.Skills.Files;
using AgentKit.ToolLoop;

IFileAgentService fileAgent = new FileAgentService(baseDirectory: @"C:\workspace");
IAgenticChatService chat = new AgenticChatService(fileAgent);

// sendToLlm is your own delegate that sends a prompt to whatever LLM backend you're using
// and returns its raw text reply.
Func<string, Task<string>> sendToLlm = prompt => myLlmClient.CompleteAsync(prompt);

AgenticChatResult result = await chat.SendWithToolsAsync(
    "Create recept.txt with a pancake recipe.",
    sendToLlm);

Console.WriteLine(result.FinalResponse);
foreach (var invocation in result.ToolInvocations)
    Console.WriteLine($"{invocation.Command} -> {invocation.ResultSummary}");
```

The model doesn't need native function-calling — `IFileAgentService` tells it about commands
like `/skapa <fil>` and `/fyll <fil> <beskrivning>` via a system prompt, `AgenticChatService`
watches the reply for one of those commands, executes it, and feeds the result back for a
final answer.

### Full setup: all skills wired in

```csharp
using AgentKit.Skills.External;
using AgentKit.Skills.Files;
using AgentKit.Skills.Qb64;
using AgentKit.Skills.Utility;
using AgentKit.ToolLoop;

IFileAgentService fileAgent = new FileAgentService(@"C:\workspace");

IUtilityToolsService utilityTools = new UtilityToolsService(
    new UtilityToolsOptions
    {
        Endpoints = { new ApiEndpointOptions { Name = "weather", Url = "https://api.example.com/weather?q={input}" } }
    },
    httpClientProvider: () => new HttpClient());

IExternalToolsService externalTools = new ExternalToolsService(
    new[] { new ExternalToolOptions { Command = "väder", ExecutablePath = @"C:\tools\weather.exe" } });

IQb64ToolService qb64Tools = new Qb64ToolService(
    new Qb64Options { CompilerPath = @"C:\qb64\qb64.exe" },
    fileAgent);

IAgenticChatService chat = new AgenticChatService(
    fileAgent,
    utilityTools,
    maxToolCallRounds: 3,
    externalTools,
    qb64Tools);
```

Each skill is independent and optional beyond `IFileAgentService` — pass `null` (or omit) any
of `utilityTools`, `externalTools`, `qb64Tools` to leave that skill's commands out of what's
offered to the model.

### Using a skill directly (no chat loop)

The skills also work standalone, without `IAgenticChatService`, if you just need the file
operations from your own code:

```csharp
var fileAgent = new FileAgentService(@"C:\workspace");

FileAgentResult created = await fileAgent.ExecuteAsync("/skapa notes.txt");

await fileAgent.WriteExtractedContentAsync("notes.txt", "First line\nSecond line");

FileAgentResult read = await fileAgent.ReadFileRawAsync("notes.txt");
Console.WriteLine(read.InjectedContext); // "First line\nSecond line"
```

## Status

Extracted out of the OfflineAI dashboard app so the tool-calling core can be reused/tested on
its own. Currently packaged locally only (`dotnet pack`) — no public or private feed yet.
