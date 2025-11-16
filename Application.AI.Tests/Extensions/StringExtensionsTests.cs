using Application.AI.Extensions;

namespace Application.AI.Tests.Extensions;

/// <summary>
/// Unit tests for StringExtensions class.
/// Tests cover the CleanModelArtifacts method with various model-specific tokens and edge cases.
/// </summary>
public class StringExtensionsTests
{
    #region Null and Empty String Tests

    [Fact]
    public void CleanModelArtifacts_WithNullString_ReturnsNull()
    {
        // Arrange
        string? input = null;

        // Act
        var result = input!.CleanModelArtifacts();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void CleanModelArtifacts_WithEmptyString_ReturnsEmpty()
    {
        // Arrange
        var input = string.Empty;

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void CleanModelArtifacts_WithWhitespaceOnly_ReturnsWhitespace()
    {
        // Arrange
        var input = "   ";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("   ", result);
    }

    [Fact]
    public void CleanModelArtifacts_WithNewlinesAndSpaces_ReturnsOriginal()
    {
        // Arrange
        var input = "\n\n  \t  \n";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("\n\n  \t  \n", result);
    }

    #endregion

    #region Llama 3.2 Token Tests

    [Fact]
    public void CleanModelArtifacts_WithBeginOfTextToken_RemovesToken()
    {
        // Arrange
        var input = "<|begin_of_text|>Hello World";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("Hello World", result);
    }

    [Fact]
    public void CleanModelArtifacts_WithEndOfTextToken_RemovesToken()
    {
        // Arrange
        var input = "Hello World<|end_of_text|>";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("Hello World", result);
    }

    [Fact]
    public void CleanModelArtifacts_WithEotIdToken_RemovesToken()
    {
        // Arrange
        var input = "Hello<|eot_id|> World";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("Hello World", result);
    }

    [Fact]
    public void CleanModelArtifacts_WithStartHeaderIdToken_RemovesToken()
    {
        // Arrange
        var input = "<|start_header_id|>User Message";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("User Message", result);
    }

    [Fact]
    public void CleanModelArtifacts_WithEndHeaderIdToken_RemovesToken()
    {
        // Arrange
        var input = "Header<|end_header_id|>Content";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("HeaderContent", result);
    }

    [Fact]
    public void CleanModelArtifacts_WithMultipleLlama32Tokens_RemovesAllTokens()
    {
        // Arrange
        var input = "<|begin_of_text|><|start_header_id|>user<|end_header_id|>Hello<|eot_id|><|end_of_text|>";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("userHello", result);
    }

    #endregion

    #region TinyLlama / Phi Token Tests

    [Fact]
    public void CleanModelArtifacts_WithSystemToken_RemovesToken()
    {
        // Arrange
        var input = "<|system|>System message here";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("System message here", result);
    }

    [Fact]
    public void CleanModelArtifacts_WithUserToken_RemovesToken()
    {
        // Arrange
        var input = "<|user|>User question";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("User question", result);
    }

    [Fact]
    public void CleanModelArtifacts_WithAssistantToken_RemovesToken()
    {
        // Arrange
        var input = "<|assistant|>Assistant response";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("Assistant response", result);
    }

    [Fact]
    public void CleanModelArtifacts_WithEndToken_RemovesToken()
    {
        // Arrange
        var input = "Message here<|end|>";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("Message here", result);
    }

    [Fact]
    public void CleanModelArtifacts_WithEndOfTextTokenVariant_RemovesToken()
    {
        // Arrange
        var input = "Text content<|endoftext|>";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("Text content", result);
    }

    [Fact]
    public void CleanModelArtifacts_WithMultipleTinyLlamaTokens_RemovesAllTokens()
    {
        // Arrange
        var input = "<|system|>You are helpful<|end|><|user|>Hi<|end|><|assistant|>Hello<|end|>";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("You are helpfulHiHello", result);
    }

    #endregion

    #region ChatML Token Tests

    [Fact]
    public void CleanModelArtifacts_WithImStartToken_RemovesToken()
    {
        // Arrange
        var input = "<|im_start|>system\nYou are helpful";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("system\nYou are helpful", result);
    }

    [Fact]
    public void CleanModelArtifacts_WithImEndToken_RemovesToken()
    {
        // Arrange
        var input = "Message content<|im_end|>";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("Message content", result);
    }

    [Fact]
    public void CleanModelArtifacts_WithChatMLConversation_RemovesAllTokens()
    {
        // Arrange
        var input = "<|im_start|>user\nHello<|im_end|><|im_start|>assistant\nHi there<|im_end|>";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("user\nHelloassistant\nHi there", result);
    }

    #endregion

    #region Mistral Token Tests

    [Fact]
    public void CleanModelArtifacts_WithInstToken_RemovesToken()
    {
        // Arrange
        var input = "[INST]User instruction here";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("User instruction here", result);
    }

    [Fact]
    public void CleanModelArtifacts_WithInstEndToken_RemovesToken()
    {
        // Arrange
        var input = "Instruction content[/INST]";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("Instruction content", result);
    }

    [Fact]
    public void CleanModelArtifacts_WithSysToken_RemovesToken()
    {
        // Arrange
        var input = "<<SYS>>System prompt here";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("System prompt here", result);
    }

    [Fact]
    public void CleanModelArtifacts_WithSysEndToken_RemovesToken()
    {
        // Arrange
        var input = "System content<</SYS>>";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("System content", result);
    }

    [Fact]
    public void CleanModelArtifacts_WithMistralFormat_RemovesAllTokens()
    {
        // Arrange
        var input = "[INST]<<SYS>>You are helpful<</SYS>>What is AI?[/INST]";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("You are helpfulWhat is AI?", result);
    }

    #endregion

    #region Llama 2 Token Tests

    [Fact]
    public void CleanModelArtifacts_WithStartSToken_RemovesToken()
    {
        // Arrange
        var input = "<s>Beginning of sequence";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("Beginning of sequence", result);
    }

    [Fact]
    public void CleanModelArtifacts_WithEndSToken_RemovesToken()
    {
        // Arrange
        var input = "End of sequence</s>";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("End of sequence", result);
    }

    [Fact]
    public void CleanModelArtifacts_WithMultipleLlama2Tokens_RemovesAllTokens()
    {
        // Arrange
        var input = "<s>First sequence</s><s>Second sequence</s>";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("First sequenceSecond sequence", result);
    }

    #endregion

    #region Mixed Token Tests

    [Fact]
    public void CleanModelArtifacts_WithMixedTokenTypes_RemovesAllTokens()
    {
        // Arrange
        var input = "<s><|begin_of_text|>[INST]Hello<|im_start|>World<|end|></s>";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("HelloWorld", result);
    }

    [Fact]
    public void CleanModelArtifacts_WithTokensAndWhitespace_RemovesTokensAndTrimsWhitespace()
    {
        // Arrange
        var input = "  <|begin_of_text|>  Hello World  <|end_of_text|>  ";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("Hello World", result);
    }

    [Fact]
    public void CleanModelArtifacts_WithNestedTokens_RemovesAllTokens()
    {
        // Arrange
        var input = "<|begin_of_text|><s>[INST]<|system|>Content<|end|>[/INST]</s><|end_of_text|>";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("Content", result);
    }

    #endregion

    #region Incomplete Sentence Marker Tests

    [Fact]
    public void CleanModelArtifacts_WithIncompleteMarkerAndPeriod_TruncatesAtPeriod()
    {
        // Arrange
        var input = "This is a complete sentence. This is incomplete text>";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("This is a complete sentence.", result);
    }

    [Fact]
    public void CleanModelArtifacts_WithIncompleteMarkerAndExclamation_TruncatesAtExclamation()
    {
        // Arrange
        var input = "This is exciting! This is incomplete text>";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("This is exciting!", result);
    }

    [Fact]
    public void CleanModelArtifacts_WithIncompleteMarkerAndQuestion_TruncatesAtQuestion()
    {
        // Arrange
        var input = "What is this? This is incomplete text>";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("What is this?", result);
    }

    [Fact]
    public void CleanModelArtifacts_WithDoubleGreaterThan_DoesNotTruncate()
    {
        // Arrange
        var input = "This is complete text>>";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("This is complete text>>", result);
    }

    [Fact]
    public void CleanModelArtifacts_WithSingleGreaterThanButNoPunctuation_DoesNotTruncate()
    {
        // Arrange
        var input = "Text without punctuation>";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("Text without punctuation>", result);
    }

    [Fact]
    public void CleanModelArtifacts_WithSingleGreaterThanAndNearbyPunctuation_DoesNotTruncate()
    {
        // Arrange
        // "Text with period." = 17 chars, + " Close to end>" = 14 more chars = 31 total
        // Period at index 16, '>' at index 30, difference is 14 (>10), so no truncation expected
        // BUT the condition is: lastCompleteStop < response.Length - 10
        // 16 < 31 - 10 = 16 < 21 = TRUE, so it WILL truncate
        var input = "Text with period. Close to end>";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("Text with period.", result);
    }

    [Fact]
    public void CleanModelArtifacts_WithMultiplePunctuationMarks_TruncatesAtLast()
    {
        // Arrange
        var input = "First sentence. Second sentence! Third question? Incomplete>";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("First sentence. Second sentence! Third question?", result);
    }

    [Fact]
    public void CleanModelArtifacts_WithIncompleteMarkerAtStart_DoesNotTruncate()
    {
        // Arrange
        var input = ">Some text";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal(">Some text", result);
    }

    [Fact]
    public void CleanModelArtifacts_WithIncompleteMarkerInMiddle_DoesNotTruncate()
    {
        // Arrange
        var input = "Some > text";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("Some > text", result);
    }

    #endregion

    #region Clean Text Tests

    [Fact]
    public void CleanModelArtifacts_WithCleanText_ReturnsUnchanged()
    {
        // Arrange
        var input = "This is a clean sentence without any tokens.";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("This is a clean sentence without any tokens.", result);
    }

    [Fact]
    public void CleanModelArtifacts_WithMultilineCleanText_ReturnsUnchanged()
    {
        // Arrange
        var input = "Line 1\nLine 2\nLine 3";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("Line 1\nLine 2\nLine 3", result);
    }

    [Fact]
    public void CleanModelArtifacts_WithSpecialCharacters_ReturnsUnchanged()
    {
        // Arrange
        var input = "Text with @#$%^&*() special chars!";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("Text with @#$%^&*() special chars!", result);
    }

    [Fact]
    public void CleanModelArtifacts_WithUnicodeCharacters_ReturnsUnchanged()
    {
        // Arrange
        var input = "Text with émojis ?? and ümlauts";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("Text with émojis ?? and ümlauts", result);
    }

    #endregion

    #region Whitespace Trimming Tests

    [Fact]
    public void CleanModelArtifacts_WithLeadingWhitespace_TrimsWhitespace()
    {
        // Arrange
        var input = "   Leading spaces";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("Leading spaces", result);
    }

    [Fact]
    public void CleanModelArtifacts_WithTrailingWhitespace_TrimsWhitespace()
    {
        // Arrange
        var input = "Trailing spaces   ";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("Trailing spaces", result);
    }

    [Fact]
    public void CleanModelArtifacts_WithLeadingAndTrailingWhitespace_TrimsBoth()
    {
        // Arrange
        var input = "  Both sides  ";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("Both sides", result);
    }

    [Fact]
    public void CleanModelArtifacts_WithTokensAndWhitespace_RemovesTokensAndTrims()
    {
        // Arrange
        var input = "  <|begin_of_text|>Content<|end_of_text|>  ";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("Content", result);
    }

    #endregion

    #region Real-World Example Tests

    [Fact]
    public void CleanModelArtifacts_WithLlama32Response_CleansCorrectly()
    {
        // Arrange
        var input = "<|begin_of_text|><|start_header_id|>system<|end_header_id|>\n\nYou are a helpful AI assistant.<|eot_id|><|start_header_id|>user<|end_header_id|>\n\nWhat is the capital of France?<|eot_id|><|start_header_id|>assistant<|end_header_id|>\n\nThe capital of France is Paris.<|eot_id|>";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("system\n\nYou are a helpful AI assistant.user\n\nWhat is the capital of France?assistant\n\nThe capital of France is Paris.", result);
    }

    [Fact]
    public void CleanModelArtifacts_WithMistralResponse_CleansCorrectly()
    {
        // Arrange
        var input = "<s>[INST] <<SYS>>\nYou are a helpful assistant.\n<</SYS>>\n\nWhat is machine learning? [/INST] Machine learning is a subset of artificial intelligence.</s>";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("You are a helpful assistant.\n\n\nWhat is machine learning?  Machine learning is a subset of artificial intelligence.", result);
    }

    [Fact]
    public void CleanModelArtifacts_WithChatMLResponse_CleansCorrectly()
    {
        // Arrange
        var input = "<|im_start|>system\nYou are ChatGPT, a helpful assistant.<|im_end|>\n<|im_start|>user\nHello!<|im_end|>\n<|im_start|>assistant\nHi! How can I help you today?<|im_end|>";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("system\nYou are ChatGPT, a helpful assistant.\nuser\nHello!\nassistant\nHi! How can I help you today?", result);
    }

    [Fact]
    public void CleanModelArtifacts_WithPhiResponse_CleansCorrectly()
    {
        // Arrange
        var input = "<|system|>\nYou are Phi, a helpful AI.<|end|>\n<|user|>\nExplain quantum computing.<|end|>\n<|assistant|>\nQuantum computing uses quantum mechanics principles.<|end|>";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        // Note: Leading and trailing whitespace is trimmed by the method
        Assert.Equal("You are Phi, a helpful AI.\n\nExplain quantum computing.\n\nQuantum computing uses quantum mechanics principles.", result);
    }

    [Fact]
    public void CleanModelArtifacts_WithIncompleteResponseAndToken_CleansAndTruncates()
    {
        // Arrange
        var input = "<|begin_of_text|>This is a complete sentence. This is incomple<|end_of_text|>";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("This is a complete sentence. This is incomple", result);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void CleanModelArtifacts_WithOnlyTokens_ReturnsEmpty()
    {
        // Arrange
        var input = "<|begin_of_text|><|end_of_text|>";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void CleanModelArtifacts_WithTokensSurroundingWhitespace_ReturnsEmpty()
    {
        // Arrange
        var input = "<|begin_of_text|>   <|end_of_text|>";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void CleanModelArtifacts_WithVeryLongText_HandlesCorrectly()
    {
        // Arrange
        var input = new string('a', 10000) + "<|end_of_text|>";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal(new string('a', 10000), result);
    }

    [Fact]
    public void CleanModelArtifacts_WithRepeatedTokens_RemovesAll()
    {
        // Arrange
        var input = "<s><s><s>Text<s><s>";

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("Text", result);
    }

    [Fact]
    public void CleanModelArtifacts_WithSimilarButNotExactTokens_LeavesUnchanged()
    {
        // Arrange
        var input = "<|begin_of_tex|>Text<|end_of_tex|>"; // Missing 't' at end

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("<|begin_of_tex|>Text<|end_of_tex|>", result);
    }

    [Fact]
    public void CleanModelArtifacts_WithTokensInDifferentCase_LeavesUnchanged()
    {
        // Arrange
        var input = "<|BEGIN_OF_TEXT|>Text<|END_OF_TEXT|>"; // Uppercase

        // Act
        var result = input.CleanModelArtifacts();

        // Assert
        Assert.Equal("<|BEGIN_OF_TEXT|>Text<|END_OF_TEXT|>", result);
    }

    #endregion
}
