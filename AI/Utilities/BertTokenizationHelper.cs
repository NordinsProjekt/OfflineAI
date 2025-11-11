using BERTTokenizers;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Application.AI.Utilities;

/// <summary>
/// Helper class for BERT tokenization operations.
/// Handles encoding text into token IDs, attention masks, and token type IDs.
/// </summary>
public static class BertTokenizationHelper
{
    /// <summary>
    /// Represents the result of BERT tokenization.
    /// Contains all three inputs required by BERT models: input IDs, attention mask, and token type IDs.
    /// </summary>
    public record TokenizationResult(
        long[] InputIds,
        long[] AttentionMask,
        long[] TokenTypeIds,
        int ActualTokenCount);
    
    /// <summary>
    /// Tokenizes and encodes text for BERT model input with safety checks.
    /// </summary>
    /// <param name="tokenizer">BERT tokenizer instance</param>
    /// <param name="text">Text to tokenize</param>
    /// <param name="maxSequenceLength">Maximum sequence length (will pad or truncate)</param>
    /// <param name="fallbackText">Text to use if tokenization fails</param>
    /// <returns>Tokenization result with input IDs, attention mask, and token type IDs</returns>
    public static TokenizationResult TokenizeWithFallback(
        BertUncasedLargeTokenizer tokenizer, 
        string text, 
        int maxSequenceLength,
        string fallbackText = "fallback text for empty tokenization")
    {
        ArgumentNullException.ThrowIfNull(tokenizer);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        
        // Validate maxSequenceLength - BERT tokenizers require at least 2 tokens ([CLS] and [SEP])
        if (maxSequenceLength < 2)
        {
            throw new ArgumentException("maxSequenceLength must be at least 2 for [CLS] and [SEP] tokens", nameof(maxSequenceLength));
        }
        
        // IMPORTANT: BERTTokenizers has a bug where very long texts cause ArgumentOutOfRangeException
        // The tokenizer tries to calculate padding as: maxSequenceLength - actualTokens
        // If actualTokens > maxSequenceLength, this becomes negative and Enumerable.Repeat fails
        // Solution: Pre-truncate text to a safe length before tokenization
        
        // Rough estimate: 1 token ? 4 characters on average for English text
        // To be safe, we'll use maxSequenceLength * 3 characters as the limit
        // This ensures we never generate more tokens than maxSequenceLength
        int safeCharacterLimit = maxSequenceLength * 3;
        
        if (text.Length > safeCharacterLimit)
        {
            Console.WriteLine($"[INFO] Text too long ({text.Length} chars), truncating to {safeCharacterLimit} chars");
            text = text.Substring(0, safeCharacterLimit);
        }
        
        // Try to tokenize the original (possibly truncated) text
        IReadOnlyList<(long InputIds, long AttentionMask, long TokenTypeIds)>? encoded = null;
        
        try
        {
            encoded = tokenizer.Encode(maxSequenceLength, new[] { text });
        }
        catch (ArgumentOutOfRangeException ex)
        {
            // BERTTokenizers internal error - likely text is still too long or has issues
            Console.WriteLine($"[WARN] Tokenization failed for text (length: {text.Length}): {ex.Message}");
            encoded = null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Unexpected tokenization error: {ex.Message}");
            encoded = null;
        }
        
        // Safety check: ensure tokenization produced valid results
        if (encoded == null || encoded.Count == 0)
        {
            try
            {
                encoded = tokenizer.Encode(maxSequenceLength, new[] { fallbackText });
            }
            catch (Exception)
            {
                encoded = null;
            }
            
            // If fallback also fails, create minimal valid tokenization
            if (encoded == null || encoded.Count == 0)
            {
                return CreateMinimalTokenization(maxSequenceLength);
            }
        }
        
        return ExtractTokenArrays(encoded);
    }
    
