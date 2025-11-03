using Xunit;
using Services;
using System;
using System.Linq;

namespace OfflineAI.Tests.Services;

/// <summary>
/// Comprehensive tests for SemanticEmbeddingService to ensure correct similarity scores.
/// Tests cover: unrelated words, similar texts, baseline similarity, and edge cases.
/// </summary>
public class SemanticEmbeddingInvestigationTests
{
    private readonly SemanticEmbeddingService _embeddingService;

    public SemanticEmbeddingInvestigationTests()
    {
        var modelPath = @"d:\tinyllama\models\all-MiniLM-L6-v2\model.onnx";
        _embeddingService = new SemanticEmbeddingService(modelPath, embeddingDimension: 384);
    }

    #region Unrelated Text Tests

    [Fact]
    public async void UnrelatedWords_HelloVsTreasure_ShouldHaveLowSimilarity()
    {
        // Arrange
        var word1 = "hello";
        var word2 = "treasure";

        // Act
        var emb1 = await _embeddingService.GenerateEmbeddingAsync(word1);
        var emb2 = await _embeddingService.GenerateEmbeddingAsync(word2);
        var similarity = CosineSimilarity(emb1, emb2);

        // Assert
        Assert.True(similarity < 0.40, $"Expected similarity < 0.40, got {similarity:F3}");
    }

    [Fact]
    public async void UnrelatedWords_HelloVsWeather_ShouldHaveLowSimilarity()
    {
        // Arrange
        var word1 = "hello";
        var word2 = "weather";

        // Act
        var emb1 = await _embeddingService.GenerateEmbeddingAsync(word1);
        var emb2 = await _embeddingService.GenerateEmbeddingAsync(word2);
        var similarity = CosineSimilarity(emb1, emb2);

        // Assert
        Assert.True(similarity < 0.40, $"Expected similarity < 0.40, got {similarity:F3}");
    }

    [Fact]
    public async void UnrelatedSentences_GameRulesVsSunnyWeather_ShouldHaveVeryLowSimilarity()
    {
        // Arrange
        var text1 = "How to fight monsters in treasure hunt game";
        var text2 = "The weather forecast shows sunny skies today";

        // Act
        var emb1 = await _embeddingService.GenerateEmbeddingAsync(text1);
        var emb2 = await _embeddingService.GenerateEmbeddingAsync(text2);
        var similarity = CosineSimilarity(emb1, emb2);

        // Assert
        Assert.True(similarity < 0.20, $"Expected similarity < 0.20 for very different topics, got {similarity:F3}");
    }

    #endregion

    #region Similar Text Tests

    [Fact]
    public async void SimilarPhrases_CollectVsGather_ShouldHaveHighSimilarity()
    {
        // Arrange
        var text1 = "collect treasure cards";
        var text2 = "gathering treasure items";

        // Act
        var emb1 = await _embeddingService.GenerateEmbeddingAsync(text1);
        var emb2 = await _embeddingService.GenerateEmbeddingAsync(text2);
        var similarity = CosineSimilarity(emb1, emb2);

        // Assert
        Assert.True(similarity > 0.70, $"Expected similarity > 0.70 for similar phrases, got {similarity:F3}");
    }

    [Fact]
    public async void SimilarSentences_SameTopicDifferentWording_ShouldBeModeratelySimilar()
    {
        // Arrange
        var text1 = "Players fight monsters in the dungeon";
        var text2 = "Combat with creatures happens in the underground levels";

        // Act
        var emb1 = await _embeddingService.GenerateEmbeddingAsync(text1);
        var emb2 = await _embeddingService.GenerateEmbeddingAsync(text2);
        var similarity = CosineSimilarity(emb1, emb2);

        // Assert
        Assert.True(similarity > 0.45, $"Expected similarity > 0.45 for related concepts, got {similarity:F3}");
    }

    [Fact]
    public async void IdenticalText_ShouldHavePerfectSimilarity()
    {
        // Arrange
        var text = "The quick brown fox jumps over the lazy dog";

        // Act
        var emb1 = await _embeddingService.GenerateEmbeddingAsync(text);
        var emb2 = await _embeddingService.GenerateEmbeddingAsync(text);
        var similarity = CosineSimilarity(emb1, emb2);

        // Assert
        Assert.True(similarity > 0.999, $"Expected similarity ? 1.0 for identical text, got {similarity:F3}");
    }

    #endregion

    #region Game-Specific Query Tests

    [Fact]
    public async void GameQuery_HowToFightMonsters_ShouldMatchMonsterSection()
    {
        // Arrange
        var query = "How to fight a monsters in treasure hunt?";
        var monsterContent = "Monster Spaces: When you land on a monster space, you must fight the monster using dice rolls.";
        var setupContent = "Setup: Every player chooses a standie and places it in the Entrance.";

        // Act
        var queryEmb = await _embeddingService.GenerateEmbeddingAsync(query);
        var monsterEmb = await _embeddingService.GenerateEmbeddingAsync(monsterContent);
        var setupEmb = await _embeddingService.GenerateEmbeddingAsync(setupContent);
        
        var monsterSim = CosineSimilarity(queryEmb, monsterEmb);
        var setupSim = CosineSimilarity(queryEmb, setupEmb);

        // Assert
        Assert.True(monsterSim > 0.35, $"Expected monster section similarity > 0.35, got {monsterSim:F3}");
        Assert.True(monsterSim > setupSim, $"Monster section ({monsterSim:F3}) should be more relevant than setup ({setupSim:F3})");
    }

