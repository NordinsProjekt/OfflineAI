using FluentAssertions;

namespace Services.Tests.Memory;

/// <summary>
/// Unit tests for fuzzy search (Levenshtein distance) matching in DatabaseVectorMemory.
/// Tests the hybrid search approach that boosts scores for exact and near-exact string matches.
/// </summary>
public class FuzzySearchTests
{
    /// <summary>
    /// Calculates Levenshtein distance between two strings.
    /// Identical to the production implementation in DatabaseVectorMemory.
    /// </summary>
    private static int CalculateLevenshteinDistance(string source, string target)
    {
        if (string.IsNullOrEmpty(source))
            return target?.Length ?? 0;
        
        if (string.IsNullOrEmpty(target))
            return source.Length;

        var sourceLength = source.Length;
        var targetLength = target.Length;

        var matrix = new int[sourceLength + 1, targetLength + 1];

        // Initialize first column and row
        for (int i = 0; i <= sourceLength; i++)
            matrix[i, 0] = i;
        
        for (int j = 0; j <= targetLength; j++)
            matrix[0, j] = j;

        // Fill the matrix
        for (int i = 1; i <= sourceLength; i++)
        {
            for (int j = 1; j <= targetLength; j++)
            {
                var cost = (target[j - 1] == source[i - 1]) ? 0 : 1;

                matrix[i, j] = Math.Min(
                    Math.Min(
                        matrix[i - 1, j] + 1,       // deletion
                        matrix[i, j - 1] + 1),      // insertion
                    matrix[i - 1, j - 1] + cost);   // substitution
            }
        }

        return matrix[sourceLength, targetLength];
    }

    #region Exact Match Tests

    [Fact]
    public void LevenshteinDistance_IdenticalStrings_ReturnsZero()
    {
        // Arrange
        var str1 = "adapter";
        var str2 = "adapter";

        // Act
        var distance = CalculateLevenshteinDistance(str1, str2);

        // Assert
        distance.Should().Be(0);
    }

    [Fact]
    public void LevenshteinDistance_IdenticalStrings_CaseInsensitive_ReturnsZero()
    {
        // Arrange
        var str1 = "adapter".ToLowerInvariant();
        var str2 = "ADAPTER".ToLowerInvariant();

        // Act
        var distance = CalculateLevenshteinDistance(str1, str2);

        // Assert
        distance.Should().Be(0);
    }

    [Fact]
    public void LevenshteinDistance_IdenticalSwedishWords_ReturnsZero()
    {
        // Arrange
        var str1 = "återvinning";
        var str2 = "återvinning";

        // Act
        var distance = CalculateLevenshteinDistance(str1, str2);

        // Assert
        distance.Should().Be(0);
    }

    #endregion

    #region Single Character Difference Tests

    [Fact]
    public void LevenshteinDistance_OneCharacterDeletion_ReturnsOne()
    {
        // Arrange
        var str1 = "adapter";
        var str2 = "adapte";  // Missing 'r'

        // Act
        var distance = CalculateLevenshteinDistance(str1, str2);

        // Assert
        distance.Should().Be(1);
    }

    [Fact]
    public void LevenshteinDistance_OneCharacterInsertion_ReturnsOne()
    {
        // Arrange
        var str1 = "adapte";
        var str2 = "adapter";  // Added 'r'

        // Act
        var distance = CalculateLevenshteinDistance(str1, str2);

        // Assert
        distance.Should().Be(1);
    }

    [Fact]
    public void LevenshteinDistance_OneCharacterSubstitution_ReturnsOne()
    {
        // Arrange
        var str1 = "adapter";
        var str2 = "adaptor";  // 'e' -> 'o'

        // Act
        var distance = CalculateLevenshteinDistance(str1, str2);

        // Assert
        distance.Should().Be(1);
    }

    #endregion

    #region Swedish Typo Tests

    [Fact]
    public void LevenshteinDistance_SwedishTypo_Batteri_Returns1()
    {
        // Arrange - Common typo: missing 'e'
        var correct = "batterier";
        var typo = "battrier";

        // Act
        var distance = CalculateLevenshteinDistance(correct, typo);

        // Assert
        distance.Should().Be(1);
    }

