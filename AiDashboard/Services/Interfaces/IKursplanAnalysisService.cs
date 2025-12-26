namespace AiDashboard.Services;

/// <summary>
/// Interface for analyzing Swedish YH course plans (Yrkeshögskola kursplaner).
/// </summary>
public interface IKursplanAnalysisService
{
    /// <summary>
    /// Extract course objectives (Kursmål) from a Swedish YH course plan document.
    /// </summary>
    /// <param name="documentText">Full text of the course plan</param>
    /// <param name="documentType">Type of document (YH, Skolverket, etc.)</param>
    /// <returns>Structured result with extracted Kunskaper, Färdigheter, and Kompetenser</returns>
    KursplanResult ExtractKursmal(string documentText, DocumentTypeDetector.DocumentType documentType);

    /// <summary>
    /// Format the result as clean text (without codes).
    /// Output format matches your requirement:
    /// ## BUV25 - HTML & CSS
    /// #Kunskap
    /// Item 1
    /// Item 2
    /// </summary>
    string FormatResult(KursplanResult result);

    /// <summary>
    /// Format the result with tracking codes (K01, F02, Ko03...).
    /// </summary>
    string FormatResultWithCodes(KursplanResult result);

    /// <summary>
    /// Create an AI prompt for enhanced extraction if parsing fails.
    /// </summary>
    string CreateAiEnhancementPrompt(KursplanResult preliminaryResult, string rawText);
}
