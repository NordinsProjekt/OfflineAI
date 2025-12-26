using System.Text;
using System.Text.RegularExpressions;
using AiDashboard.Services.Interfaces;

namespace AiDashboard.Services;

/// <summary>
/// Formats LLM responses by detecting and formatting code blocks with proper indentation and syntax highlighting.
/// Supports C#, Python, Java, JavaScript, HTML, and other common languages with IDE-like formatting.
/// </summary>
public class LlmResponseFormatterService : ILlmResponseFormatterService
{
    private static readonly string[] SupportedLanguages = 
    {
        "csharp", "cs", "c#",
        "python", "py",
        "java",
        "javascript", "js",
        "typescript", "ts",
        "html",
        "css",
        "sql",
        "json",
        "xml",
        "bash", "sh",
        "powershell", "ps1",
        "razor", "cshtml"
    };

    /// <summary>
    /// Formats an LLM response by detecting and formatting code blocks.
    /// Handles both markdown-style code blocks (```language) and inline code markers.
    /// Returns HTML-formatted text ready for display.
    /// </summary>
    public string FormatResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return response;

        // First, try to detect markdown-style code blocks (```language ... ```
        var formattedResponse = FormatMarkdownCodeBlocks(response);
        
        // If no markdown blocks found, try to detect inline code patterns
        if (formattedResponse == response)
        {
            formattedResponse = FormatInlineCodePatterns(response);
        }

        // Convert line breaks to <br> ONLY for text outside <pre> tags
        formattedResponse = ConvertNewlinesToBrTagsOutsidePre(formattedResponse);

