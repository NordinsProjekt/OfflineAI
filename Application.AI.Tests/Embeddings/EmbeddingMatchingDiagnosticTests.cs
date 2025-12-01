using Application.AI.Embeddings;
using Application.AI.Extensions;
using Xunit.Abstractions;

namespace Application.AI.Tests.Embeddings;

/// <summary>
/// Diagnostic tests for embedding matching quality.
/// Helps identify why certain queries don't match expected content well.
/// </summary>
public class EmbeddingMatchingDiagnosticTests
{
    private readonly ITestOutputHelper _output;
    
    // Actual model paths
    private const string ModelPath = @"D:\tinyllama\models\paraphrase-multilingual-mpnet-base-v2\model.onnx";
    private const string VocabPath = @"D:\tinyllama\models\paraphrase-multilingual-mpnet-base-v2\tokenizer.json";
    private const int EmbeddingDimension = 768;

    public EmbeddingMatchingDiagnosticTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Tests why "How to win in Happy little dinosaurs" doesn't match well with its expected content.
    /// This helps diagnose embedding quality, normalization, and similarity calculation issues.
    /// </summary>
    [Fact]
    public async Task DiagnoseHappyLittleDinosaursMatching()
    {
        // Arrange
        if (!File.Exists(ModelPath) || !File.Exists(VocabPath))
        {
            _output.WriteLine("SKIPPED: Model files not found");
            _output.WriteLine($"Expected model: {ModelPath}");
            _output.WriteLine($"Expected vocab: {VocabPath}");
            return;
        }

        var embeddingService = new SemanticEmbeddingService(
            modelPath: ModelPath,
            vocabPath: VocabPath,
            embeddingDimension: EmbeddingDimension,
            debugMode: true);

        // Test query
        var query = "How to win in Happy little dinosaurs";

        // Expected matching content
        var expectedCategory = "##Happy Little Dinosaurs - How to win";
        var expectedContent = @"There are two ways to win the game: Be the first Dinosaur to reach 50 points on your Escape Route. Be the last Dinosaur left in the game. Looks like you've successfully delayed your inevitable extinction. Unfortunately, all of your friends are now dead :( But on the bright side, you're a winner.";

        // Alternative queries to test
        var alternativeQueries = new[]
        {
            "how to win happy little dinosaurs",
            "winning happy little dinosaurs",
            "victory conditions happy little dinosaurs",
            "how do I win at happy little dinosaurs",
            "happy little dinosaurs win condition"
        };

        // Generate embeddings
        _output.WriteLine("=== QUERY ANALYSIS ===");
        _output.WriteLine($"Original Query: '{query}'");
        _output.WriteLine($"Query Length: {query.Length} characters");
        _output.WriteLine($"Query Word Count: {query.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length} words");

        var queryEmbedding = await embeddingService.GenerateEmbeddingAsync(query, kernel: null!, CancellationToken.None);
        _output.WriteLine($"Query Embedding Dimension: {queryEmbedding.Length}");
        _output.WriteLine($"Query Embedding First 5 values: [{string.Join(", ", queryEmbedding.Span.Slice(0, 5).ToArray().Select(v => v.ToString("F4")))}]");
        _output.WriteLine($"Query Embedding L2 Norm: {CalculateL2Norm(queryEmbedding):F6}");

        // Test with expected content
        _output.WriteLine("=== EXPECTED CONTENT ANALYSIS ===");
        _output.WriteLine($"Category: '{expectedCategory}'");
        _output.WriteLine($"Content Length: {expectedContent.Length} characters");
        _output.WriteLine($"Content Word Count: {expectedContent.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length} words");
        _output.WriteLine($"Content Preview: {expectedContent.Substring(0, Math.Min(100, expectedContent.Length))}...");

        var contentEmbedding = await embeddingService.GenerateEmbeddingAsync(expectedContent, kernel: null!, CancellationToken.None);
        _output.WriteLine($"Content Embedding Dimension: {contentEmbedding.Length}");
        _output.WriteLine($"Content Embedding First 5 values: [{string.Join(", ", contentEmbedding.Span.Slice(0, 5).ToArray().Select(v => v.ToString("F4")))}]");
        _output.WriteLine($"Content Embedding L2 Norm: {CalculateL2Norm(contentEmbedding):F6}");

        // Calculate similarity
        var similarity = EmbeddingExtensions.CosineSimilarity(queryEmbedding, contentEmbedding);
        _output.WriteLine("=== SIMILARITY RESULTS ===");
        _output.WriteLine($"Cosine Similarity (Query vs Content): {similarity:F6}");
        _output.WriteLine($"Similarity Percentage: {similarity * 100:F2}%");

        // Threshold analysis
        _output.WriteLine("=== THRESHOLD ANALYSIS ===");
        var thresholds = new[] { 0.3, 0.4, 0.5, 0.6, 0.7, 0.8 };
        foreach (var threshold in thresholds)
        {
            var wouldMatch = similarity >= threshold;
            _output.WriteLine($"Threshold {threshold:F2}: {(wouldMatch ? "✓ MATCH" : "✗ NO MATCH")}");
        }

        // Test category embedding separately
        _output.WriteLine("=== CATEGORY ANALYSIS ===");
        var categoryEmbedding = await embeddingService.GenerateEmbeddingAsync(expectedCategory, kernel: null!, CancellationToken.None);
        var categorySimilarity = EmbeddingExtensions.CosineSimilarity(queryEmbedding, categoryEmbedding);
        _output.WriteLine($"Category: '{expectedCategory}'");
        _output.WriteLine($"Category Similarity: {categorySimilarity:F6} ({categorySimilarity * 100:F2}%)");

        // Test combined category + content
        var combinedText = $"{expectedCategory}\n\n{expectedContent}";
        var combinedEmbedding = await embeddingService.GenerateEmbeddingAsync(combinedText, kernel: null!, CancellationToken.None);
        var combinedSimilarity = EmbeddingExtensions.CosineSimilarity(queryEmbedding, combinedEmbedding);
        _output.WriteLine($"Combined (Category + Content) Similarity: {combinedSimilarity:F6} ({combinedSimilarity * 100:F2}%)");

        // Test alternative queries
        _output.WriteLine("=== ALTERNATIVE QUERY ANALYSIS ===");
        foreach (var altQuery in alternativeQueries)
        {
            var altEmbedding = await embeddingService.GenerateEmbeddingAsync(altQuery, kernel: null!, CancellationToken.None);
            var altSimilarity = EmbeddingExtensions.CosineSimilarity(altEmbedding, contentEmbedding);
            _output.WriteLine($"Query: '{altQuery}'");
            _output.WriteLine($"  Similarity: {altSimilarity:F6} ({altSimilarity * 100:F2}%)");
        }

        // Test individual key phrases
        _output.WriteLine("=== KEY PHRASE ANALYSIS ===");
        var keyPhrases = new[]
        {
            "Happy Little Dinosaurs",
            "how to win",
            "win the game",
            "50 points",
            "Escape Route",
            "last Dinosaur left"
        };

        foreach (var phrase in keyPhrases)
        {
            var phraseEmbedding = await embeddingService.GenerateEmbeddingAsync(phrase, kernel: null!, CancellationToken.None);
            var phraseSimilarity = EmbeddingExtensions.CosineSimilarity(queryEmbedding, phraseEmbedding);
            _output.WriteLine($"Phrase: '{phrase}'");
            _output.WriteLine($"  Similarity to Query: {phraseSimilarity:F6} ({phraseSimilarity * 100:F2}%)");
        }

        // Recommendations
        _output.WriteLine("=== DIAGNOSTIC RECOMMENDATIONS ===");
        
        if (similarity < 0.5)
        {
            _output.WriteLine("⚠️  LOW SIMILARITY DETECTED (<0.5)");
            _output.WriteLine("Possible Issues:");
            _output.WriteLine("1. Query and content use different vocabulary/phrasing");
            _output.WriteLine("2. Content is too long and dilutes specific meaning");
            _output.WriteLine("3. Category information not being indexed properly");
            _output.WriteLine("4. Embedding model not trained on this domain");
        }

        if (categorySimilarity > similarity)
        {
            _output.WriteLine("✓ Category has higher similarity than content");
            _output.WriteLine("  → Consider giving category text more weight in matching");
        }

        if (combinedSimilarity > similarity)
        {
            _output.WriteLine("✓ Combined text performs better");
            _output.WriteLine("  → Embedding category+content together improves matching");
        }

        _output.WriteLine("");
        _output.WriteLine("Suggested Improvements:");
        _output.WriteLine("1. Store embeddings for: Category, Content, and Category+Content separately");
        _output.WriteLine("2. Use weighted combination: Category (0.4) + Content (0.3) + Combined (0.3)");
        _output.WriteLine("3. Implement query expansion (add synonyms/variants)");
        _output.WriteLine("4. Consider chunking long content into smaller pieces");
        _output.WriteLine("5. Add domain-specific keywords to fragments");

        // Assert minimum acceptable similarity
        Assert.True(similarity > 0.3, 
            $"Similarity too low: {similarity:F6}. Expected >0.3 for related content.");
    }

