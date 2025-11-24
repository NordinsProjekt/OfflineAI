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
/// Uses weighted similarity with multiple embeddings for improved matching.
/// </summary>
public class DatabaseVectorMemory(
    ITextEmbeddingGenerationService embeddingService,
    IVectorMemoryRepository repository,
    string collectionName)
    : ISearchableMemory
{
    private readonly ITextEmbeddingGenerationService _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
    private readonly IVectorMemoryRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private string _collectionName = collectionName ?? throw new ArgumentNullException(nameof(collectionName));

    /// <summary>
    /// Update the collection name that will be used for searches.
    /// This allows switching between collections without creating a new instance.
    /// </summary>
    public void SetCollectionName(string collectionName)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be null or empty", nameof(collectionName));
        
        _collectionName = collectionName;
        Console.WriteLine($"[*] DatabaseVectorMemory now using collection: {_collectionName}");
    }
    
    /// <summary>
    /// Get the current collection name.
    /// </summary>
    public string GetCollectionName() => _collectionName;

    public void ImportMemory(Entities.IMemoryFragment section)
    {
        // Not supported for database-backed memory - use VectorMemoryPersistenceService instead
        throw new NotSupportedException("Use VectorMemoryPersistenceService to import memory into database");
    }

    /// <summary>
    /// Search the database for relevant fragments based on semantic similarity.
    /// Uses weighted similarity combining category, content, and combined embeddings.
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

        // Extract keywords from query to focus on the object/topic rather than the question structure
        // For recycling queries like "Hur sorterar jag adapter?", extract just "adapter"
        var extractedKeywords = ExtractKeywords(query);
        var searchQuery = !string.IsNullOrEmpty(extractedKeywords) ? extractedKeywords : query;
        
        if (extractedKeywords != query)
        {
            Console.WriteLine($"[*] Extracted keywords: '{extractedKeywords}' from query: '{query}'");
        }

        // Generate embedding for the search query
        Console.WriteLine($"[*] Searching database for: '{searchQuery}'");
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(searchQuery);

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

        // Calculate similarity scores using weighted strategy
        var scoredFragments = allFragments
            .Select(entity =>
            {
                double score;
                
                // Try weighted similarity if multiple embeddings available
                var categoryEmb = entity.GetCategoryEmbeddingAsMemory();
                var contentEmb = entity.GetContentEmbeddingAsMemory();
                var combinedEmb = entity.GetEmbeddingAsMemory();
                
                if (!categoryEmb.IsEmpty || !contentEmb.IsEmpty)
                {
                    // Use weighted similarity (category: 40%, content: 30%, combined: 30%)
                    score = VectorExtensions.WeightedCosineSimilarity(
                        queryEmbedding,
                        categoryEmb,
                        contentEmb,
                        combinedEmb);
                }
                else if (!combinedEmb.IsEmpty)
                {
                    // Fallback to combined embedding only (legacy)
                    score = queryEmbedding.CosineSimilarityWithNormalization(combinedEmb);
                }
                else
                {
                    score = 0.0;
                }
                
                return new
                {
                    Entity = entity,
                    Score = score
                };
            })
            .OrderByDescending(x => x.Score)
            .ToList();

        // ?? DEBUG: Show top 10 matches to diagnose matching issues
        Console.WriteLine($"[DEBUG] Top 10 similarity scores:");
        foreach (var top in scoredFragments.Take(10))
        {
            Console.WriteLine($"    {top.Score:F3} - {top.Entity.Category}");
        }
        Console.WriteLine();

        // Filter by threshold and take top K
        var results = scoredFragments
            .Where(x => x.Score >= minRelevanceScore)
            .Take(topK)
            .ToList();

        if (results.Count == 0)
        {
            Console.WriteLine($"[!] No fragments met minimum relevance score of {minRelevanceScore}");
            Console.WriteLine($"[!] Highest score was: {scoredFragments.FirstOrDefault()?.Score:F3} for '{scoredFragments.FirstOrDefault()?.Entity.Category}'");
            return null;
        }

        // Show matching fragments with their categories and scores
        Console.WriteLine($"[*] Found {results.Count} relevant fragments:");
        foreach (var result in results)
        {
            Console.WriteLine($"    {result.Entity.Category} ({result.Score:F3})");
        }

        // Build result string
        var sb = new StringBuilder();
        int totalContentLength = 0;
        
        foreach (var result in results)
        {
            var content = result.Entity.Content;

            // Truncate individual fragments if limit specified
            if (maxCharsPerFragment.HasValue && content.Length > maxCharsPerFragment.Value)
            {
                content = TruncateAtSentenceBoundary(content, maxCharsPerFragment.Value);
            }

            if (includeMetadata)
            {
                sb.AppendLine($"[Relevance: {result.Score:F3}]");
                sb.AppendLine($"[{result.Entity.Category}]");
            }

            sb.AppendLine(content);
            sb.AppendLine();
            
            totalContentLength += content.Length;
        }

        var resultText = sb.ToString();
        
        // ?? DEBUG: Check for EOS/EOF markers in database results
        var dbReport = EosEofDebugger.ScanForMarkers(resultText, "Database Search Results");
        EosEofDebugger.LogReport(dbReport, onlyIfDirty: true);
        
        if (!dbReport.IsClean)
        {
            Console.WriteLine("??  WARNING: EOS/EOF markers found in database - cleaning...");
            resultText = EosEofDebugger.CleanMarkers(resultText);
            
            // Verify cleaning
            var verifyReport = EosEofDebugger.ScanForMarkers(resultText, "Database Results After Cleaning");
            if (!verifyReport.IsClean)
            {
                Console.WriteLine("? ERROR: Failed to clean markers from database results!");
                EosEofDebugger.LogReport(verifyReport, onlyIfDirty: false);
            }
            else
            {
                Console.WriteLine("? Database results cleaned successfully");
            }
        }
        
        // EDGE CASE FIX: Check if total retrieved content is too small
        // For short, precise answers (like "Kulspruta"), even 50 chars is enough!
        if (totalContentLength < 100 && results.Count < 3)
        {
            Console.WriteLine($"[!] WARNING: Retrieved context is small ({totalContentLength} chars with {results.Count} fragments)");
            Console.WriteLine($"[*] Attempting to retrieve more context by lowering threshold...");
            
            // Try again with lower threshold and more results
            var expandedResults = scoredFragments
                .Where(x => x.Score >= Math.Max(0.3, minRelevanceScore - 0.2))
                .Take(Math.Min(topK + 3, 7))
                .ToList();
            
            if (expandedResults.Count > results.Count)
            {
                Console.WriteLine($"[*] Expanded to {expandedResults.Count} fragments with lower threshold");
                
                // Rebuild with expanded results
                sb.Clear();
                totalContentLength = 0;
                
                foreach (var result in expandedResults)
                {
                    var content = result.Entity.Content;

                    if (maxCharsPerFragment.HasValue && content.Length > maxCharsPerFragment.Value)
                    {
                        content = TruncateAtSentenceBoundary(content, maxCharsPerFragment.Value);
                    }

                    if (includeMetadata)
                    {
                        sb.AppendLine($"[Relevance: {result.Score:F3}]");
                        sb.AppendLine($"[{result.Entity.Category}]");
                    }

                    sb.AppendLine(content);
                    sb.AppendLine();
                    
                    totalContentLength += content.Length;
                }
                
                resultText = sb.ToString();
                Console.WriteLine($"[*] Expanded context to {totalContentLength} chars");
            }
        }
        
        // Accept any context with at least 1 relevant fragment, even if very short
        // Short answers are often the BEST answers for recycling queries:
        // "Adapter ? Elektronik, Småelektronik" is perfect (37 chars)!
        // "Kulspruta ? call police" is perfect!
        // DON'T reject short but correct answers!
        if (results.Count == 0 || string.IsNullOrWhiteSpace(resultText))
        {
            Console.WriteLine($"[!] No valid context found after expansion");
            return null;
        }
        
        Console.WriteLine($"[?] Returning context with {totalContentLength} chars from {results.Count} fragments");

        return resultText;
    }

    private static string TruncateAtSentenceBoundary(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;

        // Try to find last complete sentence before maxLength
        var truncated = text.Substring(0, maxLength);
        
        // Look for sentence endings (., !, ?)
        var lastPeriod = truncated.LastIndexOf(". ", StringComparison.Ordinal);
        var lastQuestion = truncated.LastIndexOf("? ", StringComparison.Ordinal);
        var lastExclamation = truncated.LastIndexOf("! ", StringComparison.Ordinal);
        
        var lastSentenceEnd = Math.Max(lastPeriod, Math.Max(lastQuestion, lastExclamation));
        
        // If we found a sentence boundary and it's not too far back (within last 30% of truncated text)
        if (lastSentenceEnd > maxLength * 0.7)
        {
            return text.Substring(0, lastSentenceEnd + 2).Trim();
        }
        
        // Otherwise truncate at word boundary
        var lastSpace = truncated.LastIndexOf(' ');
        if (lastSpace > maxLength - 50)
        {
            truncated = truncated.Substring(0, lastSpace);
        }

        return truncated.Trim() + "...";
    }
    
    /// <summary>
    /// Extracts keywords from a query to focus on the object/topic.
    /// Removes Swedish question words and focuses on nouns.
    /// Examples:
    ///   "Hur sorterar jag adapter?" ? "adapter"
    ///   "Var ska airfryer slängas?" ? "airfryer"
    ///   "adapter" ? "adapter" (unchanged if already simple)
    /// </summary>
    private static string ExtractKeywords(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return query;
        
        // Swedish question words and common filler words to remove
        var stopWords = new[]
        {
            "hur", "var", "vad", "när", "varför", "vem", "vilken", "vilket",
            "ska", "kan", "måste", "bör", "sorterar", "sortera", "slänger", "slänga",
            "jag", "vi", "du", "ni", "man",
            "en", "ett", "den", "det", "de",
            "i", "på", "till", "från", "med", "av",
            "som", "för", "om", "åt"
        };
        
        // Remove punctuation and split into words
        var cleanQuery = query.ToLowerInvariant()
            .Replace("?", "")
            .Replace("!", "")
            .Replace(".", "")
            .Replace(",", "")
            .Trim();
        
        var words = cleanQuery.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        
        // Filter out stop words
        var keywords = words
            .Where(w => !stopWords.Contains(w) && w.Length > 2)
            .ToList();
        
        // If we filtered everything out, return original query
        if (keywords.Count == 0)
            return query;
        
        // Return the extracted keywords (usually the object name)
        return string.Join(" ", keywords);
    }
}
