# Quick LLM Chat - Complete Guide

## Overview

The **Quick LLM Chat** at `/chat` provides a streamlined interface for conversing with your local LLM without the complexity of the full dashboard.

## Features

? **Simple Chat Interface** - Clean, minimalist design  
? **Model Dropdown** - Quickly switch between LLM models  
? **Code Formatting** - Automatic syntax highlighting and formatting  
? **RAG Toggle** - Enable/disable vector search  
? **Settings Integration** - Uses temperature, GPU settings, etc.  
? **Real-time Responses** - Streaming output as LLM generates  

## How It Works

### Message Flow

```
User Types Message
    ?
SendMessage() called
    ?
Dashboard.SendMessageAsync(message)
    ?
ChatService.SendMessageAsync(message, ragMode, debugMode, ...)
    ?
LLM generates response
    ?
FormatMessage() applies syntax highlighting
    ?
Display in chat bubble
```

### RAG Mode Behavior

#### RAG Mode OFF (Quick Ask)
- **No vector search** - Direct LLM conversation
- **Faster responses** - No database lookups
- **General knowledge** - LLM uses training data only
- **Best for**: General questions, code generation, explanations

**Example**:
```
You: Explain quantum computing in simple terms
AI: Quantum computing uses quantum mechanics principles...
    (Response based on LLM training data)
```

#### RAG Mode ON (Knowledge-Enhanced)
- **Vector search active** - Searches loaded collection
- **Context-aware** - Augments prompt with relevant fragments
- **Specialized knowledge** - Uses your custom documents
- **Best for**: Domain-specific questions, document queries

**Example**:
```
You: What are the return policies? (with "Webhallen" collection loaded)
AI: Based on the policy documents, Webhallen offers...
    (Response uses your uploaded policy documents)
```

## Current Implementation

### File: `Home.razor.cs`

```csharp
private async Task SendMessage()
{
    if (string.IsNullOrWhiteSpace(composerText) || isProcessing) return;

    var userMessage = composerText.Trim();
    composerText = string.Empty;
    isProcessing = true;

    // Add user message
    var userMsg = new ChatMessageModel { IsUser = true, Text = userMessage };
    userMsg.FormattedText = FormatMessage(userMsg.Text, isUser: true);
    messages.Add(userMsg);
    StateHasChanged();

    try
    {
        // Get AI response - respects current RAG mode setting
        var response = await Dashboard.SendMessageAsync(userMessage);

        // Add AI response - formatter handles code blocks
        var aiMsg = new ChatMessageModel { IsUser = false, Text = response };
        aiMsg.FormattedText = FormatMessage(aiMsg.Text, isUser: false);
        messages.Add(aiMsg);
    }
    catch (Exception ex)
    {
        var errorMsg = new ChatMessageModel { IsUser = false, Text = $"[ERROR] {ex.Message}" };
        errorMsg.FormattedText = FormatMessage(errorMsg.Text, isUser: false);
        messages.Add(errorMsg);
    }
    finally
    {
        isProcessing = false;
        StateHasChanged();
    }
}
```

### File: `DashboardState.cs`

```csharp
public async Task<string> SendMessageAsync(string message)
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
            SettingsService.RagMode,          // ? Uses current RAG setting
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
        return $"[ERROR] {ex.Message}";
    }
}
```

## What "Quick Ask" Means

**"Quick Ask"** refers to asking the LLM **without RAG mode**, meaning:

1. **No Vector Search**: Doesn't search your document collections
2. **Direct LLM**: Uses only the LLM's training knowledge
3. **Faster**: No database queries or context retrieval
4. **General Purpose**: Good for explanations, code, general questions

### To Use Quick Ask:
1. Navigate to `/chat`
2. **Turn RAG OFF** in the top bar (shows `[RAG: OFF]`)
3. Type your question
4. Get response from pure LLM knowledge

### To Use Knowledge-Enhanced Chat:
1. Navigate to `/chat`
2. **Turn RAG ON** in the top bar (shows `[RAG: ON]`)
3. Load a collection (in sidebar)
4. Type your question
5. Get response with context from your documents

## UI Elements

### Top Bar

```
????????????????????????????????????????????????????????????
? ? Home  [RAG: OFF] [Model: ? phi-3.5...] [Temp: 0.7]   ?
????????????????????????????????????????????????????????????
```

- **? Home**: Return to dashboard
- **[RAG: OFF/ON]**: Toggle vector search
- **[Model: ?]**: Select LLM model
- **[Temp: 0.7]**: Current temperature setting
- **[GPU: ON/OFF]**: GPU acceleration status