    /// <summary>
    /// Tests embedding quality for short vs long content
    /// </summary>
    [Fact]
    public async Task DiagnoseContentLengthImpact()
    {
        // Arrange
        if (!File.Exists(ModelPath) || !File.Exists(VocabPath))
        {
            _output.WriteLine("SKIPPED: Model files not found");
            return;
        }

        var embeddingService = new SemanticEmbeddingService(ModelPath, VocabPath, EmbeddingDimension, debugMode: true);

        var query = "How to win Happy Little Dinosaurs";

        // Test different content lengths
        var shortContent = "Win by reaching 50 points or being the last dinosaur.";
        var mediumContent = "There are two ways to win: reach 50 points on your Escape Route or be the last Dinosaur left in the game.";
        var longContent = "There are two ways to win the game: Be the first Dinosaur to reach 50 points on your Escape Route. Be the last Dinosaur left in the game. Looks like you've successfully delayed your inevitable extinction. Unfortunately, all of your friends are now dead :( But on the bright side, you're a winner.";

        _output.WriteLine("=== CONTENT LENGTH IMPACT ===");
        _output.WriteLine($"Query: '{query}'");

        var queryEmbedding = await embeddingService.GenerateEmbeddingAsync(query, kernel: null!, CancellationToken.None);

        var testCases = new[]
        {
            ("Short (55 chars)", shortContent),
            ("Medium (105 chars)", mediumContent),
            ("Long (294 chars)", longContent)
        };

        foreach (var (label, content) in testCases)
        {
            var contentEmbedding = await embeddingService.GenerateEmbeddingAsync(content, kernel: null!, CancellationToken.None);
            var similarity = EmbeddingExtensions.CosineSimilarity(queryEmbedding, contentEmbedding);
            
            _output.WriteLine($"{label}:");
            _output.WriteLine($"  Content: {content}");
            _output.WriteLine($"  Similarity: {similarity:F6} ({similarity * 100:F2}%)");
        }
    }

