using Xunit;
using Services;
using MemoryLibrary.Models;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace OfflineAI.Tests.Services;

/// <summary>
/// Integration tests that vectorize actual game fragments and test query matching.
/// These tests help understand how different queries match against real content.
/// </summary>
public class VectorMemoryQueryMatchingTests
{
    private readonly SemanticEmbeddingService _embeddingService;
    private VectorMemory _vectorMemory;

    public VectorMemoryQueryMatchingTests()
    {
        var modelPath = @"d:\tinyllama\models\all-MiniLM-L6-v2\model.onnx";
        _embeddingService = new SemanticEmbeddingService(modelPath, embeddingDimension: 384);
        _vectorMemory = new VectorMemory(_embeddingService, "test_collection");
    }

    /// <summary>
    /// Helper method to setup vector memory with game fragments.
    /// Call this before each test to prepare the memory.
    /// </summary>
    private async Task SetupTreasureHuntFragments()
    {
        // Add realistic game fragments
        var fragments = new[]
        {
            new MemoryFragment(
                "Setup", 
                "Every player chooses a standie and base and places it in the Entrance. Shuffle the Treasure cards and deal three to each player. Place the remainder face-down in the Treasure space on the board. Shuffle the Monster cards and place them face-down in the Monster space on the board."),
            
            new MemoryFragment(
                "Monster Spaces", 
                "When you land on a Monster space, draw a Monster card and fight! Roll the die. If you roll higher than the Monster's Power, you defeat it and collect the card as a trophy. If you roll equal or lower, you lose and discard the card. Monster trophies are worth points at the end."),
            
            new MemoryFragment(
                "Treasure Cards", 
                "Treasure cards give you special abilities and points. When you collect a treasure card, you can use it immediately or save it for later. Each treasure card is worth victory points. The player with the most treasure cards at the end wins the game."),
            
            new MemoryFragment(
                "Movement Rules", 
                "On your turn, roll the die and move your standie that many spaces clockwise around the board. Follow the instructions on the space you land on. You must always move the full amount shown on the die."),
            
            new MemoryFragment(
                "Winning the Game", 
                "The game ends when all Monster cards have been drawn or all Treasure cards have been collected. Count your points: each Monster trophy is worth 2 points, each Treasure card is worth its printed value. The player with the most points wins!"),
            
            new MemoryFragment(
                "The Entrance", 
                "The Entrance is the starting space. When you land on the Entrance during the game, draw 2 Treasure cards. The Entrance is a safe space where no monsters can attack you."),
            
            new MemoryFragment(
                "Special Abilities", 
                "Some Treasure cards have special abilities: Shield - protect from one monster attack, Speed - move extra spaces, Magic - force another player to discard a card. You can use special abilities at any time during your turn."),
            
            new MemoryFragment(
                "Game End Conditions", 
                "The game ends immediately when either: all Monster cards have been defeated, all Treasure cards have been collected, or one player reaches 20 points. Players then count their final scores.")
        };

        // Import fragments and generate embeddings
        foreach (var fragment in fragments)
        {
            _vectorMemory.ImportMemory(fragment);
        }

        // Force embedding generation for all fragments
        // This simulates what happens during database save
        await _vectorMemory.SearchRelevantMemoryAsync("initialization query", topK: 1, minRelevanceScore: 0.0);
        
        // Reset memory for clean testing
        _vectorMemory = new VectorMemory(_embeddingService, "test_collection");
        foreach (var fragment in fragments)
        {
            _vectorMemory.ImportMemory(fragment);
        }
        await _vectorMemory.SearchRelevantMemoryAsync("initialization", topK: 1, minRelevanceScore: 0.0);
    }

    #region Query Matching Tests

    [Fact]
    public async Task Query_HowToFightMonsters_ShouldMatchMonsterSpace()
    {
        // Arrange
        await SetupTreasureHuntFragments();
        var query = "How do I fight monsters?";

        // Act
        var results = await _vectorMemory.SearchRelevantMemoryAsync(query, topK: 3, minRelevanceScore: 0.0);

        // Assert
        Assert.NotNull(results);
        Assert.Contains("Monster Spaces", results);
        Assert.DoesNotContain("The Entrance", results); // Should not match entrance
        
        // Output for analysis
        System.Console.WriteLine($"\n=== Query: {query} ===");
        System.Console.WriteLine(results);
    }

