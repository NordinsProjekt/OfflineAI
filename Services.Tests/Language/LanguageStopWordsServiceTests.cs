using FluentAssertions;
using Services.Language;

namespace Services.Tests.Language;

/// <summary>
/// Unit tests for LanguageStopWordsService to verify language-specific stop word filtering.
/// Tests both Swedish and English stop words, as well as light filtering for phrase preservation.
/// </summary>
public class LanguageStopWordsServiceTests
{
    private readonly ILanguageStopWordsService _service;

    public LanguageStopWordsServiceTests()
    {
        _service = new LanguageStopWordsService();
    }

    #region Swedish Stop Words Tests

    [Fact]
    public void GetStopWords_Swedish_ReturnsSwedishStopWords()
    {
        // Act
        var stopWords = _service.GetStopWords("Swedish");

        // Assert
        stopWords.Should().NotBeEmpty();
        stopWords.Should().Contain("hur");
        stopWords.Should().Contain("sorterar");
        stopWords.Should().Contain("jag");
        stopWords.Should().Contain("en");
        stopWords.Should().Contain("på");
    }

    [Fact]
    public void GetStopWords_Svenska_ReturnsSwedishStopWords()
    {
        // Act - Swedish native name
        var stopWords = _service.GetStopWords("svenska");

        // Assert
        stopWords.Should().NotBeEmpty();
        stopWords.Should().Contain("hur");
        stopWords.Should().Contain("vad");
        stopWords.Should().Contain("när");
    }

    [Fact]
    public void GetStopWords_Sv_ReturnsSwedishStopWords()
    {
        // Act - ISO language code
        var stopWords = _service.GetStopWords("sv");

        // Assert
        stopWords.Should().NotBeEmpty();
        stopWords.Should().Contain("återvinna");
        stopWords.Should().Contain("sorterar");
    }

    [Fact]
    public void GetStopWords_Swedish_ContainsRecyclingVerbs()
    {
        // Act
        var stopWords = _service.GetStopWords("Swedish");

        // Assert - Recycling-specific verbs should be included
        stopWords.Should().Contain("sorterar");
        stopWords.Should().Contain("sortera");
        stopWords.Should().Contain("slänger");
        stopWords.Should().Contain("slänga");
        stopWords.Should().Contain("återvinna");
        stopWords.Should().Contain("återvinner");
    }

    [Fact]
    public void GetStopWords_Swedish_ContainsQuestionWords()
    {
        // Act
        var stopWords = _service.GetStopWords("Swedish");

        // Assert - Question words should be filtered
        stopWords.Should().Contain("hur");
        stopWords.Should().Contain("var");
        stopWords.Should().Contain("vad");
        stopWords.Should().Contain("när");
        stopWords.Should().Contain("varför");
        stopWords.Should().Contain("vem");
        stopWords.Should().Contain("vilken");
        stopWords.Should().Contain("vilket");
    }

    [Fact]
    public void GetStopWords_Swedish_ContainsModalVerbs()
    {
        // Act
        var stopWords = _service.GetStopWords("Swedish");

        // Assert - Modal verbs should be filtered
        stopWords.Should().Contain("ska");
        stopWords.Should().Contain("kan");
        stopWords.Should().Contain("måste");
        stopWords.Should().Contain("bör");
    }

    [Fact]
    public void GetStopWords_Swedish_ContainsPronouns()
    {
        // Act
        var stopWords = _service.GetStopWords("Swedish");

        // Assert
        stopWords.Should().Contain("jag");
        stopWords.Should().Contain("vi");
        stopWords.Should().Contain("du");
        stopWords.Should().Contain("ni");
        stopWords.Should().Contain("man");
    }

