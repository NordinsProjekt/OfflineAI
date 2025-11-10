using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;

namespace OfflineAI.Tests.Mocks;

/// <summary>
/// Mock embedding service for unit tests.
/// Generates simple deterministic embeddings without requiring external dependencies.
/// </summary>
public class MockEmbeddingService : ITextEmbeddingGenerationService
{
    private readonly int _embeddingDimension;

    public MockEmbeddingService(int embeddingDimension = 384)
    {
        _embeddingDimension = embeddingDimension;
    }

    public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();

    public async Task<IList<ReadOnlyMemory<float>>> GenerateEmbeddingsAsync(
        IList<string> data,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ReadOnlyMemory<float>>();
        foreach (var text in data)
        {
            var embedding = await GenerateEmbeddingAsync(text, kernel, cancellationToken);
            results.Add(embedding);
        }

        return results;
    }

    public Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(
        string data,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        var embedding = CreateSimpleEmbedding(data);
        return Task.FromResult(embedding);
    }

    /// <summary>
    /// Creates a simple deterministic embedding for testing.
    /// Uses character frequencies for basic similarity matching.
    /// </summary>
    private ReadOnlyMemory<float> CreateSimpleEmbedding(string text)
    {
        var embedding = new float[_embeddingDimension];
        var normalized = text.ToLowerInvariant();

        // Simple character frequency distribution
        for (int i = 0; i < 26 && i < _embeddingDimension; i++)
        {
            char targetChar = (char)('a' + i);
            embedding[i] = normalized.Count(c => c == targetChar) / (float)Math.Max(1, normalized.Length);
        }

        // Word frequency for common words
        var words = normalized.Split(new[] { ' ', '\t', '\r', '\n', '.', ',', '!', '?', ';', ':' },
            StringSplitOptions.RemoveEmptyEntries);

        var commonWords = new[] {
            "the", "be", "to", "of", "and", "a", "in", "that", "have", "i",
            "it", "for", "not", "on", "with", "he", "as", "you", "do", "at",
            "fight", "monster", "player", "game", "win", "gold", "treasure", "roll", "dice"
        };

        for (int i = 0; i < commonWords.Length && i + 26 < _embeddingDimension; i++)
        {
            embedding[26 + i] = words.Count(w => w == commonWords[i]) / (float)Math.Max(1, words.Length);
        }

        // Normalize to unit length
        var magnitude = Math.Sqrt(embedding.Sum(x => x * x));
        if (magnitude > 0)
        {
            for (int i = 0; i < embedding.Length; i++)
            {
                embedding[i] /= (float)magnitude;
            }
        }

        return new ReadOnlyMemory<float>(embedding);
    }
}