        return formattedResponse;
    }

    /// <summary>
    /// Converts newlines to br tags only outside pre tags.
    /// Preserves newlines inside pre tags for proper code formatting.
    /// </summary>
    private string ConvertNewlinesToBrTagsOutsidePre(string html)
    {
        var result = new StringBuilder();
        var inPreTag = false;
        var i = 0;
        
        while (i < html.Length)
        {
            // Check if we're entering a <pre> tag
            if (!inPreTag && i + 4 < html.Length && html.Substring(i, 4) == "<pre")
            {
                inPreTag = true;
                result.Append(html[i]);
                i++;
                continue;
            }
            
            // Check if we're exiting a </pre> tag
            if (inPreTag && i + 6 < html.Length && html.Substring(i, 6) == "</pre>")
            {
                inPreTag = false;
                result.Append("</pre>");
                i += 6;
                continue;
            }
            
            // Handle newlines
            if (html[i] == '\n')
            {
                if (inPreTag)
                {
                    // Inside <pre> tag - keep newline as-is
                    result.Append('\n');
                }
                else
                {
                    // Outside <pre> tag - convert to <br>
                    result.Append("<br>");
                }
                i++;
                continue;
            }
            
            // Regular character
            result.Append(html[i]);
            i++;
        }
        
        return result.ToString();
    }

    /// <summary>
    /// Detects if the text contains code blocks.
    /// </summary>
    public bool ContainsCodeBlocks(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        // Check for markdown code blocks
        if (Regex.IsMatch(text, @"```\w+"))
            return true;

        // Check for common code patterns (brackets, semicolons, etc.)
        var codePatterns = new[]
        {
            @"\{[^}]*\}", // Curly braces
            @";\s*$",     // Semicolons at end of line
            @"\b(public|private|protected|internal|class|interface|namespace|using)\s+\w+", // C# keywords
            @"\bfunction\s+\w+", // function keyword
            @"\bdef\s+\w+",     // Python def
            @"<[a-zA-Z][^>]*>.*?</[a-zA-Z][^>]*>",    // HTML tags with content
            @"<[a-zA-Z][^>]*/>",    // Self-closing HTML tags
        };

        return codePatterns.Any(pattern => Regex.IsMatch(text, pattern));
    }

    /// <summary>
    /// Extracts all code blocks from the text.
    /// </summary>
    public List<CodeBlock> ExtractCodeBlocks(string text)
    {
        var codeBlocks = new List<CodeBlock>();

        if (string.IsNullOrWhiteSpace(text))
            return codeBlocks;

        // Pattern to match markdown code blocks using known language names
        // Order languages by length (longest first) to ensure proper matching
        var languages = new[] { "javascript", "typescript", "powershell", "csharp", "python", "bash", "html", "razor", "cshtml", "java", "json", "css", "sql", "xml", "c#", "ts", "js", "py", "cs", "sh", "ps1" };
        var pattern = $@"```({string.Join("|", languages.Select(Regex.Escape))})(.*?)```";
        var matches = Regex.Matches(text, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);

        foreach (Match match in matches)
        {
            var language = match.Groups[1].Value.ToLowerInvariant();
            var rawCode = match.Groups[2].Value.Trim();
            
            // Skip empty code blocks
            if (string.IsNullOrWhiteSpace(rawCode))
                continue;
            
            var codeBlock = new CodeBlock
            {
                Language = NormalizeLanguageName(language),
                RawCode = rawCode,
                FormattedCode = FormatCode(rawCode, language),
                StartIndex = match.Index,
                EndIndex = match.Index + match.Length
            };

            codeBlocks.Add(codeBlock);
        }

        // If no markdown blocks, try to detect inline code
        if (codeBlocks.Count == 0)
        {
            var inlineBlock = DetectInlineCode(text);
            if (inlineBlock != null)
            {
                codeBlocks.Add(inlineBlock);
            }
        }

        return codeBlocks;
    }

    private string FormatMarkdownCodeBlocks(string response)
    {
        // Pattern to match ```language code``` where language is one of our known languages
        // Order languages by length (longest first) to ensure proper matching
        var languages = new[] { "javascript", "typescript", "powershell", "csharp", "python", "bash", "html", "razor", "cshtml", "java", "json", "css", "sql", "xml", "c#", "ts", "js", "py", "cs", "sh", "ps1" };
        var pattern = $@"```({string.Join("|", languages.Select(Regex.Escape))})(.*?)```";
        
        return Regex.Replace(response, pattern, match =>
        {
            var language = match.Groups[1].Value.ToLowerInvariant();
            var rawCode = match.Groups[2].Value;
            
            // Remove leading/trailing whitespace from code
            rawCode = rawCode.Trim();
            
            // Skip if no code content
            if (string.IsNullOrWhiteSpace(rawCode))
                return match.Value;
            
            var formattedCode = FormatCode(rawCode, language);
            var highlightedCode = ApplySyntaxHighlighting(formattedCode, language);
            
            // Return formatted code block with language header wrapped in <pre> tag
            // <pre> preserves whitespace and line breaks
            var languageDisplay = NormalizeLanguageName(language);
            return $"<br><br><div class=\"code-block-header\">[{languageDisplay} Code]</div><pre class=\"code-block\">{highlightedCode}</pre><div class=\"code-block-footer\">[End {languageDisplay} Code]</div><br><br>";
        }, RegexOptions.Singleline | RegexOptions.IgnoreCase);
    }

    private string FormatInlineCodePatterns(string response)
    {
        // Try to detect code patterns without markdown
        var inlineBlock = DetectInlineCode(response);
        
        if (inlineBlock != null)
        {
            var languageDisplay = NormalizeLanguageName(inlineBlock.Language);
            var highlightedCode = ApplySyntaxHighlighting(inlineBlock.FormattedCode, inlineBlock.Language.ToLowerInvariant());
            // Wrap in pre tag to preserve formatting
            var formatted = response.Replace(inlineBlock.RawCode, 
                $"<br><br><div class=\"code-block-header\">[{languageDisplay} Code]</div><pre class=\"code-block\">{highlightedCode}</pre><div class=\"code-block-footer\">[End {languageDisplay} Code]</div><br><br>");
            return formatted;
        }

        return response;
    }

    private CodeBlock? DetectInlineCode(string text)
    {
        // Look for code-like patterns
        // Pattern for C# method: public/private ... { ... }
        var csharpPattern = @"(public|private|protected|internal)\s+\w+\s+\w+\s*\([^)]*\)\s*\{[^}]+\}";
        var match = Regex.Match(text, csharpPattern, RegexOptions.Singleline);
        
        if (match.Success)
        {
            return new CodeBlock
            {
                Language = "C#",
                RawCode = match.Value,
                FormattedCode = FormatCode(match.Value, "csharp"),
                StartIndex = match.Index,
                EndIndex = match.Index + match.Length
            };
        }

        // Pattern for HTML tags
        var htmlPattern = @"<[a-zA-Z][^>]*>[\s\S]*?</[a-zA-Z][^>]*>";
        match = Regex.Match(text, htmlPattern);
        
        if (match.Success && match.Value.Length > 20) // Ensure it's substantial HTML
        {
            return new CodeBlock
            {
                Language = "HTML",
                RawCode = match.Value,
                FormattedCode = FormatCode(match.Value, "html"),
                StartIndex = match.Index,
                EndIndex = match.Index + match.Length
            };
        }

        // Pattern for Python function: def name():
        var pythonPattern = @"def\s+\w+\s*\([^)]*\)\s*:[^\n]+";
        match = Regex.Match(text, pythonPattern);
        
        if (match.Success)
        {
            return new CodeBlock
            {
                Language = "Python",
                RawCode = match.Value,
                FormattedCode = FormatCode(match.Value, "python"),
                StartIndex = match.Index,
                EndIndex = match.Index + match.Length
            };
        }

        return null;
    }

    /// <summary>
    /// Applies syntax highlighting to code based on language.
    /// Returns HTML with span elements for different syntax elements.
    /// </summary>
    private string ApplySyntaxHighlighting(string code, string language)
    {
        var languageLower = language.ToLowerInvariant();
        
        return languageLower switch
        {
            "csharp" or "cs" or "c#" => HighlightCSharp(code),
            "html" or "xml" or "razor" or "cshtml" => HighlightHtml(code),
            "javascript" or "js" or "typescript" or "ts" => HighlightJavaScript(code),
            "css" => HighlightCss(code),
            "json" => HighlightJson(code),
            "sql" => HighlightSql(code),
            _ => System.Net.WebUtility.HtmlEncode(code) // Fallback: just HTML encode
        };
    }

    private string HighlightCSharp(string code)
    {
        // HTML encode first
        code = System.Net.WebUtility.HtmlEncode(code);
        
        // C# keywords
        var keywords = new[] { 
            "public", "private", "protected", "internal", "static", "readonly", "const",
            "class", "interface", "struct", "enum", "namespace", "using",
            "void", "int", "string", "bool", "double", "float", "decimal", "long", "char", "byte",
            "var", "object", "dynamic",
            "if", "else", "switch", "case", "default", "break", "continue", "return",
            "for", "foreach", "while", "do",
            "try", "catch", "finally", "throw",
            "new", "this", "base", "null", "true", "false",
            "async", "await", "Task",
            "get", "set", "value", "property",
            "abstract", "virtual", "override", "sealed",
            "in", "out", "ref", "params"
        };
        
        foreach (var keyword in keywords)
        {
            code = Regex.Replace(code, $@"\b({keyword})\b", "<span class='syntax-keyword'>$1</span>");
        }
        
        // String literals (double quotes)
        code = Regex.Replace(code, @"&quot;([^&]|&(?!quot;))*?&quot;", "<span class='syntax-string'>$0</span>");
        
        // Comments
        code = Regex.Replace(code, @"//.*?(?=\r?\n|$)", "<span class='syntax-comment'>$0</span>");
        code = Regex.Replace(code, @"/\*[\s\S]*?\*/", "<span class='syntax-comment'>$0</span>");
        
        // Numbers
        code = Regex.Replace(code, @"\b(\d+\.?\d*)\b", "<span class='syntax-number'>$1</span>");
        
        // Class/Type names (PascalCase words after 'new', ':', or as type names)
        code = Regex.Replace(code, @"\b([A-Z][a-zA-Z0-9]*)\b", "<span class='syntax-type'>$1</span>");
        
        return code;
    }

    private string HighlightHtml(string code)
    {
        // HTML encode first
        code = System.Net.WebUtility.HtmlEncode(code);
        
        // HTML tags
        code = Regex.Replace(code, @"&lt;(/?)([a-zA-Z][a-zA-Z0-9]*)", 
            "&lt;$1<span class='syntax-tag'>$2</span>");
        
        // Attributes
        code = Regex.Replace(code, @"\s([a-zA-Z-]+)=", " <span class='syntax-attribute'>$1</span>=");
        
        // Attribute values
        code = Regex.Replace(code, @"=&quot;([^&]|&(?!quot;))*?&quot;", 
            "=<span class='syntax-string'>$0</span>");
        
        // Comments
        code = Regex.Replace(code, @"&lt;!--[\s\S]*?--&gt;", "<span class='syntax-comment'>$0</span>");
        
        // Razor syntax (for .razor/.cshtml files)
        code = Regex.Replace(code, @"@[\w]+", "<span class='syntax-razor'>$0</span>");
        
        return code;
    }

    private string HighlightJavaScript(string code)
    {
        code = System.Net.WebUtility.HtmlEncode(code);
        
        var keywords = new[] { 
            "function", "const", "let", "var", "if", "else", "return", "for", "while", 
            "switch", "case", "break", "continue", "try", "catch", "finally", "throw",
            "new", "this", "null", "undefined", "true", "false",
            "async", "await", "class", "extends", "static", "import", "export", "from"
        };
        
        foreach (var keyword in keywords)
        {
            code = Regex.Replace(code, $@"\b({keyword})\b", "<span class='syntax-keyword'>$1</span>");
        }
        
        code = Regex.Replace(code, @"&quot;([^&]|&(?!quot;))*?&quot;", "<span class='syntax-string'>$0</span>");
        code = Regex.Replace(code, @"'([^'])*?'", "<span class='syntax-string'>$0</span>");
        code = Regex.Replace(code, @"//.*?(?=\r?\n|$)", "<span class='syntax-comment'>$0</span>");
        code = Regex.Replace(code, @"/\*[\s\S]*?\*/", "<span class='syntax-comment'>$0</span>");
        code = Regex.Replace(code, @"\b(\d+\.?\d*)\b", "<span class='syntax-number'>$1</span>");
        
        return code;
    }

    private string HighlightCss(string code)
    {
        code = System.Net.WebUtility.HtmlEncode(code);
        
        // Selectors
        code = Regex.Replace(code, @"([.#]?[\w-]+)\s*\{", "<span class='syntax-selector'>$1</span> {");
        
        // Properties
        code = Regex.Replace(code, @"\b([\w-]+)\s*:", "<span class='syntax-property'>$1</span>:");
        
        // Values
        code = Regex.Replace(code, @":\s*([^;]+);", ": <span class='syntax-value'>$1</span>;");
        
        // Comments
        code = Regex.Replace(code, @"/\*[\s\S]*?\*/", "<span class='syntax-comment'>$0</span>");
        
        return code;
    }

    private string HighlightJson(string code)
    {
        code = System.Net.WebUtility.HtmlEncode(code);
        
        // Property names
        code = Regex.Replace(code, @"&quot;([^&]|&(?!quot;))*?&quot;\s*:", 
            "<span class='syntax-property'>$0</span>");
        
        // String values
        code = Regex.Replace(code, @":\s*&quot;([^&]|&(?!quot;))*?&quot;", 
            ": <span class='syntax-string'>$0</span>");
        
        // Boolean and null
        code = Regex.Replace(code, @"\b(true|false|null)\b", "<span class='syntax-keyword'>$1</span>");
        
        // Numbers
        code = Regex.Replace(code, @"\b(\d+\.?\d*)\b", "<span class='syntax-number'>$1</span>");
        
        return code;
    }

    private string HighlightSql(string code)
    {
        code = System.Net.WebUtility.HtmlEncode(code);
        
        var keywords = new[] { 
            "SELECT", "FROM", "WHERE", "INSERT", "UPDATE", "DELETE", "JOIN", "LEFT", "RIGHT", "INNER",
            "ORDER BY", "GROUP BY", "HAVING", "AS", "ON", "AND", "OR", "NOT", "IN", "LIKE",
            "CREATE", "TABLE", "ALTER", "DROP", "PRIMARY KEY", "FOREIGN KEY"
        };
        
        foreach (var keyword in keywords)
        {
            code = Regex.Replace(code, $@"\b({keyword})\b", "<span class='syntax-keyword'>$1</span>", RegexOptions.IgnoreCase);
        }
        
        code = Regex.Replace(code, @"'([^'])*?'", "<span class='syntax-string'>$0</span>");
        code = Regex.Replace(code, @"--.*?(?=\r?\n|$)", "<span class='syntax-comment'>$0</span>");
        
        return code;
    }

    private string FormatCode(string rawCode, string language)
    {
        if (string.IsNullOrWhiteSpace(rawCode))
            return rawCode;

        // FIRST: Insert line breaks based on code structure if there are none
        rawCode = InsertLineBreaksInCode(rawCode, language);

        var lines = rawCode.Split(new[] { '\n', '\r' }, StringSplitOptions.None);
        var formattedLines = new List<string>();
        var indentLevel = 0;
        const string indent = "    "; // 4 spaces

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            
            if (string.IsNullOrWhiteSpace(trimmedLine))
            {
                formattedLines.Add(string.Empty);
                continue;
            }

            // Adjust indent based on language-specific rules
            var (newIndentLevel, lineIndent) = CalculateIndent(trimmedLine, indentLevel, language);
            
            // Add the formatted line
            var indentation = string.Concat(Enumerable.Repeat(indent, lineIndent));
            formattedLines.Add(indentation + trimmedLine);
            
            indentLevel = newIndentLevel;
        }

        return string.Join(Environment.NewLine, formattedLines);
    }

    /// <summary>
    /// Inserts line breaks into code that has none (common in LLM output).
    /// </summary>
    private string InsertLineBreaksInCode(string code, string language)
    {
        // If code already has decent line breaks, don't modify
        var lineCount = code.Split('\n').Length;
        if (lineCount > 3) // Already has multiple lines
            return code;

        var languageLower = language.ToLowerInvariant();
        
        // For brace-based languages (C#, Java, JavaScript, etc.)
        if (IsBraceBasedLanguage(languageLower))
        {
            code = InsertLineBreaksForBraceLanguages(code);
        }
        // For Python
        else if (languageLower == "python" || languageLower == "py")
        {
            code = InsertLineBreaksForPython(code);
        }
        
        return code;
    }

    private bool IsBraceBasedLanguage(string language)
    {
        var braceLanguages = new[] { "csharp", "cs", "c#", "java", "javascript", "js", "typescript", "ts", "css" };
        return braceLanguages.Contains(language);
    }

    private string InsertLineBreaksForBraceLanguages(string code)
    {
        // Insert line breaks after: { } ; and before certain keywords
        code = Regex.Replace(code, @"\{", "{\n");           // After opening brace
        code = Regex.Replace(code, @"\}", "\n}\n");         // Around closing brace
        code = Regex.Replace(code, @";(?!\s*\))", ";\n");   // After semicolon (not before closing paren)
        
        // Add line break before certain keywords (namespace, class, public, private, etc.)
        var keywords = new[] { "namespace", "class", "public", "private", "protected", "static", "void", "int", "string", "var", "using", "interface", "enum" };
        foreach (var keyword in keywords)
        {
            // Only add newline if not already at start of line
            code = Regex.Replace(code, $@"(?<!^|\n)\s*\b({keyword})\b", $"\n{keyword}");
        }
        
        return code;
    }

    private string InsertLineBreaksForPython(string code)
    {
        // Insert line breaks after colons and before keywords
        code = Regex.Replace(code, @":", ":\n");            // After colon
        
        // Add line break before keywords
        var keywords = new[] { "def", "class", "if", "elif", "else", "for", "while", "try", "except", "import", "from" };
        foreach (var keyword in keywords)
        {
            code = Regex.Replace(code, $@"(?<!^|\n)\s*\b({keyword})\b", $"\n{keyword}");
        }
        
        return code;
    }

    private (int newIndentLevel, int lineIndent) CalculateIndent(string line, int currentIndent, string language)
    {
        var languageLower = language.ToLowerInvariant();
        
        // Language-specific indentation rules
        switch (languageLower)
        {
            case "csharp":
            case "cs":
            case "c#":
            case "java":
            case "javascript":
            case "js":
            case "typescript":
            case "ts":
                return CalculateBraceBasedIndent(line, currentIndent);
            
            case "python":
            case "py":
                return CalculatePythonIndent(line, currentIndent);
            
            case "html":
            case "xml":
                return CalculateTagBasedIndent(line, currentIndent);
            
            default:
                // Generic indentation
                return CalculateBraceBasedIndent(line, currentIndent);
        }
    }

    private (int newIndentLevel, int lineIndent) CalculateBraceBasedIndent(string line, int currentIndent)
    {
        var lineIndent = currentIndent;
        var newIndentLevel = currentIndent;

        // Decrease indent if line starts with closing brace
        if (line.TrimStart().StartsWith("}") || line.TrimStart().StartsWith("]"))
        {
            lineIndent = Math.Max(0, currentIndent - 1);
            newIndentLevel = lineIndent;
        }
        // Increase indent if line ends with opening brace
        else if (line.TrimEnd().EndsWith("{") || line.TrimEnd().EndsWith("["))
        {
            newIndentLevel = currentIndent + 1;
        }
        // Keep same indent for regular statements
        else if (line.Contains("{") && line.Contains("}"))
        {
            // Single-line block, no indent change
            newIndentLevel = currentIndent;
        }
        else
        {
            newIndentLevel = currentIndent;
        }

        return (newIndentLevel, lineIndent);
    }

    private (int newIndentLevel, int lineIndent) CalculatePythonIndent(string line, int currentIndent)
    {
        var lineIndent = currentIndent;
        var newIndentLevel = currentIndent;

        // Increase indent after lines ending with :
        if (line.TrimEnd().EndsWith(":"))
        {
            newIndentLevel = currentIndent + 1;
        }
        // Detect decrease in indentation (return, break, continue, pass)
        else if (line.StartsWith("return ") || line.StartsWith("break") || 
                 line.StartsWith("continue") || line.StartsWith("pass"))
        {
            // Keep current indent for these statements
            newIndentLevel = currentIndent;
        }
        // Dedent for else, elif, except, finally
        else if (Regex.IsMatch(line, @"^\s*(else|elif|except|finally)\s*:"))
        {
            lineIndent = Math.Max(0, currentIndent - 1);
            newIndentLevel = lineIndent + 1;
        }

        return (newIndentLevel, lineIndent);
    }

    private (int newIndentLevel, int lineIndent) CalculateTagBasedIndent(string line, int currentIndent)
    {
        var lineIndent = currentIndent;
        var newIndentLevel = currentIndent;

        // Self-closing tags or closing tags
        if (line.Contains("/>") || Regex.IsMatch(line, @"</\w+>"))
        {
            if (line.TrimStart().StartsWith("</"))
            {
                lineIndent = Math.Max(0, currentIndent - 1);
                newIndentLevel = lineIndent;
            }
        }
        // Opening tags
        else if (Regex.IsMatch(line, @"<\w+[^>]*>") && !line.Contains("/>"))
        {
            newIndentLevel = currentIndent + 1;
        }

        return (newIndentLevel, lineIndent);
    }

    private string NormalizeLanguageName(string language)
    {
        return language.ToLowerInvariant() switch
        {
            "csharp" or "cs" or "c#" => "C#",
            "python" or "py" => "Python",
            "java" => "Java",
            "javascript" or "js" => "JavaScript",
            "typescript" or "ts" => "TypeScript",
            "html" => "HTML",
            "css" => "CSS",
            "sql" => "SQL",
            "json" => "JSON",
            "xml" => "XML",
            "bash" or "sh" => "Bash",
            "powershell" or "ps1" => "PowerShell",
            "razor" or "cshtml" => "Razor",
            _ => char.ToUpper(language[0]) + language.Substring(1).ToLower()
        };
    }
}
