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
    /// Converts smart quotes, curly apostrophes, backticks, and other problematic characters to ASCII equivalents.
    /// </summary>
    /// <param name="text">The text to normalize</param>
    /// <returns>Normalized text with ASCII equivalents for special Unicode characters</returns>
    public static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        
        // Replace backticks and grave accents (these can cause tokenizer issues)
        text = text.Replace('`', '\''); // Backtick to single quote
        text = text.Replace('´', '\''); // Acute accent to single quote
        text = text.Replace('?', '\''); // Modifier letter grave accent to single quote
        
        // Replace smart/curly quotes with straight quotes
        text = text.Replace('\u201C', '"'); // " (left double quotation mark)
        text = text.Replace('\u201D', '"'); // " (right double quotation mark)
        text = text.Replace('\u2018', '\''); // ' (left single quotation mark)
        text = text.Replace('\u2019', '\''); // ' (right single quotation mark)
        text = text.Replace('\u201B', '\''); // ? (single high-reversed-9 quotation mark)
        
        // Replace em dash and en dash with regular dash
        text = text.Replace('\u2013', '-'); // – (en dash)
        text = text.Replace('\u2014', '-'); // — (em dash)
        text = text.Replace('\u2212', '-'); // ? (minus sign)
        
        // Replace ellipsis (multi-char replacement needs string version)
        text = text.Replace("\u2026", "..."); // … (horizontal ellipsis)
        
        // Remove or replace other problematic Unicode characters
        text = text.Replace('\u00A0', ' '); // Non-breaking space
        text = text.Replace("\u200B", ""); // Zero-width space
        text = text.Replace("\u200C", ""); // Zero-width non-joiner
        text = text.Replace("\u200D", ""); // Zero-width joiner
        text = text.Replace("\uFEFF", ""); // Zero-width no-break space (BOM)
        
        // Replace other quotation-like characters
        text = text.Replace('«', '"'); // Left-pointing double angle quotation mark
        text = text.Replace('»', '"'); // Right-pointing double angle quotation mark
        text = text.Replace('‹', '\''); // Single left-pointing angle quotation mark
        text = text.Replace('›', '\''); // Single right-pointing angle quotation mark
        
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
    /// Normalizes text with length limits and additional safety checks.
    /// </summary>
    /// <param name="text">The text to normalize</param>
    /// <param name="maxLength">Maximum allowed length (text will be truncated if longer)</param>
    /// <param name="fallbackText">Text to use if normalization results in empty string</param>
    /// <returns>Normalized text within length limits</returns>
    public static string NormalizeWithLimits(string text, int maxLength = 5000, string fallbackText = "[empty text]")
    {
        // Normalize first
        text = Normalize(text);
        
        // Safety check: ensure we have valid text after normalization
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallbackText;
        }
        
        // Ensure text is not too long
        if (text.Length > maxLength)
        {
            text = text.Substring(0, maxLength);
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
    /// Replaces Unicode quotation marks and backticks with ASCII equivalents.
    /// </summary>
    /// <param name="text">The text containing Unicode quotes</param>
    /// <returns>Text with ASCII quotes</returns>
    public static string NormalizeQuotes(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        
        return text
            .Replace('`', '\'')      // Backtick
            .Replace('´', '\'')      // Acute accent
            .Replace('\u201C', '"')  // " (left double quotation mark)
            .Replace('\u201D', '"')  // " (right double quotation mark)
            .Replace('\u2018', '\'') // ' (left single quotation mark)
            .Replace('\u2019', '\'') // ' (right single quotation mark)
            .Replace('\u201B', '\'') // ? (single high-reversed-9 quotation mark)
            .Replace('«', '"')       // Left angle quote
            .Replace('»', '"')       // Right angle quote
            .Replace('‹', '\'')      // Single left angle quote
            .Replace('›', '\'');     // Single right angle quote
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
            .Replace('\u2014', '-')  // — (em dash)
            .Replace('\u2212', '-'); // ? (minus sign)
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
            .Replace('\u00A0', ' ')   // Non-breaking space
            .Replace("\u200B", "")    // Zero-width space
            .Replace("\u200C", "")    // Zero-width non-joiner
            .Replace("\u200D", "")    // Zero-width joiner
            .Replace("\uFEFF", "")    // Zero-width no-break space
            .Replace("\u2026", "..."); // … (horizontal ellipsis)
    }
}
