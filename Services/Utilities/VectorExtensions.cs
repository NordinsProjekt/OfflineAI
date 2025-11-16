namespace Services.Utilities;

/// <summary>
/// Extension methods for working with embedding vectors (ReadOnlyMemory&lt;float&gt;).
/// </summary>
public static class VectorExtensions
{
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
}
