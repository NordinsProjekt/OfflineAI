using Xunit;
using AiDashboard.Services;
using AiDashboard.Services.Interfaces;

namespace Presentation.AiDashboard.Tests.Services;

/// <summary>
/// Unit tests for LlmResponseFormatterService.
/// Tests code block detection and formatting for various programming languages.
/// </summary>
public class LlmResponseFormatterServiceTests
{
    private readonly ILlmResponseFormatterService _formatter;

    public LlmResponseFormatterServiceTests()
    {
        _formatter = new LlmResponseFormatterService();
    }

    [Fact]
    public void FormatResponse_WithCSharpMarkdownBlock_FormatsCorrectly()
    {
        // Arrange
        var input = "Here is a C# function:```csharppublic static void DisplayColoredText(ConsoleColor color, string text){    Console.ForegroundColor = color;    Console.WriteLine(text);    Console.ResetColor();}```This function displays colored text.";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        Assert.Contains("[C# Code]", result);
        Assert.Contains("DisplayColoredText", result);
        Assert.Contains("Console", result); // Keywords will be in spans but text still present
        Assert.Contains("[End C# Code]", result);
        Assert.Contains("\n", result); // Check for line breaks
    }

    [Fact]
    public void FormatResponse_WithCSharpSyntaxHighlighting_ContainsSyntaxSpans()
    {
        // Arrange
        var input = @"```csharp
public class Person
{
    public string Name { get; set; }
    public int Age { get; set; }
    
    // Constructor
    public Person(string name, int age)
    {
        Name = name;
        Age = age;
    }
}
```";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        Assert.Contains("[C# Code]", result);
        Assert.Contains("syntax-keyword", result); // Should have keyword highlighting
        Assert.Contains("syntax-type", result); // Should have type highlighting
        Assert.Contains("syntax-comment", result); // Should have comment highlighting
    }

    [Fact]
    public void FormatResponse_WithHtmlMarkdownBlock_FormatsCorrectly()
    {
        // Arrange
        var input = @"```html
<div class=""container"">
    <h1>Welcome</h1>
    <p>Hello World!</p>
</div>
```";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        Assert.Contains("[HTML Code]", result);
        Assert.Contains("syntax-tag", result); // Should have tag highlighting
        Assert.Contains("syntax-attribute", result); // Should have attribute highlighting
    }

    [Fact]
    public void FormatResponse_WithHtmlSyntaxHighlighting_HighlightsTags()
    {
        // Arrange
        var input = @"```html
<!DOCTYPE html>
<html>
<head>
    <title>Test Page</title>
</head>
<body>
    <div id=""main"" class=""content"">
        <h1>Hello</h1>
    </div>
</body>
</html>
```";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        Assert.Contains("[HTML Code]", result);
        Assert.Contains("syntax-tag", result); // Tags should be highlighted
        Assert.Contains("syntax-attribute", result); // Attributes like id, class should be highlighted
    }

    [Fact]
    public void FormatResponse_WithPythonMarkdownBlock_FormatsCorrectly()
    {
        // Arrange
        var input = "Here is Python code:```pythondef hello_world():print(\"Hello, World!\")if __name__ == \"__main__\":hello_world()```Done.";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        Assert.Contains("[Python Code]", result);
        Assert.Contains("hello_world", result);
        Assert.Contains("print", result);
        Assert.Contains("[End Python Code]", result);
    }

    [Fact]
    public void FormatResponse_WithJavaScriptMarkdownBlock_FormatsCorrectly()
    {
        // Arrange
        var input = "JavaScript example:```javascriptfunction greet(name){console.log(\"Hello \" + name);return true;}```End.";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        Assert.Contains("[JavaScript Code]", result);
        Assert.Contains("greet", result);
        Assert.Contains("console", result);
        Assert.Contains("[End JavaScript Code]", result);
    }

    [Fact]
    public void FormatResponse_WithHTMLMarkdownBlock_FormatsCorrectly()
    {
        // Arrange
        var input = "HTML example:```html<div><p>Hello</p></div>```Done.";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        Assert.Contains("[HTML Code]", result);
        // HTML will be encoded, so look for encoded versions
        Assert.Contains("div", result);
        Assert.Contains("Hello", result);
    }

    [Fact]
    public void FormatResponse_WithNoCodeBlocks_ReturnsUnchanged()
    {
        // Arrange
        var input = "This is just regular text without any code blocks.";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        Assert.Equal(input, result);
    }

    [Fact]
    public void FormatResponse_WithMultipleCodeBlocks_FormatsAll()
    {
        // Arrange
        var input = "First:```csharppublic void Test(){}```Second:```pythondef test():pass```Done.";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        Assert.Contains("[C# Code]", result);
        Assert.Contains("[Python Code]", result);
        Assert.Contains("Test", result);
        Assert.Contains("test", result);
    }

    [Fact]
    public void ContainsCodeBlocks_WithMarkdownCode_ReturnsTrue()
    {
        // Arrange
        var input = "Here is code:```csharpvar x = 5;```";

        // Act
        var result = _formatter.ContainsCodeBlocks(input);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ContainsCodeBlocks_WithoutCode_ReturnsFalse()
    {
        // Arrange
        var input = "This is just text.";

        // Act
        var result = _formatter.ContainsCodeBlocks(input);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ContainsCodeBlocks_WithCodeLikePatterns_ReturnsTrue()
    {
        // Arrange
        var input = "public class MyClass { }";

        // Act
        var result = _formatter.ContainsCodeBlocks(input);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ExtractCodeBlocks_WithMarkdownCode_ExtractsCorrectly()
    {
        // Arrange
        var input = "Text before```csharppublic void Test(){}```Text after";

        // Act
        var blocks = _formatter.ExtractCodeBlocks(input);

        // Assert
        Assert.Single(blocks);
        Assert.Equal("C#", blocks[0].Language);
        Assert.Contains("Test", blocks[0].RawCode);
    }

    [Fact]
    public void ExtractCodeBlocks_WithMultipleBlocks_ExtractsAll()
    {
        // Arrange
        var input = "```csharpvar x = 1;```and```pythondef f():pass```";

        // Act
        var blocks = _formatter.ExtractCodeBlocks(input);

        // Assert
        Assert.Equal(2, blocks.Count);
        Assert.Equal("C#", blocks[0].Language);
        Assert.Equal("Python", blocks[1].Language);
    }

    [Fact]
    public void ExtractCodeBlocks_WithNoCode_ReturnsEmpty()
    {
        // Arrange
        var input = "Just text.";

        // Act
        var blocks = _formatter.ExtractCodeBlocks(input);

        // Assert
        Assert.Empty(blocks);
    }

    [Fact]
    public void FormatResponse_WithNestedBraces_FormatsCorrectly()
    {
        // Arrange
        var input = "```csharpif (x > 0){if (y > 0){DoSomething();}}```";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        // Should have proper nested indentation
        Assert.Contains("DoSomething", result);
        // Check that line breaks were inserted
        var lines = result.Split('\n');
        Assert.True(lines.Length > 3);
    }

    [Fact]
    public void FormatResponse_WithSQLCode_FormatsCorrectly()
    {
        // Arrange
        var input = "SQL query:```sqlSELECT * FROM Users WHERE Id = 1;```Done.";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        Assert.Contains("[SQL Code]", result);
        Assert.Contains("SELECT", result);
        Assert.Contains("Users", result);
    }

    [Fact]
    public void FormatResponse_WithJSONCode_FormatsCorrectly()
    {
        // Arrange
        var input = "JSON:```json{\"name\":\"John\",\"age\":30}```";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        Assert.Contains("[JSON Code]", result);
        // JSON will be HTML encoded
        Assert.Contains("name", result);
        Assert.Contains("John", result);
    }

    [Fact]
    public void FormatResponse_PreservesTextOutsideCodeBlocks()
    {
        // Arrange
        var input = "Before text ```csharpvar x = 1;``` After text";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        Assert.Contains("Before text", result);
        Assert.Contains("After text", result);
    }

    [Fact]
    public void FormatResponse_WithEmptyInput_ReturnsEmpty()
    {
        // Arrange
        var input = "";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        Assert.Equal(input, result);
    }

    [Fact]
    public void FormatResponse_WithNullInput_ReturnsNull()
    {
        // Arrange
        string? input = null;

        // Act
        var result = _formatter.FormatResponse(input!);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void FormatResponse_WithRealLlmOutput_FormatsCorrectly()
    {
        // Arrange - Real LLM output example from the user
        var input = "[Detected format: Assistant:]Here is a simple C# function that uses the Console.WriteLine method to display colored text in a console application:```csharppublic static void DisplayColoredText(ConsoleColor color, string text){    Console.ForegroundColor = color;    Console.WriteLine(text);    Console.ResetColor();}```This function takes two parameters...";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        Assert.Contains("[C# Code]", result);
        Assert.Contains("DisplayColoredText", result);
        Assert.Contains("Console", result);
        Assert.Contains("[End C# Code]", result);
        
        // Verify line breaks exist
        var lines = result.Split('\n');
        Assert.True(lines.Length > 5, "Should have multiple lines for formatted code");
    }

    [Theory]
    [InlineData("csharp", "C#")]
    [InlineData("cs", "C#")]
    [InlineData("python", "Python")]
    [InlineData("py", "Python")]
    [InlineData("javascript", "JavaScript")]
    [InlineData("js", "JavaScript")]
    [InlineData("html", "HTML")]
    [InlineData("sql", "SQL")]
    public void FormatResponse_LanguageNames_NormalizedCorrectly(string inputLang, string expectedDisplay)
    {
        // Arrange
        var input = $"```{inputLang}\ncode\n```";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        Assert.Contains($"[{expectedDisplay} Code]", result);
    }

    [Fact]
    public void FormatResponse_WithClosingBraceOnSameLine_FormatsCorrectly()
    {
        // Arrange
        var input = "```csharpif (x) { DoSomething(); }```";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        // Single-line blocks should be formatted
        Assert.Contains("DoSomething", result);
    }

    [Fact]
    public void FormatResponse_WithPythonIndentation_FormatsCorrectly()
    {
        // Arrange
        var input = "```pythondef calculate(x):if x > 0:return x * 2else:return 0```";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        Assert.Contains("[Python Code]", result);
        Assert.Contains("calculate", result);
        Assert.Contains("return", result);
    }
    
    [Fact]
    public void FormatResponse_WithRealHelloWorldExample_FormatsCorrectly()
    {
        // Arrange - Real example from user showing formatting issue
        var input = "Here's the \"Hello World\" program in C# and JavaScript:C#:```csharpusing System;namespace HelloWorld{ class Program { static void Main(string[] args) { Console.WriteLine(\"Hello World!\"); } }}```JavaScript:```javascriptalert(\"Hello World!\");```In C#, you need to use the `Console.WriteLine()` method to display the message on the console.";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        Assert.Contains("[C# Code]", result);
        Assert.Contains("[JavaScript Code]", result);
        
        // Verify C# code is properly formatted
        Assert.Contains("System", result);
        Assert.Contains("HelloWorld", result);
        Assert.Contains("Program", result);
        Assert.Contains("Console", result);
        
        // Verify JavaScript code is present
        Assert.Contains("alert", result);
        
        // Verify text outside code blocks is preserved
        Assert.Contains("In C#, you need to use", result);
    }
    
    [Fact]
    public void FormatResponse_WithMultipleCodeBlocksAndText_PreservesStructure()
    {
        // Arrange
        var input = "First explanation.```csharpvar x = 1;```Middle text.```pythondef f():pass```End text.";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        Assert.Contains("First explanation", result);
        Assert.Contains("[C# Code]", result);
        Assert.Contains("[Python Code]", result);
        Assert.Contains("Middle text", result);
        Assert.Contains("End text", result);
        
        // Verify order is maintained
        var csharpIndex = result.IndexOf("[C# Code]");
        var middleIndex = result.IndexOf("Middle text");
        var pythonIndex = result.IndexOf("[Python Code]");
        var endIndex = result.IndexOf("End text");
        
        Assert.True(csharpIndex < middleIndex);
        Assert.True(middleIndex < pythonIndex);
        Assert.True(pythonIndex < endIndex);
    }
    
    [Fact]
    public void FormatResponse_WithNamespaceAndNestedBraces_FormatsCorrectly()
    {
        // Arrange
        var input = "```csharpusing System;namespace HelloWorld{class Program{static void Main(string[] args){Console.WriteLine(\"Hello\");}}}```";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        Assert.Contains("[C# Code]", result);
        Assert.Contains("System", result);
        Assert.Contains("HelloWorld", result);
        Assert.Contains("Program", result);
        Assert.Contains("Console", result);
    }
    
    [Fact]
    public void FormatResponse_WithAlertFunction_FormatsCorrectly()
    {
        // Arrange
        var input = "```javascriptalert(\"Hello World!\");```";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        Assert.Contains("[JavaScript Code]", result);
        Assert.Contains("alert", result);
        Assert.Contains("[End JavaScript Code]", result);
    }
    
    [Fact]
    public void FormatResponse_WithConsoleLog_FormatsCorrectly()
    {
        // Arrange
        var input = "Use console.log instead:```javascriptconsole.log(\"Hello World!\");```";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        Assert.Contains("Use console.log instead", result);
        Assert.Contains("[JavaScript Code]", result);
        Assert.Contains("console", result);
    }
    
    [Fact]
    public void FormatResponse_WithEndOfTextMarker_RemovesMarker()
    {
        // Arrange
        var input = "Here is code:```csharpvar x = 1;```Done. [end of text]";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        Assert.Contains("[C# Code]", result);
        Assert.Contains("Done.", result);
        // The [end of text] marker should be preserved as it's part of the response
        Assert.Contains("[end of text]", result);
    }
    
    [Fact]
    public void FormatResponse_WithEmptyCodeBlock_HandlesGracefully()
    {
        // Arrange
        var input = "Empty code:```csharp```More text.";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        // Should not create empty code blocks
        Assert.DoesNotContain("[C# Code]", result);
        Assert.Contains("Empty code", result);
        Assert.Contains("More text", result);
    }
    
    [Fact]
    public void FormatResponse_WithWhitespaceOnlyCodeBlock_HandlesGracefully()
    {
        // Arrange
        var input = "Whitespace only:```csharp   ```Text after.";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        // Should not create code blocks for whitespace-only content
        Assert.DoesNotContain("[C# Code]", result);
        Assert.Contains("Whitespace only", result);
        Assert.Contains("Text after", result);
    }
    
    [Fact]
    public void FormatResponse_WithSpecialCharactersInCode_PreservesCharacters()
    {
        // Arrange
        var input = "```csharpvar message = \"Hello, \\\"World\\\"!\";Console.WriteLine($\"Message: {message}\");```";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        Assert.Contains("[C# Code]", result);
        // Special characters will be HTML encoded
        Assert.Contains("message", result);
        Assert.Contains("Console", result);
    }
    
    [Fact]
    public void FormatResponse_WithLongCodeBlock_FormatsEntireBlock()
    {
        // Arrange
        var longCode = "using System;namespace Test{class Program{static void Main(){for(int i=0;i<10;i++){Console.WriteLine(i);}}}}";
        var input = $"```csharp{longCode}```";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        Assert.Contains("[C# Code]", result);
        Assert.Contains("System", result);
        Assert.Contains("Test", result);
        Assert.Contains("Program", result);
        Assert.Contains("Console", result);
        Assert.Contains("[End C# Code]", result);
    }
    
    [Fact]
    public void FormatResponse_WithMixedLanguageResponse_FormatsAllBlocks()
    {
        // Arrange - Similar to user's actual output
        var input = "Here's C#:```csharpusing System;class Program{static void Main(){Console.WriteLine(\"Hello\");}}```And JavaScript:```javascriptalert(\"Hello\");```Both work!";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        Assert.Contains("[C# Code]", result);
        Assert.Contains("[JavaScript Code]", result);
        Assert.Contains("System", result);
        Assert.Contains("alert", result);
        Assert.Contains("Both work!", result);
        
        // Verify both code blocks are properly terminated
        Assert.Contains("[End C# Code]", result);
        Assert.Contains("[End JavaScript Code]", result);
    }
    
    [Fact]
    public void FormatResponse_WithInlineBackticks_DoesNotConfuseWithCodeBlocks()
    {
        // Arrange
        var input = "Use the `Console.WriteLine()` method. Not a code block.```csharpvar x = 1;```Done.";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        Assert.Contains("Use the `Console.WriteLine()` method", result);
        Assert.Contains("[C# Code]", result);
        Assert.Contains("Done.", result);
    }
    
    [Fact]
    public void FormatResponse_WithLanguageAliases_RecognizesAllVariants()
    {
        // Arrange
        var testCases = new[]
        {
            ("```csharpvar x = 1;```", "C#"),
            ("```csvar x = 1;```", "C#"),
            ("```pythonx = 1```", "Python"),
            ("```pyx = 1```", "Python"),
            ("```javascriptvar x = 1;```", "JavaScript"),
            ("```jsx = 1;```", "JavaScript")
        };

        foreach (var (input, expectedLanguage) in testCases)
        {
            // Act
            var result = _formatter.FormatResponse(input);

            // Assert
            Assert.Contains($"[{expectedLanguage} Code]", result);
        }
    }

    [Fact]
    public void FormatResponse_WithNoLineBreaksInCode_InsertsLineBreaks()
    {
        // Arrange - Real LLM output with NO line breaks
        var input = "```csharpusing System;namespace HelloWorld{ class Program { static void Main(string[] args) { Console.WriteLine(\"Hello World!\"); } }}```";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        Assert.Contains("[C# Code]", result);
        
        // Verify line breaks were inserted
        var lines = result.Split('\n');
        Assert.True(lines.Length > 5, $"Expected multiple lines but got {lines.Length}");
        
        // Verify specific elements are present
        Assert.Contains("System", result);
        Assert.Contains("HelloWorld", result);
        Assert.Contains("Program", result);
        Assert.Contains("Console", result);
    }
    
    [Fact]
    public void FormatResponse_WithRealLlmOutputNoLineBreaks_FormatsCorrectly()
    {
        // Arrange - Your exact example
        var input = "Sure, here's a simple \"Hello World\" program in C#:```csharpusing System;namespace HelloWorld{ class Program { static void Main(string[] args) { Console.WriteLine(\"Hello World!\"); } }}```To run this program, save it as `Program.cs`.";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        Assert.Contains("[C# Code]", result);
        // Prose is HTML-encoded for safety, so an apostrophe is emitted as its HTML entity and
        // renders back to an apostrophe in the browser.
        Assert.Contains("Sure, here&#39;s a simple", result);
        Assert.Contains("To run this program", result);
        
        // Verify code elements are present
        Assert.Contains("System", result);
        Assert.Contains("HelloWorld", result);
        Assert.Contains("Console", result);
    }

    // ----- StripLlmArtifacts (Step 0: every response is normalized first) -----

    [Fact]
    public void FormatResponse_WithInstructionTokens_StripsThem()
    {
        // Arrange
        var input = "[INST] What is 2+2? [/INST]The answer is 4.";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        Assert.DoesNotContain("[INST]", result);
        Assert.DoesNotContain("[/INST]", result);
        Assert.Contains("The answer is 4.", result);
    }

    [Fact]
    public void FormatResponse_WithSystemTokenBlock_StripsIt()
    {
        // Arrange
        var input = "<<SYS>>You are a helpful assistant.<</SYS>>Hello there!";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        Assert.DoesNotContain("<<SYS>>", result);
        Assert.DoesNotContain("You are a helpful assistant.", result);
        Assert.Contains("Hello there!", result);
    }

    [Fact]
    public void FormatResponse_WithChatMlTokens_StripsThem()
    {
        // Arrange
        var input = "<|im_start|>assistant\nHi, how can I help?<|im_end|>";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        Assert.DoesNotContain("<|im_start|>", result);
        Assert.DoesNotContain("<|im_end|>", result);
        Assert.Contains("Hi, how can I help?", result);
    }

    [Fact]
    public void FormatResponse_WithDetectedFormatHeader_StripsIt()
    {
        // Arrange
        var input = "[Detected format: Assistant:]Here is your answer.";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        Assert.DoesNotContain("[Detected format", result);
        Assert.Contains("Here is your answer.", result);
    }

    [Fact]
    public void FormatResponse_WithGenerationCompleteFooter_StripsIt()
    {
        // Arrange
        var input = "Here is your answer.\n[Generation complete - 10s pause detected]";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        Assert.DoesNotContain("[Generation complete", result);
        Assert.Contains("Here is your answer.", result);
    }

    // ----- Human-readable prose rules (Step 4) -----

    [Fact]
    public void FormatResponse_WithMarkdownHeading_ConvertsToHeadingTag()
    {
        // Arrange
        var input = "# Overview\nThis explains the topic.\n## Details\nMore info here.";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        Assert.Contains("<h1 class=\"llm-heading\">Overview</h1>", result);
        Assert.Contains("<h2 class=\"llm-heading\">Details</h2>", result);
    }

    [Fact]
    public void FormatResponse_WithHorizontalRule_ConvertsToHrTag()
    {
        // Arrange
        var input = "First section.\n---\nSecond section.";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        Assert.Contains("<hr class=\"llm-hr\">", result);
        Assert.Contains("First section.", result);
        Assert.Contains("Second section.", result);
    }

    [Fact]
    public void FormatResponse_WithBlockquote_ConvertsToBlockquoteTag()
    {
        // Arrange
        var input = "As the docs say:\n> This is important.\n> Read carefully.";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        Assert.Contains("<blockquote class=\"llm-quote\">", result);
        Assert.Contains("This is important.", result);
        Assert.Contains("Read carefully.", result);
    }

    [Fact]
    public void FormatResponse_WithBulletList_ConvertsToUnorderedList()
    {
        // Arrange
        var input = "Steps to follow:\n- First step\n- Second step\n- Third step";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        Assert.Contains("<ul class=\"llm-list\">", result);
        Assert.Contains("<li>First step</li>", result);
        Assert.Contains("<li>Second step</li>", result);
        Assert.Contains("<li>Third step</li>", result);
        Assert.Contains("</ul>", result);
    }

    [Fact]
    public void FormatResponse_WithBoldText_ConvertsToStrongTag()
    {
        // Arrange
        var input = "This is **very important** information.";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        Assert.Contains("<strong>very important</strong>", result);
    }

    [Fact]
    public void FormatResponse_WithItalicText_ConvertsToEmTag()
    {
        // Arrange
        var input = "This is *somewhat* nuanced.";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        Assert.Contains("<em>somewhat</em>", result);
    }

    [Fact]
    public void FormatResponse_WithUnderscoreInIdentifier_DoesNotApplyItalic()
    {
        // Arrange
        var input = "Use the my_variable_name field to store the result.";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        Assert.Contains("my_variable_name", result);
        Assert.DoesNotContain("<em>", result);
    }

    [Fact]
    public void FormatResponse_WithProseAndCodeBlock_DoesNotFormatInsideCode()
    {
        // Arrange - a heading/list marker and emphasis-like text INSIDE the code block should be left alone
        var input = "# Example\nHere is code:```csharp// - not a list\n// **not bold**\nvar x = 1;```Done.";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        Assert.Contains("<h1 class=\"llm-heading\">Example</h1>", result);
        Assert.Contains("[C# Code]", result);
        // The code contents must not have been turned into list/strong tags
        Assert.DoesNotContain("<ul class=\"llm-list\">", result);
        Assert.DoesNotContain("<strong>not bold</strong>", result);
    }

    // ----- XSS: model text is rendered as a raw MarkupString, so any markup in it must be encoded -----

    [Fact]
    public void FormatResponse_WithScriptTagInProse_EncodesIt()
    {
        // Arrange
        var input = "Here is the answer.<script>alert('xss')</script>";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert - the raw executable tag must not survive into the rendered markup
        Assert.DoesNotContain("<script>", result);
        Assert.Contains("&lt;script&gt;", result);
    }

    [Fact]
    public void FormatResponse_WithEventHandlerAttributeInProse_EncodesIt()
    {
        // Arrange
        var input = "Look at this image: <img src=x onerror=\"alert(document.cookie)\">";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert - no live <img> element / event handler is emitted
        Assert.DoesNotContain("<img", result);
        Assert.Contains("&lt;img", result);
    }

    [Fact]
    public void FormatResponse_WithHtmlInsideCodeBlock_EncodesButStillHighlights()
    {
        // Arrange - a script tag inside a code fence must be shown as text, never executed
        var input = "```html\n<script>alert(1)</script>\n```";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        Assert.Contains("[HTML Code]", result);
        Assert.DoesNotContain("<script>", result);
        Assert.Contains("&lt;", result); // encoded angle brackets present
    }

    [Fact]
    public void FormatResponse_WithNumberedListAndBoldText_BothFormatCorrectly()
    {
        // Arrange
        var input = "Here are the **key** steps:\n1. First do this\n2. Then do that";

        // Act
        var result = _formatter.FormatResponse(input);

        // Assert
        Assert.Contains("<strong>key</strong>", result);
        Assert.Contains("<ol class=\"llm-list\">", result);
        Assert.Contains("<li>First do this</li>", result);
        Assert.Contains("<li>Then do that</li>", result);
    }
}
