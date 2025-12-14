namespace AiDashboard.Services;

/// <summary>
/// Detects document types and provides recommended actions for Swedish educational documents.
/// </summary>
public class DocumentTypeDetector : IDocumentTypeDetector
{
    public enum DocumentType
    {
        Unknown,
        SwedishYhKursplan,         // YH (Yrkeshögskola) course plan
        SwedishSkolverketKursplan, // Skolverket (Gymnasie) course plan
        SwedishKursplan,           // Generic Swedish course plan
        SwedishAmnesplan           // Swedish subject plan
    }

    private static readonly string[] SwedishYhKeywords =
    [
        "yhp", "yrkeshögskola", "kunskaper", "färdigheter", "kompetenser",
        "efter genomgången kurs", "utbildning som kursen ingår i"
    ];

    private static readonly string[] SwedishSkolverketKeywords =
    [
        "skolverket", "gymnasiepoäng", "betyg e", "betyg c", "betyg a",
        "ämnets syfte", "centralt innehåll", "kunskapskrav"
    ];

    public DocumentTypeResult DetectType(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new DocumentTypeResult
            {
                Type = DocumentType.Unknown,
                Confidence = 0,
                Reason = "Empty document"
            };
        }

        var lowerContent = content.ToLowerInvariant();

        // Check for YH course plan
        var yhMatches = SwedishYhKeywords.Count(keyword => lowerContent.Contains(keyword));
        if (yhMatches >= 3)
        {
            return new DocumentTypeResult
            {
                Type = DocumentType.SwedishYhKursplan,
                Confidence = Math.Min(95, 60 + (yhMatches * 8)),
                Reason = $"Detected {yhMatches} YH-specific keywords (yhp, kunskaper, färdigheter, kompetenser)"
            };
        }

        // Check for Skolverket course plan
        var skolverketMatches = SwedishSkolverketKeywords.Count(keyword => lowerContent.Contains(keyword));
        if (skolverketMatches >= 3)
        {
            return new DocumentTypeResult
            {
                Type = DocumentType.SwedishSkolverketKursplan,
                Confidence = Math.Min(90, 55 + (skolverketMatches * 8)),
                Reason = $"Detected {skolverketMatches} Skolverket-specific keywords"
            };
        }

        // Generic Swedish course plan
        if (lowerContent.Contains("kursplan") || lowerContent.Contains("kunskapsmål"))
        {
            return new DocumentTypeResult
            {
                Type = DocumentType.SwedishKursplan,
                Confidence = 50,
                Reason = "Contains 'kursplan' or 'kunskapsmål'"
            };
        }

        return new DocumentTypeResult
        {
            Type = DocumentType.Unknown,
            Confidence = 0,
            Reason = "No specific Swedish educational keywords detected"
        };
    }

    public List<AnalysisAction> GetRecommendedActions(DocumentType type)
    {
        return type switch
        {
            DocumentType.SwedishYhKursplan =>
            [
                new AnalysisAction("kunskapsmal", "Extrahera Kursmål", "Extraherar Kunskaper, Färdigheter och Kompetenser", true),
                new AnalysisAction("betygskriterier", "Betygskriterier", "Visar betygskriterier", false)
            ],
            DocumentType.SwedishSkolverketKursplan =>
            [
                new AnalysisAction("kunskapsmal", "Extrahera Kursmål", "Extraherar kunskapskrav", true),
                new AnalysisAction("betygskriterier", "Betygskriterier", "Visar E/C/A-kriterier", false)
            ],
            DocumentType.SwedishKursplan =>
            [
                new AnalysisAction("kunskapsmal", "Extrahera Kursmål", "Extraherar alla kursmål", true)
            ],
            _ =>
            [
                new AnalysisAction("kunskapsmal", "Extrahera Kursmål", "Försök extrahera kursmål", true)
            ]
        };
    }

    public string GetTypeName(DocumentType type)
    {
        return type switch
        {
            DocumentType.SwedishYhKursplan => "YH-Kursplan",
            DocumentType.SwedishSkolverketKursplan => "Skolverket Kursplan",
            DocumentType.SwedishKursplan => "Svensk Kursplan",
            DocumentType.SwedishAmnesplan => "Svensk Ämnesplan",
            _ => "Okänd typ"
        };
    }

    public string GetTypeIcon(DocumentTypeDetector.DocumentType type)
    {
        return type switch
        {
            DocumentType.SwedishYhKursplan => "[YH]",
            DocumentType.SwedishSkolverketKursplan => "[SK]",
            DocumentType.SwedishKursplan => "[KP]",
            DocumentType.SwedishAmnesplan => "[AP]",
            _ => "[?]"
        };
    }
}

/// <summary>
/// Result of document type detection.
/// </summary>
public class DocumentTypeResult
{
    public DocumentTypeDetector.DocumentType Type { get; set; }
    public int Confidence { get; set; }
    public string Reason { get; set; } = "";
}

/// <summary>
/// A recommended analysis action for a document type.
/// </summary>
public class AnalysisAction
{
    public string Id { get; set; }
    public string Label { get; set; }
    public string Description { get; set; }
    public bool IsRecommended { get; set; }

    public AnalysisAction(string id, string label, string description, bool isRecommended)
    {
        Id = id;
        Label = label;
        Description = description;
        IsRecommended = isRecommended;
    }
}