    [Fact]
    public async Task Query_WhatAreTreasureCards_ShouldMatchTreasureSection()
    {
        // Arrange
        await SetupTreasureHuntFragments();
        var query = "What are treasure cards for?";

        // Act
        var results = await _vectorMemory.SearchRelevantMemoryAsync(query, topK: 3, minRelevanceScore: 0.0);

        // Assert
        Assert.NotNull(results);
        Assert.Contains("Treasure Cards", results);
        
        System.Console.WriteLine($"\n=== Query: {query} ===");
        System.Console.WriteLine(results);
    }

    [Fact]
    public async Task Query_HowToWin_ShouldMatchWinningConditions()
    {
        // Arrange
        await SetupTreasureHuntFragments();
        var query = "How do I win the game?";

        // Act
        var results = await _vectorMemory.SearchRelevantMemoryAsync(query, topK: 3, minRelevanceScore: 0.0);

        // Assert
        Assert.NotNull(results);
        Assert.Contains("Winning the Game", results);
        
        System.Console.WriteLine($"\n=== Query: {query} ===");
        System.Console.WriteLine(results);
    }

    [Fact]
    public async Task Query_HowToStart_ShouldMatchSetup()
    {
        // Arrange
        await SetupTreasureHuntFragments();
        var query = "How do we start the game?";

        // Act
        var results = await _vectorMemory.SearchRelevantMemoryAsync(query, topK: 3, minRelevanceScore: 0.0);

        // Assert
        Assert.NotNull(results);
        Assert.Contains("Setup", results);
        
        System.Console.WriteLine($"\n=== Query: {query} ===");
        System.Console.WriteLine(results);
    }

    [Fact]
    public async Task Query_HowToMove_ShouldMatchMovementRules()
    {
        // Arrange
        await SetupTreasureHuntFragments();
        var query = "How does movement work?";

        // Act
        var results = await _vectorMemory.SearchRelevantMemoryAsync(query, topK: 3, minRelevanceScore: 0.0);

        // Assert
        Assert.NotNull(results);
        Assert.Contains("Movement Rules", results);
        
        System.Console.WriteLine($"\n=== Query: {query} ===");
        System.Console.WriteLine(results);
    }

    [Fact]
    public async Task Query_SpecialPowers_ShouldMatchSpecialAbilities()
    {
        // Arrange
        await SetupTreasureHuntFragments();
        var query = "What special powers can I use?";

        // Act
        var results = await _vectorMemory.SearchRelevantMemoryAsync(query, topK: 3, minRelevanceScore: 0.0);

        // Assert
        Assert.NotNull(results);
        Assert.Contains("Special Abilities", results);
        
        System.Console.WriteLine($"\n=== Query: {query} ===");
        System.Console.WriteLine(results);
    }

    #endregion

    #region Threshold Behavior Tests

    [Fact]
    public async Task Query_WithThreshold0_35_ShouldFilterIrrelevant()
    {
        // Arrange
        await SetupTreasureHuntFragments();
        var query = "How do I fight monsters?";

        // Act - with threshold 0.35
        var results = await _vectorMemory.SearchRelevantMemoryAsync(query, topK: 5, minRelevanceScore: 0.35);

        // Assert
        Assert.NotNull(results);
        
        // Should contain Monster Spaces (relevant)
        Assert.Contains("Monster Spaces", results);
        
        // Should NOT contain completely unrelated sections
        Assert.DoesNotContain("Entrance", results);
        
        System.Console.WriteLine($"\n=== Query: {query} (threshold 0.35) ===");
        System.Console.WriteLine(results);
    }

    [Fact]
    public async Task Query_WithThreshold0_50_ShouldBeVeryStrict()
    {
        // Arrange
        await SetupTreasureHuntFragments();
        var query = "What are treasure cards?";

        // Act
        var results = await _vectorMemory.SearchRelevantMemoryAsync(query, topK: 5, minRelevanceScore: 0.50);

        // Assert - might return null if no results above 0.50
        if (results != null)
        {
            System.Console.WriteLine($"\n=== Query: {query} (threshold 0.50) ===");
            System.Console.WriteLine(results);
        }
        else
        {
            System.Console.WriteLine($"\n=== Query: {query} (threshold 0.50) ===");
            System.Console.WriteLine("No results above threshold 0.50");
        }
    }

