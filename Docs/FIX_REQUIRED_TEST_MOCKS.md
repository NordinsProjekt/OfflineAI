# Fix Required: Application.AI.Tests Mock Setup

## Problem
10 tests in `Application.AI.Tests/Chat/AiChatServicePooledTests.cs` are failing because the mock setup for `SearchRelevantMemoryAsync` doesn't match the updated interface signature.

## Root Cause
The `ISearchableMemory.SearchRelevantMemoryAsync` method was updated to include a `language` parameter (7th parameter), but the test mocks are still using the old signature with only 6 parameters.

### Interface Signature (Current)
```csharp
Task<string?> SearchRelevantMemoryAsync(
    string query,
    int topK = 5,
    double minRelevanceScore = 0.5,
    List<string>? domainFilter = null,
    int? maxCharsPerFragment = null,
    bool includeMetadata = true,
    string language = "English");  // NEW PARAMETER
```

### Test Mock Setup (Old - Wrong)
```csharp
_mockSearchableMemory.Setup(m => m.SearchRelevantMemoryAsync(
    It.IsAny<string>(),
    It.IsAny<int>(),
    It.IsAny<double>(),
    It.IsAny<List<string>>(),
    It.IsAny<int>(),
    It.IsAny<bool>()))  // Missing 7th parameter!
    .ReturnsAsync(context);
```

## Solution
Update ALL mock setups in the test file to include the 7th `language` parameter.

### Fixed Mock Setup
```csharp
_mockSearchableMemory.Setup(m => m.SearchRelevantMemoryAsync(
    It.IsAny<string>(),
    It.IsAny<int>(),
    It.IsAny<double>(),
    It.IsAny<List<string>?>(),
    It.IsAny<int?>(),
    It.IsAny<bool>(),
    It.IsAny<string>()))  // Add 7th parameter for language
    .ReturnsAsync(context);
```

## Affected Tests (10 tests failing)
1. `SendMessageAsync_WithEmptyResponse_ReturnsWarningMessage`
2. `SendMessageAsync_WithTimeoutException_ReturnsErrorMessage`
3. `SendMessageAsync_WithGeneralException_ReturnsErrorMessage`
4. `SendMessageAsync_WithLongContext_TruncatesContext`
5. `SendMessageAsync_WithCancellationToken_PassesToPool`
6. `SendMessageAsync_WithPerformanceMetrics_CalculatesCorrectly`
7. `SendMessageAsync_WithValidQuestion_StoresInConversationMemory`
8. `SendMessageAsync_WithValidResponse_StoresInConversationMemory`
9. `SendMessageAsync_WithRagSettings_UsesConfiguredValues`
10. `SendMessageAsync_WithDomainDetector_DetectsDomains`

## Fix Pattern

### BEFORE (Wrong)
```csharp
_mockSearchableMemory.Setup(m => m.SearchRelevantMemoryAsync(
    It.IsAny<string>(),
    It.IsAny<int>(),
    It.IsAny<double>(),
    It.IsAny<List<string>>(),  // Note: Not nullable in old tests
    It.IsAny<int>(),           // Note: Not nullable in old tests
    It.IsAny<bool>()))         // Missing 7th parameter
    .ReturnsAsync(context);
```

### AFTER (Correct)
```csharp
_mockSearchableMemory.Setup(m => m.SearchRelevantMemoryAsync(
    It.IsAny<string>(),
    It.IsAny<int>(),
    It.IsAny<double>(),
    It.IsAny<List<string>?>(),  // Make nullable to match interface
    It.IsAny<int?>(),            // Make nullable to match interface  
    It.IsAny<bool>(),
    It.IsAny<string>()))         // Add language parameter
    .ReturnsAsync(context);
```

## Search and Replace Pattern

**Find:**
```regex
\.SearchRelevantMemoryAsync\(\s*It\.IsAny<string>\(\),\s*It\.IsAny<int>\(\),\s*It\.IsAny<double>\(\),\s*It\.IsAny<List<string>>\(\),\s*It\.IsAny<int>\(\),\s*It\.IsAny<bool>\(\)\)
```

