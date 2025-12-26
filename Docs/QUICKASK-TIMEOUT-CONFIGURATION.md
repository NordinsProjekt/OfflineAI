# QuickAsk Timeout Configuration

## Change Summary
Set QuickAsk timeout to a fixed 10 seconds for faster response handling.

## Rationale

QuickAsk is designed for quick, direct questions to the LLM without RAG overhead. A shorter timeout makes sense because:

1. **No Database Queries**: RAG is disabled, so no time spent searching collections
2. **Direct LLM Only**: Only waiting for LLM generation, not context retrieval
3. **Quick Answers Expected**: Users expect faster responses for simple questions
4. **Better UX**: Prevents long waits for simple queries

## Implementation

### File: `AiDashboard/State/DashboardState.cs`

**Before:**
```csharp
public async Task<string> SendQuickAskAsync(string message)
{
    // ...
    return await ChatService.SendMessageAsync(
        message,
        ragMode: false,
        SettingsService.DebugMode,
        SettingsService.PerformanceMetrics,
        genSettings,
        PersonalityService?.CurrentPersonality,
        SettingsService.UseGpu,
        SettingsService.GpuLayers,
        SettingsService.TimeoutSeconds);  // Used global setting
}
```

**After:**
```csharp
public async Task<string> SendQuickAskAsync(string message)
{
    // ...
    return await ChatService.SendMessageAsync(
        message,
        ragMode: false,
        SettingsService.DebugMode,
        SettingsService.PerformanceMetrics,
        genSettings,
        PersonalityService?.CurrentPersonality,
        SettingsService.UseGpu,
        SettingsService.GpuLayers,
        timeoutSeconds: 10);  // Fixed 10-second timeout
}
```

## Timeout Comparison

| Page | Timeout | Reason |
|------|---------|--------|
| **QuickAsk** | 10 seconds | Direct LLM only, no RAG overhead |
| **Main Chat** | User configurable | May need longer for RAG + complex queries |

## Benefits

### Faster Failure Detection
- If LLM hangs, user gets error after 10 seconds
- No need to wait for default timeout (often 30-60 seconds)
- User can retry or rephrase question quickly

### Consistent Experience
- QuickAsk always has same timeout behavior
- Independent of global timeout settings
- Predictable for users

### Better Resource Management
- Prevents long-running queries from blocking
- Frees up resources faster on timeout
- Better for multiple concurrent users

## User Impact

### Positive
- Faster error feedback
- More responsive interface
- Clear expectations for response time

### Considerations
- Complex questions may timeout
- Long code generation might be cut off
- Users should use main chat for complex queries

## Recommended Usage

### Use QuickAsk For:
- Quick factual questions
- Short explanations
- Simple code snippets
- General knowledge queries
- Questions that typically generate responses in under 10 seconds

### Use Main Chat For:
- Complex analysis
- Long code generation
- Multi-step reasoning
- RAG-enhanced queries
- Questions needing extensive context

## Technical Details

### Timeout Flow

```
User sends question
    ?
SendQuickAskAsync() called
    ?
ChatService.SendMessageAsync(..., timeoutSeconds: 10)
    ?
LlmProcessManager starts generation
    ?
Timer set for 10 seconds
    ?
If response arrives within 10s: Success
If 10s elapsed: TimeoutException thrown
    ?
Exception caught and error message displayed
```

### Error Message

If timeout occurs:
```
Error: The operation has timed out after 10 seconds.
```

User can then:
- Retry the question
- Rephrase for simpler answer
- Use main chat with longer timeout

## Configuration

The 10-second timeout is hardcoded in `SendQuickAskAsync()` and cannot be changed by users. This is intentional to:

1. Keep QuickAsk simple and fast
2. Maintain consistent behavior
3. Avoid confusion with global settings
4. Enforce the "quick ask" purpose

For configurable timeouts, users should use the main chat page where they can adjust settings.

## Testing

To verify the timeout works:

1. Navigate to `/quick-ask`
2. Ask a question that takes longer than 10 seconds
3. Verify timeout error appears after 10 seconds
4. Verify user can retry immediately

Example long-running question:
```
"Generate a complete REST API with authentication, database models, 
and unit tests in C# with detailed comments"
```

This should timeout after 10 seconds on most systems.

## Build Status

Build successful. Change is ready for use.

## Summary

QuickAsk now has a fixed 10-second timeout that:
- Provides faster error feedback
- Matches the "quick ask" purpose
- Independent of global timeout settings
- Better resource management
- Clear expectations for users

This makes QuickAsk truly "quick" by ensuring responses come back within 10 seconds or fail fast.