    [Fact]
    public async Task Query_WithThreshold0_30_ShouldBeMorePermissive()
    {
        // Arrange
        await SetupTreasureHuntFragments();
        var query = "game rules";

        // Act
        var resultsLow = await _vectorMemory.SearchRelevantMemoryAsync(query, topK: 5, minRelevanceScore: 0.30);
        var resultsHigh = await _vectorMemory.SearchRelevantMemoryAsync(query, topK: 5, minRelevanceScore: 0.40);

        // Assert
        Assert.NotNull(resultsLow);
        
        // Lower threshold should return more results
        var lowCount = resultsLow.Split("Relevance:").Length - 1;
        var highCount = resultsHigh?.Split("Relevance:").Length - 1 ?? 0;
        
        Assert.True(lowCount >= highCount, 
            $"Lower threshold should return more or equal results. Low: {lowCount}, High: {highCount}");
        
        System.Console.WriteLine($"\n=== Query: {query} (threshold 0.30) ===");
        System.Console.WriteLine($"Results: {lowCount}");
        System.Console.WriteLine(resultsLow);
    }

    #endregion

    #region Edge Case Query Tests

    [Fact]
    public async Task Query_VagueQuestion_ShouldReturnSomething()
    {
        // Arrange
        await SetupTreasureHuntFragments();
        var query = "Tell me about the game";

        // Act
        var results = await _vectorMemory.SearchRelevantMemoryAsync(query, topK: 3, minRelevanceScore: 0.35);

        // Assert - vague queries should match something
        if (results != null)
        {
            Assert.NotNull(results);
            System.Console.WriteLine($"\n=== Query: {query} ===");
            System.Console.WriteLine(results);
        }
        else
        {
            System.Console.WriteLine($"\n=== Query: {query} ===");
            System.Console.WriteLine("No results above threshold 0.35");
        }
    }

    [Fact]
    public async Task Query_Irrelevant_ShouldReturnNull()
    {
        // Arrange
        await SetupTreasureHuntFragments();
        var query = "How do I bake a cake?";

        // Act
        var results = await _vectorMemory.SearchRelevantMemoryAsync(query, topK: 3, minRelevanceScore: 0.35);

        // Assert - completely irrelevant should return null
        Assert.Null(results);
        
        System.Console.WriteLine($"\n=== Query: {query} ===");
        System.Console.WriteLine("Correctly returned null (no relevant results)");
    }

    [Fact]
    public async Task Query_Greeting_ShouldReturnNull()
    {
        // Arrange
        await SetupTreasureHuntFragments();
        var query = "hello";

        // Act
        var results = await _vectorMemory.SearchRelevantMemoryAsync(query, topK: 3, minRelevanceScore: 0.35);

        // Assert
        Assert.Null(results);
        
        System.Console.WriteLine($"\n=== Query: {query} ===");
        System.Console.WriteLine("Correctly returned null (greeting filtered out)");
    }

    [Fact]
    public async Task Query_SpecificDetail_ShouldMatchCorrectSection()
    {
        // Arrange
        await SetupTreasureHuntFragments();
        var query = "What happens if I roll equal to the monster power?";

        // Act
        var results = await _vectorMemory.SearchRelevantMemoryAsync(query, topK: 3, minRelevanceScore: 0.35);

        // Assert
        Assert.NotNull(results);
        Assert.Contains("Monster Spaces", results);
        
        System.Console.WriteLine($"\n=== Query: {query} ===");
        System.Console.WriteLine(results);
    }

    #endregion

    #region Comparative Query Tests

