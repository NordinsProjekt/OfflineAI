namespace Services.Language;

/// <summary>
/// Provides language-specific stop words (filler words) for query processing.
/// </summary>
public interface ILanguageStopWordsService
{
    /// <summary>
    /// Gets the stop words for the specified language.
    /// </summary>
    /// <param name="language">Language code (e.g., "Swedish", "English")</param>
    /// <returns>Array of stop words for filtering, or empty array if language not supported</returns>
    string[] GetStopWords(string language);
    
    /// <summary>
    /// Gets the light stop words (only articles/prepositions) for the specified language.
    /// Used when preserving multi-word phrases is important.
    /// </summary>
    /// <param name="language">Language code (e.g., "Swedish", "English")</param>
    /// <returns>Array of light stop words, or empty array if language not supported</returns>
    string[] GetLightStopWords(string language);
}
