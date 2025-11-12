using Microsoft.SemanticKernel.Embeddings;
using System.Text;
using Entities;
using Services.Interfaces;
using Services.Utilities;

namespace Services.Memory;

/// <summary>
/// A vector-based memory implementation that uses embeddings to store and retrieve relevant memory fragments.
/// </summary>
public class VectorMemory : ILlmMemory
{
    private readonly ITextEmbeddingGenerationService _embeddingService;
    private readonly List<VectorMemoryEntry> _entries = new();
    private readonly string _collectionName;

    public VectorMemory(ITextEmbeddingGenerationService embeddingService, string collectionName = "default")
    {
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _collectionName = collectionName;
    }

    public void ImportMemory(IMemoryFragment section)
    {
        // Store the fragment - we'll generate embeddings on-demand during search
        _entries.Add(new VectorMemoryEntry
        {
            Fragment = section,
            Id = Guid.NewGuid().ToString()
        });
    }

    /// <summary>
    /// Set embedding for the most recently added fragment.
    /// Used when loading from database with pre-computed embeddings.
    /// </summary>
    public void SetEmbeddingForLastFragment(ReadOnlyMemory<float> embedding)
    {
        if (_entries.Count == 0)
        {
            throw new InvalidOperationException("No fragments to set embedding for.");
        }

        _entries[^1].Embedding = embedding;
    }

    /// <summary>
    /// Get the number of fragments in memory.
    /// </summary>
    public int Count => _entries.Count;
    
    /// <summary>
    /// Gets all fragments for debugging purposes.
    /// </summary>
    /// <returns>List of all memory fragments</returns>
    public List<IMemoryFragment> GetAllFragments()
    {
        return _entries.Select(e => e.Fragment).ToList();
    }

    /// <summary>
    /// Retrieves the most relevant memory fragments based on semantic similarity to the query,
    /// optionally filtering by game name.
    /// Returns null if no fragments meet the minimum relevance threshold.
    /// </summary>
    /// <param name="query">The search query</param>
    /// <param name="topK">Number of top results to return</param>
    /// <param name="minRelevanceScore">Minimum similarity score (0-1)</param>
    /// <param name="gameFilter">List of game IDs to filter by (null = no filtering)</param>
    /// <param name="maxCharsPerFragment">Maximum characters per fragment (default: no limit)</param>
    /// <param name="includeMetadata">Include relevance scores and categories in output (for debug)</param>
    /// <returns>String containing the most relevant memory fragments, or null if none found</returns>
    public async Task<string?> SearchRelevantMemoryAsync(
        string query, 
        int topK = 5, 
        double minRelevanceScore = 0.5,
        List<string>? gameFilter = null,
        int? maxCharsPerFragment = null,
        bool includeMetadata = true)
    {
        if (string.IsNullOrWhiteSpace(query) || _entries.Count == 0)
        {
            return null; // No fragments available
        }

        // Generate embedding for the query
        Console.Write($"[*] Searching knowledge base... ");
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query);
        Console.WriteLine($"Done");

        // Count how many need embeddings
        int totalToProcess = _entries.Count(e => e.Embedding == null);
        
