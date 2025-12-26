# QuickAsk Service Extraction - Service Layer Pattern

## Summary
Extracted business logic from `QuickAsk.razor` into a dedicated service layer following separation of concerns principles.

## Changes Made

### 1. Created New Service Files in Services Project

#### `Services/Chat/QuickAskMessage.cs`
Data model for QuickAsk messages:

```csharp
public class QuickAskMessage
{
    public bool IsUser { get; set; }
    public string Text { get; set; } = "";
    public string FormattedText { get; set; } = "";
    public DateTime? Timestamp { get; set; }
    public double? TokensPerSecond { get; set; }
}
```

**Benefits:**
- Reusable across different presentation layers
- Clear data contract
- Can be serialized for export/storage
- Testable without UI dependencies

#### `Services/Chat/IQuickAskService.cs`
Service interface defining QuickAsk operations:

```csharp
public interface IQuickAskService
{
    string FormatMessage(string text, bool isUser);
    QuickAskMessage CreateUserMessage(string text);
    QuickAskMessage CreateAiMessage(string text, DateTime startTime);
    QuickAskMessage CreateErrorMessage(string errorMessage);
    string FormatModelName(string modelFileName);
}
```

**Benefits:**
- Testable interface
- Dependency injection ready
- Easy to mock for unit tests
- Clear API contract

#### `Services/Chat/QuickAskService.cs`
Service implementation with business logic:

```csharp
public class QuickAskService : IQuickAskService
{
    public string FormatMessage(string text, bool isUser)
    {
        // User message formatting logic
        // HTML encoding, bold markdown, line breaks
    }

    public QuickAskMessage CreateUserMessage(string text)
    {
        // Creates formatted user message with timestamp
    }

    public QuickAskMessage CreateAiMessage(string text, DateTime startTime)
    {
        // Creates AI message with tokens/sec calculation
    }

    public QuickAskMessage CreateErrorMessage(string errorMessage)
    {
        // Creates formatted error message
    }

    public string FormatModelName(string modelFileName)
    {
        // Intelligent model name truncation
    }
}
```

**Features:**
- Message creation factory methods
- Performance metric calculation
- Model name formatting
- User message HTML formatting

### 2. Updated QuickAsk.razor

#### Before (Monolithic):
```razor
@code {
    private class QuickMessage { ... }
    
    private string FormatMessage(string text, bool isUser) { ... }
    private string FormatModelName(string modelFileName) { ... }
    private async Task SendQuestion() 
    {
        // Complex logic mixed with message creation
        var userMessage = new QuickMessage { ... };
        userMessage.FormattedText = FormatMessage(...);
        // etc.
    }
}
```

**Issues:**
- All logic in Razor component
- Hard to test business logic
- Tight coupling
- No reusability
- Violates Single Responsibility Principle

#### After (Clean Separation):
```razor
@inject global::Services.Chat.IQuickAskService QuickAskService

@code {
    private List<global::Services.Chat.QuickAskMessage> messages = new();
    
    private async Task SendQuestion() 
    {
        // Simple delegation to service
        var userMessage = QuickAskService.CreateUserMessage(currentQuestion);
        messages.Add(userMessage);
        
        var response = await Dashboard.SendQuickAskAsync(question);
        var aiMessage = QuickAskService.CreateAiMessage(response, startTime);
        aiMessage.FormattedText = Formatter.FormatResponse(aiMessage.Text);
        messages.Add(aiMessage);
    }
}
```

**Benefits:**
- Clean component code
- Testable service layer
- Reusable business logic
- Clear separation of concerns
- Follows SOLID principles

### 3. Registered Service in Program.cs

```csharp
// Register QuickAsk service for conversation management
builder.Services.AddSingleton<global::Services.Chat.IQuickAskService, 
                              global::Services.Chat.QuickAskService>();
```

**Configuration:**
- Singleton lifetime (stateless service)
- Dependency injection ready
- Available throughout application

## Architecture Benefits

### Separation of Concerns

**Before:**
```
QuickAsk.razor (All Responsibilities)
??? UI Rendering
??? Event Handling
??? Business Logic
??? Data Formatting
??? Model Name Formatting
??? Message Creation
```

**After:**
```
QuickAsk.razor (Presentation Only)
??? UI Rendering
??? Event Handling
    ?
Services.Chat.QuickAskService (Business Logic)
??? Message Creation
??? Data Formatting
??? Model Name Formatting
??? Performance Calculation
```

