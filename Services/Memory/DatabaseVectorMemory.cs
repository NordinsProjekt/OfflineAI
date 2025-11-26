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
    
    // Debug counter for string matching diagnostics
    private static int _debugMatchCounter = 0;

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

        // Reset debug counter for this search
        System.Threading.Interlocked.Exchange(ref _debugMatchCounter, 0);

        // Calculate similarity scores using weighted strategy + exact string matching
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
                
                // HYBRID SEARCH: Boost exact string matches (for weak Swedish embeddings)
                // If the search query appears in the category, add a significant boost
                var categoryLower = entity.Category.ToLowerInvariant();
                var queryLower = searchQuery.ToLowerInvariant();
                
                double originalScore = score;
                
                // DEBUG: Log the first 20 comparisons to see what's being matched
                var debugCounter = System.Threading.Interlocked.Increment(ref _debugMatchCounter);
                if (debugCounter <= 20)
                {
                    Console.WriteLine($"[DEBUG MATCH] Comparing query='{queryLower}' with category='{categoryLower}'");
                    Console.WriteLine($"              Contains check: {categoryLower.Contains(queryLower)}");
                }
                
                // PHASE 1: Check for important multi-word phrases in the ORIGINAL query
                // These phrases are meaningful as complete units (don't rely on keyword extraction)
                var importantPhrases = new[]
                {
                    "how to win", "how to play", "how to setup", "how to fight",
                    "game setup", "game components", "winning condition", "turn order",
                    "player turn", "end game", "victory points", "setup instructions"
                };
                
                var originalQueryLower = query.ToLowerInvariant();
                foreach (var phrase in importantPhrases)
                {
                    // Check if phrase appears in BOTH the original query AND the category
                    if (originalQueryLower.Contains(phrase) && categoryLower.Contains(phrase))
                    {
                        score += 0.4; // Strong boost for important phrase match
                        Console.WriteLine($"[BOOST] '{entity.Category}' phrase match '{phrase}': {originalScore:F3} ? {score:F3} (+0.4)");
                        break; // Only apply one phrase boost
                    }
                }
                
                // PHASE 2: Check for query word match in category (exact word or substring)
                if (categoryLower.Contains(queryLower))
                {
                    // Split category into words to check for exact word match
                    var categoryWords = categoryLower.Split(new[] { ' ', '-', ',', '(', ')', '[', ']' }, StringSplitOptions.RemoveEmptyEntries);
                    var queryWords = queryLower.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    
                    if (debugCounter <= 20)
                    {
                        Console.WriteLine($"[DEBUG MATCH] Category words: [{string.Join(", ", categoryWords)}]");
                        Console.WriteLine($"[DEBUG MATCH] Query words: [{string.Join(", ", queryWords)}]");
                    }
                    
                    // Check if ANY query word appears as a complete word in the category
                    bool hasExactWordMatch = false;
                    string? matchedWord = null;
                    
                    foreach (var queryWord in queryWords)
                    {
                        if (queryWord.Length < 3) continue; // Skip very short words
                        
                        // Check for exact word match (e.g., "leksaksbåt" in "Leksaksbåt metall")
                        if (categoryWords.Contains(queryWord))
                        {
                            hasExactWordMatch = true;
                            matchedWord = queryWord;
                            break;
                        }
                        
                        // Check if query word is START of any category word (e.g., "leksak" matches "leksaksbåt")
                        if (categoryWords.Any(w => w.StartsWith(queryWord) && w.Length > queryWord.Length))
                        {
                            hasExactWordMatch = true;
                            matchedWord = queryWord;
                            break;
                        }
                    }
                    
                    if (hasExactWordMatch)
                    {
                        score += 0.5; // Strong boost for exact word match
                        Console.WriteLine($"[BOOST] '{entity.Category}' exact word match '{matchedWord}': {originalScore:F3} ? {score:F3} (+0.5)");
                    }
                    else
                    {
                        // Substring match only (less confident)
                        score += 0.3;
                        Console.WriteLine($"[BOOST] '{entity.Category}' substring match: {originalScore:F3} ? {score:F3} (+0.3)");
                    }
                }
                else
                {
                    // PHASE 3: FUZZY MATCHING - Handle typos and misspellings
                    // Use Levenshtein distance to find similar words even with typos
                    var categoryWords = categoryLower.Split(new[] { ' ', '-', ',', '(', ')', '[', ']' }, StringSplitOptions.RemoveEmptyEntries);
                    var queryWords = queryLower.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    
                    string? fuzzyMatchedWord = null;
                    string? fuzzyMatchedCategoryWord = null;
                    int bestDistance = int.MaxValue;
                    
                    foreach (var queryWord in queryWords)
                    {
                        if (queryWord.Length < 4) continue; // Need at least 4 chars for fuzzy matching
                        
                        foreach (var categoryWord in categoryWords)
                        {
                            if (categoryWord.Length < 4) continue;
                            
                            // Calculate edit distance (number of character changes needed)
                            var distance = CalculateLevenshteinDistance(queryWord, categoryWord);
                            
                            // Allow up to 2 character differences for words 6+ chars
                            // Allow 1 character difference for words 4-5 chars
                            var maxAllowedDistance = queryWord.Length >= 6 ? 2 : 1;
                            
                            if (distance <= maxAllowedDistance && distance < bestDistance)
                            {
                                bestDistance = distance;
                                fuzzyMatchedWord = queryWord;
                                fuzzyMatchedCategoryWord = categoryWord;
                            }
                        }
                    }
                    
                    if (fuzzyMatchedWord != null && fuzzyMatchedCategoryWord != null)
                    {
                        // Fuzzy match found - give partial boost based on similarity
                        var fuzzyBoost = bestDistance == 1 ? 0.4 : 0.25; // 1 char diff = 0.4, 2 char diff = 0.25
                        score += fuzzyBoost;
                        Console.WriteLine($"[FUZZY BOOST] '{entity.Category}' fuzzy match '{fuzzyMatchedWord}' ? '{fuzzyMatchedCategoryWord}' (distance={bestDistance}): {originalScore:F3} ? {score:F3} (+{fuzzyBoost})");
                    }
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

            // ALWAYS include the category label so LLM knows what each fragment is about
            // Only include the relevance score if metadata is requested
            if (includeMetadata)
            {
                sb.AppendLine($"[Relevance: {result.Score:F3}]");
                sb.AppendLine($"[{result.Entity.Category}]");
            }
            else
            {
                // Even without metadata, include category so LLM can identify the answer
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
        // REMOVED: This check was rejecting valid short answers
        // The LLM can work with any amount of relevant context
        
        // Accept any context with at least 1 relevant fragment, even if very short
        // Short answers are often the BEST answers for recycling queries:
        // "Adapter ? Elektronik, Småelektronik" is perfect (37 chars)!
        // "Kulspruta ? call police" is perfect!
        // DON'T reject short but correct answers!
        if (results.Count == 0 || string.IsNullOrWhiteSpace(resultText))
        {
            Console.WriteLine($"[!] No valid context found");
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
    /// Handles both Swedish recycling queries and English game rule queries.
    /// Preserves important multi-word phrases like "how to win", "how to play".
    /// Examples:
    ///   Swedish: "Hur sorterar jag adapter?" ? "adapter"
    ///   English: "How to win in Munchkin?" ? "how to win munchkin" (preserves phrase)
    ///   Simple: "adapter" ? "adapter" (unchanged if already simple)
    /// </summary>
    private static string ExtractKeywords(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return query;
        
        var lowerQuery = query.ToLowerInvariant();
        
        // Detect important multi-word phrases that should be preserved
        var importantPhrases = new[]
        {
            "how to win", "how to play", "how to setup", "how to fight",
            "game setup", "game components", "winning condition", "turn order",
            "player turn", "end game", "victory points", "setup instructions"
        };
        
        // Check if query contains any important phrase - if so, use gentler filtering
        var containsImportantPhrase = importantPhrases.Any(phrase => lowerQuery.Contains(phrase));
        
        if (containsImportantPhrase)
        {
            // ENGLISH QUERY MODE: Only remove pure filler words
            var lightStopWords = new[]
            {
                "the", "a", "an", "in", "on", "at", "by", "for", "with", "from",
                "is", "are", "was", "were", "be", "been", "being"
            };
            
            // Remove punctuation but keep the structure
            var cleanQuery = lowerQuery
                .Replace("?", "")
                .Replace("!", "")
                .Replace(".", "")
                .Replace(",", "")
                .Trim();
            
            var words = cleanQuery.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            
            // Only filter out pure articles/prepositions
            var keywords = words
                .Where(w => !lightStopWords.Contains(w) && w.Length > 1)
                .ToList();
            
            if (keywords.Count > 0)
            {
                return string.Join(" ", keywords);
            }
        }
        else
        {
            // SWEDISH QUERY MODE: Aggressive filtering for recycling queries
            var stopWords = new[]
            {
                "hur", "var", "vad", "när", "varför", "vem", "vilken", "vilket",
                "ska", "kan", "måste", "bör", "sorterar", "sortera", "slänger", "slänga",
                "jag", "vi", "du", "ni", "man","återvinna","återvinner",
                "en", "ett", "den", "det", "de",
                "i", "på", "till", "från", "med", "av",
                "som", "för", "om", "åt"
            };
            
            // Remove punctuation and split into words
            var cleanQuery = lowerQuery
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
            
            if (keywords.Count > 0)
            {
                return string.Join(" ", keywords);
            }
        }
        
        // If we filtered everything out, return original query
        return query;
    }
    
    /// <summary>
    /// Calculate Levenshtein distance (edit distance) between two strings.
    /// Returns the minimum number of single-character edits (insertions, deletions, substitutions)
    /// required to change one string into another.
    /// 
    /// Examples:
    ///   "leksaksbåt" vs "leksakbåt" = 1 (missing 's')
    ///   "leksaksbåt" vs "leksaksbot" = 1 (å ? o)
    ///   "adapter" vs "adaptor" = 1 (e ? o)
    /// </summary>
    private static int CalculateLevenshteinDistance(string source, string target)
    {
        if (string.IsNullOrEmpty(source))
            return target?.Length ?? 0;
        
        if (string.IsNullOrEmpty(target))
            return source.Length;
        
        var n = source.Length;
        var m = target.Length;
        var distance = new int[n + 1, m + 1];
        
        // Initialize first column and row
        for (var i = 0; i <= n; i++)
            distance[i, 0] = i;
        
        for (var j = 0; j <= m; j++)
            distance[0, j] = j;
        
        // Calculate distances
        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var cost = (source[i - 1] == target[j - 1]) ? 0 : 1;
                
                distance[i, j] = Math.Min(
                    Math.Min(
                        distance[i - 1, j] + 1,      // Deletion
                        distance[i, j - 1] + 1),     // Insertion
                    distance[i - 1, j - 1] + cost);  // Substitution
            }
        }
        
        return distance[n, m];
    }
}
