using Application.AI.Embeddings;
using Entities;

namespace Application.AI.Tests.Memory;

/// <summary>
/// Debug tests for semantic search to identify why queries return poor matches.
/// Tests the embedding generation and similarity scoring directly without needing database or VectorMemory.
/// </summary>
public class SemanticSearchDebugTests
{
    private readonly SemanticEmbeddingService? _embeddingService;
    private readonly bool _isInitialized = false;

    public SemanticSearchDebugTests()
    {
        try
        {
            // Initialize embedding service with minimal configuration
            // Note: These paths need to be valid on your system - UPDATE THESE!
            var modelPath = @"d:\tinyllama\models\all-mpnet-base-v2\onnx\model.onnx";
            var vocabPath = @"d:\tinyllama\models\all-mpnet-base-v2\vocab.txt";
            
            if (!File.Exists(modelPath) || !File.Exists(vocabPath))
            {
                Console.WriteLine($"??  WARNING: Embedding model files not found.");
                Console.WriteLine($"   Model: {modelPath}");
                Console.WriteLine($"   Vocab: {vocabPath}");
                Console.WriteLine($"   These tests will be skipped. Update paths in test constructor to run them.");
                return;
            }

            _embeddingService = new SemanticEmbeddingService(
                modelPath,
                vocabPath,
                embeddingDimension: 768,
                debugMode: false);
            
            _isInitialized = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"??  WARNING: Failed to initialize embedding service: {ex.Message}");
            Console.WriteLine($"   These tests will be skipped.");
        }
    }

    [Fact]
    public async Task DebugSearch_MansionOfMadness_ItemCarryLimit()
    {
        // Skip if not initialized
        if (!_isInitialized || _embeddingService == null)
        {
            Console.WriteLine("Test skipped - embedding service not initialized. Update model paths in test constructor.");
            return;
        }

        // Arrange - Create test fragments similar to what would be in the PDF
        var fragments = new List<(string Category, string Content)>
        {
            // Fragment that SHOULD match (contains the answer)
            ("Mansion of Madness - Items",
                @"Items and Carrying Capacity
An investigator can carry any number of common items. However, an investigator can have only two possessions (items with the Possession trait) at a time. If an investigator gains a possession that would cause him to have more than two possessions, he must immediately discard a possession."),
            
            // Fragment that's currently being returned (score 0.577)
            ("Mansion of Madness - General",
                @"Traits appear in text in italics. Traits have no inherent effects of their own. Some effects refer to cards by their traits. For example, 'You may explore only if you have a Light Source.' TURN During the investigator phase, investigators take turns in the order of their choice. During an investigator's turn, he may perform up..."),
            
            // Fragment about pushing/strength (score 0.562)
            ("Mansion of Madness - General",
                @"If pushing another investigator, that investigator tests strength; the test difficulty is equal to the test result plus one. 4.Resolve Test: The active investigator tests his strength. If the test result equals or exceeds the test difficulty, proceed to step 5. If test result is less than the test difficulty, the activ..."),
            
            // Fragment about damage/horror (score 0.557)
            ("Mansion of Madness - General",
                @"When an investigator flips a Damage or Horror faceup, if it has the Resolve Immediately trait, he immediately resolves the effects of that card. Related Topics: Damage & Horror, Double-sided Cards, Spell"),
            
            // Additional potentially relevant fragments
            ("Mansion of Madness - Equipment",
                @"Equipment cards represent items that investigators can find and use. Common items have no special restrictions. Possessions are limited - an investigator can only have two possessions at once."),
            
            ("Mansion of Madness - Inventory",
                @"Each investigator has an inventory where they store their items and clues. The inventory has no limit for common items, but special items marked as possessions are limited to two per investigator."),
            
            ("Mansion of Madness - Turn Structure",
                @"On your turn, you may perform up to two actions. Actions include: moving, attacking, trading items with other investigators in your space, or exploring."),
        };

        // The actual user query
        var query = "How many items can an investigator carry in Mansion of Madness?";
        
        Console.WriteLine($"\n?????????????????????????????????????????????????????????????????");
        Console.WriteLine($"?  SEMANTIC SEARCH DEBUG TEST                                  ?");
        Console.WriteLine($"?????????????????????????????????????????????????????????????????\n");
        Console.WriteLine($"Query: \"{query}\"\n");

        // Generate query embedding
        Console.WriteLine("Generating query embedding...");
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query);
        Console.WriteLine($"? Query embedding generated (dimension: {queryEmbedding.Span.Length})\n");

        // Calculate similarity for each fragment
        Console.WriteLine("???????????????????????????????????????????????????????????????");
        Console.WriteLine("FRAGMENT SIMILARITY SCORES:");
        Console.WriteLine("???????????????????????????????????????????????????????????????\n");
        
        var scoredFragments = new List<(string Category, string Content, double Score)>();

        foreach (var (category, content) in fragments)
        {
            var embedding = await _embeddingService.GenerateEmbeddingAsync(content);
            var similarity = CosineSimilarity(queryEmbedding.Span, embedding.Span);
            scoredFragments.Add((category, content, similarity));
            
            Console.WriteLine($"[{similarity:F4}] {category}");
            Console.WriteLine($"  Content preview: {content.Substring(0, Math.Min(100, content.Length))}...\n");
        }

        // Sort and display top results
        var topResults = scoredFragments.OrderByDescending(x => x.Score).Take(3).ToList();

        Console.WriteLine("???????????????????????????????????????????????????????????????");
        Console.WriteLine("TOP 3 MATCHES:");
        Console.WriteLine("???????????????????????????????????????????????????????????????\n");
        
        for (int i = 0; i < topResults.Count; i++)
        {
            var (category, content, score) = topResults[i];
            Console.WriteLine($"#{i + 1} - Score: {score:F4}");
            Console.WriteLine($"Category: {category}");
            Console.WriteLine($"Content: {content}\n");
        }

        // Analysis
        Console.WriteLine("???????????????????????????????????????????????????????????????");
        Console.WriteLine("ANALYSIS:");
        Console.WriteLine("???????????????????????????????????????????????????????????????\n");
        
        var correctFragment = topResults.FirstOrDefault(x => 
            x.Content.Contains("carrying capacity", StringComparison.OrdinalIgnoreCase) ||
            x.Content.Contains("two possessions", StringComparison.OrdinalIgnoreCase));
        
        if (correctFragment == default)
        {
            Console.WriteLine("? PROBLEM DETECTED!");
            Console.WriteLine("The fragment containing the actual answer is NOT in the top 3 results.\n");
            
            var correctFragmentRank = scoredFragments
                .OrderByDescending(x => x.Score)
                .Select((item, index) => new { item, Rank = index + 1 })
                .FirstOrDefault(x => x.item.Content.Contains("two possessions", StringComparison.OrdinalIgnoreCase));
            
            if (correctFragmentRank != null)
            {
                Console.WriteLine($"The correct fragment ranked #{correctFragmentRank.Rank} with score {correctFragmentRank.item.Score:F4}");
                Console.WriteLine($"Category: {correctFragmentRank.item.Category}\n");
                Console.WriteLine("Possible causes:");
                Console.WriteLine("  1. PDF chunking is creating fragments that are too generic");
                Console.WriteLine("  2. Embedding model doesn't understand domain-specific terminology");
                Console.WriteLine("  3. Query formulation needs improvement");
                Console.WriteLine("  4. Fragment content truncation is removing key information");
            }
        }
        else
        {
            Console.WriteLine("? PASS - Correct fragment found in top 3!");
            Console.WriteLine($"Score: {correctFragment.Score:F4}");
            Console.WriteLine($"Category: {correctFragment.Category}");
        }

        // Assert
        Assert.NotEqual(default, correctFragment);
        Assert.True(correctFragment.Score > 0.6, 
            $"Expected similarity score > 0.6 for correct fragment, but got {correctFragment.Score:F4}");
    }

    [Fact]
    public async Task DebugEmbedding_QueryVsFragmentComparison()
    {
        if (!_isInitialized || _embeddingService == null)
        {
            Console.WriteLine("Test skipped - embedding service not initialized");
            return;
        }

        var testCases = new[]
        {
            new
            {
                Query = "How many items can I carry?",
                GoodMatch = "An investigator can carry two possessions at a time.",
                BadMatch = "Traits appear in text in italics and have no effects."
            },
            new
            {
                Query = "What is the item limit?",
                GoodMatch = "Investigators are limited to two possessions but can have unlimited common items.",
                BadMatch = "During your turn you may perform up to two actions."
            },
            new
            {
                Query = "carrying capacity rules",
                GoodMatch = "Each investigator can have only two possessions at once.",
                BadMatch = "Test your strength to push another investigator."
            }
        };

        Console.WriteLine("\n?????????????????????????????????????????????????????????????????");
        Console.WriteLine("?  EMBEDDING COMPARISON TESTS                                  ?");
        Console.WriteLine("?????????????????????????????????????????????????????????????????\n");

        int passCount = 0;
        int failCount = 0;

        foreach (var testCase in testCases)
        {
            Console.WriteLine($"Query: \"{testCase.Query}\"");

            var queryEmbed = await _embeddingService.GenerateEmbeddingAsync(testCase.Query);
            var goodEmbed = await _embeddingService.GenerateEmbeddingAsync(testCase.GoodMatch);
            var badEmbed = await _embeddingService.GenerateEmbeddingAsync(testCase.BadMatch);

            var goodScore = CosineSimilarity(queryEmbed.Span, goodEmbed.Span);
            var badScore = CosineSimilarity(queryEmbed.Span, badEmbed.Span);

            Console.WriteLine($"  Good Match: {goodScore:F4} - \"{testCase.GoodMatch}\"");
            Console.WriteLine($"  Bad Match:  {badScore:F4} - \"{testCase.BadMatch}\"");
            Console.WriteLine($"  Difference: {goodScore - badScore:F4}");
            
            if (goodScore > badScore)
            {
                Console.WriteLine($"  Result: ? PASS\n");
                passCount++;
            }
            else
            {
                Console.WriteLine($"  Result: ? FAIL - Bad match scored higher!\n");
                failCount++;
            }
        }

        Console.WriteLine($"???????????????????????????????????????????????????????????????");
        Console.WriteLine($"Summary: {passCount} passed, {failCount} failed");
        Console.WriteLine($"???????????????????????????????????????????????????????????????\n");

        Assert.Equal(0, failCount);
    }

    [Fact]
    public async Task DebugEmbedding_TokenizationAnalysis()
    {
        if (!_isInitialized || _embeddingService == null)
        {
            Console.WriteLine("Test skipped - embedding service not initialized");
            return;
        }

        var queries = new[]
        {
            "How many items can an investigator carry?",
            "item carrying limit",
            "possession limit",
            "inventory capacity",
            "two possessions maximum"
        };

        Console.WriteLine("\n?????????????????????????????????????????????????????????????????");
        Console.WriteLine("?  TOKENIZATION & EMBEDDING ANALYSIS                           ?");
        Console.WriteLine("?????????????????????????????????????????????????????????????????\n");

        var embeddings = new List<(string Query, ReadOnlyMemory<float> Embedding)>();

        foreach (var query in queries)
        {
            var embedding = await _embeddingService.GenerateEmbeddingAsync(query);
            embeddings.Add((query, embedding));

            Console.WriteLine($"Query: \"{query}\"");
            Console.WriteLine($"  Length: {query.Length} chars");
            Console.WriteLine($"  Word count: {query.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length}");
            Console.WriteLine($"  Embedding magnitude: {CalculateMagnitude(embedding.Span):F4}");
            Console.WriteLine($"  First 10 values: [{string.Join(", ", embedding.Span[..10].ToArray().Select(x => $"{x:F3}"))}]\n");
        }

        Console.WriteLine("???????????????????????????????????????????????????????????????");
        Console.WriteLine("CROSS-SIMILARITY MATRIX:");
        Console.WriteLine("???????????????????????????????????????????????????????????????\n");

        Console.Write("          ");
        for (int i = 0; i < embeddings.Count; i++)
        {
            Console.Write($"Q{i + 1}      ");
        }
        Console.WriteLine();

        for (int i = 0; i < embeddings.Count; i++)
        {
            Console.Write($"Query {i + 1}:  ");
            for (int j = 0; j < embeddings.Count; j++)
            {
                var similarity = CosineSimilarity(embeddings[i].Embedding.Span, embeddings[j].Embedding.Span);
                Console.Write($"{similarity:F4}  ");
            }
            Console.WriteLine();
        }

        Console.WriteLine("\nNote: Diagonal should be ~1.0 (self-similarity)");
        Console.WriteLine("High off-diagonal values indicate semantic similarity\n");
    }

    [Fact]
    public async Task DebugSearch_CompareWithAndWithoutDomainName()
    {
        if (!_isInitialized || _embeddingService == null)
        {
            Console.WriteLine("Test skipped - embedding service not initialized");
            return;
        }

        var fragments = new[]
        {
            ("Mansion of Madness - Items", "An investigator can carry two possessions maximum. Common items have no limit."),
            ("Mansion of Madness - General", "Traits appear in italics and have no effects of their own."),
        };

        Console.WriteLine("\n?????????????????????????????????????????????????????????????????");
        Console.WriteLine("?  DOMAIN NAME IMPACT TEST                                     ?");
        Console.WriteLine("?????????????????????????????????????????????????????????????????\n");

        var queries = new[]
        {
            "How many items can an investigator carry?",
            "How many items can an investigator carry in Mansion of Madness?",
            "carrying capacity",
            "Mansion of Madness carrying capacity"
        };

        foreach (var query in queries)
        {
            Console.WriteLine($"Query: \"{query}\"\n");

            var queryEmbed = await _embeddingService.GenerateEmbeddingAsync(query);
            
            foreach (var (category, content) in fragments)
            {
                var fragEmbed = await _embeddingService.GenerateEmbeddingAsync(content);
                var score = CosineSimilarity(queryEmbed.Span, fragEmbed.Span);
                
                Console.WriteLine($"  [{score:F4}] {category}");
            }
            
            Console.WriteLine();
        }
    }

    #region Helper Methods

    private static double CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Vectors must have the same dimension");

        double dotProduct = 0;
        double magnitudeA = 0;
        double magnitudeB = 0;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            magnitudeA += a[i] * a[i];
            magnitudeB += b[i] * b[i];
        }

        magnitudeA = Math.Sqrt(magnitudeA);
        magnitudeB = Math.Sqrt(magnitudeB);

        if (magnitudeA == 0 || magnitudeB == 0)
            return 0;

        return dotProduct / (magnitudeA * magnitudeB);
    }

    private static double CalculateMagnitude(ReadOnlySpan<float> vector)
    {
        double sum = 0;
        for (int i = 0; i < vector.Length; i++)
        {
            sum += vector[i] * vector[i];
        }
        return Math.Sqrt(sum);
    }

    #endregion
}
