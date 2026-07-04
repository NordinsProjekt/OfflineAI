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
        "razor", "cshtml",
        "php"
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

        // Step 0: strip common LLM wrapper artifacts (instruction tokens, metadata headers, etc.)
        // so every response is normalized before any structural formatting is applied.
        response = StripLlmArtifacts(response);

        if (string.IsNullOrWhiteSpace(response))
            return response;

        // Step 1: ensure numbered list items each start on a new line (handles concatenated output)
        response = InsertLineBreaksBeforeNumberedItems(response);

        // Step 2a: handle [Language Code] ... [End Language Code] markers produced by some LLMs
        response = FormatCustomCodeMarkers(response);

        // Step 2b: format markdown-style code blocks (```language ... ```)
        var formattedResponse = FormatMarkdownCodeBlocks(response);

        // If no code blocks were found at all, try to detect inline code patterns
        if (formattedResponse == response)
        {
            formattedResponse = FormatInlineCodePatterns(response);
        }

        // Step 3: convert numbered list runs to <ol> (skips content inside <pre> blocks)
        formattedResponse = FormatNumberedLists(formattedResponse);

        // Step 4: apply human-readable prose rules - headers, emphasis, bullet lists, blockquotes,
        // and horizontal rules (skips content inside <pre> blocks) so every response reads more naturally
        formattedResponse = FormatHumanReadableProse(formattedResponse);

        // Step 5: convert remaining newlines to <br> outside <pre> tags
        formattedResponse = ConvertNewlinesToBrTagsOutsidePre(formattedResponse);

        return formattedResponse;
    }

    /// <summary>
    /// Strips common LLM wrapper artifacts before any other formatting is applied, so every
    /// response is normalized the same way regardless of which model produced it. Removes
    /// instruction tokens ([INST]/[/INST], &lt;&lt;SYS&gt;&gt;), ChatML-style tokens
    /// (&lt;|im_start|&gt;, &lt;|im_end|&gt;, etc.), metadata header lines (e.g. "[Detected format: Assistant:]"),
    /// and generation-complete footers, then collapses excess blank lines left behind.
    /// </summary>
    private string StripLlmArtifacts(string response)
    {
        // Llama / Mistral / Qwen instruction tokens: [INST], [/INST], <<SYS>> ... <</SYS>>
        response = Regex.Replace(response, @"\[/?INST\]", string.Empty, RegexOptions.IgnoreCase);
        response = Regex.Replace(response, @"<<SYS>>.*?<</SYS>>", string.Empty,
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // ChatML-style tokens: <|im_start|>, <|im_end|>, <|endoftext|>, etc.
        response = Regex.Replace(response, @"<\|[^|]*\|>", string.Empty);

        // Metadata header lines emitted by some wrappers:
        //   [Detected format: Assistant:]   [System:]   [User:]
        response = Regex.Replace(response,
            @"^\[(?:Detected format|System|User|Assistant)[^\]]*\]\s*",
            string.Empty,
            RegexOptions.Multiline | RegexOptions.IgnoreCase);

        // Generation-complete footers: [Generation complete - 10s pause detected]
        response = Regex.Replace(response,
            @"\[Generation complete[^\]]*\]\s*$",
            string.Empty,
            RegexOptions.Multiline | RegexOptions.IgnoreCase);

        // Collapse runs of blank lines left after stripping
        response = Regex.Replace(response, @"\n{3,}", "\n\n");

        return response.Trim();
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
        var languages = new[] { "javascript", "typescript", "powershell", "csharp", "python", "bash", "html", "razor", "cshtml", "java", "json", "css", "sql", "xml", "php", "c#", "ts", "js", "py", "cs", "sh", "ps1" };
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
        var languages = new[] { "javascript", "typescript", "powershell", "csharp", "python", "bash", "html", "razor", "cshtml", "java", "json", "css", "sql", "xml", "php", "c#", "ts", "js", "py", "cs", "sh", "ps1" };
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

    /// <summary>
    /// Handles [Language Code] ... [End Language Code] markers that some LLMs emit directly.
    /// Extracts the code, re-formats it with proper indentation and syntax highlighting,
    /// and wraps it in the same HTML structure used for markdown code blocks.
    /// </summary>
    private string FormatCustomCodeMarkers(string response)
    {
        // Matches e.g. [C# Code] ... [End C# Code] or [Python Code] ... [End Python Code]
        var pattern = @"\[([A-Za-z#+ ]+?) Code\](.*?)\[End \1 Code\]";

        return Regex.Replace(response, pattern, match =>
        {
            var languageRaw = match.Groups[1].Value.Trim();
            var rawCode = match.Groups[2].Value.Trim();

            if (string.IsNullOrWhiteSpace(rawCode))
                return match.Value;

            var language = languageRaw.ToLowerInvariant();
            var formattedCode = FormatCode(rawCode, language);
            var highlightedCode = ApplySyntaxHighlighting(formattedCode, language);
            var languageDisplay = NormalizeLanguageName(language);

            return $"<br><br><div class=\"code-block-header\">[{languageDisplay} Code]</div><pre class=\"code-block\">{highlightedCode}</pre><div class=\"code-block-footer\">[End {languageDisplay} Code]</div><br><br>";
        }, RegexOptions.Singleline | RegexOptions.IgnoreCase);
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
            "php" => HighlightPhp(code),
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
            code = ReplaceKeywordOutsideTags(code, keyword);
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

        // Apply single-quoted strings before keyword spans are added (same reason as HighlightPhp).
        code = Regex.Replace(code, @"'([^'\r\n])*?'", "<span class='syntax-string'>$0</span>");

        var keywords = new[] { 
            "function", "const", "let", "var", "if", "else", "return", "for", "while", 
            "switch", "case", "break", "continue", "try", "catch", "finally", "throw",
            "new", "this", "null", "undefined", "true", "false",
            "async", "await", "class", "extends", "static", "import", "export", "from"
        };

        foreach (var keyword in keywords)
        {
            code = ReplaceKeywordOutsideTags(code, keyword);
        }

        code = Regex.Replace(code, @"&quot;([^&]|&(?!quot;))*?&quot;", "<span class='syntax-string'>$0</span>");
        code = Regex.Replace(code, @"//.*?(?=\r?\n|$)", "<span class='syntax-comment'>$0</span>");
        code = Regex.Replace(code, @"/\*[\s\S]*?\*/", "<span class='syntax-comment'>$0</span>");
        code = Regex.Replace(code, @"\b(\d+\.?\d*)\b", "<span class='syntax-number'>$1</span>");

        return code;
    }

    private string HighlightPhp(string code)
    {
        // HTML encode first
        code = System.Net.WebUtility.HtmlEncode(code);

        // Apply single-quoted string replacement BEFORE adding any span tags.
        // If applied after keyword/variable spans, the pattern '...' matches the
        // single-quoted attribute values inside those spans (e.g. class='syntax-keyword')
        // and produces malformed HTML that the browser silently drops.
        code = Regex.Replace(code, @"'([^'\r\n])*?'", "<span class='syntax-string'>$0</span>");

        var keywords = new[]
        {
            "echo", "print", "isset", "empty", "unset", "var_dump",
            "if", "else", "elseif", "while", "for", "foreach", "switch", "case", "break", "continue", "return",
            "function", "class", "interface", "extends", "implements", "new", "self", "parent",
            "public", "private", "protected", "static", "abstract", "final",
            "include", "require", "include_once", "require_once",
            "namespace", "use", "try", "catch", "finally", "throw",
            "true", "false", "null", "array", "list"
        };

        // Use tag-aware replacement so that the keyword 'class' (and others) cannot match
        // inside the span attribute values added by the single-quoted string step above.
        foreach (var keyword in keywords)
        {
            code = ReplaceKeywordOutsideTags(code, keyword, ignoreCase: true);
        }

        // PHP variables ($varName) — '$' never appears in generated span attributes, safe to run as-is
        code = Regex.Replace(code, @"\$[a-zA-Z_]\w*", "<span class='syntax-variable'>$0</span>");

        // String literals (double-quoted – encoded as &quot; after HtmlEncode)
        code = Regex.Replace(code, @"&quot;([^&]|&(?!quot;))*?&quot;", "<span class='syntax-string'>$0</span>");

        // Comments
        code = Regex.Replace(code, @"//.*?(?=\r?\n|$)", "<span class='syntax-comment'>$0</span>");
        code = Regex.Replace(code, @"/\*[\s\S]*?\*/", "<span class='syntax-comment'>$0</span>");
        code = Regex.Replace(code, @"#.*?(?=\r?\n|$)", "<span class='syntax-comment'>$0</span>");

        // Numbers
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
        
        // Apply string literals before keyword spans to avoid '...' matching span attribute values.
        code = Regex.Replace(code, @"'([^'\r\n])*?'", "<span class='syntax-string'>$0</span>");

        foreach (var keyword in keywords)
        {
            code = ReplaceKeywordOutsideTags(code, keyword, ignoreCase: true);
        }

        code = Regex.Replace(code, @"--.*?(?=\r?\n|$)", "<span class='syntax-comment'>$0</span>");

        return code;
    }

    /// <summary>
    /// Ensures each numbered list item starts on its own line.
    /// Handles LLM responses where items are concatenated without newlines,
    /// e.g. "...sentence.1. Next item 2. Another item".
    /// </summary>
    private string InsertLineBreaksBeforeNumberedItems(string text)
    {
        // Insert a newline before "N. " or "NN. " patterns that don't already start a line.
        // Require the item text to begin with an uppercase letter or digit to avoid false positives
        // like "e.g.", "i.e.", or version numbers.
        return Regex.Replace(text, @"(?<!\n)(?<=\S[ \t]*)(\d{1,2})\.\s+(?=[A-Z0-9])", "\n$1. ");
    }

    /// <summary>
    /// Finds numbered list runs in the non-code parts of HTML and wraps them in &lt;ol&gt; tags.
    /// Works on the output of FormatMarkdownCodeBlocks so that &lt;pre&gt; blocks are skipped.
    /// Only converts a run to &lt;ol&gt; when two or more consecutive items are found.
    /// </summary>
    private string FormatNumberedLists(string html)
    {
        var result = new StringBuilder();
        var i = 0;

        while (i < html.Length)
        {
            var preStart = html.IndexOf("<pre", i, StringComparison.OrdinalIgnoreCase);

            if (preStart < 0)
            {
                // No more <pre> blocks — convert lists in the remaining text
                result.Append(ConvertNumberedListsToHtml(html[i..]));
                break;
            }

            // Convert lists in the text before the <pre> block
            result.Append(ConvertNumberedListsToHtml(html[i..preStart]));

            // Find matching </pre>
            var preEnd = html.IndexOf("</pre>", preStart, StringComparison.OrdinalIgnoreCase);
            if (preEnd < 0)
            {
                result.Append(html[preStart..]);
                break;
            }

            // Copy the entire <pre>…</pre> block unchanged
            result.Append(html[preStart..(preEnd + 6)]);
            i = preEnd + 6;
        }

        return result.ToString();
    }

    /// <summary>
    /// Replaces runs of "N. text" lines in plain text with an HTML &lt;ol&gt; list.
    /// </summary>
    private string ConvertNumberedListsToHtml(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        // Match a consecutive block of numbered items, each on its own line.
        // A single isolated "1." is left unchanged to avoid false positives.
        return Regex.Replace(
            text,
            @"(?:^|\n)((?:\d{1,2}\.\s+[^\n]+\n?){2,})",
            match =>
            {
                var listText = match.Groups[1].Value;
                var items = Regex.Matches(listText, @"\d{1,2}\.\s+([^\n]+)");
                if (items.Count < 2)
                    return match.Value;

                var sb = new StringBuilder("\n<ol class=\"llm-list\">");
                foreach (Match item in items)
                    sb.Append($"<li>{item.Groups[1].Value.Trim()}</li>");
                sb.Append("</ol>\n");
                return sb.ToString();
            },
            RegexOptions.Multiline);
    }

    /// <summary>
    /// Applies human-readable prose formatting rules (headings, horizontal rules, blockquotes,
    /// bullet lists, and bold/italic emphasis) to the non-code parts of the HTML so every LLM
    /// response reads like natural, well-structured text instead of a wall of plain text.
    /// Skips content inside &lt;pre&gt; blocks (same strategy as <see cref="FormatNumberedLists"/>)
    /// so code is never altered by these rules.
    /// </summary>
    private string FormatHumanReadableProse(string html)
    {
        var result = new StringBuilder();
        var i = 0;

        while (i < html.Length)
        {
            var preStart = html.IndexOf("<pre", i, StringComparison.OrdinalIgnoreCase);

            if (preStart < 0)
            {
                // No more <pre> blocks - apply prose rules to the remaining text
                result.Append(ConvertProseElements(html[i..]));
                break;
            }

            // Apply prose rules to the text before the <pre> block
            result.Append(ConvertProseElements(html[i..preStart]));

            // Find matching </pre>
            var preEnd = html.IndexOf("</pre>", preStart, StringComparison.OrdinalIgnoreCase);
            if (preEnd < 0)
            {
                result.Append(html[preStart..]);
                break;
            }

            // Copy the entire <pre>…</pre> block unchanged
            result.Append(html[preStart..(preEnd + 6)]);
            i = preEnd + 6;
        }

        return result.ToString();
    }

    /// <summary>
    /// Runs the individual prose rules in an order that keeps them from interfering with each
    /// other: whole-line rules (horizontal rules, headings, blockquotes, bullet lists) run first,
    /// then inline emphasis (bold/italic) runs last so it never disturbs a list/quote marker.
    /// </summary>
    private string ConvertProseElements(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        text = ConvertHorizontalRules(text);
        text = ConvertHeadings(text);
        text = ConvertBlockquotes(text);
        text = ConvertBulletLists(text);
        text = ConvertBoldItalic(text);

        return text;
    }

    /// <summary>
    /// Converts markdown-style horizontal rule lines (---, ***, ___) into &lt;hr&gt; elements.
    /// A line must consist solely of 3+ of the same rule character (optionally spaced) to match,
    /// which keeps it from firing on bullet items or emphasis markers.
    /// </summary>
    private string ConvertHorizontalRules(string text)
    {
        return Regex.Replace(
            text,
            @"(?:^|\n)[ \t]*(?:(?:-[ \t]*){3,}|(?:\*[ \t]*){3,}|(?:_[ \t]*){3,})[ \t]*(?=\n|$)",
            "\n<hr class=\"llm-hr\">\n",
            RegexOptions.Multiline);
    }

    /// <summary>
    /// Converts markdown-style ATX headings (# through ######) into &lt;h1&gt;-&lt;h6&gt; elements.
    /// Trailing '#' characters (e.g. "## Title ##") are stripped from the heading text.
    /// </summary>
    private string ConvertHeadings(string text)
    {
        return Regex.Replace(
            text,
            @"^[ \t]{0,3}(#{1,6})[ \t]+(.+?)[ \t]*#*[ \t]*$",
            match =>
            {
                var level = match.Groups[1].Value.Length;
                var content = match.Groups[2].Value.Trim();
                if (content.Length == 0)
                    return match.Value;

                return $"<h{level} class=\"llm-heading\">{content}</h{level}>";
            },
            RegexOptions.Multiline);
    }

    /// <summary>
    /// Converts consecutive markdown blockquote lines ("&gt; text") into a single &lt;blockquote&gt;
    /// element so quoted material stands out from regular prose.
    /// </summary>
    private string ConvertBlockquotes(string text)
    {
        return Regex.Replace(
            text,
            @"(?:^|\n)((?:[ \t]{0,3}>[ \t]?[^\n]*(?:\n|$))+)",
            match =>
            {
                var lines = match.Groups[1].Value
                    .Split('\n')
                    .Select(l => Regex.Replace(l, @"^[ \t]{0,3}>[ \t]?", string.Empty).Trim())
                    .Where(l => l.Length > 0)
                    .ToList();

                if (lines.Count == 0)
                    return match.Value;

                return "\n<blockquote class=\"llm-quote\">" + string.Join("<br>", lines) + "</blockquote>\n";
            },
            RegexOptions.Multiline);
    }

    /// <summary>
    /// Replaces runs of "- item" / "* item" / "+ item" lines (2 or more consecutive) with an
    /// HTML &lt;ul&gt; list. Requires a space right after the marker so it never matches bullet
    /// characters that are actually part of bold/italic emphasis (e.g. "**bold**").
    /// </summary>
    private string ConvertBulletLists(string text)
    {
        return Regex.Replace(
            text,
            @"(?:^|\n)((?:[ \t]{0,3}[-*+][ \t]+[^\n]+\n?){2,})",
            match =>
            {
                var listText = match.Groups[1].Value;
                var items = Regex.Matches(listText, @"[ \t]{0,3}[-*+][ \t]+([^\n]+)");
                if (items.Count < 2)
                    return match.Value;

                var sb = new StringBuilder("\n<ul class=\"llm-list\">");
                foreach (Match item in items)
                    sb.Append($"<li>{item.Groups[1].Value.Trim()}</li>");
                sb.Append("</ul>\n");
                return sb.ToString();
            },
            RegexOptions.Multiline);
    }

    /// <summary>
    /// Converts markdown-style bold (**text**/__text__) and italic (*text*/_text_) emphasis into
    /// &lt;strong&gt;/&lt;em&gt; tags. Bold markers are processed before italic markers so a single
    /// '*' rule never splits a '**' pair, and italic underscores require non-word boundaries so
    /// identifiers like "my_variable_name" are left untouched.
    /// </summary>
    private string ConvertBoldItalic(string text)
    {
        // Bold: **text** or __text__
        text = Regex.Replace(text, @"\*\*(?!\s)([^\n*]+?)(?<!\s)\*\*", "<strong>$1</strong>");
        text = Regex.Replace(text, @"__(?!\s)([^\n_]+?)(?<!\s)__", "<strong>$1</strong>");

        // Italic: *text* or _text_ (single markers only, not part of a already-consumed ** / __ pair)
        text = Regex.Replace(text, @"(?<!\*)\*(?!\*)(?!\s)([^\n*]+?)(?<!\s)\*(?!\*)", "<em>$1</em>");
        text = Regex.Replace(text, @"(?<![\w_])_(?!\s)([^\n_]+?)(?<!\s)_(?![\w_])", "<em>$1</em>");

        return text;
    }

    private string FormatCode(string rawCode, string language)
    {
        if (string.IsNullOrWhiteSpace(rawCode))
            return rawCode;

        // FIRST: Insert line breaks based on code structure if there are none
        rawCode = InsertLineBreaksInCode(rawCode, language);

        // SECOND: for brace-based languages, merge continuation lines and normalise internal whitespace
        if (IsBraceBasedLanguage(language.ToLowerInvariant()))
        {
            rawCode = JoinContinuationLines(rawCode);
            rawCode = NormalizeInternalWhitespace(rawCode);
        }

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
        var braceLanguages = new[] { "csharp", "cs", "c#", "java", "javascript", "js", "typescript", "ts", "css", "php" };
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

    /// <summary>
    /// Merges lines that don't end a statement or block into the following line.
    /// Fixes LLM output like "public partial\nclass Course{" → "public partial class Course{".
    /// </summary>
    private static string JoinContinuationLines(string code)
    {
        var rawLines = code.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        var result = new List<string>();

        for (int i = 0; i < rawLines.Length; i++)
        {
            var trimmed = rawLines[i].Trim();

            if (string.IsNullOrEmpty(trimmed))
            {
                result.Add(string.Empty);
                continue;
            }

            // Comments and attributes are never continuation lines
            if (trimmed.StartsWith("//") || trimmed.StartsWith("/*") ||
                trimmed.StartsWith("*") || trimmed.StartsWith("["))
            {
                result.Add(trimmed);
                continue;
            }

            // A line is complete when it ends with one of these terminators
            var isComplete = trimmed.EndsWith(";") || trimmed.EndsWith("{") ||
                             trimmed.EndsWith("}") || trimmed.EndsWith(",") ||
                             trimmed.EndsWith(":");

            if (!isComplete && i + 1 < rawLines.Length)
            {
                var next = rawLines[i + 1].Trim();
                if (!string.IsNullOrEmpty(next) &&
                    !next.StartsWith("//") &&
                    !next.StartsWith("["))
                {
                    result.Add(trimmed + " " + next);
                    i++; // skip the merged line
                    continue;
                }
            }

            result.Add(trimmed);
        }

        return string.Join("\n", result);
    }

    /// <summary>
    /// Collapses runs of two or more spaces within each code line to a single space.
    /// Fixes LLM output like "Apply(event)    {" → "Apply(event) {".
    /// </summary>
    private static string NormalizeInternalWhitespace(string code)
    {
        var lines = code.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(lines[i]))
                lines[i] = Regex.Replace(lines[i].Trim(), @" {2,}", " ");
        }
        return string.Join("\n", lines);
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

    /// <summary>
    /// Replaces keyword occurrences only in text nodes, skipping content inside HTML tags.
    /// Prevents keyword substitution from corrupting span attributes that were generated
    /// by earlier highlighting passes (e.g. matching 'class' in class='syntax-keyword').
    /// </summary>
    private static string ReplaceKeywordOutsideTags(string html, string keyword, bool ignoreCase = false)
    {
        var result = new StringBuilder(html.Length + 64);
        var options = ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
        var pattern = new Regex($@"\b{Regex.Escape(keyword)}\b", options);
        var i = 0;

        while (i < html.Length)
        {
            if (html[i] == '<')
            {
                // Copy the entire tag (<...>) unchanged
                var tagEnd = html.IndexOf('>', i + 1);
                if (tagEnd < 0)
                {
                    result.Append(html[i..]);
                    break;
                }
                result.Append(html[i..(tagEnd + 1)]);
                i = tagEnd + 1;
            }
            else
            {
                // Plain-text segment — apply keyword replacement here only
                var nextTag = html.IndexOf('<', i);
                if (nextTag < 0) nextTag = html.Length;
                var segment = html[i..nextTag];
                result.Append(pattern.Replace(segment, "<span class='syntax-keyword'>$0</span>"));
                i = nextTag;
            }
        }

        return result.ToString();
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
            "php" => "PHP",
            _ => char.ToUpper(language[0]) + language.Substring(1).ToLower()
        };
    }
}
