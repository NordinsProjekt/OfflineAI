# LlmFactory Tests - Quick Reference

## Test File Location
`OfflineAI.Tests\Factories\LlmFactoryTests.cs`

## Quick Stats
- **Total Tests**: 45
- **All Passing**: ?
- **Coverage**: 100% of public methods
- **Execution Time**: ~1 second

## Run Tests

### All LlmFactory tests
```bash
dotnet test --filter "FullyQualifiedName~LlmFactoryTests"
```

### By method
```bash
# Create() tests
dotnet test --filter "FullyQualifiedName~LlmFactoryTests.Create_"

# CreateForLlama() tests  
dotnet test --filter "FullyQualifiedName~LlmFactoryTests.CreateForLlama_"

# CreateForBoardGame() tests
dotnet test --filter "FullyQualifiedName~LlmFactoryTests.CreateForBoardGame_"
```

## Test Structure

### 1. Create() Tests (5 tests)
- Basic instance creation
- Default values validation
- Instance isolation

### 2. CreateForLlama() Tests (9 tests)
- Path configuration
- Model arguments
- Path handling (spaces, special chars)
- Fluent API support

### 3. CreateForBoardGame() Tests (18 tests)
- Default parameters (maxTokens: 200, temp: 0.4)
- Custom parameters
- Sampling parameters
- Edge cases (zero, negative, large values)
- Fluent API chaining

### 4. Integration Tests (10 tests)
- Cross-method consistency
- Special characters
- Path formats (Windows/Unix)
- Complete configuration scenarios

### 5. Documentation Tests (3 tests)
- Real-world usage examples
- Board game Q&A scenario
- Basic Llama configuration

## Key Test Patterns

### Basic Test
```csharp
[Fact]
public void Create_ReturnsProcessStartInfo()
{
    // Act
    var result = LlmFactory.Create();

    // Assert
    Assert.NotNull(result);
    Assert.IsType<ProcessStartInfo>(result);
}
```

### Culture-Aware Test
```csharp
[Fact]
public void CreateForBoardGame_WithDefaultParameters_UsesDefaultTemperature()
{
    // Act
    var result = LlmFactory.CreateForBoardGame(cliPath, modelPath);

    // Assert
    Assert.True(
        result.Arguments.Contains("--temp 0.4") || 
        result.Arguments.Contains("--temp 0,4"));
}
```

### Theory Test
```csharp
[Theory]
[InlineData(50, 0.1f)]
[InlineData(100, 0.3f)]
[InlineData(200, 0.4f)]
public void CreateForBoardGame_WithVariousParameters_ConfiguresCorrectly(
    int maxTokens, float temperature)
{
    // Act & Assert
    var result = LlmFactory.CreateForBoardGame(
        cliPath, modelPath, maxTokens, temperature);
    Assert.Contains($"-n {maxTokens}", result.Arguments);
}
```

## What's Tested

### ProcessStartInfo Properties
? UseShellExecute (false)
? RedirectStandardOutput (true)  
? RedirectStandardError (true)
? CreateNoWindow (true)
? FileName
? Arguments

### LlmFactory Methods
? Create()
? CreateForLlama(cliPath, modelPath)
? CreateForBoardGame(cliPath, modelPath, maxTokens, temperature)

### Arguments Generated
? Model path: `-m "model.gguf"`
? Max tokens: `-n 200`
? Temperature: `--temp 0.4`
? Top-K: `--top-k 20`
? Top-P: `--top-p 0.8`
? Repeat penalty: `--repeat-penalty 1.1`
? Repeat last N: `--repeat-last-n 32`

### Edge Cases
? Empty paths
? Paths with spaces
? Special characters
? Zero values
? Negative values
? Large values
? Windows paths (\)
? Unix paths (/)
? Culture-specific decimals

## Common Assertions

```csharp
// Instance type
Assert.IsType<ProcessStartInfo>(result);

// Not null
Assert.NotNull(result);

// Contains argument
Assert.Contains("-m \"model.gguf\"", result.Arguments);

// Exact match
Assert.Equal(cliPath, result.FileName);

// Boolean properties
Assert.True(result.RedirectStandardOutput);
Assert.False(result.UseShellExecute);

// Culture-aware decimal
Assert.True(
    result.Arguments.Contains("--temp 0.4") || 
    result.Arguments.Contains("--temp 0,4"));
```

## Test Coverage Matrix

| Method | Basic | Custom Params | Edge Cases | Fluent API | Culture |
|--------|-------|---------------|------------|------------|---------|
| Create() | ? | N/A | ? | ? | N/A |
| CreateForLlama() | ? | N/A | ? | ? | N/A |
| CreateForBoardGame() | ? | ? | ? | ? | ? |

## Related Documentation
- Full test summary: `LlmFactoryTests-Summary.md`
- Implementation: `Factories\LlmFactory.cs`
- Extensions: `Factories\Extensions\ProcessStartInfoExtensions.cs`
