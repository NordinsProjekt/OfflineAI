# Factories Project - Complete Component Reference

## Overview
The **Factories** project provides a clean, fluent API for creating `ProcessStartInfo` objects used to spawn LLM processes. This small but critical project ensures consistent process configuration across the entire solution.

---

## Project Structure

```
Factories/
??? LlmFactory.cs                          # Main factory class
??? Extensions/
?   ??? ProcessStartInfoExtensions.cs      # Fluent API extension methods
??? Factories.csproj                       # Project file (no dependencies)
```

---

## LlmFactory.cs

### Purpose
Static factory class that creates pre-configured `ProcessStartInfo` objects for LLM execution.

### Class Definition
```csharp
namespace Factories;

public static class LlmFactory
{
    public static ProcessStartInfo Create();
    public static ProcessStartInfo CreateForLlama(string cliPath, string modelPath);
}
```

### Methods

#### 1. `Create()`
**Purpose**: Creates a `ProcessStartInfo` with default values for any process execution.

**Signature**:
```csharp
public static ProcessStartInfo Create()
```

**Returns**: 
- `ProcessStartInfo` with baseline configuration applied

**Usage**:
```csharp
var psi = LlmFactory.Create();
// Now configure with custom settings
psi.FileName = "myapp.exe";
psi.Arguments = "--flag value";
```

**What It Does**:
```csharp
return new ProcessStartInfo()
    .SetDefaultValues(); // Calls extension method
```

**Default Values Applied**:
- `UseShellExecute = false` - Direct process execution
- `RedirectStandardOutput = true` - Capture output
- `RedirectStandardError = true` - Capture errors
- `CreateNoWindow = true` - Hidden console window
- `StandardOutputEncoding = UTF-8` - Multi-language support
- `StandardErrorEncoding = UTF-8` - Multi-language support

---

#### 2. `CreateForLlama(string cliPath, string modelPath)`
**Purpose**: Creates a fully configured `ProcessStartInfo` for llama.cpp execution.

**Signature**:
```csharp
public static ProcessStartInfo CreateForLlama(string cliPath, string modelPath)
```

**Parameters**:
- `cliPath` (string): Path to `llama-cli.exe` executable
- `modelPath` (string): Path to GGUF model file

**Returns**: 
- `ProcessStartInfo` ready for additional arguments and `.Build()`

**Usage**:
```csharp
var process = LlmFactory.CreateForLlama(
    "d:/llama.cpp/llama-cli.exe",
    "d:/models/tinyllama-1.1b.gguf"
).Build();

process.Start();
```

**What It Does**:
```csharp
return Create()                    // Get baseline config
    .SetLlmCli(cliPath)           // Set executable path
    .SetModel(modelPath);          // Add model argument
```

**Resulting Configuration**:
```
FileName: "d:/llama.cpp/llama-cli.exe"
Arguments: "-m "d:/models/tinyllama-1.1b.gguf""
+ All default values from Create()
```

---

### Complete Usage Examples

#### Example 1: Basic LLM Process
```csharp
using Factories;
using Factories.Extensions;

var process = LlmFactory.CreateForLlama(
    @"C:\llama.cpp\llama-cli.exe",
    @"C:\models\tinyllama-1.1b-q5_k_m.gguf"
).Build();

process.OutputDataReceived += (s, e) => Console.WriteLine(e.Data);
process.Start();
process.BeginOutputReadLine();
process.WaitForExit();
```

#### Example 2: Custom Generation Parameters
```csharp
var processInfo = LlmFactory.CreateForLlama(llmPath, modelPath);

// Add generation parameters
processInfo.Arguments += " -p \"What is AI?\"";  // Prompt
processInfo.Arguments += " -n 200";              // Max tokens
processInfo.Arguments += " --temp 0.3";          // Temperature
processInfo.Arguments += " --top-k 40";          // Top-K sampling
processInfo.Arguments += " --top-p 0.9";         // Top-P sampling
processInfo.Arguments += " -ngl 0";              // CPU-only (no GPU)

var process = processInfo.Build();
process.Start();
```

#### Example 3: GPU Acceleration
```csharp
var processInfo = LlmFactory.CreateForLlama(llmPath, modelPath);

// GPU offloading
processInfo.Arguments += " -ngl 35";             // Offload 35 layers to GPU
processInfo.Arguments += " -c 4096";             // 4K context window

var process = processInfo.Build();
process.Start();
```