### Testability

#### Before (Hard to Test):
```csharp
// Can't test FormatMessage without Razor component
// Can't test CreateUserMessage without UI dependencies
// Can't test tokens/sec calculation in isolation
```

#### After (Easy to Test):
```csharp
[Test]
public void CreateUserMessage_SetsPropertiesCorrectly()
{
    var service = new QuickAskService();
    var message = service.CreateUserMessage("Test question");
    
    Assert.IsTrue(message.IsUser);
    Assert.AreEqual("Test question", message.Text);
    Assert.IsNotNull(message.Timestamp);
    Assert.IsNotNull(message.FormattedText);
}

[Test]
public void FormatModelName_TruncatesLongNames()
{
    var service = new QuickAskService();
    var formatted = service.FormatModelName(
        "Mistral-14b-Merge-Base-Q5_K_M.gguf");
    
    Assert.AreEqual("Mistral-14b...Q5_K_M", formatted);
}
```

### Reusability

The service can now be used in:
- QuickAsk Razor component
- Future mobile app
- Desktop application
- API endpoints
- Background services
- Unit tests

### Maintainability

#### Single Responsibility:
- **QuickAskService**: Business logic only
- **QuickAsk.razor**: UI/UX only
- **LlmResponseFormatterService**: Code formatting only

#### Clear Dependencies:
- QuickAsk.razor depends on IQuickAskService
- IQuickAskService has no UI dependencies
- Easy to identify what each component does

## Design Patterns Applied

### 1. Service Layer Pattern
- Business logic in dedicated service
- Presentation logic in Razor component
- Clear layer separation

### 2. Factory Pattern
```csharp
CreateUserMessage(text)    // Factory for user messages
CreateAiMessage(...)       // Factory for AI messages
CreateErrorMessage(...)    // Factory for error messages
```

### 3. Dependency Injection
- Service registered in DI container
- Injected where needed
- Loosely coupled

### 4. Interface Segregation
- Small, focused interface
- Only methods needed for QuickAsk
- Easy to implement/mock

## Code Reduction in QuickAsk.razor

**Before**: ~290 lines
**After**: ~230 lines

**Removed from Razor:**
- QuickMessage class definition (moved to Services)
- FormatMessage method (moved to service)
- FormatModelName method (moved to service)
- Message creation logic (moved to service)
- Tokens/sec calculation (moved to service)

**Result**: 60 lines (~20%) reduction + cleaner code

## Namespace Resolution

Due to naming conflict between `AiDashboard.Services` and root `Services` namespaces, we use `global::` prefix:

```csharp
// In Razor files and Program.cs
@inject global::Services.Chat.IQuickAskService QuickAskService
private List<global::Services.Chat.QuickAskMessage> messages = new();
```

This ensures the compiler resolves to the correct namespace.

## Service Methods

### FormatMessage(string text, bool isUser)
**Purpose**: Formats message text for HTML display

**For User Messages:**
- Converts `**bold**` to `<strong>bold</strong>`
- HTML encodes to prevent injection
- Converts line breaks to `<br>`

**For AI Messages:**
- Returns raw text (formatting done by LlmResponseFormatterService)

**Example:**
```csharp
var formatted = service.FormatMessage("Hello **world**", isUser: true);
// Result: "Hello <strong>world</strong>"
```

### CreateUserMessage(string text)
**Purpose**: Creates a complete user message object

**Returns:**
```csharp
{
    IsUser = true,
    Text = "original text",
    FormattedText = "HTML formatted text",
    Timestamp = DateTime.Now,
    TokensPerSecond = null
}
```

### CreateAiMessage(string text, DateTime startTime)
**Purpose**: Creates AI message with performance metrics

**Calculation:**
```csharp
var elapsed = (DateTime.Now - startTime).TotalSeconds;
var estimatedTokens = text.Length / 4;
var tokensPerSecond = estimatedTokens / elapsed;
```

**Returns:**
```csharp
{
    IsUser = false,
    Text = "AI response",
    FormattedText = "AI response", // Will be updated by caller
    Timestamp = DateTime.Now,
    TokensPerSecond = 15.3 // Example
}
```

### CreateErrorMessage(string errorMessage)
**Purpose**: Creates formatted error message

