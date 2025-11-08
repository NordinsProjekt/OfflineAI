namespace Services.Extensions;

/// <summary>
/// Extension methods for working with embedding vectors stored as ReadOnlyMemory&lt;float&gt;.
/// </summary>
public static class EmbeddingExtensions
{
    /// <summary>
    /// Converts a float array to ReadOnlyMemory&lt;float&gt; for embedding storage.
    /// </summary>
    /// <param name="embedding">Embedding array</param>
    /// <returns>ReadOnlyMemory wrapper</returns>
    public static ReadOnlyMemory<float> AsReadOnlyMemory(this float[] embedding)
    {
        ArgumentNullException.ThrowIfNull(embedding);
        return new ReadOnlyMemory<float>(embedding);
    }
    
    /// <summary>
    /// Calculates the magnitude (L2 norm) of an embedding vector.
    /// </summary>
    /// <param name="embedding">Embedding vector</param>
    /// <returns>L2 magnitude</returns>
    public static float GetMagnitude(this ReadOnlyMemory<float> embedding)
    {
        var span = embedding.Span;
        double sumOfSquares = 0;
        
        for (int i = 0; i < span.Length; i++)
        {
            sumOfSquares += span[i] * span[i];
        }
        
        return (float)Math.Sqrt(sumOfSquares);
    }
    
    /// <summary>
    /// Calculates cosine similarity between two embedding vectors.
    /// Both vectors should be normalized for accurate results.
    /// </summary>
    /// <param name="embedding1">First embedding</param>
    /// <param name="embedding2">Second embedding</param>
    /// <returns>Cosine similarity (0 to 1 for normalized vectors)</returns>
    public static float CosineSimilarity(this ReadOnlyMemory<float> embedding1, ReadOnlyMemory<float> embedding2)
    {
        var span1 = embedding1.Span;
        var span2 = embedding2.Span;
        
        if (span1.Length != span2.Length)
            throw new ArgumentException("Embeddings must have the same dimension");
        
        float dotProduct = 0;
        for (int i = 0; i < span1.Length; i++)
        {
            dotProduct += span1[i] * span2[i];
        }
        
        return dotProduct;
    }
    
    /// <summary>
    /// Checks if an embedding vector is normalized (magnitude ? 1.0).
    /// </summary>
    /// <param name="embedding">Embedding to check</param>
    /// <param name="tolerance">Tolerance for magnitude comparison (default 0.001)</param>
    /// <returns>True if the embedding is normalized within tolerance</returns>
    public static bool IsNormalized(this ReadOnlyMemory<float> embedding, float tolerance = 0.001f)
    {
        var magnitude = embedding.GetMagnitude();
        return Math.Abs(magnitude - 1.0f) < tolerance;
    }
}
