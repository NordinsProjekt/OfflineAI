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

## Status

Extracted out of the OfflineAI dashboard app so the tool-calling core can be reused/tested on
its own. Currently packaged locally only (`dotnet pack`) — no public or private feed yet.
