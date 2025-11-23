using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Services.Utilities;

/// <summary>
/// Utility for cleaning memory fragments of special tokens, control characters, and artifacts.
/// Removes EOS (End of Sequence), EOF, and other problematic characters that can interfere with embeddings.
/// </summary>
public static class MemoryFragmentCleaner
{
    /// <summary>
    /// Clean text by removing special tokens, control characters, and other artifacts.
    /// </summary>
    public static string CleanText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;
        
        // Step 1: Remove special tokens
        text = RemoveSpecialTokens(text);
        
        // Step 2: Remove control characters
        text = RemoveControlCharacters(text);
        
        // Step 3: Fix encoding issues
        text = FixEncodingIssues(text);
        
        // Step 4: Normalize whitespace
        text = NormalizeWhitespace(text);
        
        // Step 5: Remove duplicate spaces and normalize line endings
        text = NormalizeSpacing(text);
        
        return text.Trim();
    }
    
    /// <summary>
    /// Remove common special tokens found in LLM outputs and tokenizer vocabularies.
    /// </summary>
    private static string RemoveSpecialTokens(string text)
    {
        // Common LLM special tokens
        var llmTokens = new[]
        {
            "<|endoftext|>",
            "<|end_of_text|>",
            "<|eot_id|>",
            "<|begin_of_text|>",
            "<|start_header_id|>",
            "<|end_header_id|>",
            "<|system|>",
            "<|user|>",
            "<|assistant|>",
            "<|end|>",
            "<|im_start|>",
            "<|im_end|>",
            "[INST]",
            "[/INST]",
            "<<SYS>>",
            "<</SYS>>",
            "<s>",
            "</s>",
            "[CLS]",
            "[SEP]",
            "[MASK]",
            "[PAD]",
            "[UNK]"
        };
        
        foreach (var token in llmTokens)
        {
            text = text.Replace(token, "");
        }
        
        // Remove patterns like [unused0], [unused1], etc.
        text = Regex.Replace(text, @"\[unused\d+\]", "");
        
        // Remove EOS/EOF markers
        text = Regex.Replace(text, @"<EOS>|<EOF>|\[EOS\]|\[EOF\]", "", RegexOptions.IgnoreCase);
        
        // Remove any remaining special tokens in brackets like [...]
        text = Regex.Replace(text, @"\[[A-Z_]{3,}\]", "");
        
        return text;
    }
    
    /// <summary>
    /// Remove control characters (except common whitespace characters).
    /// </summary>
    private static string RemoveControlCharacters(string text)
    {
        var sb = new StringBuilder(text.Length);
        
        foreach (char c in text)
        {
            // Keep only printable characters and common whitespace
            if (!char.IsControl(c) || c == '\n' || c == '\r' || c == '\t')
            {
                sb.Append(c);
            }
        }
        
        return sb.ToString();
    }
    
    /// <summary>
    /// Fix common encoding issues (mojibake, incorrect UTF-8 decoding, etc.)
    /// </summary>
    private static string FixEncodingIssues(string text)
    {
        // Fix common mojibake patterns using Unicode escape sequences
        var replacements = new Dictionary<string, string>
        {
            // Common UTF-8 to Latin-1 issues
            { "\u00E2\u0080\u0099", "'" },  // â€™ ? '
            { "\u00E2\u0080\u009C", "\"" }, // â€œ ? "
            { "\u00E2\u0080\u009D", "\"" }, // â€ ? "
            { "\u00E2\u0080\u0094", "\u2014" }, // â€" ? em dash
            { "\u00E2\u0080\u0093", "\u2013" }, // â€" ? en dash
            { "\u00E2\u0080\u00A6", "\u2026" }, // â€¦ ? …
            { "\u00C2\u0020", " " },  // Â  ? space
            { "\u00C3\u00A9", "\u00E9" }, // Ã© ? é
            { "\u00C3\u00A8", "\u00E8" }, // Ã¨ ? è
            { "\u00C3\u00A0", "\u00E0" }, // Ã  ? à
            { "\u00C2\u00B0", "\u00B0" }, // Â° ? °
            
            // Zero-width and other invisible characters
            { "\u200B", "" },  // Zero-width space
            { "\u200C", "" },  // Zero-width non-joiner
            { "\u200D", "" },  // Zero-width joiner
            { "\uFEFF", "" },  // Zero-width no-break space (BOM)
            { "\u00A0", " " }, // Non-breaking space to regular space
        };
        
        foreach (var (bad, good) in replacements)
        {
            text = text.Replace(bad, good);
        }
        
        return text;
    }
    
    /// <summary>
    /// Normalize whitespace characters to standard spaces and newlines.
    /// </summary>
    private static string NormalizeWhitespace(string text)
    {
        // Replace various Unicode spaces with regular space
        text = Regex.Replace(text, @"[\u00A0\u1680\u2000-\u200B\u202F\u205F\u3000]", " ");
        
        // Normalize line endings to \n
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");
        
        // Replace tabs with spaces
        text = text.Replace("\t", "    ");
        
        return text;
    }
    
    /// <summary>
    /// Normalize spacing (remove duplicate spaces, fix line breaks).
    /// </summary>
    private static string NormalizeSpacing(string text)
    {
        // Replace multiple spaces with single space
        text = Regex.Replace(text, @" {2,}", " ");
        
        // Replace more than 2 consecutive newlines with 2 newlines
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        
        // Remove spaces at start/end of lines
        text = Regex.Replace(text, @"[ \t]+$", "", RegexOptions.Multiline);
        text = Regex.Replace(text, @"^[ \t]+", "", RegexOptions.Multiline);
        
        return text;
    }
    
    /// <summary>
    /// Check if text contains special tokens or control characters that need cleaning.
    /// </summary>
    public static bool NeedsCleaning(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        
        // Check for special tokens
        if (Regex.IsMatch(text, @"<\|.*?\|>|\[.*?\]|<EOS>|<EOF>", RegexOptions.IgnoreCase))
            return true;
        
        // Check for control characters (except common whitespace)
        if (text.Any(c => char.IsControl(c) && c != '\n' && c != '\r' && c != '\t'))
            return true;
        
        // Check for encoding issues
        if (text.Contains("\u00E2\u0080") || text.Contains("\u00C3") || text.Contains("\u200B"))
            return true;
        
        // Check for excessive whitespace
        if (Regex.IsMatch(text, @" {3,}|\n{4,}"))
            return true;
        
        return false;
    }
    
    /// <summary>
    /// Get a report of issues found in the text.
    /// </summary>
    public static CleaningReport AnalyzeText(string text)
    {
        var report = new CleaningReport();
        
        if (string.IsNullOrWhiteSpace(text))
            return report;
        
        // Count special tokens
        report.SpecialTokenCount = Regex.Matches(text, @"<\|.*?\|>|\[.*?\]|<EOS>|<EOF>", RegexOptions.IgnoreCase).Count;
        
        // Count control characters
        report.ControlCharacterCount = text.Count(c => char.IsControl(c) && c != '\n' && c != '\r' && c != '\t');
        
        // Check for encoding issues
        report.HasEncodingIssues = text.Contains("\u00E2\u0080") || text.Contains("\u00C3") || text.Contains("\u200B");
        
        // Check for excessive whitespace
        report.ExcessiveWhitespaceCount = Regex.Matches(text, @" {3,}").Count + Regex.Matches(text, @"\n{4,}").Count;
        
        // Calculate percentage of problematic characters
        int totalIssues = report.SpecialTokenCount + report.ControlCharacterCount + report.ExcessiveWhitespaceCount;
        report.IssuePercentage = text.Length > 0 ? (double)totalIssues / text.Length * 100 : 0;
        
        return report;
    }
    
    /// <summary>
    /// Report of cleaning analysis results.
    /// </summary>
    public class CleaningReport
    {
        public int SpecialTokenCount { get; set; }
        public int ControlCharacterCount { get; set; }
        public bool HasEncodingIssues { get; set; }
        public int ExcessiveWhitespaceCount { get; set; }
        public double IssuePercentage { get; set; }
        
        public bool HasIssues => SpecialTokenCount > 0 || ControlCharacterCount > 0 || 
                                 HasEncodingIssues || ExcessiveWhitespaceCount > 0;
        
        public override string ToString()
        {
            if (!HasIssues)
                return "No issues found";
            
            var issues = new List<string>();
            
            if (SpecialTokenCount > 0)
                issues.Add($"{SpecialTokenCount} special tokens");
            if (ControlCharacterCount > 0)
                issues.Add($"{ControlCharacterCount} control characters");
            if (HasEncodingIssues)
                issues.Add("encoding issues");
            if (ExcessiveWhitespaceCount > 0)
                issues.Add($"{ExcessiveWhitespaceCount} excessive whitespace");
            
            return $"Found: {string.Join(", ", issues)} ({IssuePercentage:F2}% of text)";
        }
    }
}
