using Application.AI.Utilities;

namespace OfflineAI.Tests.Utilities;

/// <summary>
/// Tests for TextNormalizer handling of problematic characters like backticks and accents.
/// These characters can cause BERT tokenizer to consume excessive memory.
/// </summary>
public class TextNormalizerProblematicCharsTests
{
    [Fact]
    public void Normalize_ShouldReplaceBacktick_WithSingleQuote()
    {
        // Arrange
        var input = "Can you explain `this` code?";
        
        // Act
        var result = TextNormalizer.Normalize(input);
        
        // Assert
        Assert.DoesNotContain("`", result);
        Assert.Contains("'this'", result);
    }
    
    [Fact]
    public void Normalize_ShouldReplaceAcuteAccent_WithSingleQuote()
    {
        // Arrange  
        var input = "What\u00B4s the meaning?"; // ´ is acute accent (U+00B4)
        
        // Act
        var result = TextNormalizer.Normalize(input);
        
        // Assert
        Assert.DoesNotContain("\u00B4", result);
        Assert.Contains("'s", result);
    }
    
    [Fact]
    public void Normalize_ShouldHandleMultipleBackticks()
    {
        // Arrange
        var input = "`start` middle `end`";
        
        // Act
        var result = TextNormalizer.Normalize(input);
        
        // Assert
        Assert.DoesNotContain("`", result);
        Assert.Equal("'start' middle 'end'", result);
    }
    
    [Fact]
    public void Normalize_ShouldHandleCodeSnippets()
    {
        // Arrange
        var input = "Use `var result = DoSomething();` in your code";
        
        // Act
        var result = TextNormalizer.Normalize(input);
        
        // Assert
        Assert.DoesNotContain("`", result);
        Assert.Contains("'var result = DoSomething();'", result);
    }
    
    [Fact]
    public void Normalize_ShouldHandleMixedQuotationMarks()
    {
        // Arrange
        var input = "`backtick` 'single' \"double\" \u00B4accent\u00B4 \u00ABangle\u00BB";
        
        // Act
        var result = TextNormalizer.Normalize(input);
        
        // Assert
        Assert.DoesNotContain("`", result);
        Assert.DoesNotContain("\u00B4", result);
        Assert.DoesNotContain("\u00AB", result);
        Assert.DoesNotContain("\u00BB", result);
        // All should be normalized to ASCII quotes
        Assert.Contains("'", result); // Should have single quotes
        Assert.Contains("\"", result); // Should have double quotes
    }
    
    [Fact]
    public void Normalize_ShouldRemoveZeroWidthCharacters()
    {
        // Arrange
        var input = "Word\u200BWord\u200C\u200D\uFEFFTest"; // Zero-width chars between words
        
        // Act
        var result = TextNormalizer.Normalize(input);
        
        // Assert
        Assert.DoesNotContain("\u200B", result); // Zero-width space
        Assert.DoesNotContain("\u200C", result); // Zero-width non-joiner
        Assert.DoesNotContain("\u200D", result); // Zero-width joiner
        Assert.DoesNotContain("\uFEFF", result); // Zero-width no-break space
        Assert.Equal("WordWordTest", result);
    }
    
    [Fact]
    public void NormalizeWithLimits_ShouldHandleBackticksAndTruncate()
    {
        // Arrange
        var longInput = new string('`', 10000); // Lots of backticks
        
        // Act
        var result = TextNormalizer.NormalizeWithLimits(longInput, maxLength: 100);
        
        // Assert
        Assert.DoesNotContain("`", result);
        Assert.True(result.Length <= 100);
        Assert.All(result, c => Assert.Equal('\'', c)); // All should be single quotes
    }
    
    [Fact]
    public void NormalizeQuotes_ShouldHandleAllQuoteTypes()
    {
        // Arrange
        var input = "`grave` \u00B4acute\u00B4 'single' \u201Cdouble\u201D \u00ABguillemet\u00BB \u2039angle\u203A";
        
        // Act
        var result = TextNormalizer.NormalizeQuotes(input);
        
        // Assert
        Assert.DoesNotContain("`", result);
        Assert.DoesNotContain("\u00B4", result);
        Assert.DoesNotContain("\u201C", result);
        Assert.DoesNotContain("\u201D", result);
        Assert.DoesNotContain("\u00AB", result);
        Assert.DoesNotContain("\u00BB", result);
        Assert.DoesNotContain("\u2039", result);
        Assert.DoesNotContain("\u203A", result);
    }
    
    [Theory]
    [InlineData("`What's this?`", "'What's this?'")]
    [InlineData("Use `code` here", "Use 'code' here")]
    [InlineData("Test`with`many`backticks`", "Test'with'many'backticks'")]
    public void Normalize_VariousInputs_ProducesExpectedOutput(string input, string expected)
    {
        // Act
        var result = TextNormalizer.Normalize(input);
        
        // Assert
        Assert.Equal(expected, result);
    }
    
    [Fact]
    public void Normalize_RealWorldExample_PreventsBertMemoryIssue()
    {
        // Arrange - Simulate a question with problematic characters that caused memory spike
        var input = "What\u00B4s the rule for `combat` when monsters attack?";
        
        // Act
        var result = TextNormalizer.Normalize(input);
        
        // Assert
        Assert.DoesNotContain("`", result);
        Assert.DoesNotContain("\u00B4", result);
        // Should be safe for BERT tokenizer now
        Assert.Equal("What's the rule for 'combat' when monsters attack?", result);
    }
    
    [Fact]
    public void Normalize_EmptyString_ReturnsEmpty()
    {
        // Arrange & Act
        var result = TextNormalizer.Normalize("");
        
        // Assert
        Assert.Equal("", result);
    }
    
    [Fact]
    public void Normalize_NullString_ReturnsNull()
    {
        // Arrange & Act
        var result = TextNormalizer.Normalize(null!);
        
        // Assert
        Assert.Null(result);
    }
}
