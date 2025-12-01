namespace Services.Language;

/// <summary>
/// Provides language-specific stop words (filler words) for query processing.
/// Supports Swedish and English with extensibility for more languages.
/// </summary>
public class LanguageStopWordsService : ILanguageStopWordsService
{
    // Swedish stop words for recycling/everyday queries
    private static readonly string[] SwedishStopWords = new[]
    {
        "hur", "var", "vad", "när", "varför", "vem", "vilken", "vilket",
        "ska", "kan", "måste", "bör", "sorterar", "sortera", "slänger", "slänga",
        "jag", "vi", "du", "ni", "man", "återvinna", "återvinner",
        "en", "ett", "den", "det", "de",
        "i", "på", "till", "från", "med", "av",
        "som", "för", "om", "åt"
    };
    
    // Swedish light stop words (only pure articles/prepositions)
    private static readonly string[] SwedishLightStopWords = new[]
    {
        "en", "ett", "den", "det", "de",
        "i", "på", "till", "från", "med", "av",
        "är", "var", "blev", "vara"
    };
    
    // English stop words for general queries
    private static readonly string[] EnglishStopWords = new[]
    {
        "the", "a", "an", "in", "on", "at", "by", "for", "with", "from",
        "is", "are", "was", "were", "be", "been", "being",
        "how", "what", "where", "when", "why", "who", "which",
        "do", "does", "did", "can", "could", "should", "would",
        "to", "of", "and", "or", "but"
    };
    
    // English light stop words (only articles/prepositions)
    private static readonly string[] EnglishLightStopWords = new[]
    {
        "the", "a", "an", "in", "on", "at", "by", "for", "with", "from",
        "is", "are", "was", "were", "be", "been", "being"
    };
    
    public string[] GetStopWords(string language)
    {
        return language?.ToLowerInvariant() switch
        {
            "swedish" or "svenska" or "sv" => SwedishStopWords,
            "english" or "engelska" or "en" => EnglishStopWords,
            _ => Array.Empty<string>() // Unknown language - no filtering
        };
    }
    
    public string[] GetLightStopWords(string language)
    {
        return language?.ToLowerInvariant() switch
        {
            "swedish" or "svenska" or "sv" => SwedishLightStopWords,
            "english" or "engelska" or "en" => EnglishLightStopWords,
            _ => Array.Empty<string>() // Unknown language - no filtering
        };
    }
}