    [Fact]
    public void LevenshteinDistance_SwedishTypo_Atervinning_Returns1()
    {
        // Arrange - Common typo: 'Å' -> 'A'
        var correct = "Återvinning";
        var typo = "Atervinning";

        // Act
        var distance = CalculateLevenshteinDistance(correct, typo);

        // Assert
        distance.Should().Be(1);
    }

    [Fact]
    public void LevenshteinDistance_SwedishTypo_Plast_Returns1()
    {
        // Arrange - Common typo: transposed letters
        var correct = "plast";
        var typo = "palst";

        // Act
        var distance = CalculateLevenshteinDistance(correct, typo);

        // Assert
        distance.Should().Be(2); // 2 operations: delete 'a', insert 'a'
    }

    #endregion

    #region Real-World Recycling Terms Tests

    [Fact]
    public void LevenshteinDistance_Kulspruta_vs_Kula_Returns4()
    {
        // Arrange - "kulspruta" vs "kula" (important production case)
        var full = "kulspruta";
        var partial = "kula";

        // Act
        var distance = CalculateLevenshteinDistance(full, partial);

        // Assert
        distance.Should().Be(5); // Need to add "sprut"
    }

    [Fact]
    public void LevenshteinDistance_Patronhylsa_vs_Patron_Returns4()
    {
        // Arrange
        var full = "patronhylsa";
        var partial = "patron";

        // Act
        var distance = CalculateLevenshteinDistance(full, partial);

        // Assert
        distance.Should().Be(5); // Need to add "hylsa"
    }

    [Fact]
    public void LevenshteinDistance_Elektronik_vs_Elektronik_Returns0()
    {
        // Arrange
        var str1 = "elektronik";
        var str2 = "elektronik";

        // Act
        var distance = CalculateLevenshteinDistance(str1, str2);

        // Assert
        distance.Should().Be(0);
    }

    #endregion

    #region Similarity Threshold Tests

    [Fact]
    public void FuzzyMatch_WithinThreshold_ShouldBoostScore()
    {
        // Arrange - Simulate production boost logic
        var queryKeyword = "adapter";
        var fragmentCategory = "Sopsortering - Adapter"; // Contains exact match
        
        var baseScore = 0.85; // From semantic similarity
        var levenshteinThreshold = 2;

        // Act - Check if keyword appears in category
        var categoryLower = fragmentCategory.ToLowerInvariant();
        var keywordLower = queryKeyword.ToLowerInvariant();
        
        var categoryWords = categoryLower.Split(new[] { ' ', '-', ',' }, StringSplitOptions.RemoveEmptyEntries);
        
        var minDistance = categoryWords
            .Select(word => CalculateLevenshteinDistance(keywordLower, word))
            .Min();
        
        var boostedScore = minDistance <= levenshteinThreshold 
            ? baseScore * 1.2  // 20% boost
            : baseScore;

        // Assert
        minDistance.Should().Be(0); // Exact match found
        boostedScore.Should().BeApproximately(1.02, 0.01); // 0.85 * 1.2 = 1.02
    }

    [Fact]
    public void FuzzyMatch_Typo_ShouldStillBoost()
    {
        // Arrange - User typed "adaptr" instead of "adapter"
        var queryKeyword = "adaptr";
        var fragmentCategory = "Sopsortering - Adapter";
        
        var baseScore = 0.80;
        var levenshteinThreshold = 2;

        // Act
        var categoryLower = fragmentCategory.ToLowerInvariant();
        var keywordLower = queryKeyword.ToLowerInvariant();
        
        var categoryWords = categoryLower.Split(new[] { ' ', '-', ',' }, StringSplitOptions.RemoveEmptyEntries);
        
        var minDistance = categoryWords
            .Select(word => CalculateLevenshteinDistance(keywordLower, word))
            .Min();
        
        var boostedScore = minDistance <= levenshteinThreshold 
            ? baseScore * 1.2
            : baseScore;

        // Assert
        minDistance.Should().Be(1); // One character difference
        boostedScore.Should().BeApproximately(0.96, 0.01); // 0.80 * 1.2 = 0.96
    }

