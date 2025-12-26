using Services.Configuration;

namespace Services.UI;

/// <summary>
/// Service for handling console display and user interaction.
/// Centralizes all console UI logic for better maintainability.
/// </summary>
public static class DisplayService
{
   
    #region Embedding Service Initialization
    
    public static void ShowAttemptingGpuAcceleration(string provider)
    {
        Console.WriteLine($"[*] Attempting to enable {provider} GPU acceleration...");
    }
    
    public static void ShowGpuAccelerationEnabled(string provider)
    {
        Console.WriteLine($"[+] {provider} GPU acceleration enabled!");
    }
    
    public static void ShowGpuAccelerationNotAvailable(string provider, string message)
    {
        Console.WriteLine($"[!] {provider} not available: {message}");
    }
    
    public static void ShowFallingBackToCpu()
    {
        Console.WriteLine("[*] Falling back to memory-optimized CPU processing");
    }
    
    public static void ShowGpuConfiguration()
    {
        Console.WriteLine("[*] GPU Configuration:");
        Console.WriteLine("    Optimization: Full");
        Console.WriteLine("    Memory Arena: Enabled");
    }
    
    public static void ShowCpuConfiguration()
    {
        Console.WriteLine("[*] Memory-Optimized CPU Configuration:");
        Console.WriteLine("    Target: < 2GB RAM usage");
        Console.WriteLine("    Memory Arena: DISABLED (saves ~500MB)");
        Console.WriteLine("    Threading: Single-threaded (saves ~200MB per thread)");
        Console.WriteLine("    Execution: Sequential (minimal memory footprint)");
        Console.WriteLine("    Optimization: Basic (reduced temporary allocations)");
        Console.WriteLine("    [!] WARNING: This will be SLOW but memory-safe");
    }
    
    public static void ShowEmbeddingServiceInitialized(string modelName, int embeddingDimension, bool isGpu)
    {
        Console.WriteLine("[+] REAL BERT embeddings initialized!");
        Console.WriteLine($"    Model: {modelName}");
        Console.WriteLine($"    Embedding dimension: {embeddingDimension}");
        Console.WriteLine($"    Execution: {(isGpu ? "GPU" : "CPU (memory-optimized)")}");
        Console.WriteLine($"    Processing: Sequential (one embedding at a time)");
    }
    
    public static void ShowEmbeddingError(string message)
    {
        Console.WriteLine($"[ERROR] BERT embedding failed: {message}");
    }
    
    #endregion

    #region Collections Display

    #endregion
    
    #region Debug and Statistics

    /// <summary>
    /// Shows the system prompt that will be sent to the LLM for debugging purposes.
    /// </summary>
    /// <param name="relevantMemory">The relevant context being sent</param>
    /// <param name="debug">If true, displays the debug information. Default is false.</param>
    public static void ShowSystemPromptDebug(string relevantMemory, bool debug = false)
    {
        if (!debug)
        {
            return;
        }

        Console.WriteLine("\n╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  RAG CONTEXT RETRIEVED FROM DATABASE (DEBUG MODE)            ║");
        Console.WriteLine("║  (Before truncation to fit LLM context window)              ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        Console.WriteLine($"Total Length: {relevantMemory.Length} characters");
        Console.WriteLine($"Estimated Tokens: ~{relevantMemory.Length / 4} tokens\n");
        Console.WriteLine("─────────────────────────────────────────────────────────────────");
        Console.WriteLine("FULL RETRIEVED CONTEXT (from vector search):");
        Console.WriteLine("─────────────────────────────────────────────────────────────────\n");
        
        // Display the FULL relevant memory content without truncation
        Console.WriteLine(relevantMemory);
        
        Console.WriteLine("\n─────────────────────────────────────────────────────────────────");
        Console.WriteLine($"End of context ({relevantMemory.Length} chars)");
        Console.WriteLine("═══════════════════════════════════════════════════════════════\n");
    }

    #endregion

    #region Loading Progress

    public static void ShowLoadingFile(string gameName, string filePath)
    {
        Console.WriteLine($"    Loading {gameName} from {filePath}...");
    }

    public static void ShowCollectedSections(int sectionCount, string gameName)
    {
        Console.WriteLine($"    Collected {sectionCount} sections from {gameName}");
    }

    #endregion

    #region Utilities

    public static void WriteLine(string message = "")
    {
        Console.WriteLine(message);
    }

    #endregion

    #region Response Formatting
    
    /// <summary>
    /// Format and display an LLM response with proper code block formatting.
    /// </summary>
    /// <param name="response">Raw LLM response</param>
    /// <param name="useFormatter">Whether to apply code formatting. Default is true.</param>
    public static void ShowLlmResponse(string response, bool useFormatter = true)
    {
        if (useFormatter)
        {
            // Use the formatter service if available
            // For now, we'll do basic formatting inline
            response = FormatCodeBlocks(response);
        }
        
        Console.WriteLine(response);
    }
    
    /// <summary>
    /// Basic code block formatting for console output.
    /// Detects ```language code``` patterns and formats them.
    /// </summary>
    private static string FormatCodeBlocks(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;
        
        // Pattern to detect code blocks
        var pattern = @"```([a-zA-Z]+)(.*?)```";
        var matches = System.Text.RegularExpressions.Regex.Matches(text, pattern, 
            System.Text.RegularExpressions.RegexOptions.Singleline);
        
        if (matches.Count == 0)
            return text; // No code blocks found
        
        var result = text;
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var language = match.Groups[1].Value;
            var code = match.Groups[2].Value.Trim();
            
            // Format the code block with headers
            var formatted = $"\n{'='}{language.ToUpper()} CODE{'='}\n{FormatCode(code)}\n{'='}\n";
            result = result.Replace(match.Value, formatted);
        }
        
        return result;
    }
    
