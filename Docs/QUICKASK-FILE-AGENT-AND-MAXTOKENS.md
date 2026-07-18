# QuickAsk — File Agent & MaxTokens Preset

## Summary

Two features added to the **QuickAsk** page (`/quick-ask`):

1. **File Agent commands** — `/skapa`, `/fyll`, `/läs <fil> <instruktion>`, `/lista` now work in QuickAsk, identical to the Dashboard (Home) page. Regular questions also route through the [agentic tool-calling pattern](AGENTIC-CHAT-TOOL-CALLING.md), letting the LLM invoke these commands itself.
2. **MaxTokens preset selector** — a dropdown in the info bar lets the user choose how many output tokens the LLM may generate per response.

---

## File Agent in QuickAsk

### What changed

`QuickAskPage.razor` now injects `IFileAgentService` and `IAgenticChatService`, and intercepts
slash commands in `SendQuestion()` before the message reaches the LLM. Regular (non-command)
questions are routed through `IAgenticChatService` instead of calling the backend directly, so
the LLM can request a file-agent tool itself while answering — see
[AGENTIC-CHAT-TOOL-CALLING.md](AGENTIC-CHAT-TOOL-CALLING.md) for that flow.

```razor
@using global::Services.FileAgent
@using global::Services.AgentTools
@inject IFileAgentService FileAgent
@inject IAgenticChatService AgenticChat
```

### Flow in `SendQuestion()`

```
User types message
        ↓
FileAgent.IsCommand(input)?
        ↓ yes                              ↓ no
ExecuteAsync(input)           AgenticChat.SendWithToolsAsync(input, SendQuickAskActiveAsync)
        ↓                                     ↓
┌─────────────────────┐           LLM generates response (may invoke a tool internally)
│ FileCreated/        │
│ FileFilled / Error  │ → plain system message in chat (no tokens/s shown)
│                     │
│ FileRead            │ → SendQuickAskAsync(InjectedContext) // instruction + file content
└─────────────────────┘        ↓
                        LLM responds per the instruction, using the file content as context
```

### System message for /skapa and /fyll

Unlike AI responses, file operation confirmations are shown without a tokens/s counter.
They are created directly as `QuickAskMessage` objects with `TokensPerSecond = null`:

```csharp
messages.Add(new QuickAskMessage
{
    IsUser        = false,
    Text          = result.Message,
    FormattedText = result.Message,
    Timestamp     = DateTime.Now
    // TokensPerSecond omitted → not displayed
});
```

### Page support summary

| Page | File Agent support |
|------|:-----------------:|
| Home (Dashboard) — `Home.razor.cs` | ✅ |
| QuickAsk — `QuickAskPage.razor` | ✅ |

---

## MaxTokens Preset Selector

### What it does

Allows the user to choose between four token limits before each message is sent.
The selected value is written to `Dashboard.SettingsService.MaxTokens` immediately
before calling `SendQuickAskAsync`, so it takes effect for that request.

### The enum — `Services\QuickAsk\MaxTokensPreset.cs`

```csharp
public enum MaxTokensPreset
{
    Tokens2K   = 2048,
    Tokens4K   = 4096,
    Tokens128K = 128000,   // default
    Tokens256K = 256000
}
```

Extension methods:

```csharp
preset.ToInt()    // → 128000 (the raw value passed to the LLM)
preset.ToLabel()  // → "128K tokens" (shown in the dropdown)
```

### UI — dropdown in the info bar

The dropdown sits next to the Model selector at the bottom of the QuickAsk card,
reusing the existing `.oa-model-selector` and `.oa-model-dropdown` CSS classes.
A narrower override `.oa-tokens-dropdown` sets `min-width: 130px`.

```razor
<div class="oa-info-badge oa-model-selector">
    <span class="model-label-text">Tokens:</span>
    <select class="oa-model-dropdown oa-tokens-dropdown" @bind="_selectedMaxTokens">
        @foreach (var preset in Enum.GetValues<MaxTokensPreset>())
        {
            <option value="@preset">@preset.ToLabel()</option>
        }
    </select>
</div>
```

Blazor's `@bind` matches option `value="Tokens128K"` (enum name as string) back to
`MaxTokensPreset.Tokens128K` via `Enum.Parse` automatically.

### How MaxTokens is applied

```csharp
// Applied before every call inside SendQuestion()
Dashboard.SettingsService.MaxTokens = _selectedMaxTokens.ToInt();
```

This writes through to `GenerationSettings.MaxTokens`, which the LLM runner reads
when building the inference parameters.

### Default value

```csharp
private MaxTokensPreset _selectedMaxTokens = MaxTokensPreset.Tokens128K;
```

Matches the existing `GenerationSettingsService` default of `128000`.

---

## Files Changed

| File | Change |
|------|--------|
| `Services/QuickAsk/MaxTokensPreset.cs` | New — `MaxTokensPreset` enum + `ToInt()` / `ToLabel()` extensions |
| `AiDashboard/Components/Pages/QuickAskPage.razor` | `@using` + `@inject IFileAgentService`; `_selectedMaxTokens` field; Tokens dropdown in info bar; `SendQuestion()` rewritten |
| `AiDashboard/wwwroot/css/quickask.css` | New `.oa-tokens-dropdown` class (narrower width override) |

### Agentic tool-calling extension

| File | Change |
|------|--------|
| `AiDashboard/Components/Pages/QuickAskPage.razor` | `@inject IAgenticChatService AgenticChat`; regular questions routed through `AgenticChat.SendWithToolsAsync`; tool-usage status messages rendered before the final answer |

> See [AGENTIC-CHAT-TOOL-CALLING.md](AGENTIC-CHAT-TOOL-CALLING.md) for the full design of the agentic pattern shared with the Dashboard chat.