    /// <summary>
    /// Tests if normalization is being applied correctly
    /// </summary>
    [Fact]
    public async Task DiagnoseNormalizationIssues()
    {
        // Arrange
        if (!File.Exists(ModelPath) || !File.Exists(VocabPath))
        {
            _output.WriteLine("SKIPPED: Model files not found");
            return;
        }

        var embeddingService = new SemanticEmbeddingService(ModelPath, VocabPath, EmbeddingDimension, debugMode: true);

        var testText = "Happy Little Dinosaurs";

        _output.WriteLine("=== NORMALIZATION ANALYSIS ===");
        _output.WriteLine($"Test Text: '{testText}'");

        var embedding = await embeddingService.GenerateEmbeddingAsync(testText, kernel: null!, CancellationToken.None);

        // Check if normalized
        var l2Norm = CalculateL2Norm(embedding);
        _output.WriteLine($"L2 Norm: {l2Norm:F6}");
        _output.WriteLine($"Is Normalized (≈1.0): {Math.Abs(l2Norm - 1.0) < 0.0001}");

        // Check for zero vectors
        var hasNonZero = embedding.Span.ToArray().Any(v => Math.Abs(v) > 0.0001);
        _output.WriteLine($"Has Non-Zero Values: {hasNonZero}");
        _output.WriteLine($"Min Value: {embedding.Span.ToArray().Min():F6}");
        _output.WriteLine($"Max Value: {embedding.Span.ToArray().Max():F6}");
        _output.WriteLine($"Mean Value: {embedding.Span.ToArray().Average():F6}");

        // Assert
        Assert.True(hasNonZero, "Embedding should contain non-zero values");
        Assert.InRange(l2Norm, 0.99, 1.01); // Should be normalized to ~1.0
    }

    /// <summary>
    /// Tests if category markers (## and #) affect embedding quality
    /// </summary>
    [Fact]
    public async Task DiagnoseCategoryMarkerImpact()
    {
        // Arrange
        if (!File.Exists(ModelPath) || !File.Exists(VocabPath))
        {
            _output.WriteLine("SKIPPED: Model files not found");
            return;
        }

        var embeddingService = new SemanticEmbeddingService(ModelPath, VocabPath, EmbeddingDimension, debugMode: true);

        var query = "How to win Happy Little Dinosaurs";

        _output.WriteLine("=== CATEGORY MARKER IMPACT ===");
        _output.WriteLine($"Query: '{query}'");

        var queryEmbedding = await embeddingService.GenerateEmbeddingAsync(query, kernel: null!, CancellationToken.None);

        var testVariants = new[]
        {
            ("With ## marker", "##Happy Little Dinosaurs - How to win"),
            ("Without markers", "Happy Little Dinosaurs - How to win"),
            ("Only game name", "Happy Little Dinosaurs"),
            ("Only topic", "How to win")
        };

        foreach (var (label, variant) in testVariants)
        {
            var variantEmbedding = await embeddingService.GenerateEmbeddingAsync(variant, kernel: null!, CancellationToken.None);
            var similarity = EmbeddingExtensions.CosineSimilarity(queryEmbedding, variantEmbedding);
            
            _output.WriteLine($"{label}:");
            _output.WriteLine($"  Text: '{variant}'");
            _output.WriteLine($"  Similarity: {similarity:F6} ({similarity * 100:F2}%)");
            _output.WriteLine("");
        }
    }

    // Helper method
    private static double CalculateL2Norm(ReadOnlyMemory<float> vector)
    {
        var sumOfSquares = 0.0;
        foreach (var value in vector.Span)
        {
            sumOfSquares += value * value;
        }
        return Math.Sqrt(sumOfSquares);
    }
}