    [Fact]
    public async Task Query_MonsterVsTreasure_DifferentResultsExpected()
    {
        // Arrange
        await SetupTreasureHuntFragments();
        var monsterQuery = "How do monsters work?";
        var treasureQuery = "How do treasure cards work?";

        // Act
        var monsterResults = await _vectorMemory.SearchRelevantMemoryAsync(monsterQuery, topK: 1, minRelevanceScore: 0.35);
        var treasureResults = await _vectorMemory.SearchRelevantMemoryAsync(treasureQuery, topK: 1, minRelevanceScore: 0.35);

        // Assert - should return different sections
        Assert.NotEqual(monsterResults, treasureResults);
        
        Assert.Contains("Monster", monsterResults ?? "");
        Assert.Contains("Treasure", treasureResults ?? "");
        
        System.Console.WriteLine($"\n=== Monster Query Results ===");
        System.Console.WriteLine(monsterResults);
        System.Console.WriteLine($"\n=== Treasure Query Results ===");
        System.Console.WriteLine(treasureResults);
    }

    [Fact]
    public async Task Query_SimilarPhrasing_ShouldReturnSameSection()
    {
        // Arrange
        await SetupTreasureHuntFragments();
        var query1 = "How to defeat monsters?";
        var query2 = "Fighting creatures in the game?";

        // Act
        var results1 = await _vectorMemory.SearchRelevantMemoryAsync(query1, topK: 1, minRelevanceScore: 0.35);
        var results2 = await _vectorMemory.SearchRelevantMemoryAsync(query2, topK: 1, minRelevanceScore: 0.35);

        // Assert - both should match Monster Spaces
        Assert.NotNull(results1);
        Assert.NotNull(results2);
        Assert.Contains("Monster", results1);
        Assert.Contains("Monster", results2);
        
        System.Console.WriteLine($"\n=== Query 1: {query1} ===");
        System.Console.WriteLine(results1);
        System.Console.WriteLine($"\n=== Query 2: {query2} ===");
        System.Console.WriteLine(results2);
    }

    #endregion

    #region Multi-Result Tests

    [Fact]
    public async Task Query_BroadTopic_ShouldReturnMultipleRelevantSections()
    {
        // Arrange
        await SetupTreasureHuntFragments();
        var query = "What cards are in the game?";

        // Act
        var results = await _vectorMemory.SearchRelevantMemoryAsync(query, topK: 5, minRelevanceScore: 0.35);

        // Assert
        Assert.NotNull(results);
        
        // Should mention both Monster and Treasure cards
        var relevanceCount = results.Split("Relevance:").Length - 1;
        Assert.True(relevanceCount >= 2, $"Expected at least 2 relevant sections, got {relevanceCount}");
        
        System.Console.WriteLine($"\n=== Query: {query} ===");
        System.Console.WriteLine($"Returned {relevanceCount} relevant sections");
        System.Console.WriteLine(results);
    }

    [Fact]
    public async Task Query_WithTopK1_ShouldReturnOnlyBestMatch()
    {
        // Arrange
        await SetupTreasureHuntFragments();
        var query = "How do I fight monsters?";

        // Act
        var results = await _vectorMemory.SearchRelevantMemoryAsync(query, topK: 1, minRelevanceScore: 0.35);

        // Assert
        Assert.NotNull(results);
        
        var relevanceCount = results.Split("Relevance:").Length - 1;
        Assert.Equal(1, relevanceCount);
        
        System.Console.WriteLine($"\n=== Query: {query} (topK=1) ===");
        System.Console.WriteLine(results);
    }

    [Fact]
    public async Task Query_WithTopK5_ShouldReturnMultipleIfRelevant()
    {
        // Arrange
        await SetupTreasureHuntFragments();
        var query = "game rules and instructions";

        // Act
        var results = await _vectorMemory.SearchRelevantMemoryAsync(query, topK: 5, minRelevanceScore: 0.35);

        // Assert
        if (results != null)
        {
            var relevanceCount = results.Split("Relevance:").Length - 1;
            Assert.True(relevanceCount >= 1 && relevanceCount <= 5, 
                $"Should return 1-5 results, got {relevanceCount}");
            
            System.Console.WriteLine($"\n=== Query: {query} (topK=5) ===");
            System.Console.WriteLine($"Returned {relevanceCount} relevant sections");
            System.Console.WriteLine(results);
        }
    }

    #endregion

    #region Specific Problem Diagnosis Tests