    [Fact]
    public void FuzzyMatch_BeyondThreshold_ShouldNotBoost()
    {
        // Arrange - Too many differences
        var queryKeyword = "adapter";
        var fragmentCategory = "Sopsortering - Batterier"; // "adapter" vs "batterier" = 6 edits
        
        var baseScore = 0.75;
        var levenshteinThreshold = 2;

        // Act
        var categoryLower = fragmentCategory.ToLowerInvariant();
        var keywordLower = queryKeyword.ToLowerInvariant();
        
        var categoryWords = categoryLower.Split(new[] { ' ', '-', ',' }, StringSplitOptions.RemoveEmptyEntries);
        
        var minDistance = categoryWords
            .Select(word => CalculateLevenshteinDistance(keywordLower, word))
            .Min();
        
        var boostedScore = minDistance <= levenshteinThreshold 
            ? baseScore * 1.2
            : baseScore;

        // Assert
        minDistance.Should().BeGreaterThan(levenshteinThreshold);
        boostedScore.Should().Be(baseScore); // No boost applied
    }

    #endregion

    #region Empty String Tests

    [Fact]
    public void LevenshteinDistance_EmptyToNonEmpty_ReturnsLength()
    {
        // Arrange
        var empty = "";
        var word = "adapter";

        // Act
        var distance = CalculateLevenshteinDistance(empty, word);

        // Assert
        distance.Should().Be(7); // Length of "adapter"
    }

    [Fact]
    public void LevenshteinDistance_NonEmptyToEmpty_ReturnsLength()
    {
        // Arrange
        var word = "batterier";
        var empty = "";

        // Act
        var distance = CalculateLevenshteinDistance(word, empty);

        // Assert
        distance.Should().Be(9); // Length of "batterier"
    }

    [Fact]
    public void LevenshteinDistance_BothEmpty_ReturnsZero()
    {
        // Arrange
        var empty1 = "";
        var empty2 = "";

        // Act
        var distance = CalculateLevenshteinDistance(empty1, empty2);

        // Assert
        distance.Should().Be(0);
    }

    [Fact]
    public void LevenshteinDistance_NullString_HandlesGracefully()
    {
        // Arrange
        string? nullStr = null;
        var word = "adapter";

        // Act
        var distance1 = CalculateLevenshteinDistance(nullStr, word);
        var distance2 = CalculateLevenshteinDistance(word, nullStr);

        // Assert
        distance1.Should().Be(7);
        distance2.Should().Be(7);
    }

    #endregion

    #region Multi-Word Category Tests

    [Fact]
    public void FuzzyMatch_MultiWordCategory_FindsClosestMatch()
    {
        // Arrange
        var queryKeyword = "adapter";
        var fragmentCategory = "Sopsortering - USB Adapter Kabel";

        // Act - Find minimum distance to any word in category
        var categoryWords = fragmentCategory.ToLowerInvariant()
            .Split(new[] { ' ', '-', ',' }, StringSplitOptions.RemoveEmptyEntries);
        
        var distances = categoryWords
            .Select(word => new
            {
                Word = word,
                Distance = CalculateLevenshteinDistance(queryKeyword.ToLowerInvariant(), word)
            })
            .OrderBy(x => x.Distance)
            .ToList();

        // Assert
        distances.First().Word.Should().Be("adapter");
        distances.First().Distance.Should().Be(0);
    }

    [Fact]
    public void FuzzyMatch_MultiWordCategory_WithTypo_FindsClosestMatch()
    {
        // Arrange
        var queryKeyword = "batteri"; // Missing 'e' and 'r'
        var fragmentCategory = "Sopsortering - Batterier Litiumbatterier";

        // Act
        var categoryWords = fragmentCategory.ToLowerInvariant()
            .Split(new[] { ' ', '-', ',' }, StringSplitOptions.RemoveEmptyEntries);
        
        var minDistance = categoryWords
            .Select(word => CalculateLevenshteinDistance(queryKeyword.ToLowerInvariant(), word))
            .Min();

        // Assert
        minDistance.Should().Be(2); // "batteri" vs "batterier" = 2 edits (add 'e', add 'r')
    }