    /// <summary>
    /// Format code with basic indentation.
    /// </summary>
    private static string FormatCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return code;
        
        var lines = code.Split(new[] { '\n', '\r' }, StringSplitOptions.None);
        var formatted = new List<string>();
        var indent = 0;
        
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                formatted.Add("");
                continue;
            }
            
            // Decrease indent for closing braces
            if (trimmed.StartsWith("}") || trimmed.StartsWith("]"))
                indent = Math.Max(0, indent - 1);
            
            // Add indented line
            formatted.Add(new string(' ', indent * 4) + trimmed);
            
            // Increase indent for opening braces
            if (trimmed.EndsWith("{") || trimmed.EndsWith("["))
                indent++;
        }
        
        return string.Join(Environment.NewLine, formatted);
    }
    
    /// <summary>
    /// Format performance metrics to append to a response.
    /// </summary>
    /// <param name="totalTimeMs">Total time in milliseconds</param>
    /// <param name="promptTokens">Number of prompt tokens</param>
    /// <param name="completionTokens">Number of completion tokens</param>
    /// <returns>Formatted performance metrics string</returns>
    public static string FormatPerformanceMetrics(double totalTimeMs, int promptTokens, int completionTokens)
    {
        var tokensPerSec = completionTokens / (totalTimeMs / 1000.0);
        var totalTokens = promptTokens + completionTokens;
        
        return $"\n\n" +
               $"============================\n" +
               $"| **Performance Metrics**\n" +
               $"============================\n" +
               $"|  **Time:** {totalTimeMs / 1000.0:F2}s\n" +
               $"|  **Tokens:** {promptTokens} prompt + {completionTokens} completion = {totalTokens} total\n" +
               $"|  **Speed:** {tokensPerSec:F1} tokens/sec\n" +
               $"============================";
    }
    
    #endregion
    
    /// <summary>
    /// Display generation settings being used for the query
    /// </summary>
    public static void ShowGenerationSettings(GenerationSettings settings, bool enableRag)
    {
        WriteLine($"\n╔═══════════════════════════════════════════════════════════════╗");
        WriteLine($"║  Generation Settings for Query                                ║");
        WriteLine($"╚═══════════════════════════════════════════════════════════════╝");
        WriteLine($"  RAG Mode:            {(enableRag ? "ENABLED" : "DISABLED")}");
        WriteLine($"  Temperature:         {settings.Temperature:F2}");
        WriteLine($"  Max Tokens:          {settings.MaxTokens}");
        WriteLine($"  Top-K:               {settings.TopK}");
        WriteLine($"  Top-P:               {settings.TopP:F2}");
        WriteLine($"  Repeat Penalty:      {settings.RepeatPenalty:F2}");
        WriteLine($"  Presence Penalty:    {settings.PresencePenalty:F2}");
        WriteLine($"  Frequency Penalty:   {settings.FrequencyPenalty:F2}");
        WriteLine($"");
    }
}