**Returns:**
```csharp
{
    IsUser = false,
    Text = "Error: Something went wrong",
    FormattedText = "Error: Something went wrong",
    Timestamp = DateTime.Now,
    TokensPerSecond = null
}
```

### FormatModelName(string modelFileName)
**Purpose**: Shortens long model filenames

**Examples:**
```csharp
"Mistral-14b-Merge-Base-Q5_K_M.gguf" ? "Mistral-14b...Q5_K_M"
"aya-23-8B-Q6_K.gguf" ? "aya-23-8B-Q6_K"
"llama-3.2-1b-instruct.gguf" ? "llama-3.2-1b-instruct"
```

**Algorithm:**
1. Remove `.gguf` extension
2. If length > 30 characters:
   - Split by `-` and `_`
   - Keep first 2 parts + last part
   - Join with `...`
3. Fallback: Simple truncation if splitting fails

## Testing Examples

### Unit Test for Message Creation
```csharp
[TestClass]
public class QuickAskServiceTests
{
    private QuickAskService _service;

    [TestInitialize]
    public void Setup()
    {
        _service = new QuickAskService();
    }

    [TestMethod]
    public void CreateUserMessage_SetsCorrectProperties()
    {
        // Arrange
        var text = "What is AI?";
        
        // Act
        var message = _service.CreateUserMessage(text);
        
        // Assert
        Assert.IsTrue(message.IsUser);
        Assert.AreEqual(text, message.Text);
        Assert.IsNotNull(message.Timestamp);
        Assert.IsNull(message.TokensPerSecond);
        Assert.IsNotNull(message.FormattedText);
    }

    [TestMethod]
    public void CreateAiMessage_CalculatesTokensPerSecond()
    {
        // Arrange
        var text = new string('a', 400); // 400 chars = ~100 tokens
        var startTime = DateTime.Now.AddSeconds(-10); // 10 seconds ago
        
        // Act
        var message = _service.CreateAiMessage(text, startTime);
        
        // Assert
        Assert.IsFalse(message.IsUser);
        Assert.IsNotNull(message.TokensPerSecond);
        Assert.IsTrue(message.TokensPerSecond > 0);
        Assert.IsTrue(message.TokensPerSecond < 15); // ~100 tokens / 10 sec = ~10 t/s
    }

    [TestMethod]
    public void FormatModelName_TruncatesLongName()
    {
        // Arrange
        var longName = "Mistral-14b-Merge-Base-Q5_K_M.gguf";
        
        // Act
        var formatted = _service.FormatModelName(longName);
        
        // Assert
        Assert.AreEqual("Mistral-14b...Q5_K_M", formatted);
        Assert.IsTrue(formatted.Length < 30);
    }

    [TestMethod]
    public void FormatMessage_FormatsUserMessageCorrectly()
    {
        // Arrange
        var text = "Hello **world**\nNew line";
        
        // Act
        var formatted = _service.FormatMessage(text, isUser: true);
        
        // Assert
        Assert.IsTrue(formatted.Contains("<strong>world</strong>"));
        Assert.IsTrue(formatted.Contains("<br>"));
    }
}
```

## Migration Checklist

- [x] Create QuickAskMessage data model
- [x] Create IQuickAskService interface
- [x] Create QuickAskService implementation
- [x] Register service in Program.cs
- [x] Update QuickAsk.razor to use service
- [x] Remove duplicate code from Razor
- [x] Fix namespace conflicts with global::
- [x] Build and verify compilation
- [x] Test functionality

## Build Status

Build successful. All changes compile without errors.

## Files Created

1. **`Services/Chat/QuickAskMessage.cs`** - Message data model
2. **`Services/Chat/IQuickAskService.cs`** - Service interface
3. **`Services/Chat/QuickAskService.cs`** - Service implementation

## Files Modified

1. **`AiDashboard/Components/Pages/QuickAsk.razor`** - Updated to use service
2. **`AiDashboard/Program.cs`** - Added service registration

## Summary

The QuickAsk functionality has been successfully refactored following best practices:

? **Separation of Concerns**: UI and business logic separated  
? **Testability**: Business logic can be unit tested  
? **Reusability**: Service can be used anywhere  
? **Maintainability**: Clear, focused responsibilities  
? **SOLID Principles**: Single Responsibility, Dependency Injection  
? **Clean Architecture**: Service layer properly implemented  

The codebase is now more professional, maintainable, and testable.
