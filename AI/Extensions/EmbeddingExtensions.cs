namespace Application.AI.Extensions;

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
    /// Returns dot product for normalized vectors.
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
    /// Calculates cosine similarity between two embedding vectors with full normalization.
    /// This version handles non-normalized vectors by computing magnitudes.
    /// Returns a value between -1 and 1 (typically 0 to 1 for similar vectors).
    /// </summary>
    /// <param name="vector1">First embedding vector</param>
    /// <param name="vector2">Second embedding vector</param>
    /// <returns>Cosine similarity score (-1 to 1)</returns>
    public static double CosineSimilarityWithNormalization(this ReadOnlyMemory<float> vector1, ReadOnlyMemory<float> vector2)
    {
        var v1 = vector1.Span;
        var v2 = vector2.Span;

        if (v1.Length != v2.Length)
        {
            throw new ArgumentException("Vectors must have the same length");
        }

        double dotProduct = 0;
        double magnitude1 = 0;
        double magnitude2 = 0;

        for (int i = 0; i < v1.Length; i++)
        {
            dotProduct += v1[i] * v2[i];
            magnitude1 += v1[i] * v1[i];
            magnitude2 += v2[i] * v2[i];
        }

        magnitude1 = Math.Sqrt(magnitude1);
        magnitude2 = Math.Sqrt(magnitude2);

        if (magnitude1 == 0 || magnitude2 == 0)
        {
            return 0;
        }

        return dotProduct / (magnitude1 * magnitude2);
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

    /// <summary>
    /// Calculate weighted similarity using multiple embedding types.
    /// Uses category (40%), content (30%), and combined (30%) embeddings for optimal matching.
    /// Falls back gracefully if some embeddings are missing.
    /// </summary>
    /// <param name="queryEmbedding">The query embedding vector</param>
    /// <param name="categoryEmbedding">Category-only embedding (weight: 0.4)</param>
    /// <param name="contentEmbedding">Content-only embedding (weight: 0.3)</param>
    /// <param name="combinedEmbedding">Combined category+content embedding (weight: 0.3)</param>
    /// <returns>Weighted cosine similarity score between 0 and 1</returns>
    public static double WeightedCosineSimilarity(
        ReadOnlyMemory<float> queryEmbedding,
        ReadOnlyMemory<float> categoryEmbedding,
        ReadOnlyMemory<float> contentEmbedding,
        ReadOnlyMemory<float> combinedEmbedding)
    {
        var weights = new List<(double similarity, double weight)>();
        
        // Category embedding (weight: 0.4) - highest weight for domain/topic matching
        if (!categoryEmbedding.IsEmpty && categoryEmbedding.Length == queryEmbedding.Length)
        {
            var catSim = queryEmbedding.CosineSimilarityWithNormalization(categoryEmbedding);
            weights.Add((catSim, 0.4));
        }
        
        // Content embedding (weight: 0.3) - for detailed content matching
        if (!contentEmbedding.IsEmpty && contentEmbedding.Length == queryEmbedding.Length)
        {
            var contentSim = queryEmbedding.CosineSimilarityWithNormalization(contentEmbedding);
            weights.Add((contentSim, 0.3));
        }
        
        // Combined embedding (weight: 0.3) - fallback/balance
        if (!combinedEmbedding.IsEmpty && combinedEmbedding.Length == queryEmbedding.Length)
        {
            var combinedSim = queryEmbedding.CosineSimilarityWithNormalization(combinedEmbedding);
            weights.Add((combinedSim, 0.3));
        }
        
        // If no embeddings available, return 0
        if (weights.Count == 0)
        {
            return 0;
        }
        
        // Calculate weighted average
        var totalWeight = weights.Sum(w => w.weight);
        var weightedSum = weights.Sum(w => w.similarity * w.weight);
        
        return weightedSum / totalWeight;
    }
    
    /// <summary>
    /// Calculate weighted similarity with custom weights.
    /// </summary>
    public static double WeightedCosineSimilarity(
        ReadOnlyMemory<float> queryEmbedding,
        ReadOnlyMemory<float> categoryEmbedding,
        ReadOnlyMemory<float> contentEmbedding,
        ReadOnlyMemory<float> combinedEmbedding,
        double categoryWeight = 0.4,
        double contentWeight = 0.3,
        double combinedWeight = 0.3)
    {
        var weights = new List<(double similarity, double weight)>();
        
        if (!categoryEmbedding.IsEmpty && categoryEmbedding.Length == queryEmbedding.Length)
        {
            var catSim = queryEmbedding.CosineSimilarityWithNormalization(categoryEmbedding);
            weights.Add((catSim, categoryWeight));
        }
        
        if (!contentEmbedding.IsEmpty && contentEmbedding.Length == queryEmbedding.Length)
        {
            var contentSim = queryEmbedding.CosineSimilarityWithNormalization(contentEmbedding);
            weights.Add((contentSim, contentWeight));
        }
        
        if (!combinedEmbedding.IsEmpty && combinedEmbedding.Length == queryEmbedding.Length)
        {
            var combinedSim = queryEmbedding.CosineSimilarityWithNormalization(combinedEmbedding);
            weights.Add((combinedSim, combinedWeight));
        }
        
        if (weights.Count == 0)
        {
            return 0;
        }
        
        var totalWeight = weights.Sum(w => w.weight);
        var weightedSum = weights.Sum(w => w.similarity * w.weight);
        
        return weightedSum / totalWeight;
    }

    /// <summary>
    /// Normalize a vector to unit length (L2 normalization).
    /// </summary>
    public static ReadOnlyMemory<float> Normalize(ReadOnlyMemory<float> vector)
    {
        var span = vector.Span;
        var magnitude = 0.0;

        // Calculate magnitude
        for (int i = 0; i < span.Length; i++)
        {
            magnitude += span[i] * span[i];
        }

        magnitude = Math.Sqrt(magnitude);

        // Avoid division by zero
        if (magnitude == 0)
        {
            return vector;
        }

        // Normalize
        var normalized = new float[span.Length];
        for (int i = 0; i < span.Length; i++)
        {
            normalized[i] = (float)(span[i] / magnitude);
        }

        return new ReadOnlyMemory<float>(normalized);
    }
}