    [Fact]
    public void GetStopWords_Swedish_ContainsArticlesAndPrepositions()
    {
        // Act
        var stopWords = _service.GetStopWords("Swedish");

        // Assert
        stopWords.Should().Contain("en");
        stopWords.Should().Contain("ett");
        stopWords.Should().Contain("den");
        stopWords.Should().Contain("det");
        stopWords.Should().Contain("de");
        stopWords.Should().Contain("i");
        stopWords.Should().Contain("på");
        stopWords.Should().Contain("till");
        stopWords.Should().Contain("från");
        stopWords.Should().Contain("med");
        stopWords.Should().Contain("av");
    }

    #endregion

    #region English Stop Words Tests

    [Fact]
    public void GetStopWords_English_ReturnsEnglishStopWords()
    {
        // Act
        var stopWords = _service.GetStopWords("English");

        // Assert
        stopWords.Should().NotBeEmpty();
        stopWords.Should().Contain("the");
        stopWords.Should().Contain("a");
        stopWords.Should().Contain("how");
        stopWords.Should().Contain("what");
        stopWords.Should().Contain("is");
    }

    [Fact]
    public void GetStopWords_Engelska_ReturnsEnglishStopWords()
    {
        // Act - Swedish name for English
        var stopWords = _service.GetStopWords("engelska");

        // Assert
        stopWords.Should().NotBeEmpty();
        stopWords.Should().Contain("the");
        stopWords.Should().Contain("how");
    }

    [Fact]
    public void GetStopWords_En_ReturnsEnglishStopWords()
    {
        // Act - ISO language code
        var stopWords = _service.GetStopWords("en");

        // Assert
        stopWords.Should().NotBeEmpty();
        stopWords.Should().Contain("how");
        stopWords.Should().Contain("what");
    }

    [Fact]
    public void GetStopWords_English_ContainsArticles()
    {
        // Act
        var stopWords = _service.GetStopWords("English");

        // Assert
        stopWords.Should().Contain("the");
        stopWords.Should().Contain("a");
        stopWords.Should().Contain("an");
    }

    [Fact]
    public void GetStopWords_English_ContainsPrepositions()
    {
        // Act
        var stopWords = _service.GetStopWords("English");

        // Assert
        stopWords.Should().Contain("in");
        stopWords.Should().Contain("on");
        stopWords.Should().Contain("at");
        stopWords.Should().Contain("by");
        stopWords.Should().Contain("for");
        stopWords.Should().Contain("with");
        stopWords.Should().Contain("from");
        stopWords.Should().Contain("to");
        stopWords.Should().Contain("of");
    }

    [Fact]
    public void GetStopWords_English_ContainsQuestionWords()
    {
        // Act
        var stopWords = _service.GetStopWords("English");

        // Assert
        stopWords.Should().Contain("how");
        stopWords.Should().Contain("what");
        stopWords.Should().Contain("where");
        stopWords.Should().Contain("when");
        stopWords.Should().Contain("why");
        stopWords.Should().Contain("who");
        stopWords.Should().Contain("which");
    }

    [Fact]
    public void GetStopWords_English_ContainsModalVerbs()
    {
        // Act
        var stopWords = _service.GetStopWords("English");

        // Assert
        stopWords.Should().Contain("do");
        stopWords.Should().Contain("does");
        stopWords.Should().Contain("did");
        stopWords.Should().Contain("can");
        stopWords.Should().Contain("could");
        stopWords.Should().Contain("should");
        stopWords.Should().Contain("would");
    }

    [Fact]
    public void GetStopWords_English_ContainsAuxiliaryVerbs()
    {
        // Act
        var stopWords = _service.GetStopWords("English");

        // Assert
        stopWords.Should().Contain("is");
        stopWords.Should().Contain("are");
        stopWords.Should().Contain("was");
        stopWords.Should().Contain("were");
        stopWords.Should().Contain("be");
        stopWords.Should().Contain("been");
        stopWords.Should().Contain("being");
    }

    #endregion

    #region Light Stop Words Tests

    [Fact]
    public void GetLightStopWords_Swedish_ReturnsFewerStopWords()
    {
        // Act
        var fullStopWords = _service.GetStopWords("Swedish");
        var lightStopWords = _service.GetLightStopWords("Swedish");

        // Assert
        lightStopWords.Should().NotBeEmpty();
        lightStopWords.Length.Should().BeLessThan(fullStopWords.Length);
    }

