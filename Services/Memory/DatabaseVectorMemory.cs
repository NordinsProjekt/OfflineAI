using Microsoft.SemanticKernel.Embeddings;
using System.Text;
using Services.Interfaces;
using Services.Repositories;
using Services.Utilities;

namespace Services.Memory;

/// <summary>
/// Database-backed vector memory that queries on-demand instead of loading all fragments into memory.
/// Suitable for large knowledge bases and web scenarios.
/// </summary>
public class DatabaseVectorMemory : ISearchableMemory
{
    private readonly ITextEmbeddingGenerationService _embeddingService;
    private readonly IVectorMemoryRepository _repository;
    private readonly string _collectionName;

    public DatabaseVectorMemory(
        ITextEmbeddingGenerationService embeddingService,
        IVectorMemoryRepository repository,
        string collectionName)
    {
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _collectionName = collectionName ?? throw new ArgumentNullException(nameof(collectionName));
    }

    public void ImportMemory(Entities.IMemoryFragment section)
    {
        // Not supported for database-backed memory - use VectorMemoryPersistenceService instead
        throw new NotSupportedException("Use VectorMemoryPersistenceService to import memory into database");
    }

    /// <summary>
    /// Search the database for relevant fragments based on semantic similarity.
    /// </summary>
    public async Task<string?> SearchRelevantMemoryAsync(
        string query,
        int topK = 5,
        double minRelevanceScore = 0.5,
        List<string>? gameFilter = null,
        int? maxCharsPerFragment = null,
        bool includeMetadata = true)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        // Generate embedding for the query
        Console.WriteLine($"[*] Searching database for: '{query}'");
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query);

        // Load all fragments from the collection (with pre-computed embeddings)
        var allFragments = await _repository.LoadByCollectionAsync(_collectionName);

        if (allFragments.Count == 0)
        {
            Console.WriteLine($"[!] No fragments found in collection '{_collectionName}'");
            return null;
        }

        Console.WriteLine($"[*] Loaded {allFragments.Count} fragments from database");

        // Calculate similarity scores
        var scoredFragments = allFragments
            .Select(entity => new
            {
                Entity = entity,
                Score = entity.Embedding != null ? CosineSimilarity(queryEmbedding, entity.GetEmbeddingAsMemory()) : 0.0
            })
            .OrderByDescending(x => x.Score)
            .ToList();

        // Apply game filtering if specified
        if (gameFilter != null && gameFilter.Count > 0)
        {
            Console.WriteLine($"[*] Filtering by game(s): {string.Join(", ", gameFilter)}");

            scoredFragments = scoredFragments
                .Where(x => MatchesAnyGame(x.Entity.Category, gameFilter))
                .ToList();

            if (scoredFragments.Count == 0)
            {
                Console.WriteLine($"[!] No fragments found matching game filter");
            }
            else
            {
                Console.WriteLine($"[*] {scoredFragments.Count} fragments after game filter");
            }
        }

        // Filter by threshold and take top K
        var results = scoredFragments
            .Where(x => x.Score >= minRelevanceScore)
            .Take(topK)
            .ToList();

        if (results.Count == 0)
        {
            Console.WriteLine($"[!] No fragments met minimum relevance score of {minRelevanceScore}");
            return null;
        }

        Console.WriteLine($"[*] Found {results.Count} relevant fragments (scores: {string.Join(", ", results.Select(r => r.Score.ToString("F3")))})");

        // Build result string
        var sb = new StringBuilder();
        foreach (var result in results)
        {
            var content = result.Entity.Content;

            // Truncate individual fragments if limit specified
            if (maxCharsPerFragment.HasValue && content.Length > maxCharsPerFragment.Value)
            {
                content = TruncateString(content, maxCharsPerFragment.Value);
            }

            if (includeMetadata)
            {
                sb.AppendLine($"[Relevance: {result.Score:F3}]");
                sb.AppendLine($"[{result.Entity.Category}]");
            }

            sb.AppendLine(content);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Check if a category matches any of the game filters.
    /// Simple string contains matching since GameDetector isn't available here.
    /// </summary>
    private static bool MatchesAnyGame(string category, List<string> gameFilter)
    {
        if (string.IsNullOrWhiteSpace(category))
            return false;

        var lowerCategory = category.ToLowerInvariant();

        foreach (var gameId in gameFilter)
        {
            // Check if game ID (e.g., "gloomhaven") matches category
            var gameIdWithSpaces = gameId.Replace("-", " ");
            
            if (lowerCategory.Contains(gameIdWithSpaces, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public override string ToString()
    {
        return $"DatabaseVectorMemory(Collection: {_collectionName})";
    }

    private static double CosineSimilarity(ReadOnlyMemory<float> vector1, ReadOnlyMemory<float> vector2)
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

    private static string TruncateString(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;

        return text.Substring(0, maxLength - 3) + "...";
    }
}