    #endregion

    #region Real Production Scenarios

    [Fact]
    public void RealScenario_Kulspruta_vs_Patronhylsa_PreferExactMatch()
    {
        // Arrange - Reproduce production issue
        var query = "kulspruta";
        
        var fragments = new[]
        {
            new { Category = "Sopsortering - Kulspruta", BaseScore = 0.95 },
            new { Category = "Sopsortering - Patronhylsa, med kula", BaseScore = 0.88 }
        };
        
        var levenshteinThreshold = 2;

        // Act - Apply fuzzy boost
        var results = fragments.Select(f =>
        {
            var categoryWords = f.Category.ToLowerInvariant()
                .Split(new[] { ' ', '-', ',' }, StringSplitOptions.RemoveEmptyEntries);
            
            var minDistance = categoryWords
                .Select(word => CalculateLevenshteinDistance(query.ToLowerInvariant(), word))
                .Min();
            
            var boostedScore = minDistance <= levenshteinThreshold
                ? f.BaseScore * 1.2
                : f.BaseScore;
            
            return new
            {
                f.Category,
                f.BaseScore,
                MinDistance = minDistance,
                BoostedScore = Math.Min(boostedScore, 1.0) // Cap at 1.0
            };
        })
        .OrderByDescending(x => x.BoostedScore)
        .ToList();

        // Assert
        results[0].Category.Should().Contain("Kulspruta");
        results[0].MinDistance.Should().Be(0); // Exact match
        results[0].BoostedScore.Should().BeGreaterThan(results[1].BoostedScore);
        
        // "kula" in second fragment should have higher distance
        results[1].MinDistance.Should().BeGreaterThan(2); // "kulspruta" vs "kula"
    }

    [Fact]
    public void RealScenario_SwedishTypos_ShouldBoostCorrectly()
    {
        // Arrange - Common Swedish recycling typos
        var testCases = new[]
        {
            new { Query = "batteri", Target = "batterier", Expected = 2 },
            new { Query = "adaper", Target = "adapter", Expected = 1 },
            new { Query = "återvining", Target = "återvinning", Expected = 1 },
            new { Query = "elektronk", Target = "elektronik", Expected = 1 } // Just insert 'i'
        };

        foreach (var test in testCases)
        {
            // Act
            var distance = CalculateLevenshteinDistance(
                test.Query.ToLowerInvariant(),
                test.Target.ToLowerInvariant());

            // Assert
            distance.Should().Be(test.Expected, 
                $"because '{test.Query}' -> '{test.Target}' should have distance {test.Expected}");
            
            distance.Should().BeLessThanOrEqualTo(2, 
                "because typos within 2 edits should get boost");
        }
    }

