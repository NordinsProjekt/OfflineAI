using Services.Utilities;
using Xunit;

namespace OfflineAI.Tests.Utilities;

public class TextNormalizerTests
{
    [Fact]
    public void Normalize_WithSmartQuotes_ReplacesWithStraightQuotes()
    {
        // Arrange
        var input = "\u201CHello\u201D and \u2018world\u2019";
        
        // Act
        var result = TextNormalizer.Normalize(input);
        
        // Assert
        Assert.Equal("\"Hello\" and 'world'", result);
    }
    
    [Fact]
    public void Normalize_WithEmAndEnDashes_ReplacesWithHyphen()
    {
        // Arrange
        var input = "em\u2013dash and en\u2014dash";
        
        // Act
        var result = TextNormalizer.Normalize(input);
        
        // Assert
        Assert.Equal("em-dash and en-dash", result);
    }
    
    [Fact]
    public void Normalize_WithEllipsis_ReplacesWithThreeDots()
    {
        // Arrange
        var input = "Wait\u2026 what?";
        
        // Act
        var result = TextNormalizer.Normalize(input);
        
        // Assert
        Assert.Equal("Wait... what?", result);
    }
    
    [Fact]
    public void Normalize_WithNonBreakingSpace_ReplacesWithSpace()
    {
        // Arrange
        var input = "Hello\u00A0World"; // Non-breaking space
        
        // Act
        var result = TextNormalizer.Normalize(input);
        
        // Assert
        Assert.Equal("Hello World", result);
    }
    
    [Fact]
    public void Normalize_WithZeroWidthSpace_RemovesIt()
    {
        // Arrange
        var input = "Hello\u200BWorld"; // Zero-width space
        
        // Act
        var result = TextNormalizer.Normalize(input);
        
        // Assert
        Assert.Equal("HelloWorld", result);
    }
    
    [Fact]
    public void Normalize_WithControlCharacters_RemovesThem()
    {
        // Arrange
        var input = "Hello\u0001World\u0002Test"; // Control characters
        
        // Act
        var result = TextNormalizer.Normalize(input);
        
        // Assert
        Assert.Equal("HelloWorldTest", result);
    }
    
    [Fact]
    public void Normalize_PreservesCommonWhitespace()
    {
        // Arrange
        var input = "Line1\nLine2\tTabbed\r\nLine3";
        
        // Act
        var result = TextNormalizer.Normalize(input);
        
        // Assert
        Assert.Equal("Line1\nLine2\tTabbed\r\nLine3", result);
    }
    
    [Fact]
    public void Normalize_WithNullOrEmpty_ReturnsInput()
    {
        // Arrange & Act & Assert
        Assert.Null(TextNormalizer.Normalize(null!));
        Assert.Equal("", TextNormalizer.Normalize(""));
    }
    
    [Fact]
    public void NormalizeWithLimits_TruncatesLongText()
    {
        // Arrange
        var input = new string('a', 6000);
        
        // Act
        var result = TextNormalizer.NormalizeWithLimits(input, maxLength: 5000);
        
        // Assert
        Assert.Equal(5000, result.Length);
    }
    
    [Fact]
    public void NormalizeWithLimits_WithWhitespaceOnly_UsesFallback()
    {
        // Arrange
        var input = "   \t\n   ";
        
        // Act
        var result = TextNormalizer.NormalizeWithLimits(input, fallbackText: "[empty]");
        
        // Assert
        Assert.Equal("[empty]", result);
    }
    
    [Fact]
    public void NormalizeWithLimits_WithValidText_ReturnsNormalized()
    {
        // Arrange
        var input = "\u201CHello\u201D world";
        
        // Act
        var result = TextNormalizer.NormalizeWithLimits(input);
        
        // Assert
        Assert.Equal("\"Hello\" world", result);
    }
    
    [Fact]
    public void RemoveControlCharacters_RemovesNonPrintable()
    {
        // Arrange
        var input = "Hello\u0001\u0002\u0003World";
        
        // Act
        var result = TextNormalizer.RemoveControlCharacters(input);
        
        // Assert
        Assert.Equal("HelloWorld", result);
    }
    
    [Fact]
    public void NormalizeQuotes_OnlyReplacesQuotes()
    {
        // Arrange
        var input = "\u201Cquote\u201D and dash\u2013here";
        
        // Act
        var result = TextNormalizer.NormalizeQuotes(input);
        
        // Assert
        Assert.Equal("\"quote\" and dash\u2013here", result); // Dash not replaced
    }
    
    [Fact]
    public void NormalizeDashes_OnlyReplacesDashes()
    {
        // Arrange
        var input = "\u201Cquote\u201D and dash\u2013here";
        
        // Act
        var result = TextNormalizer.NormalizeDashes(input);
        
        // Assert
        Assert.Equal("\u201Cquote\u201D and dash-here", result); // Quotes not replaced
    }
    
    [Fact]
    public void NormalizeWhitespace_ReplacesSpecialWhitespace()
    {
        // Arrange
        var input = "Hello\u00A0World\u200Band\u2026more";
        
        // Act
        var result = TextNormalizer.NormalizeWhitespace(input);
        
        // Assert
        Assert.Equal("Hello Worldand...more", result); // Zero-width space is removed (not replaced with space)
    }
    
    [Fact]
    public void Normalize_WithComplexMixedContent_NormalizesAll()
    {
        // Arrange
        var input = "\u201CSmart quotes\u201D, dash\u2013here, ellipsis\u2026 and\u00A0more\u200Bstuff";
        
        // Act
        var result = TextNormalizer.Normalize(input);
        
        // Assert
        Assert.Equal("\"Smart quotes\", dash-here, ellipsis... and morestuff", result);
    }
    
    [Fact]
    public void Normalize_PreservesLettersDigitsPunctuation()
    {
        // Arrange
        var input = "Hello123!@#$%World";
        
        // Act
        var result = TextNormalizer.Normalize(input);
        
        // Assert
        Assert.Equal("Hello123!@#$%World", result);
    }
    
    [Theory]
    [InlineData("", "")]
    [InlineData("simple text", "simple text")]
    [InlineData("123", "123")]
    [InlineData("!@#$%", "!@#$%")]
    public void Normalize_WithSimpleText_ReturnsUnchanged(string input, string expected)
    {
        // Act
        var result = TextNormalizer.Normalize(input);
        
        // Assert
        Assert.Equal(expected, result);
    }
}