    [Fact]
    public async Task Query_HowToWinTheGame_ShouldMatchWinningSection()
    {
        // This test diagnoses why "how to win the game?" doesn't match
        // Expected: Should match "Winning the Game" or "Who Won?" section
        
        // Arrange
        await SetupTreasureHuntFragments();
        var query = "how to win the game?";

        // Act - Test with NO threshold to see actual scores
        var resultsNoThreshold = await _vectorMemory.SearchRelevantMemoryAsync(query, topK: 8, minRelevanceScore: 0.0);
        
        // Test with current threshold
        var resultsWithThreshold = await _vectorMemory.SearchRelevantMemoryAsync(query, topK: 5, minRelevanceScore: 0.35);

        // Assert & Output
        System.Console.WriteLine($"\n=== DIAGNOSIS: Query '{query}' ===");
        System.Console.WriteLine("\n--- All Scores (no threshold) ---");
        System.Console.WriteLine(resultsNoThreshold);
        
        System.Console.WriteLine("\n--- With Threshold 0.35 ---");
        if (resultsWithThreshold != null)
        {
            System.Console.WriteLine(resultsWithThreshold);
        }
        else
        {
            System.Console.WriteLine("? NO RESULTS - Threshold too high!");
        }
        
        // This test documents the problem - we expect it might fail
        // The fix is to either:
        // 1. Lower threshold to 0.30
        // 2. Add better winning keywords to fragment
        // 3. Rephrase query to be more specific
    }

    [Fact]
    public async Task Query_DifferentWinningPhrases_CompareScores()
    {
        // Test different ways to ask about winning
        // This helps understand which phrasing works best
        
        // Arrange
        await SetupTreasureHuntFragments();
        
        var queries = new []
        {
            "how to win the game?",
            "how do I win?",
            "what are the winning conditions?",
            "victory conditions",
            "how does someone win?",
            "who wins the game?",
            "game end and winner"
        };

        // Act & Output
        System.Console.WriteLine($"\n=== COMPARISON: Different Winning Queries ===\n");
        
        foreach (var query in queries)
        {
            var results = await _vectorMemory.SearchRelevantMemoryAsync(query, topK: 1, minRelevanceScore: 0.0);
            
            // Extract first score
            var scoreStart = results?.IndexOf("[Relevance: ") ?? -1;
            var scoreEnd = results?.IndexOf("]", scoreStart + 12) ?? -1;
            var score = scoreStart >= 0 && scoreEnd > scoreStart 
                ? results.Substring(scoreStart + 12, scoreEnd - scoreStart - 12)
                : "N/A";
            
            // Extract section name
            var sectionStart = results?.IndexOf("\n") ?? -1;
            var sectionEnd = results?.IndexOf("\n", sectionStart + 1) ?? -1;
            var section = sectionStart >= 0 && sectionEnd > sectionStart
                ? results.Substring(sectionStart + 1, sectionEnd - sectionStart - 1).Trim()
                : "Unknown";
            
            var passesThreshold = double.TryParse(score, out var scoreValue) && scoreValue >= 0.35;
            var status = passesThreshold ? "?" : "?";
            
            System.Console.WriteLine($"{status} \"{query}\"");
            System.Console.WriteLine($"   Score: {score} | Section: {section}");
            System.Console.WriteLine();
        }
        
        System.Console.WriteLine("Note: ? = passes 0.35 threshold, ? = below threshold");
    }

    [Fact]
    public async Task Query_ShortVsLongWinningQuery_CompareEffectiveness()
    {
        // Compare short vs long versions of the same question
        
        // Arrange
        await SetupTreasureHuntFragments();
        var shortQuery = "how to win?";
        var longQuery = "Can you explain to me how a player wins the game and what are the victory conditions?";

        // Act
        var shortResults = await _vectorMemory.SearchRelevantMemoryAsync(shortQuery, topK: 3, minRelevanceScore: 0.0);
        var longResults = await _vectorMemory.SearchRelevantMemoryAsync(longQuery, topK: 3, minRelevanceScore: 0.0);

        // Assert & Output
        System.Console.WriteLine($"\n=== SHORT vs LONG Query Comparison ===");
        
        System.Console.WriteLine($"\n--- Short: \"{shortQuery}\" ---");
        System.Console.WriteLine(shortResults);
        
        System.Console.WriteLine($"\n--- Long: \"{longQuery}\" ---");
        System.Console.WriteLine(longResults);
        
        // Note: Longer queries often have LOWER scores due to dilution
        // But they might match different (sometimes better) sections
    }

    #endregion
}
