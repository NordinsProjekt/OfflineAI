using System.Text;

namespace Services.Utilities;

/// <summary>
/// Comprehensive debugging utility for tracking EOS (End of Sequence) and EOF (End of File) characters
/// throughout the RAG pipeline and LLM processing.
/// </summary>
public static class EosEofDebugger
{
    // Common EOS/EOF markers from various model formats
    private static readonly string[] EosMarkers = 
    [
        "</s>",               // Llama 2 EOS
        "<|endoftext|>",      // GPT-style EOS
        "<|eot_id|>",         // Llama 3.2 end of turn
        "<|end|>",            // TinyLlama, Phi EOS
        "<|im_end|>",         // ChatML EOS
        "<|end_of_text|>",    // Llama 3.2 variation
        "[EOS]",              // Generic EOS marker
        "[EOF]",              // Generic EOF marker
        "\0",                 // Null terminator
        "\x04",               // EOT (End of Transmission) ASCII control character
        "\x1A"                // SUB (Substitute/EOF) ASCII control character
    ];
    
    /// <summary>
    /// Scan text for EOS/EOF markers and return detailed report.
    /// </summary>
    public static EosEofReport ScanForMarkers(string text, string location)
    {
        var report = new EosEofReport
        {
            Location = location,
            TextLength = text?.Length ?? 0,
            Timestamp = DateTime.UtcNow
        };
        
        if (string.IsNullOrEmpty(text))
        {
            report.IsClean = true;
            return report;
        }
        
        var findings = new List<EosEofFinding>();
        
        // Check for each known EOS/EOF marker
        foreach (var marker in EosMarkers)
        {
            var index = 0;
            while ((index = text.IndexOf(marker, index, StringComparison.Ordinal)) != -1)
            {
                findings.Add(new EosEofFinding
                {
                    Marker = marker,
                    Position = index,
                    Context = ExtractContext(text, index, marker.Length)
                });
                index += marker.Length;
            }
        }
        
        // Check for suspicious control characters
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c < 32 && c != '\r' && c != '\n' && c != '\t')
            {
                findings.Add(new EosEofFinding
                {
                    Marker = $"\\x{(int)c:X2}",
                    Position = i,
                    Context = ExtractContext(text, i, 1),
                    IsControlChar = true
                });
            }
        }
        
        report.Findings = findings;
        report.IsClean = findings.Count == 0;
        
        return report;
    }
    
    /// <summary>
    /// Validate that text is clean before sending to LLM.
    /// Throws exception if EOS/EOF markers are found.
    /// </summary>
    public static void ValidateCleanBeforeLlm(string text, string context)
    {
        var report = ScanForMarkers(text, context);
        
        if (!report.IsClean)
        {
            var message = new StringBuilder();
            message.AppendLine($"? CRITICAL: EOS/EOF markers detected in text before LLM!");
            message.AppendLine($"Location: {context}");
            message.AppendLine($"Found {report.Findings.Count} marker(s):");
            
            foreach (var finding in report.Findings.Take(5))
            {
                message.AppendLine($"  - {finding.Marker} at position {finding.Position}");
                message.AppendLine($"    Context: ...{finding.Context}...");
            }
            
            if (report.Findings.Count > 5)
            {
                message.AppendLine($"  ... and {report.Findings.Count - 5} more");
            }
            
            throw new InvalidOperationException(message.ToString());
        }
    }
    
    /// <summary>
    /// Log scan results to console with detailed formatting.
    /// </summary>
    public static void LogReport(EosEofReport report, bool onlyIfDirty = true)
    {
        if (onlyIfDirty && report.IsClean)
            return;
        
        Console.WriteLine();
        Console.WriteLine("?????????????????????????????????????????????????????????????????");
        Console.WriteLine($"?  EOS/EOF Scan Report: {report.Location,-40} ?");
        Console.WriteLine("?????????????????????????????????????????????????????????????????");
        Console.WriteLine($"Timestamp:    {report.Timestamp:yyyy-MM-dd HH:mm:ss.fff}");
        Console.WriteLine($"Text Length:  {report.TextLength} characters");
        Console.WriteLine($"Status:       {(report.IsClean ? "? CLEAN" : "??  CONTAINS MARKERS")}");
        Console.WriteLine($"Findings:     {report.Findings.Count}");
        
        if (!report.IsClean)
        {
            Console.WriteLine();
            Console.WriteLine("???????????????????????????????????????????????????????????????");
            Console.WriteLine("DETECTED MARKERS:");
            Console.WriteLine("???????????????????????????????????????????????????????????????");
            
            var grouped = report.Findings.GroupBy(f => f.Marker);
            foreach (var group in grouped)
            {
                Console.WriteLine($"\n??  Marker: {group.Key} (found {group.Count()} time(s))");
                
                foreach (var finding in group.Take(3))
                {
                    Console.WriteLine($"   Position {finding.Position}:");
                    Console.WriteLine($"   Context:  ...{finding.Context}...");
                }
                
                if (group.Count() > 3)
                {
                    Console.WriteLine($"   ... and {group.Count() - 3} more occurrences");
                }
            }
        }
        
        Console.WriteLine("???????????????????????????????????????????????????????????????");
        Console.WriteLine();
    }
    
    /// <summary>
    /// Remove all known EOS/EOF markers from text.
    /// </summary>
    public static string CleanMarkers(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        
        var cleaned = text;
        
        // Remove all known EOS markers
        foreach (var marker in EosMarkers)
        {
            cleaned = cleaned.Replace(marker, "");
        }
        
        // Remove control characters except whitespace
        var sb = new StringBuilder(cleaned.Length);
        foreach (char c in cleaned)
        {
            if (c >= 32 || c == '\r' || c == '\n' || c == '\t')
            {
                sb.Append(c);
            }
        }
        
        return sb.ToString();
    }
    
    private static string ExtractContext(string text, int position, int markerLength, int contextChars = 50)
    {
        int start = Math.Max(0, position - contextChars);
        int end = Math.Min(text.Length, position + markerLength + contextChars);
        
        var context = text.Substring(start, end - start);
        
        // Escape special characters for display
        context = context
            .Replace("\r", "\\r")
            .Replace("\n", "\\n")
            .Replace("\t", "\\t")
            .Replace("\0", "\\0");
        
        return context;
    }
}

/// <summary>
/// Report containing scan results for EOS/EOF markers.
/// </summary>
public class EosEofReport
{
    public string Location { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public int TextLength { get; set; }
    public bool IsClean { get; set; }
    public List<EosEofFinding> Findings { get; set; } = new();
    
    public override string ToString()
    {
        return $"[{Location}] {(IsClean ? "? Clean" : $"??  {Findings.Count} markers found")} ({TextLength} chars)";
    }
}

/// <summary>
/// Individual finding of an EOS/EOF marker.
/// </summary>
public class EosEofFinding
{
    public string Marker { get; set; } = "";
    public int Position { get; set; }
    public string Context { get; set; } = "";
    public bool IsControlChar { get; set; }
    
    public override string ToString()
    {
        return $"{Marker} at position {Position}";
    }
}