#### Example 4: In PersistentLlmProcess (Real Usage)
```csharp
// From AI/Processing/PersistentLlmProcess.cs
var processInfo = LlmFactory.CreateForLlama(_llmPath, _modelPath);

// Build full prompt
var fullPrompt = $"{systemPrompt}\n\nUser: {userQuestion}\nAssistant:";

// Configure
processInfo.Arguments += $" -p \"{fullPrompt}\"";
processInfo.Arguments += $" -c 2048";
processInfo.Arguments += $" -ngl {(useGpu ? gpuLayers : 0)}";
processInfo.Arguments += $" -n {maxTokens}";
processInfo.Arguments += $" --temp {temperature:F2}";

var process = processInfo.Build();
process.Start();
```

---

## ProcessStartInfoExtensions.cs

### Purpose
Extension methods that provide fluent API for configuring `ProcessStartInfo` objects.

### Namespace
```csharp
namespace Factories.Extensions;

public static class ProcessStartInfoExtensions
```

### Extension Methods

---

#### 1. `SetDefaultValues()`
**Purpose**: Applies baseline configuration for process execution.

**Signature**:
```csharp
public static ProcessStartInfo SetDefaultValues(this ProcessStartInfo processStartInfo)
```

**Returns**: `this` (enables chaining)

**Configuration Applied**:
```csharp
processStartInfo.UseShellExecute = false;
processStartInfo.RedirectStandardOutput = true;
processStartInfo.RedirectStandardError = true;
processStartInfo.CreateNoWindow = true;
processStartInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;
processStartInfo.StandardErrorEncoding = System.Text.Encoding.UTF8;
```

**Why Each Setting**:
| Setting | Value | Reason |
|---------|-------|--------|
| `UseShellExecute` | `false` | Direct process execution (required for redirection) |
| `RedirectStandardOutput` | `true` | Capture LLM output for processing |
| `RedirectStandardError` | `true` | Capture errors for diagnostics |
| `CreateNoWindow` | `true` | Hide console window (background execution) |
| `StandardOutputEncoding` | `UTF-8` | **Critical**: Preserve Swedish/international characters |
| `StandardErrorEncoding` | `UTF-8` | **Critical**: Preserve error messages with special chars |

**UTF-8 Encoding - Critical for Multi-Language Support**:
```csharp
// ? Without UTF-8 (Windows-1252 default):
// Swedish: "Plastpåsar" ? "Plast?Ñ?Ñsar" (corrupted)
// Arabic: "????" ? "?????" (unreadable)

// ? With UTF-8:
// Swedish: "Plastpåsar" ? "Plastpåsar" (correct)
// Arabic: "????" ? "????" (correct)
```

**Usage**:
```csharp
var psi = new ProcessStartInfo().SetDefaultValues();
// Now customize as needed
```

---

#### 2. `SetLlmCli(string fileName)`
**Purpose**: Sets the executable path for the LLM CLI.

**Signature**:
```csharp
public static ProcessStartInfo SetLlmCli(this ProcessStartInfo processStartInfo, string fileName)
```

**Parameters**:
- `fileName` (string): Path to executable (e.g., `"llama-cli.exe"`)

**Returns**: `this` (enables chaining)

**Implementation**:
```csharp
processStartInfo.FileName = fileName;
return processStartInfo;
```

**Usage**:
```csharp
var psi = new ProcessStartInfo()
    .SetDefaultValues()
    .SetLlmCli(@"C:\llama.cpp\llama-cli.exe");
```

**Path Formats Supported**:
```csharp
// Absolute paths
.SetLlmCli(@"C:\llama.cpp\llama-cli.exe")          // ?
.SetLlmCli("C:/llama.cpp/llama-cli.exe")            // ?

// Relative paths (relative to current directory)
.SetLlmCli("./llama-cli.exe")                       // ?
.SetLlmCli("../tools/llama-cli.exe")                // ?

// Environment variables (expanded by OS)
.SetLlmCli("%LLAMA_HOME%/llama-cli.exe")            // ?
```

---

#### 3. `SetModel(string modelPath)`
**Purpose**: Adds model path argument to process arguments.

**Signature**:
```csharp
public static ProcessStartInfo SetModel(this ProcessStartInfo processStartInfo, string modelPath)
```

**Parameters**:
- `modelPath` (string): Path to GGUF model file

**Returns**: `this` (enables chaining)