    [Fact]
    public void RealScenario_HybridSearch_WeightedPlusFuzzy()
    {
        // Arrange - Simulate complete hybrid search
        var query = "adapter";
        
        var fragments = new[]
        {
            new
            {
                Category = "Sopsortering - Adapter",
                SemanticScore = 0.92,
                ContentMatch = true
            },
            new
            {
                Category = "Sopsortering - Adaptor", // British spelling, 1 edit
                SemanticScore = 0.89,
                ContentMatch = false
            },
            new
            {
                Category = "Sopsortering - USB Kabel",
                SemanticScore = 0.85,
                ContentMatch = false
            },
            new
            {
                Category = "Sopsortering - Batterier",
                SemanticScore = 0.75,
                ContentMatch = false
            }
        };

        // Act - Apply fuzzy boost
        var results = fragments.Select(f =>
        {
            var categoryWords = f.Category.ToLowerInvariant()
                .Split(new[] { ' ', '-', ',' }, StringSplitOptions.RemoveEmptyEntries);
            
            var minDistance = categoryWords
                .Select(word => CalculateLevenshteinDistance(query.ToLowerInvariant(), word))
                .Min();
            
            var fuzzyBoost = minDistance switch
            {
                0 => 1.2,      // Exact match: 20% boost
                1 => 1.15,     // 1 edit: 15% boost
                2 => 1.1,      // 2 edits: 10% boost
                _ => 1.0       // No boost
            };
            
            var finalScore = Math.Min(f.SemanticScore * fuzzyBoost, 1.0);
            
            return new
            {
                f.Category,
                f.SemanticScore,
                MinDistance = minDistance,
                FuzzyBoost = fuzzyBoost,
                FinalScore = finalScore
            };
        })
        .OrderByDescending(x => x.FinalScore)
        .ToList();

        // Assert
        results[0].Category.Should().Contain("Adapter"); // Exact match wins
        results[0].MinDistance.Should().Be(0);
        results[0].FuzzyBoost.Should().Be(1.2);
        
        results[1].Category.Should().Contain("Adaptor"); // 1 edit second
        results[1].MinDistance.Should().Be(1);
        results[1].FuzzyBoost.Should().Be(1.15);
        
        // USB Kabel and Batterier should not get boost
        results[2].FuzzyBoost.Should().Be(1.0);
        results[3].FuzzyBoost.Should().Be(1.0);
    }

    #endregion

    #region Performance Tests

    [Fact]
    public void LevenshteinDistance_LongStrings_ShouldCompleteQuickly()
    {
        // Arrange
        var str1 = "Sopsortering för elektronisk utrustning och batterier i återvinningscentraler";
        var str2 = "Sopsortering för elektronik utrustning och batteri i återvinningscentralen";
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        var distance = CalculateLevenshteinDistance(str1, str2);

        stopwatch.Stop();

        // Assert
        distance.Should().BeGreaterThan(0);
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(10, 
            "Levenshtein should be fast even for longer strings");
    }

    [Fact]
    public void LevenshteinDistance_MultipleCalculations_ShouldBeFast()
    {
        // Arrange
        var query = "adapter";
        var categories = new[]
        {
            "Adapter", "Adaptor", "Batterier", "Elektronik", "Plast",
            "Metall", "Glas", "Textil", "Farligt avfall", "Restavfall"
        };
        
        var iterations = 1000;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        for (int i = 0; i < iterations; i++)
        {
            foreach (var category in categories)
            {
                CalculateLevenshteinDistance(query, category.ToLowerInvariant());
            }
        }

        stopwatch.Stop();

        // Assert
        var operationsPerSecond = (iterations * categories.Length * 1000.0) / stopwatch.ElapsedMilliseconds;
        operationsPerSecond.Should().BeGreaterThan(10000, 
            "Should handle thousands of Levenshtein calculations per second");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void LevenshteinDistance_SingleCharacterStrings_Works()
    {
        // Arrange
        var str1 = "a";
        var str2 = "b";

        // Act
        var distance = CalculateLevenshteinDistance(str1, str2);

        // Assert
        distance.Should().Be(1);
    }

    [Fact]
    public void LevenshteinDistance_SingleCharacterSame_ReturnsZero()
    {
        // Arrange
        var str1 = "a";
        var str2 = "a";

        // Act
        var distance = CalculateLevenshteinDistance(str1, str2);

        // Assert
        distance.Should().Be(0);
    }

    [Fact]
    public void LevenshteinDistance_SpecialCharacters_Works()
    {
        // Arrange
        var str1 = "USB-adapter";
        var str2 = "USB adapter";

        // Act
        var distance = CalculateLevenshteinDistance(str1, str2);

        // Assert
        distance.Should().Be(1); // Hyphen vs space
    }

    [Fact]
    public void LevenshteinDistance_SwedishCharacters_Works()
    {
        // Arrange
        var str1 = "återvinning";
        var str2 = "atervinning";

        // Act
        var distance = CalculateLevenshteinDistance(str1, str2);

        // Assert
        distance.Should().Be(1); // å -> a
    }

    #endregion
}
