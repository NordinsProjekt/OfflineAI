using BERTTokenizers;
using Services.Utilities;
using Xunit;

namespace OfflineAI.Tests.Utilities;

/// <summary>
/// Tests for BertTokenizationHelper to ensure proper tokenization and fallback handling.
/// </summary>
public class BertTokenizationHelperTests
{
    private readonly BertUncasedLargeTokenizer _tokenizer;
    private const int MaxSequenceLength = 128;

    public BertTokenizationHelperTests()
    {
        _tokenizer = new BertUncasedLargeTokenizer();
    }

    [Fact]
    public void TokenizeWithFallback_ValidText_ReturnsValidTokenization()
    {
        // Arrange
        var text = "This is a test sentence for BERT tokenization.";

        // Act
        var result = BertTokenizationHelper.TokenizeWithFallback(
            _tokenizer, 
            text, 
            MaxSequenceLength);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(MaxSequenceLength, result.InputIds.Length);
        Assert.Equal(MaxSequenceLength, result.AttentionMask.Length);
        Assert.Equal(MaxSequenceLength, result.TokenTypeIds.Length);
        Assert.True(result.ActualTokenCount > 0);
        Assert.True(result.ActualTokenCount <= MaxSequenceLength);
    }

    [Fact]
    public void TokenizeWithFallback_EmptyString_ThrowsArgumentException()
    {
        // Arrange
        var text = "";

        // Act & Assert
        Assert.Throws<ArgumentException>(() => 
            BertTokenizationHelper.TokenizeWithFallback(_tokenizer, text, MaxSequenceLength));
    }

    [Fact]
    public void TokenizeWithFallback_NullTokenizer_ThrowsArgumentNullException()
    {
        // Arrange
        var text = "Test text";

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            BertTokenizationHelper.TokenizeWithFallback(null!, text, MaxSequenceLength));
    }

    [Fact]
    public void TokenizeWithFallback_VeryLongText_TruncatesCorrectly()
    {
        // Arrange
        var longText = string.Join(" ", Enumerable.Repeat("word", 500));

        // Act
        var result = BertTokenizationHelper.TokenizeWithFallback(
            _tokenizer, 
            longText, 
            MaxSequenceLength);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(MaxSequenceLength, result.InputIds.Length);
        Assert.True(result.ActualTokenCount <= MaxSequenceLength);
    }

    [Fact]
    public void TokenizeWithFallback_SpecialCharacters_HandlesCorrectly()
    {
        // Arrange
        var text = "Text with special chars: @#$%^&*()!";

        // Act
        var result = BertTokenizationHelper.TokenizeWithFallback(
            _tokenizer, 
            text, 
            MaxSequenceLength);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.ActualTokenCount > 0);
        Assert.Equal(MaxSequenceLength, result.InputIds.Length);
    }

    [Fact]
    public void CreateInputTensors_ValidTokenization_CreatesCorrectTensors()
    {
        // Arrange
        var text = "Test sentence";
        var tokenization = BertTokenizationHelper.TokenizeWithFallback(
            _tokenizer, 
            text, 
            MaxSequenceLength);

        // Act
        var tensors = BertTokenizationHelper.CreateInputTensors(tokenization);

        // Assert
        Assert.NotNull(tensors);
        Assert.Contains("input_ids", tensors.Keys);
        Assert.Contains("attention_mask", tensors.Keys);
        Assert.Contains("token_type_ids", tensors.Keys);

        // Verify tensor shapes
        var inputIdsTensor = tensors["input_ids"];
        Assert.Equal(2, inputIdsTensor.Rank);
        Assert.Equal(1, inputIdsTensor.Dimensions[0]); // Batch size
        Assert.Equal(MaxSequenceLength, inputIdsTensor.Dimensions[1]); // Sequence length
    }

    [Fact]
    public void CreateInputTensors_NullResult_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            BertTokenizationHelper.CreateInputTensors(null!));
    }

    [Fact]
    public void ExtractTokenArrays_ValidEncodedList_ReturnsCorrectArrays()
    {
        // Arrange
        var encoded = new List<(long InputIds, long AttentionMask, long TokenTypeIds)>
        {
            (101, 1, 0),  // [CLS]
            (2023, 1, 0), // "test"
            (102, 1, 0),  // [SEP]
            (0, 0, 0),    // [PAD]
            (0, 0, 0)     // [PAD]
        };

        // Act
        var result = BertTokenizationHelper.ExtractTokenArrays(encoded);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(5, result.InputIds.Length);
        Assert.Equal(5, result.AttentionMask.Length);
        Assert.Equal(5, result.TokenTypeIds.Length);
        Assert.Equal(3, result.ActualTokenCount); // [CLS], token, [SEP]
    }

    [Fact]
    public void ExtractTokenArrays_EmptyList_ThrowsArgumentException()
    {
        // Arrange
        var encoded = new List<(long InputIds, long AttentionMask, long TokenTypeIds)>();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => 
            BertTokenizationHelper.ExtractTokenArrays(encoded));
    }

    [Fact]
    public void ExtractTokenArrays_NullList_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            BertTokenizationHelper.ExtractTokenArrays(null!));
    }

    [Fact]
    public void CleanupArrays_ValidResult_ClearsArrays()
    {
        // Arrange
        var text = "Test sentence";
        var result = BertTokenizationHelper.TokenizeWithFallback(
            _tokenizer, 
            text, 
            MaxSequenceLength);
        
        // Ensure arrays have non-zero values initially
        var hasNonZeroValues = result.InputIds.Any(x => x != 0);
        Assert.True(hasNonZeroValues);

        // Act
        BertTokenizationHelper.CleanupArrays(result);

        // Assert
        Assert.All(result.InputIds, id => Assert.Equal(0, id));
        Assert.All(result.AttentionMask, mask => Assert.Equal(0, mask));
        Assert.All(result.TokenTypeIds, typeId => Assert.Equal(0, typeId));
    }

    [Fact]
    public void CleanupArrays_NullResult_DoesNotThrow()
    {
        // Act & Assert - should not throw
        BertTokenizationHelper.CleanupArrays(null!);
    }

    [Fact]
    public void TokenizeWithFallback_ProducesDifferentTokensForDifferentTexts()
    {
        // Arrange
        var text1 = "First sentence";
        var text2 = "Second sentence";

        // Act
        var result1 = BertTokenizationHelper.TokenizeWithFallback(
            _tokenizer, 
            text1, 
            MaxSequenceLength);
        var result2 = BertTokenizationHelper.TokenizeWithFallback(
            _tokenizer, 
            text2, 
            MaxSequenceLength);

        // Assert
        Assert.NotEqual(result1.InputIds[1], result2.InputIds[1]); // Different first tokens
    }

    [Fact]
    public void TokenizeWithFallback_HandlesUnicodeCharacters()
    {
        // Arrange
        var text = "Unicode test: ???? ????? ??????";

        // Act
        var result = BertTokenizationHelper.TokenizeWithFallback(
            _tokenizer, 
            text, 
            MaxSequenceLength);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.ActualTokenCount > 0);
    }

    [Fact]
    public void TokenizeWithFallback_HandlesNewlinesAndTabs()
    {
        // Arrange
        var text = "Line 1\nLine 2\tTabbed text";

        // Act
        var result = BertTokenizationHelper.TokenizeWithFallback(
            _tokenizer, 
            text, 
            MaxSequenceLength);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.ActualTokenCount > 0);
    }
}