**Implementation**:
```csharp
processStartInfo.Arguments += $" -m \"{modelPath}\"";
return processStartInfo;
```

**Why Quotes?**:
```csharp
// Without quotes: Fails if path contains spaces
// "C:\My Models\tinyllama.gguf" ? ERROR
// Arguments: -m C:\My Models\tinyllama.gguf
//                     ^^^^^^^ Interpreted as separate arguments

// With quotes: Works correctly
// "C:\My Models\tinyllama.gguf" ? SUCCESS
// Arguments: -m "C:\My Models\tinyllama.gguf"
//                ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^ Single argument
```

**Usage**:
```csharp
var psi = LlmFactory.Create()
    .SetLlmCli("llama-cli.exe")
    .SetModel(@"C:\models\tinyllama-1.1b-q5_k_m.gguf");

// Result: Arguments = "-m \"C:\models\tinyllama-1.1b-q5_k_m.gguf\""
```

**Important Note**:
- Appends to existing `Arguments` property
- Safe to call after other argument additions
- Automatically adds space before `-m`

---

#### 4. `Build()`
**Purpose**: Creates a `Process` object from configured `ProcessStartInfo`.

**Signature**:
```csharp
public static Process Build(this ProcessStartInfo processStartInfo)
```

**Returns**: `Process` instance ready to start

**Implementation**:
```csharp
var process = new Process { StartInfo = processStartInfo };
return process;
```

**Usage**:
```csharp
var process = LlmFactory.CreateForLlama(llmPath, modelPath)
    .Build(); // Creates Process

process.Start();
process.WaitForExit();
```

**Why Needed?**:
- **Fluent Terminator**: Signals end of configuration chain
- **Clear Intent**: Explicitly creates `Process` instance
- **Consistency**: All process creation follows same pattern

---

## Fluent API Patterns

### Method Chaining
**Pattern**: Each method returns `this` to enable chaining.

```csharp
// ? Fluent API (reads naturally)
var process = LlmFactory.Create()
    .SetLlmCli("llama-cli.exe")
    .SetModel("model.gguf")
    .Build();

// ? Without fluent API (verbose)
var psi = LlmFactory.Create();
psi.FileName = "llama-cli.exe";
psi.Arguments = "-m \"model.gguf\"";
var process = new Process { StartInfo = psi };
```

### Builder Pattern
**Pattern**: Separate construction from representation.

```csharp
// Construction phase (configure)
var processInfo = LlmFactory.CreateForLlama(llmPath, modelPath);
processInfo.Arguments += " -n 200 --temp 0.3";

// Representation phase (build)
var process = processInfo.Build();
```

### Extension Method Pattern
**Pattern**: Add methods to existing types without modifying them.

```csharp
// Extends System.Diagnostics.ProcessStartInfo
public static ProcessStartInfo SetModel(this ProcessStartInfo psi, ...)
```

---

## Integration with Other Projects

### Dependency Graph
```
AI\Processing\PersistentLlmProcess
    ? uses
Factories.LlmFactory.CreateForLlama()
    ? uses
Factories.Extensions.ProcessStartInfoExtensions
    ? creates
System.Diagnostics.ProcessStartInfo
    ? creates
System.Diagnostics.Process
```

### Usage in AI Project
**File**: `AI\Processing\PersistentLlmProcess.cs`

```csharp
// Create process for query
var processInfo = LlmFactory.CreateForLlama(_llmPath, _modelPath);

// Add generation parameters
processInfo.Arguments += $" -p \"{fullPrompt}\"";
processInfo.Arguments += $" -c 2048";
processInfo.Arguments += $" -ngl {gpuLayers}";
processInfo.Arguments += $" -n {maxTokens}";
processInfo.Arguments += $" --temp {temperature:F2}";
processInfo.Arguments += $" --top-k {topK}";

var process = processInfo.Build();
```

---

## Testing Strategy

