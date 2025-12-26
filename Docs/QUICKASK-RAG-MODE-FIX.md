# QuickAsk RAG Mode Fix

## Issue
QuickAsk was incorrectly using RAG mode when it should always query the LLM directly without database searches.

Console output showed:
```
RAG Mode: ENABLED
Searching database with: 'What are the benefits of local AI?'
Loaded 0 fragments from database
No fragments found in collection 'game-rules-mpnet'
```

This is wrong for QuickAsk, which should be a simple direct LLM chat without knowledge base integration.

## Root Cause

The `Dashboard.SendMessageAsync()` method uses the global `SettingsService.RagMode` setting:

```csharp
public async Task<string> SendMessageAsync(string message)
{
    // ...
    return await ChatService.SendMessageAsync(
        message,
        SettingsService.RagMode,  // Uses global RAG setting
        // ...
    );
}
```

This means:
- If user toggles RAG on in the main chat or sidebar
- QuickAsk would also use RAG mode
- QuickAsk would try to search collections
- This defeats the purpose of QuickAsk as a "quick direct answer" page

## Solution

Added a dedicated method `SendQuickAskAsync()` that always forces RAG mode to `false`.

### File: `AiDashboard/State/DashboardState.cs`

```csharp
// QuickAsk-specific method that always disables RAG
public async Task<string> SendQuickAskAsync(string message)
{
    if (ChatService == null)
    {
        return "[ERROR] Chat service not initialized.";
    }

    try
    {
        var genSettings = SettingsService.ToGenerationSettings();

        return await ChatService.SendMessageAsync(
            message,
            ragMode: false,  // Always disable RAG for QuickAsk
            SettingsService.DebugMode,
            SettingsService.PerformanceMetrics,
            genSettings,
            PersonalityService?.CurrentPersonality,
            SettingsService.UseGpu,
            SettingsService.GpuLayers,
            SettingsService.TimeoutSeconds);
    }
    catch (Exception ex)
    {
        return $"[ERROR] Failed to send message: {ex.Message}";
    }
}
```

### File: `AiDashboard/Components/Pages/QuickAsk.razor`

Changed from:
```csharp
var response = await Dashboard.SendMessageAsync(question);
```

To:
```csharp
var response = await Dashboard.SendQuickAskAsync(question);
```

## How It Works Now

### QuickAsk Flow (RAG Disabled)
```
User asks: "What are the benefits of local AI?"
    ?
QuickAsk.SendQuestion()
    ?
Dashboard.SendQuickAskAsync(question)
    ?
ChatService.SendMessageAsync(message, ragMode: false, ...)
    ?
Direct LLM query (NO database search)
    ?
LLM generates response based on training data
    ?
Response formatted and displayed
```

### Main Chat Flow (RAG Configurable)
```
User asks question in main chat
    ?
Home.SendMessage()
    ?
Dashboard.SendMessageAsync(question)
    ?
ChatService.SendMessageAsync(message, SettingsService.RagMode, ...)
    ?
If RAG enabled: Search database + augment prompt
If RAG disabled: Direct LLM query
    ?
Response generated and displayed
```

## Console Output Comparison

### Before (Incorrect - RAG Enabled):
```
Generation Settings for Query
RAG Mode:           ENABLED
Temperature:        0.30
Max Tokens:         512

User question: "What are the benefits of local AI?"
Searching database with: 'What are the benefits of local AI?'
Extracted keywords (English): 'benefits local' from query: 'What are the benefits of local AI?'
Searching database for: 'benefits local'
Loaded 0 fragments from database
No fragments found in collection 'game-rules-mpnet'
Insufficient context found - returning null
```

### After (Correct - RAG Disabled):
```
Generation Settings for Query
RAG Mode:           DISABLED
Temperature:        0.30
Max Tokens:         512

User question: "What are the benefits of local AI?"
[No database search]
[Direct LLM query]
```

## Benefits of This Fix

### Clear Separation of Concerns
- **QuickAsk**: Always direct LLM, no RAG, fast general answers
- **Main Chat**: User-configurable RAG, knowledge-enhanced answers

### Performance
- QuickAsk is faster (no database overhead)
- No wasted collection searches
- Cleaner console output

### User Experience
- QuickAsk behaves consistently
- No confusion about why it's searching collections
- Clear distinction between quick ask and knowledge chat

### Predictability
- QuickAsk behavior doesn't change based on global RAG setting
- Users know exactly what they're getting
- No unexpected database queries

## Page Purposes Now Clear

### QuickAsk Page (`/quick-ask`)
**Purpose**: Quick general questions to LLM
- RAG: Always OFF
- Collections: Not used
- Speed: Fast
- Use case: General knowledge, code generation, explanations

### Main Chat Page (`/chat`)
**Purpose**: Configurable chat with optional knowledge
- RAG: User toggleable
- Collections: Used when RAG enabled
- Speed: Depends on RAG setting
- Use case: Both general and domain-specific questions

## Testing

To verify the fix:

1. Navigate to `/quick-ask`
2. Ask a question
3. Check console output
4. Verify it shows:
   ```
   RAG Mode: DISABLED
   ```
5. Verify NO database searches occur
6. Verify response is fast and direct

## Files Modified

1. **`AiDashboard/State/DashboardState.cs`**
   - Added `SendQuickAskAsync()` method
   - Forces `ragMode: false` parameter

2. **`AiDashboard/Components/Pages/QuickAsk.razor`**
   - Changed to use `SendQuickAskAsync()` instead of `SendMessageAsync()`

## Build Status

Build successful. All changes compile without errors.

## Summary

QuickAsk now:
- Always uses direct LLM queries (no RAG)
- Doesn't search collections
- Provides fast, general answers
- Has clean console output
- Works independently of global RAG setting

The page now lives up to its name: "Quick Ask" - quick answers from the LLM without database overhead.