### Chat Area

```
????????????????????????????????????????????????????????????
? U  You • 19:54                                           ?
?    Explain quantum computing in simple terms             ?
?                                                           ?
? AI Assistant • 19:54                                     ?
?    Quantum computing uses quantum mechanics principles   ?
?    like superposition and entanglement to process...     ?
?                                                           ?
?    [C# Code]                                             ?
?    public class QuantumBit                               ?
?    {                                                      ?
?        public bool IsZero { get; set; }                  ?
?        public bool IsOne { get; set; }                   ?
?    }                                                      ?
?    [End C# Code]                                         ?
?                                                           ?
?    This is a simplified example...                       ?
????????????????????????????????????????????????????????????
```

### Message Formatting

#### User Messages
- Simple text formatting
- Bold text support (`**bold**`)
- Line breaks preserved

#### AI Messages
- **Automatic code block detection**
- **Syntax highlighting** (C#, Python, JavaScript, etc.)
- **Proper indentation** (4 spaces)
- **Dark theme** code blocks
- **Line break insertion** for poorly formatted code
- **HTML encoding** for security

## Performance

### Tokens per Second
The chat displays tokens/sec in the console:
```
18:09:10• 15,3 tokens/s
```

This shows:
- **Time**: 18:09:10
- **Speed**: 15.3 tokens per second
- Indicates LLM generation speed

### Factors Affecting Speed
- **Model Size**: Smaller models = faster (phi-3.5 > llama-3.2-1b)
- **GPU Usage**: ON = much faster than CPU
- **GPU Layers**: More layers = faster (if VRAM available)
- **Prompt Length**: Shorter prompts = faster responses
- **RAG Mode**: OFF = faster (no vector search overhead)

## Code Formatting Examples

### C# Code

**Input from LLM:**
```
Here's a C# class:```csharppublic class Person{public string Name{get;set;}}```
```

**Formatted Output:**
```
Here's a C# class:

[C# Code]
public class Person
{
    public string Name { get; set; }
}
[End C# Code]
```

With syntax highlighting:
- `public`, `class`, `string`, `get`, `set` in **blue**
- `Person` in **cyan**
- Proper 4-space indentation

### Python Code

**Input from LLM:**
```
Python function:```pythondef hello(name):print(f"Hello {name}")```
```

**Formatted Output:**
```
Python function:

[Python Code]
def hello(name):
    print(f"Hello {name}")
[End Python Code]
```

## Best Practices

### For General Questions (Quick Ask)
? Turn **RAG OFF**  
? Use for: code generation, explanations, tutoring  
? Faster responses  
? No collection needed  

### For Specific Knowledge
? Turn **RAG ON**  
? Load relevant collection  
? Use for: policy questions, documentation queries  
? Get context-aware answers  

### For Best Performance
? Enable GPU acceleration  
? Set appropriate GPU layers (34 for most models)  
? Use smaller models for faster responses  
? Keep prompts concise  

### For Code Responses
? LLM will format with markdown (` ```language `)  
? Formatter auto-detects and highlights  
? Supports: C#, Python, JavaScript, HTML, SQL, etc.  
? Preserves line breaks and indentation  

## Troubleshooting

### "Chat service not initialized"
- Check that models are configured
- Verify `Program.cs` has `DashboardChatService` registered
- Ensure model pool is initialized

### Code Not Formatted
- Check if LLM used markdown code blocks (` ```language `)
- Verify `LlmResponseFormatterService` is injected
- Check browser console for errors

### Slow Responses
- Enable GPU if available
- Increase GPU layers
- Use smaller model
- Disable RAG mode for general questions

### RAG Not Working
- Verify collection is loaded
- Check `[RAG: ON]` in top bar
- Ensure collection has fragments
- Check collection service is available

## Summary

The Quick LLM Chat provides a **simple, fast interface** for:

- ? **Direct LLM conversation** (RAG OFF)
- ? **Knowledge-enhanced chat** (RAG ON)
- ? **Model switching** via dropdown
- ? **Code formatting** with syntax highlighting
- ? **Real-time responses** with token speed

**Quick Ask** specifically refers to using the chat with **RAG mode disabled**, giving you direct access to the LLM's training knowledge without vector search overhead.

---

**Page**: `/chat`  
**Status**: ? Fully Functional  
**Features**: Complete  
**Performance**: 15+ tokens/sec (model-dependent)