    [Fact]
    public void GetLightStopWords_Swedish_OnlyContainsArticlesAndPrepositions()
    {
        // Act
        var lightStopWords = _service.GetLightStopWords("Swedish");

        // Assert - Should only contain pure grammatical words
        lightStopWords.Should().Contain("en");
        lightStopWords.Should().Contain("ett");
        lightStopWords.Should().Contain("i");
        lightStopWords.Should().Contain("på");
        
        // Should NOT contain verbs or question words
        lightStopWords.Should().NotContain("sorterar");
        lightStopWords.Should().NotContain("hur");
        lightStopWords.Should().NotContain("vad");
    }

    [Fact]
    public void GetLightStopWords_English_ReturnsFewerStopWords()
    {
        // Act
        var fullStopWords = _service.GetStopWords("English");
        var lightStopWords = _service.GetLightStopWords("English");

        // Assert
        lightStopWords.Should().NotBeEmpty();
        lightStopWords.Length.Should().BeLessThan(fullStopWords.Length);
    }

    [Fact]
    public void GetLightStopWords_English_OnlyContainsArticlesAndPrepositions()
    {
        // Act
        var lightStopWords = _service.GetLightStopWords("English");

        // Assert - Should only contain pure grammatical words
        lightStopWords.Should().Contain("the");
        lightStopWords.Should().Contain("a");
        lightStopWords.Should().Contain("in");
        lightStopWords.Should().Contain("on");
        
        // Should NOT contain question words or modal verbs
        lightStopWords.Should().NotContain("how");
        lightStopWords.Should().NotContain("what");
        lightStopWords.Should().NotContain("can");
        lightStopWords.Should().NotContain("should");
    }

    #endregion

    #region Unknown Language Tests

    [Fact]
    public void GetStopWords_UnknownLanguage_ReturnsEmptyArray()
    {
        // Act
        var stopWords = _service.GetStopWords("Klingon");

        // Assert
        stopWords.Should().BeEmpty();
    }

    [Fact]
    public void GetStopWords_NullLanguage_ReturnsEmptyArray()
    {
        // Act
        var stopWords = _service.GetStopWords(null);

        // Assert
        stopWords.Should().BeEmpty();
    }

    [Fact]
    public void GetStopWords_EmptyLanguage_ReturnsEmptyArray()
    {
        // Act
        var stopWords = _service.GetStopWords("");

        // Assert
        stopWords.Should().BeEmpty();
    }

    [Fact]
    public void GetLightStopWords_UnknownLanguage_ReturnsEmptyArray()
    {
        // Act
        var stopWords = _service.GetLightStopWords("Klingon");

        // Assert
        stopWords.Should().BeEmpty();
    }

    #endregion

    #region Case Insensitivity Tests

    [Fact]
    public void GetStopWords_CaseInsensitive_ReturnsSwedishStopWords()
    {
        // Act
        var lower = _service.GetStopWords("swedish");
        var upper = _service.GetStopWords("SWEDISH");
        var mixed = _service.GetStopWords("SwEdIsH");

        // Assert
        lower.Should().BeEquivalentTo(upper);
        lower.Should().BeEquivalentTo(mixed);
    }

    [Fact]
    public void GetStopWords_CaseInsensitive_ReturnsEnglishStopWords()
    {
        // Act
        var lower = _service.GetStopWords("english");
        var upper = _service.GetStopWords("ENGLISH");
        var mixed = _service.GetStopWords("EnGlIsH");

        // Assert
        lower.Should().BeEquivalentTo(upper);
        lower.Should().BeEquivalentTo(mixed);
    }

    #endregion

    #region Real-World Query Simulation Tests

