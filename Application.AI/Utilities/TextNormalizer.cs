using System.Text;

namespace Application.AI.Utilities;

/// <summary>
/// Utility class for normalizing text to handle special Unicode characters.
/// Converts problematic Unicode characters to ASCII equivalents for better compatibility
/// with tokenizers and processing systems.
/// </summary>
public static class TextNormalizer
{
    /// <summary>
    /// Normalizes text to handle special Unicode characters that might cause tokenizer issues.
    /// Converts smart quotes, curly apostrophes, and other problematic characters to ASCII equivalents.
    /// </summary>
    /// <param name="text">The text to normalize</param>
    /// <returns>Normalized text with ASCII equivalents for special Unicode characters</returns>
    public static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        
        // Replace smart/curly quotes with straight quotes
        text = text.Replace('\u201C', '"'); // " (left double quotation mark)
        text = text.Replace('\u201D', '"'); // " (right double quotation mark)
        text = text.Replace('\u2018', '\''); // ' (left single quotation mark)
        text = text.Replace('\u2019', '\''); // ' (right single quotation mark)
        
        // Replace em dash and en dash with regular dash
        text = text.Replace('\u2013', '-'); // – (en dash)
        text = text.Replace('\u2014', '-'); // — (em dash)
        
        // Replace ellipsis (multi-char replacement needs string version)
        text = text.Replace("\u2026", "..."); // … (horizontal ellipsis)
        
        // Remove or replace other problematic Unicode characters
        text = text.Replace('\u00A0', ' '); // Non-breaking space
        text = text.Replace("\u200B", ""); // Zero-width space
        
        // Remove control characters except common whitespace
        var normalized = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            // Keep: letters, digits, punctuation, symbols, and common whitespace
            if (char.IsLetterOrDigit(c) || 
                char.IsPunctuation(c) || 
                char.IsSymbol(c) ||
                c == ' ' || c == '\t' || c == '\r' || c == '\n')
            {
                normalized.Append(c);
            }
            // Skip other control characters and rare Unicode
        }
        
        return normalized.ToString();
    }
    
    /// <summary>
    /// Normalizes text with length limits.
    /// OPTIMIZED: Truncates text BEFORE normalization to avoid processing huge strings.
    /// </summary>
    /// <param name="text">The text to normalize</param>
    /// <param name="maxLength">Maximum allowed length (text will be truncated if longer)</param>
    /// <param name="fallbackText">Text to use if normalization results in empty string</param>
    /// <returns>Normalized text within length limits</returns>
    public static string NormalizeWithLimits(string text, int maxLength = 5000, string fallbackText = "[empty text]")
    {
        // OPTIMIZATION: Truncate BEFORE normalization to avoid processing huge texts
        // This prevents memory issues when importing large text files
        if (text != null && text.Length > maxLength)
        {
            Console.WriteLine($"[DEBUG] Truncating text from {text.Length} to {maxLength} chars before normalization");
            text = text.Substring(0, maxLength);
        }
        
        // Normalize after truncation
        text = Normalize(text);
        
        // Safety check: ensure we have valid text after normalization
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallbackText;
        }
        
        return text;
    }
    
    /// <summary>
    /// Removes all control characters from text while preserving common whitespace.
    /// </summary>
    /// <param name="text">The text to clean</param>
    /// <returns>Text with control characters removed</returns>
    public static string RemoveControlCharacters(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        
        var cleaned = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            // Keep letters, digits, punctuation, symbols, and common whitespace
            if (char.IsLetterOrDigit(c) || 
                char.IsPunctuation(c) || 
                char.IsSymbol(c) ||
                c == ' ' || c == '\t' || c == '\r' || c == '\n')
            {
                cleaned.Append(c);
            }
        }
        
        return cleaned.ToString();
    }
    
    /// <summary>
    /// Replaces Unicode quotation marks with ASCII equivalents.
    /// </summary>
    /// <param name="text">The text containing Unicode quotes</param>
    /// <returns>Text with ASCII quotes</returns>
    public static string NormalizeQuotes(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        
        return text
            .Replace('\u201C', '"')  // " (left double quotation mark)
            .Replace('\u201D', '"')  // " (right double quotation mark)
            .Replace('\u2018', '\'') // ' (left single quotation mark)
            .Replace('\u2019', '\''); // ' (right single quotation mark)
    }
    
    /// <summary>
    /// Replaces Unicode dashes with ASCII dash.
    /// </summary>
    /// <param name="text">The text containing Unicode dashes</param>
    /// <returns>Text with ASCII dashes</returns>
    public static string NormalizeDashes(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        
        return text
            .Replace('\u2013', '-')  // – (en dash)
            .Replace('\u2014', '-'); // — (em dash)
    }
    
    /// <summary>
    /// Replaces Unicode whitespace characters with standard ASCII space.
    /// </summary>
    /// <param name="text">The text containing Unicode whitespace</param>
    /// <returns>Text with ASCII whitespace</returns>
    public static string NormalizeWhitespace(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        
        return text
            .Replace('\u00A0', ' ')  // Non-breaking space
            .Replace("\u200B", "")   // Zero-width space
            .Replace("\u2026", "..."); // … (horizontal ellipsis)
    }
}