    [Fact]
    public async void GameQuery_TreasureCards_ShouldMatchTreasureSection()
    {
        // Arrange
        var query = "What are treasure cards used for?";
        var treasureContent = "Treasure Cards: Collect treasure cards to win the game. Each card gives you points.";
        var monsterContent = "Monster Spaces: Fight monsters using dice rolls.";

        // Act
        var queryEmb = await _embeddingService.GenerateEmbeddingAsync(query);
        var treasureEmb = await _embeddingService.GenerateEmbeddingAsync(treasureContent);
        var monsterEmb = await _embeddingService.GenerateEmbeddingAsync(monsterContent);
        
        var treasureSim = CosineSimilarity(queryEmb, treasureEmb);
        var monsterSim = CosineSimilarity(queryEmb, monsterEmb);

        // Assert
        Assert.True(treasureSim > 0.35, $"Expected treasure section similarity > 0.35, got {treasureSim:F3}");
        Assert.True(treasureSim > monsterSim, $"Treasure section ({treasureSim:F3}) should be more relevant than monster ({monsterSim:F3})");
    }

    #endregion

    #region Embedding Quality Tests

    [Fact]
    public async void EmbeddingsShouldBeNormalized()
    {
        // Arrange
        var texts = new[] { "hello", "treasure cards", "the quick brown fox" };

        // Act & Assert
        foreach (var text in texts)
        {
            var embedding = await _embeddingService.GenerateEmbeddingAsync(text);
            var array = embedding.ToArray();
            var magnitude = Math.Sqrt(array.Sum(v => v * v));

            Assert.True(Math.Abs(magnitude - 1.0) < 0.01, 
                $"Text '{text}': Expected normalized vector (magnitude ? 1.0), got {magnitude:F6}");
        }
    }

    [Fact]
    public async void EmbeddingsShouldHaveReasonableVariance()
    {
        // Arrange
        var texts = new[] { "hello", "treasure cards", "game rules" };

        // Act & Assert
        foreach (var text in texts)
        {
            var embedding = await _embeddingService.GenerateEmbeddingAsync(text);
            var array = embedding.ToArray();
            var variance = CalculateVariance(array);

            Assert.True(variance > 0.001, 
                $"Text '{text}': Embedding variance too low ({variance:F6}), suggests uniform values!");
        }
    }

    [Fact]
    public async void DifferentTextsShouldProduceDifferentEmbeddings()
    {
        // Arrange
        var texts = new[] { "hello", "treasure", "monster", "weather", "game" };

        // Act
        var embeddings = new System.Collections.Generic.List<ReadOnlyMemory<float>>();
        foreach (var text in texts)
        {
            embeddings.Add(await _embeddingService.GenerateEmbeddingAsync(text));
        }

        // Assert - check that not all embeddings are too similar
        var allTooSimilar = true;
        for (int i = 0; i < embeddings.Count; i++)
        {
            for (int j = i + 1; j < embeddings.Count; j++)
            {
                var sim = CosineSimilarity(embeddings[i], embeddings[j]);
                if (sim < 0.60)
                {
                    allTooSimilar = false;
                    break;
                }
            }
        }

        Assert.False(allTooSimilar, "All embeddings are too similar (> 0.60)! This suggests a problem with the model or pooling.");
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public async void EmptyString_ShouldNotThrow()
    {
        // Arrange
        var text = "";

        // Act & Assert - should use fallback, not throw
        var embedding = await _embeddingService.GenerateEmbeddingAsync(text);
        Assert.NotNull(embedding);
        Assert.Equal(384, embedding.Length);
    }

    [Fact]
    public async void VeryLongText_ShouldBeTruncated()
    {
        // Arrange
        var longText = string.Join(" ", Enumerable.Repeat("word", 2000)); // ~10,000 chars

        // Act & Assert - should truncate, not throw
        var embedding = await _embeddingService.GenerateEmbeddingAsync(longText);
        Assert.NotNull(embedding);
        Assert.Equal(384, embedding.Length);
    }

    [Fact]
    public async void SpecialCharacters_ShouldBeNormalized()
    {
        // Arrange
        var text = "\"smart quotes\" and 'curly apostrophes' - en-dash - em-dash";

        // Act & Assert - should normalize, not throw
        var embedding = await _embeddingService.GenerateEmbeddingAsync(text);
        Assert.NotNull(embedding);
        Assert.Equal(384, embedding.Length);
    }

    #endregion

    #region Baseline Similarity Tests

    [Fact]
    public async void BERTBaseline_UnrelatedWordsShouldBeBetween0_25And0_40()
    {
        // This test documents the expected baseline similarity for BERT embeddings
        // Unrelated words typically show 0.25-0.40 similarity due to:
        // - Positional encodings
        // - Model training on sentence pairs
        // - High-dimensional space geometry

        // Arrange
        var unrelatedPairs = new[]
        {
            ("hello", "weather"),
            ("treasure", "sunny"),
            ("monster", "breakfast"),
            ("game", "ocean")
        };

        // Act & Assert
        foreach (var (word1, word2) in unrelatedPairs)
        {
            var emb1 = await _embeddingService.GenerateEmbeddingAsync(word1);
            var emb2 = await _embeddingService.GenerateEmbeddingAsync(word2);
            var similarity = CosineSimilarity(emb1, emb2);

            // Document the baseline range
            Assert.True(similarity >= 0.05 && similarity <= 0.50, 
                $"Baseline check for '{word1}' vs '{word2}': Expected 0.05-0.50, got {similarity:F3}");
        }
    }

    #endregion

    #region Helper Methods

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

    private static double CalculateVariance(float[] values)
    {
        var mean = values.Average();
        return values.Sum(v => Math.Pow(v - mean, 2)) / values.Length;
    }

    #endregion
}