### Unit Tests
```csharp
[Fact]
public void SetDefaultValues_ConfiguresAllProperties()
{
    // Arrange
    var psi = new ProcessStartInfo();
    
    // Act
    psi.SetDefaultValues();
    
    // Assert
    Assert.False(psi.UseShellExecute);
    Assert.True(psi.RedirectStandardOutput);
    Assert.True(psi.RedirectStandardError);
    Assert.True(psi.CreateNoWindow);
    Assert.Equal(Encoding.UTF8, psi.StandardOutputEncoding);
    Assert.Equal(Encoding.UTF8, psi.StandardErrorEncoding);
}

[Fact]
public void SetModel_AppendsArgument()
{
    // Arrange
    var psi = new ProcessStartInfo { Arguments = "" };
    
    // Act
    psi.SetModel("model.gguf");
    
    // Assert
    Assert.Equal(" -m \"model.gguf\"", psi.Arguments);
}

[Fact]
public void SetModel_HandlesPathsWithSpaces()
{
    // Arrange
    var psi = new ProcessStartInfo { Arguments = "" };
    
    // Act
    psi.SetModel("C:\\My Models\\model.gguf");
    
    // Assert
    Assert.Contains("\"C:\\My Models\\model.gguf\"", psi.Arguments);
}

[Fact]
public void Build_CreatesProcessWithCorrectStartInfo()
{
    // Arrange
    var psi = new ProcessStartInfo { FileName = "test.exe" };
    
    // Act
    var process = psi.Build();
    
    // Assert
    Assert.NotNull(process);
    Assert.Equal("test.exe", process.StartInfo.FileName);
}

[Fact]
public void CreateForLlama_SetsFileNameAndModel()
{
    // Act
    var psi = LlmFactory.CreateForLlama("llama.exe", "model.gguf");
    
    // Assert
    Assert.Equal("llama.exe", psi.FileName);
    Assert.Contains("-m \"model.gguf\"", psi.Arguments);
    Assert.Equal(Encoding.UTF8, psi.StandardOutputEncoding);
}
```

### Integration Tests
```csharp
[Fact(Skip = "Requires llama.cpp installation")]
public void EndToEnd_ProcessCreationAndExecution()
{
    // Arrange
    var cliPath = "path/to/llama-cli.exe";
    var modelPath = "path/to/model.gguf";
    
    // Act
    var process = LlmFactory.CreateForLlama(cliPath, modelPath)
        .Build();
    
    process.Start();
    
    // Assert
    Assert.False(process.HasExited);
    
    // Cleanup
    process.Kill();
    process.Dispose();
}
```

---

## Common Patterns and Anti-Patterns

### ? Correct Patterns

#### Pattern 1: Fluent Configuration
```csharp
var process = LlmFactory.CreateForLlama(llmPath, modelPath)
    .Build();
process.Start();
```

#### Pattern 2: Custom Arguments After Factory
```csharp
var processInfo = LlmFactory.CreateForLlama(llmPath, modelPath);
processInfo.Arguments += " -n 200 --temp 0.3";
var process = processInfo.Build();
```

#### Pattern 3: Store ProcessStartInfo for Reuse
```csharp
// Configure once
var baseConfig = LlmFactory.CreateForLlama(llmPath, modelPath);

// Use multiple times with different prompts
foreach (var prompt in prompts)
{
    var psi = baseConfig; // Copy reference
    psi.Arguments += $" -p \"{prompt}\"";
    var process = psi.Build();
    process.Start();
}
```

### ? Anti-Patterns

#### Anti-Pattern 1: Manual Configuration
```csharp
// ? Don't create ProcessStartInfo manually
var psi = new ProcessStartInfo
{
    FileName = "llama-cli.exe",
    Arguments = "-m \"model.gguf\"",
    UseShellExecute = false,
    // ... 10 more lines
};
var process = new Process { StartInfo = psi };

// ? Use factory instead
var process = LlmFactory.CreateForLlama("llama-cli.exe", "model.gguf").Build();
```

#### Anti-Pattern 2: Forgetting UTF-8 Encoding
```csharp
// ? Manual config without UTF-8
var psi = new ProcessStartInfo
{
    FileName = "llama-cli.exe",
    RedirectStandardOutput = true,
    // Missing: StandardOutputEncoding = UTF-8
};
// Result: Corrupted Swedish/international characters

// ? Factory sets UTF-8 automatically
var psi = LlmFactory.Create(); // UTF-8 is set
```

#### Anti-Pattern 3: Not Calling Build()
```csharp
// ? Wrong: ProcessStartInfo is not a Process
var processInfo = LlmFactory.CreateForLlama(llmPath, modelPath);
processInfo.Start(); // ERROR: ProcessStartInfo doesn't have Start()

// ? Correct: Build Process first
var process = LlmFactory.CreateForLlama(llmPath, modelPath).Build();
process.Start(); // SUCCESS
```

---

## Performance Characteristics

