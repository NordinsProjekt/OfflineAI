using Microsoft.SemanticKernel.Embeddings;
using System.Text;
using Services.Interfaces;
using Services.Repositories;
using Services.Utilities;
using Entities;

namespace Services.Memory;

/// <summary>
/// Database-backed vector memory that queries on-demand instead of loading all fragments into memory.
/// Suitable for large knowledge bases and web scenarios.
/// </summary>
public class DatabaseVectorMemory(
    ITextEmbeddingGenerationService embeddingService,
    IVectorMemoryRepository repository,
    string collectionName)
    : ISearchableMemory
{
    private readonly ITextEmbeddingGenerationService _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
    private readonly IVectorMemoryRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly string _collectionName = collectionName ?? throw new ArgumentNullException(nameof(collectionName));

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
        List<string>? domainFilter = null,
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

        // Load fragments from the collection - filter at database level if domain filter is provided
        List<MemoryFragmentEntity> allFragments;
        
        if (domainFilter is { Count: > 0 })
        {
            Console.WriteLine($"[*] Filtering by domain(s) at database level: {string.Join(", ", domainFilter)}");
            allFragments = await _repository.LoadByCollectionAndDomainsAsync(_collectionName, domainFilter);
            Console.WriteLine($"[*] Loaded {allFragments.Count} domain-filtered fragments from database");
        }
        else
        {
            allFragments = await _repository.LoadByCollectionAsync(_collectionName);
            Console.WriteLine($"[*] Loaded {allFragments.Count} fragments from database");
        }

        if (allFragments.Count == 0)
        {
            Console.WriteLine($"[!] No fragments found in collection '{_collectionName}'");
            return null;
        }

        // Calculate similarity scores
        var scoredFragments = allFragments
            .Select(entity => new
            {
                Entity = entity,
                Score = entity.Embedding != null ? queryEmbedding.CosineSimilarityWithNormalization(entity.GetEmbeddingAsMemory()) : 0.0
            })
            .OrderByDescending(x => x.Score)
            .ToList();

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

    private static string TruncateString(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;

        return text.Substring(0, maxLength - 3) + "...";
    }
}