**Replace:**
```csharp
.SearchRelevantMemoryAsync(
    It.IsAny<string>(),
    It.IsAny<int>(),
    It.IsAny<double>(),
    It.IsAny<List<string>?>(),
    It.IsAny<int?>(),
    It.IsAny<bool>(),
    It.IsAny<string>())
```

## Manual Fix Instructions

1. **Open** `Application.AI.Tests/Chat/AiChatServicePooledTests.cs`
2. **Find all occurrences** of `SearchRelevantMemoryAsync` mock setup (approximately 20 locations)
3. **For each occurrence**, add the 7th parameter `It.IsAny<string>()` after the `bool` parameter
4. **Also fix** the nullability: Change `List<string>` to `List<string>?>` and `int` to `int?>`
5. **Save** and run tests
6. **Verify** all 357 tests pass

## Example Changes

### Line ~210: SendMessageAsync_WithValidQuestion_StoresInConversationMemory
```csharp
// BEFORE
_mockSearchableMemory.Setup(m => m.SearchRelevantMemoryAsync(
    It.IsAny<string>(),
    It.IsAny<int>(),
    It.IsAny<double>(),
    It.IsAny<List<string>>(),
    It.IsAny<int>(),
    It.IsAny<bool>()))
    .ReturnsAsync("France is a country...");

// AFTER
_mockSearchableMemory.Setup(m => m.SearchRelevantMemoryAsync(
    It.IsAny<string>(),
    It.IsAny<int>(),
    It.IsAny<double>(),
    It.IsAny<List<string>?>(),
    It.IsAny<int?>(),
    It.IsAny<bool>(),
    It.IsAny<string>()))
    .ReturnsAsync("France is a country...");
```

### Line ~250: SendMessageAsync_WithValidResponse_StoresInConversationMemory
```csharp
// BEFORE
_mockSearchableMemory.Setup(m => m.SearchRelevantMemoryAsync(
    It.IsAny<string>(),
    It.IsAny<int>(),
    It.IsAny<double>(),
    It.IsAny<List<string>>(),
    It.IsAny<int>(),
    It.IsAny<bool>()))
    .ReturnsAsync(context);

// AFTER
_mockSearchableMemory.Setup(m => m.SearchRelevantMemoryAsync(
    It.IsAny<string>(),
    It.IsAny<int>(),
    It.IsAny<double>(),
    It.IsAny<List<string>?>(),
    It.IsAny<int?>(),
    It.IsAny<bool>(),
    It.IsAny<string>()))
    .ReturnsAsync(context);
```

## Alternative: Use Helper Method
Instead of updating each occurrence manually, create or update the helper method:

```csharp
/// <summary>
/// Helper method to setup SearchRelevantMemoryAsync with all parameters.
/// </summary>
private void SetupSearchAsync(Mock<ISearchableMemory> mock, string? returnValue)
{
    mock.Setup(m => m.SearchRelevantMemoryAsync(
        It.IsAny<string>(),
        It.IsAny<int>(),
        It.IsAny<double>(),
        It.IsAny<List<string>?>(),
        It.IsAny<int?>(),
        It.IsAny<bool>(),
        It.IsAny<string>()))
        .ReturnsAsync(returnValue);
}

// Then use it like:
SetupSearchAsync(_mockSearchableMemory, context);
```

## Verification Steps

After making changes:

1. **Build the solution**
   ```bash
   dotnet build Application.AI.Tests/Application.AI.Tests.csproj
   ```
   - Should succeed with 0 errors

2. **Run the failing tests**
   ```bash
   dotnet test Application.AI.Tests/Application.AI.Tests.csproj --verbosity normal
   ```
   - Expected: All 357 tests pass (failed: 0)

3. **Run all tests**
   ```bash
   dotnet test
   ```
   - Expected: All tests across solution pass

## Status
- [x] Problem identified
- [x] Solution documented
- [ ] Fix applied
- [ ] Tests verified passing

## Priority
**CRITICAL** - Blocks all commits until fixed per Solution Testing Rules.

---

**Document Created**: 2024  
**Author**: AI Assistant  
**File**: Application.AI.Tests/Chat/AiChatServicePooledTests.cs  
**Lines Affected**: ~20 occurrences throughout file