    [Fact]
    public void FilterSwedishQuery_HurSorterarJagAdapter_ShouldKeepAdapter()
    {
        // Arrange
        var query = "Hur sorterar jag adapter?";
        var stopWords = _service.GetStopWords("Swedish");

        // Act
        var words = query.ToLowerInvariant()
            .Replace("?", "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        var filtered = words.Where(w => !stopWords.Contains(w)).ToList();

        // Assert
        filtered.Should().ContainSingle();
        filtered.Should().Contain("adapter");
    }

    [Fact]
    public void FilterSwedishQuery_VarKanJagLamnaBatterier_ShouldKeepBatterier()
    {
        // Arrange
        var query = "Var kan jag lämna batterier?";
        var stopWords = _service.GetStopWords("Swedish");

        // Act
        var words = query.ToLowerInvariant()
            .Replace("?", "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        var filtered = words.Where(w => !stopWords.Contains(w)).ToList();

        // Assert
        filtered.Should().Contain("lämna");
        filtered.Should().Contain("batterier");
    }

    [Fact]
    public void FilterEnglishQuery_HowDoIWin_WithLightStopWords_ShouldKeepPhrase()
    {
        // Arrange
        var query = "How do I win?";
        var lightStopWords = _service.GetLightStopWords("English");

        // Act
        var words = query.ToLowerInvariant()
            .Replace("?", "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        var filtered = words.Where(w => !lightStopWords.Contains(w)).ToList();

        // Assert - Light filtering should preserve "how", "do", "win"
        filtered.Should().Contain("how");
        filtered.Should().Contain("do");
        filtered.Should().Contain("win");
    }

    [Fact]
    public void FilterEnglishQuery_HowDoIWin_WithFullStopWords_ShouldKeepWin()
    {
        // Arrange
        var query = "How do I win?";
        var fullStopWords = _service.GetStopWords("English");

        // Act
        var words = query.ToLowerInvariant()
            .Replace("?", "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        var filtered = words.Where(w => !fullStopWords.Contains(w)).ToList();

        // Assert - Full filtering removes "how", "do" but keeps "i" and "win"
        // Note: "i" is not in the stop words list as it's content-bearing in English
        filtered.Should().HaveCount(2);
        filtered.Should().Contain("i");
        filtered.Should().Contain("win");
    }

    [Fact]
    public void FilterEnglishQuery_HowToPlayMunchkin_WithLightStopWords_ShouldPreservePhrase()
    {
        // Arrange
        var query = "How to play Munchkin?";
        var lightStopWords = _service.GetLightStopWords("English");

        // Act
        var words = query.ToLowerInvariant()
            .Replace("?", "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        var filtered = words.Where(w => !lightStopWords.Contains(w)).ToList();

        // Assert - "how to play" phrase should be mostly preserved
        filtered.Should().Contain("how");
        filtered.Should().Contain("play");
        filtered.Should().Contain("munchkin");
    }

    #endregion

    #region Performance Tests

    [Fact]
    public void GetStopWords_CalledMultipleTimes_ShouldBeFast()
    {
        // Arrange
        var iterations = 10000;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        for (int i = 0; i < iterations; i++)
        {
            _service.GetStopWords("Swedish");
            _service.GetStopWords("English");
        }

        stopwatch.Stop();

        // Assert - Should be very fast (static arrays)
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(100, 
            "Stop words lookup should be near-instant for static arrays");
    }

    #endregion

    #region Consistency Tests

    [Fact]
    public void GetStopWords_Swedish_ShouldBeConsistentAcrossCalls()
    {
        // Act
        var first = _service.GetStopWords("Swedish");
        var second = _service.GetStopWords("Swedish");
        var third = _service.GetStopWords("swedish");

        // Assert
        first.Should().BeEquivalentTo(second);
        first.Should().BeEquivalentTo(third);
    }

    [Fact]
    public void GetStopWords_English_ShouldBeConsistentAcrossCalls()
    {
        // Act
        var first = _service.GetStopWords("English");
        var second = _service.GetStopWords("English");
        var third = _service.GetStopWords("english");

        // Assert
        first.Should().BeEquivalentTo(second);
        first.Should().BeEquivalentTo(third);
    }

    #endregion
}
