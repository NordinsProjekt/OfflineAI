# Solution Testing Rules

## Core Principle
**100% of all unit tests MUST pass at all times before committing code.**

## Test Coverage Requirements

### 1. All Projects Must Have Tests
- Services ? Services.Tests
- AI ? Application.AI.Tests  
- Infrastructure.Data.Dapper ? (Integration tests in Services.Tests)
- Presentation (AiDashboard) ? Presentation.AiDashboard.Tests

### 2. Test Quality Standards

#### Coverage
- Minimum 80% code coverage for all new features
- 100% coverage for critical business logic
- All public APIs must have unit tests

#### Test Categories
- **Unit Tests**: Fast, isolated, no external dependencies
- **Integration Tests**: Database, file system, external services
- **End-to-End Tests**: Full workflow validation

### 3. Test Execution Before Commit

**MANDATORY: Run ALL tests before committing:**

```bash
# Run all tests across solution
dotnet test

# Run specific project tests
dotnet test Services.Tests/Services.Tests.csproj
dotnet test Application.AI.Tests/Application.AI.Tests.csproj

# Verify 100% pass rate
# Expected output: failed: 0; succeeded: [all]
```

### 4. Fixing Failing Tests

When tests fail:

1. **NEVER commit failing tests**
2. **Fix immediately** or revert changes
3. **Update test expectations** if behavior intentionally changed
4. **Add new tests** for uncovered scenarios

#### Common Test Failures

**Mock Setup Issues**:
```csharp
// ? BAD: Mock returns null unexpectedly
_mockMemory.Setup(m => m.SearchAsync(...)).ReturnsAsync(null);

// ? GOOD: Provide valid context
_mockMemory.Setup(m => m.SearchAsync(...)).ReturnsAsync("valid context");
```

**Test Dependencies**:
```csharp
// ? BAD: Test depends on implementation details
Assert.Contains("specific error text", result);

// ? GOOD: Test behavior, not implementation
Assert.Contains("ERROR", result, StringComparison.OrdinalIgnoreCase);
Assert.True(result.Contains("timeout") || result.Contains("failed"));
```

### 5. Test Maintenance

#### Duplicate Tests
- **NEVER have duplicate test methods**
- Use `edit_file` carefully to avoid duplications
- Always verify no duplicates after file edits

#### Test Naming
- Use descriptive names: `MethodName_Scenario_ExpectedBehavior`
- Example: `SendMessageAsync_WithEmptyResponse_ReturnsWarningMessage`

#### Test Organization
- Group related tests in `#region` blocks
- Keep helper methods at the top
- Order: Constructor ? Happy Path ? Edge Cases ? Error Cases

### 6. CI/CD Integration

Tests run automatically on:
- Every commit to feature branches
- Pull requests to main/develop
- Pre-merge validation

**Pipeline Failure = Block Merge**

### 7. Performance Tests

Performance-sensitive code must include benchmarks:
```csharp
[Fact]
public async Task FeatureName_Performance_MeetsRequirements()
{
    var stopwatch = Stopwatch.StartNew();
    // ... execute feature ...
    stopwatch.Stop();
    
    Assert.True(stopwatch.ElapsedMilliseconds < 100, 
        $"Expected < 100ms, got {stopwatch.ElapsedMilliseconds}ms");
}
```

### 8. Test Data Management

#### Mock Data
- Use realistic test data
- Provide sufficient context (> 150 chars for RAG tests)
- Use helper methods for complex setup

#### Test Isolation
- Each test must be independent
- No shared state between tests
- Clean up after each test if needed

### 9. Known Issues Handling

If tests fail due to pre-existing issues:
1. **Document** the issue in a separate task
2. **Create** a GitHub issue
3. **Mark** tests with `[Fact(Skip = "Issue #123")]` temporarily
4. **Fix** within same sprint

### 10. Test Review Checklist

Before committing, verify:
- [ ] All tests pass locally
- [ ] No duplicate test methods
- [ ] New features have tests
- [ ] Test names are descriptive
- [ ] Mocks are properly configured
- [ ] No hardcoded values
- [ ] Performance tests for slow operations
- [ ] Edge cases covered

## Current Status (2024)

### ? Passing Test Suites
- **Services.Tests**: 76/76 tests passing
  - Language stop words: 50+ tests
  - Fuzzy search: 40+ tests
  - Weighted embeddings: 12 tests

### ?? Known Issues
- **Application.AI.Tests**: 10 tests need fixing
  - Issue: Mock setup doesn't match actual code flow
  - Fix: Provide valid context so LLM code path is reached
  - Status: IN PROGRESS

## Example: Fixing Mock Setup Issues

### Problem
Tests returning "I don't have any relevant information" when expecting error handling:

```csharp
// ? This causes early return before LLM is called
SetupSearchRelevantMemoryAsync(_mockSearchableMemory, null);
```

### Solution
Provide valid context so the LLM code path executes:

```csharp
// ? This allows LLM to be called and exceptions to be tested
var context = "Artificial Intelligence (AI) refers to the simulation...";
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

## Automated Enforcement

### Pre-Commit Hook (Optional)
```bash
#!/bin/sh
# .git/hooks/pre-commit

dotnet test
if [ $? -ne 0 ]; then
    echo "? Tests failed. Commit blocked."
    exit 1
fi

echo "? All tests passed."
```

### GitHub Actions
```yaml
name: Test Suite
on: [push, pull_request]
jobs:
  test:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v2
      - uses: actions/setup-dotnet@v1
      - run: dotnet test --no-build --verbosity normal
```

## Summary

**Golden Rule**: If tests don't pass 100%, don't commit. Period.

This ensures:
- Code quality remains high
- Regressions are caught immediately
- Team productivity stays high
- Production deployments are confident

---

**Document Version**: 1.0  
**Last Updated**: 2024  
**Status**: MANDATORY FOR ALL COMMITS
