namespace Services.Utilities;

/// <summary>
/// Helper class for pooling and normalizing embeddings from transformer models.
/// Provides methods for attention-masked mean pooling and L2 normalization.
/// </summary>
public static class EmbeddingPooling
{
    /// <summary>
    /// Performs attention-masked mean pooling on BERT-style token embeddings.
    /// Averages only the non-padding tokens based on the attention mask.
    /// </summary>
    /// <param name="outputTensor">Flattened output tensor from BERT model</param>
    /// <param name="attentionMask">Attention mask (1 for real tokens, 0 for padding)</param>
    /// <param name="embeddingDimension">Dimension of each token embedding (e.g., 384)</param>
    /// <returns>Pooled embedding vector</returns>
    public static float[] ApplyMeanPooling(float[] outputTensor, long[] attentionMask, int embeddingDimension)
    {
        ArgumentNullException.ThrowIfNull(outputTensor);
        ArgumentNullException.ThrowIfNull(attentionMask);
        
        if (embeddingDimension <= 0)
            throw new ArgumentException("Embedding dimension must be positive", nameof(embeddingDimension));
        
        var sequenceLength = outputTensor.Length / embeddingDimension;
        var embedding = new float[embeddingDimension];
        
        // Count actual tokens (non-padding)
        int actualTokenCount = CountActualTokens(attentionMask, sequenceLength);
        
        // Mean pooling with attention mask
        for (int i = 0; i < embeddingDimension; i++)
        {
            float sum = 0;
            for (int j = 0; j < sequenceLength; j++)
            {
                // Only include tokens where attention_mask is 1 (non-padding)
                if (attentionMask[j] == 1)
                {
                    sum += outputTensor[j * embeddingDimension + i];
                }
            }
            // Divide by actual token count, not total sequence length
            embedding[i] = actualTokenCount > 0 ? sum / actualTokenCount : 0;
        }
        
        return embedding;
    }
    
    /// <summary>
    /// Normalizes an embedding vector to unit length (L2 normalization).
    /// Makes the magnitude of the vector equal to 1.0, preserving direction.
    /// This allows cosine similarity to be computed with a simple dot product.
    /// </summary>
    /// <param name="embedding">Embedding vector to normalize</param>
    /// <returns>Normalized embedding with magnitude 1.0</returns>
    public static float[] NormalizeToUnitLength(float[] embedding)
    {
        ArgumentNullException.ThrowIfNull(embedding);
        
        if (embedding.Length == 0)
            return embedding;
        
        // Calculate L2 magnitude: sqrt(sum of squares)
        var magnitude = CalculateMagnitude(embedding);
        
        // Normalize each component
        if (magnitude > 0)
        {
            for (int i = 0; i < embedding.Length; i++)
            {
                embedding[i] /= magnitude;
            }
        }
        
        return embedding;
    }
    
    /// <summary>
    /// Performs both mean pooling and L2 normalization in a single operation.
    /// This is the standard approach for sentence-transformers models.
    /// </summary>
    /// <param name="outputTensor">Flattened output tensor from BERT model</param>
    /// <param name="attentionMask">Attention mask (1 for real tokens, 0 for padding)</param>
    /// <param name="embeddingDimension">Dimension of each token embedding</param>
    /// <returns>Pooled and normalized embedding ready for similarity comparison</returns>
    public static float[] PoolAndNormalize(float[] outputTensor, long[] attentionMask, int embeddingDimension)
    {
        var pooled = ApplyMeanPooling(outputTensor, attentionMask, embeddingDimension);
        return NormalizeToUnitLength(pooled);
    }
    
    /// <summary>
    /// Counts the number of actual (non-padding) tokens based on the attention mask.
    /// </summary>
    /// <param name="attentionMask">Attention mask array</param>
    /// <param name="maxLength">Maximum length to check (defaults to full array length)</param>
    /// <returns>Count of tokens where attention_mask == 1</returns>
    public static int CountActualTokens(long[] attentionMask, int? maxLength = null)
    {
        ArgumentNullException.ThrowIfNull(attentionMask);
        
        int length = maxLength ?? attentionMask.Length;
        int count = 0;
        
        for (int i = 0; i < length && i < attentionMask.Length; i++)
        {
            if (attentionMask[i] == 1)
            {
                count++;
            }
        }
        
        return count;
    }
    
    /// <summary>
    /// Calculates the L2 magnitude (Euclidean length) of a vector.
    /// Formula: sqrt(x?² + x?² + ... + x?²)
    /// </summary>
    /// <param name="vector">Vector to measure</param>
    /// <returns>L2 magnitude of the vector</returns>
    public static float CalculateMagnitude(float[] vector)
    {
        ArgumentNullException.ThrowIfNull(vector);
        
        if (vector.Length == 0)
            return 0;
        
        double sumOfSquares = 0;
        foreach (var value in vector)
        {
            sumOfSquares += value * value;
        }
        
        return (float)Math.Sqrt(sumOfSquares);
    }
    
    /// <summary>
    /// Calculates cosine similarity between two normalized embeddings.
    /// Since embeddings are normalized, this is equivalent to dot product.
    /// </summary>
    /// <param name="embedding1">First normalized embedding</param>
    /// <param name="embedding2">Second normalized embedding</param>
    /// <returns>Cosine similarity score (0 to 1 for normalized vectors)</returns>
    public static float CosineSimilarity(float[] embedding1, float[] embedding2)
    {
        ArgumentNullException.ThrowIfNull(embedding1);
        ArgumentNullException.ThrowIfNull(embedding2);
        
        if (embedding1.Length != embedding2.Length)
            throw new ArgumentException("Embeddings must have the same dimension");
        
        float dotProduct = 0;
        for (int i = 0; i < embedding1.Length; i++)
        {
            dotProduct += embedding1[i] * embedding2[i];
        }
        
        return dotProduct;
    }
}