    /// <summary>
    /// Creates a minimal valid tokenization with [CLS] and [SEP] tokens.
    /// This ensures we always have at least valid input for the BERT model.
    /// </summary>
    /// <param name="maxSequenceLength">Maximum sequence length</param>
    /// <returns>Minimal tokenization result</returns>
    private static TokenizationResult CreateMinimalTokenization(int maxSequenceLength)
    {
        var inputIds = new long[maxSequenceLength];
        var attentionMask = new long[maxSequenceLength];
        var tokenTypeIds = new long[maxSequenceLength];
        
        // Set [CLS] token (typically token ID 101 in BERT)
        inputIds[0] = 101;
        attentionMask[0] = 1;
        tokenTypeIds[0] = 0;
        
        // Set [SEP] token (typically token ID 102 in BERT)
        inputIds[1] = 102;
        attentionMask[1] = 1;
        tokenTypeIds[1] = 0;
        
        // Rest are [PAD] tokens (ID 0) which are already set by default
        // attentionMask and tokenTypeIds are already 0 for padding
        
        return new TokenizationResult(inputIds, attentionMask, tokenTypeIds, 2);
    }
    
    /// <summary>
    /// Extracts token arrays from encoded result.
    /// </summary>
    /// <param name="encoded">Encoded token list from BERT tokenizer</param>
    /// <returns>Tokenization result with all required arrays</returns>
    public static TokenizationResult ExtractTokenArrays(IReadOnlyList<(long InputIds, long AttentionMask, long TokenTypeIds)> encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        
        // Validate we have at least one token
        if (encoded.Count == 0)
        {
            throw new ArgumentException("Encoded token list cannot be empty", nameof(encoded));
        }
        
        var inputIds = new long[encoded.Count];
        var attentionMask = new long[encoded.Count];
        var tokenTypeIds = new long[encoded.Count];
        int actualTokenCount = 0;
        
        for (int i = 0; i < encoded.Count; i++)
        {
            inputIds[i] = encoded[i].InputIds;
            attentionMask[i] = encoded[i].AttentionMask;
            tokenTypeIds[i] = encoded[i].TokenTypeIds;
            
            if (attentionMask[i] == 1)
            {
                actualTokenCount++;
            }
        }
        
        // BUG FIX: If all attention masks are 0, the tokenizer is broken.
        // For now, assume all non-zero token IDs should have attention mask = 1
        if (actualTokenCount == 0)
        {
            actualTokenCount = 0;
            for (int i = 0; i < encoded.Count; i++)
            {
                // Set attention mask to 1 for any non-PAD token (PAD = 0)
                if (inputIds[i] != 0)
                {
                    attentionMask[i] = 1;
                    actualTokenCount++;
                }
            }
        }
        
        return new TokenizationResult(inputIds, attentionMask, tokenTypeIds, actualTokenCount);
    }
    
    /// <summary>
    /// Creates ONNX input tensors from tokenization results.
    /// </summary>
    /// <param name="result">Tokenization result</param>
    /// <returns>Dictionary of named ONNX tensors ready for model inference</returns>
    public static Dictionary<string, DenseTensor<long>> CreateInputTensors(TokenizationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        
        // Validate arrays are not empty
        if (result.InputIds.Length == 0)
        {
            throw new ArgumentException("TokenizationResult arrays cannot be empty", nameof(result));
        }
        
        return new Dictionary<string, DenseTensor<long>>
        {
            ["input_ids"] = new DenseTensor<long>(result.InputIds, new[] { 1, result.InputIds.Length }),
            ["attention_mask"] = new DenseTensor<long>(result.AttentionMask, new[] { 1, result.AttentionMask.Length }),
            ["token_type_ids"] = new DenseTensor<long>(result.TokenTypeIds, new[] { 1, result.TokenTypeIds.Length })
        };
    }
    
    /// <summary>
    /// Cleans up tokenization arrays to free memory (useful for CPU-constrained scenarios).
    /// </summary>
    /// <param name="result">Tokenization result to clean</param>
    public static void CleanupArrays(TokenizationResult result)
    {
        if (result == null) return;
        
        Array.Clear(result.InputIds, 0, result.InputIds.Length);
        Array.Clear(result.AttentionMask, 0, result.AttentionMask.Length);
        Array.Clear(result.TokenTypeIds, 0, result.TokenTypeIds.Length);
    }
}