        if (totalToProcess > 0)
        {
            Console.WriteLine("?????????????????????????????????????????????????????");
            Console.WriteLine($"?  Generating Embeddings for {totalToProcess} Fragments                 ?");
            Console.WriteLine("?????????????????????????????????????????????????????");
            Console.WriteLine();
            
            var startTime = DateTime.Now;
            int processed = 0;
            
            foreach (var entry in _entries)
            {
                if (entry.Embedding == null)
                {
                    processed++;
                    
                    // Calculate timing estimates
                    var elapsed = (DateTime.Now - startTime).TotalSeconds;
                    var avgTimePerEmbedding = processed > 1 ? elapsed / (processed - 1) : 0;
                    var remaining = totalToProcess - processed;
                    var estimatedTimeRemaining = avgTimePerEmbedding > 0 ? remaining * avgTimePerEmbedding : 0;
                    
                    // Progress bar
                    var progressPercent = (processed * 100.0) / totalToProcess;
                    var barWidth = 50;
                    var filledWidth = (int)((progressPercent / 100.0) * barWidth);
                    var emptyWidth = barWidth - filledWidth;
                    
                    Console.Write($"\r  [{processed}/{totalToProcess}] [");
                    Console.Write(new string('?', filledWidth));
                    Console.Write(new string('?', emptyWidth));
                    Console.Write($"] {progressPercent:F0}%");
                    
                    if (avgTimePerEmbedding > 0)
                    {
                        Console.Write($" | ETA: {estimatedTimeRemaining:F0}s");
                    }
                    
                    try
                    {
                        // Validate content before embedding generation
                        if (string.IsNullOrWhiteSpace(entry.Fragment.Content))
                        {
                            // Create a minimal embedding for empty content (all zeros)
                            var zeroEmbedding = new float[384]; // Default dimension
                            entry.Embedding = new ReadOnlyMemory<float>(zeroEmbedding);
                            continue;
                        }
                        
                        // Include both category (title) and content for better semantic matching
                        // This ensures queries like "how to win" can match sections titled "How to win"
                        var textToEmbed = $"{entry.Fragment.Category}\n\n{entry.Fragment.Content}";
                        entry.Embedding = await _embeddingService.GenerateEmbeddingAsync(textToEmbed);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"\n[ERROR] Embedding generation failed: {ex.Message}");
                        throw;
                    }

                    // Force garbage collection every 3 embeddings to control memory
                    if (processed % 3 == 0)
                    {
                        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                        GC.WaitForPendingFinalizers();
                        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                    }
                }
            }
            
            Console.WriteLine(); // New line after progress bar
            var totalTime = (DateTime.Now - startTime).TotalSeconds;
            Console.WriteLine($"\n[?] Embeddings generated in {totalTime:F1}s (avg: {totalTime / totalToProcess:F2}s each)\n");
        }

        // Calculate similarity scores
        var allScores = _entries
            .Select(entry => new
            {
                Entry = entry,
                Score = entry.Embedding.HasValue ? CosineSimilarity(queryEmbedding, entry.Embedding.Value) : 0.0
            })
            .OrderByDescending(x => x.Score)
            .ToList();

        // Apply game filtering if specified
        if (gameFilter != null && gameFilter.Count > 0)
        {
            Console.WriteLine($"[*] Filtering by game(s): {string.Join(", ", gameFilter.Select(GameDetector.GetDisplayName))}");
            
            allScores = allScores
                .Where(x => GameDetector.MatchesGame(x.Entry.Fragment.Category, gameFilter))
                .ToList();
                
            if (allScores.Count == 0)
            {
                Console.WriteLine($"[!] No fragments found matching game filter.");
            }
        }

        // Filter by threshold
        var results = allScores
            .Where(x => x.Score >= minRelevanceScore)
            .Take(topK)
            .ToList();

        // Return null if no fragments meet the threshold
        if (results.Count == 0)
        {
            return null;
        }

        // Build result string with optional truncation
        var sb = new StringBuilder();
        foreach (var result in results)
        {
            var content = result.Entry.Fragment.Content;
            
            // Truncate individual fragments if limit specified
            if (maxCharsPerFragment.HasValue && content.Length > maxCharsPerFragment.Value)
            {
                content = TruncateString(content, maxCharsPerFragment.Value);
            }

            if (includeMetadata)
            {
                sb.AppendLine($"[Relevance: {result.Score:F3}]");
                sb.AppendLine($"[{result.Entry.Fragment.Category}]");
            }
            
            sb.AppendLine(content);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Returns all memory fragments (non-vectorized fallback).
    /// </summary>
    public override string ToString()
    {
        var sb = new StringBuilder();
        foreach (var entry in _entries)
        {
            sb.AppendLine(entry.Fragment.ToString());
        }

        return sb.ToString();
    }

    /// <summary>
    /// Calculates cosine similarity between two vectors.
    /// </summary>
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

    private class VectorMemoryEntry
    {
        public required string Id { get; set; }
        public required IMemoryFragment Fragment { get; set; }
        public ReadOnlyMemory<float>? Embedding { get; set; }
    }
}