| Operation | Time | Allocations | Notes |
|-----------|------|-------------|-------|
| `Create()` | <1µs | 1 object | `new ProcessStartInfo()` |
| `SetDefaultValues()` | <1µs | 0 objects | Sets 7 properties |
| `SetLlmCli()` | <1µs | 0 objects | Sets 1 property |
| `SetModel()` | <1µs | 1 string | String concatenation |
| `Build()` | <1µs | 1 object | `new Process()` |
| **Total** | **<5µs** | **~3 objects** | Negligible overhead |

**Comparison to Process Spawn Time**:
- Factory overhead: ~5µs
- Process spawn time: ~100,000µs (100ms)
- **Overhead**: 0.005% of total time

**Conclusion**: Factory pattern adds zero measurable overhead.

---

## Troubleshooting

### Problem: Process Doesn't Start
**Symptom**: `Process.Start()` throws `Win32Exception`

**Possible Causes**:
1. `FileName` path is incorrect
2. Executable doesn't exist
3. No execute permissions

**Solution**:
```csharp
// Validate before starting
if (!File.Exists(llmPath))
    throw new FileNotFoundException($"LLM not found: {llmPath}");

if (!File.Exists(modelPath))
    throw new FileNotFoundException($"Model not found: {modelPath}");

var process = LlmFactory.CreateForLlama(llmPath, modelPath).Build();
process.Start();
```

### Problem: Corrupted Output Characters
**Symptom**: Swedish characters show as gibberish

**Cause**: Not using factory (UTF-8 encoding missing)

**Solution**:
```csharp
// ? Wrong: Manual config
var psi = new ProcessStartInfo { FileName = "llama.exe" };
// Missing UTF-8 encoding

// ? Correct: Use factory
var psi = LlmFactory.Create(); // UTF-8 set automatically
psi.SetLlmCli("llama.exe");
```

### Problem: Arguments Not Working
**Symptom**: LLM ignores arguments

**Cause**: Improper argument formatting (missing quotes, spaces, etc.)

**Solution**:
```csharp
// ? Correct: Proper formatting
processInfo.Arguments += " -n 200";          // Space before flag
processInfo.Arguments += " --temp 0.3";      // Space before flag
processInfo.Arguments += " -p \"prompt\"";   // Quotes around text

// ? Wrong: No spaces
processInfo.Arguments += "-n200";            // Missing space
```

---

## Best Practices

### ? DO
1. **Always use factory** for LLM process creation
2. **Chain methods** for readability
3. **Call `.Build()`** to create `Process`
4. **Add custom arguments** after factory methods
5. **Validate paths** before starting process

### ? DON'T
1. **Create `ProcessStartInfo` manually** for LLM processes
2. **Forget to call `.Build()`** before starting
3. **Modify factory source** without updating docs
4. **Call `SetModel()` multiple times** on same object
5. **Forget quotes** around paths with spaces

---

## Future Enhancements (Not Yet Implemented)

### 1. Configuration Validation
```csharp
public static ProcessStartInfo CreateForLlamaWithValidation(string cli, string model)
{
    if (!File.Exists(cli))
        throw new FileNotFoundException($"CLI not found: {cli}");
    
    if (!File.Exists(model))
        throw new FileNotFoundException($"Model not found: {model}");
    
    return CreateForLlama(cli, model);
}
```

### 2. Multiple Backend Support
```csharp
public static ProcessStartInfo CreateForOllama(string baseUrl, string model)
{
    // Configure for Ollama REST API
}

public static ProcessStartInfo CreateForVllm(string apiUrl, string model)
{
    // Configure for vLLM API
}
```

### 3. Configuration Object
```csharp
public record LlmConfig(string CliPath, string ModelPath, bool UseGpu, int GpuLayers);

public static ProcessStartInfo CreateFromConfig(LlmConfig config)
{
    var psi = CreateForLlama(config.CliPath, config.ModelPath);
    if (config.UseGpu)
        psi.Arguments += $" -ngl {config.GpuLayers}";
    return psi;
}
```

**Note**: These are future ideas, not current implementation.

---

## Document Version
- **File**: `Factories\LlmFactory.cs`, `Factories\Extensions\ProcessStartInfoExtensions.cs`
- **Purpose**: Process creation factory with fluent API
- **Key Features**: UTF-8 encoding, fluent interface, minimal dependencies
- **Dependencies**: .NET System.Diagnostics only
- **Consumed By**: AI\Processing\PersistentLlmProcess
- **Last Updated**: 2024
