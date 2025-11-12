using Application.AI.Utilities;
using Xunit;

namespace OfflineAI.Tests.Utilities;

/// <summary>
/// Tests for Section 24 "Move Monsters" text that was causing tokenization errors.
/// This verifies the fix handles real-world rulebook text properly.
/// NOTE: Uses custom simplified tokenizer (no BERTTokenizers dependency).
/// </summary>
public class BertTokenizationSection24Tests
{
    private const string Section24Text = @"Move Monsters 
Move each Monster 1 space closer to the Castle or 1 space clockwise if inside  the Castle. If a Monster hits a Wall or Tower, the Monster takes 1 point of  damage and the Wall or Tower is destroyed. If the Monster has health points  remaining after destroying a Wall, the Monster stays in the Swordsman ring. If the Monster has health points remaining after destroying a Tower,  the Monster moves into the space  vacated by the Tower.  If more than 1 Monster hits a Wall  or Tower, players choose which  Monster takes the damage. If hitting  a Wall, all of the Monsters stay in  the Swordsman ring. If hitting a  Tower, all of the Monsters move  into the Tower space.  The exceptions are the 4- and  5-point Monsters. If they are at their  lowest point, they take no damage  from hitting a Castle structure.  Monsters affected by Flask of Glue  or the Sleep Potion do not move. 6. Place Monsters Draw new Monsters one at a time from the Monster  bag and place them in the Forest. The number of  Monsters drawn depends on the number of players. If you draw a Curse (or 2 or 3 or more), resolve  it and draw another Monster to place. Use the die to  place each Monster in the Forest. Place Monsters with  the largest number pointed toward the Castle. This is the Monster's starting  health points. The black edge on some Monsters has meaning only for the  More Munchkin Mini-Expansion (p. 9).";

    [Fact]
    public void Section24Text_NormalizedCorrectly()
    {
        // Act
        var normalized = TextNormalizer.NormalizeWithLimits(Section24Text, maxLength: 5000);

        // Assert
        Assert.NotNull(normalized);
        Assert.NotEmpty(normalized);
        Assert.True(normalized.Length > 0);
        Assert.Contains("Move Monsters", normalized);
        Assert.Contains("Castle", normalized);
    }

    [Fact]
    public void Section24Text_TokenizesSuccessfully()
    {
        // Arrange
        var normalized = TextNormalizer.NormalizeWithLimits(Section24Text, maxLength: 5000);

        // Act
        var result = BertTokenizationHelper.TokenizeWithFallback(normalized, 256);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.InputIds);
        Assert.NotNull(result.AttentionMask);
        Assert.NotNull(result.TokenTypeIds);
        Assert.Equal(256, result.InputIds.Length);
        Assert.True(result.ActualTokenCount > 0);
        Assert.True(result.ActualTokenCount <= 256);
    }

    [Fact]
    public void Section24Text_CreatesTensorsSuccessfully()
    {
        // Arrange
        var normalized = TextNormalizer.NormalizeWithLimits(Section24Text, maxLength: 5000);
        var tokenization = BertTokenizationHelper.TokenizeWithFallback(normalized, 256);

        // Act
        var tensors = BertTokenizationHelper.CreateInputTensors(tokenization);

        // Assert
        Assert.NotNull(tensors);
        Assert.Contains("input_ids", tensors.Keys);
        Assert.Contains("attention_mask", tensors.Keys);
        Assert.Contains("token_type_ids", tensors.Keys);

        var inputIdsTensor = tensors["input_ids"];
        Assert.Equal(2, inputIdsTensor.Rank);
        Assert.Equal(1, inputIdsTensor.Dimensions[0]); // Batch size
        Assert.Equal(256, inputIdsTensor.Dimensions[1]); // Sequence length
    }

    [Fact]
    public void Section24Text_HandlesMultipleSpaces()
    {
        // Arrange - Text with multiple spaces between words
        var textWithMultipleSpaces = "inside  the Castle";

        // Act
        var result = BertTokenizationHelper.TokenizeWithFallback(textWithMultipleSpaces, 128);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.ActualTokenCount > 0);
    }

    [Fact]
    public void Section24Text_HandlesNumbersAndPunctuation()
    {
        // Arrange - Text with numbers, dashes, and punctuation
        var textWithSpecialChars = "4- and 5-point Monsters. (p. 9)";

        // Act
        var result = BertTokenizationHelper.TokenizeWithFallback(textWithSpecialChars, 128);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.ActualTokenCount > 0);
    }

    [Fact]
    public void EmptyString_AfterNormalization_UsesFallback()
    {
        // Arrange
        var emptyText = "   \t\n   "; // Whitespace only

        // Act
        var normalized = TextNormalizer.NormalizeWithLimits(emptyText, maxLength: 5000, fallbackText: "[empty]");
        var result = BertTokenizationHelper.TokenizeWithFallback(normalized, 128);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.ActualTokenCount > 0); // Should have at least [CLS] and [SEP]
    }

    [Fact]
    public void Section24Text_MultipleLineBreaks_HandledCorrectly()
    {
        // Arrange - Text with various line break patterns
        var textWithLineBreaks = "Move Monsters\nMove each Monster\n\nNew paragraph";

        // Act
        var result = BertTokenizationHelper.TokenizeWithFallback(textWithLineBreaks, 128);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.ActualTokenCount > 0);
    }
}

