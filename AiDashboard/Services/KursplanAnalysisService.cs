using System.Text;
using System.Text.RegularExpressions;

namespace AiDashboard.Services;

/// <summary>
/// Specialized service for extracting and formatting Swedish YH (Yrkeshögskola) course objectives.
/// Parses documents to extract Kunskaper (Knowledge), Färdigheter (Skills), and Kompetenser (Competencies).
/// </summary>
public class KursplanAnalysisService : IKursplanAnalysisService
{
    /// <summary>
    /// Extract course objectives from Swedish YH course plan document.
    /// </summary>
    /// <param name="documentText">Full text of the course plan document</param>
    /// <param name="documentType">Type of document (YH, Skolverket, etc.)</param>
    /// <returns>Structured result with extracted objectives</returns>
    public KursplanResult ExtractKursmal(string documentText, DocumentTypeDetector.DocumentType documentType)
    {
        var result = new KursplanResult
        {
            ExtractedAt = DateTime.UtcNow
        };

        // Extract course name and metadata from first lines
        result.Metadata = ExtractMetadata(documentText);
        result.CourseName = result.Metadata?.CourseName;

        // Parse document into sections
        var sections = ParseDocumentIntoSections(documentText);

        // Extract items from each section
        int globalItemNumber = 1;

        foreach (var (sectionName, sectionText) in sections)
        {
            var sectionCode = GetSectionCode(sectionName);
            var items = ExtractItemsFromText(sectionText);

            if (items.Count > 0)
            {
                var kursmalSection = new KursmalSection
                {
                    SectionName = NormalizeSectionName(sectionName),
                    SectionCode = sectionCode,
                    Items = new List<KursmalItem>()
                };

                foreach (var itemText in items)
                {
                    var code = $"{sectionCode}{globalItemNumber:D2}";
                    kursmalSection.Items.Add(new KursmalItem
                    {
                        Number = globalItemNumber,
                        Code = code,
                        Text = itemText,
                        Section = sectionName
                    });
                    globalItemNumber++;
                }

                result.Sections.Add(kursmalSection);
            }
        }

        result.TotalItems = result.Sections.Sum(s => s.Items.Count);
        return result;
    }

    /// <summary>
    /// Extract course metadata from document header.
    /// </summary>
    private KursplanMetadata ExtractMetadata(string text)
    {
        var metadata = new KursplanMetadata();
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length > 0)
        {
            // First line is often the course name
            var firstLine = lines[0].Trim();
            
            // Check if it contains "yhp" - format: "Kursnamn XX yhp"
            var yhpMatch = Regex.Match(firstLine, @"^(.+?)\s+[-–]\s+(.+?)$|^(.+?)\s+\((\d+)\s*yhp\)", RegexOptions.IgnoreCase);
            if (yhpMatch.Success)
            {
                if (!string.IsNullOrEmpty(yhpMatch.Groups[1].Value))
                {
                    // Format: "BUV25 - HTML & CSS"
                    metadata.CourseCode = yhpMatch.Groups[1].Value.Trim();
                    metadata.CourseName = yhpMatch.Groups[2].Value.Trim();
                }
                else
                {
                    // Format: "HTML & CSS (35 yhp)"
                    metadata.CourseName = yhpMatch.Groups[3].Value.Trim();
                    metadata.Points = yhpMatch.Groups[4].Value + " yhp";
                }
            }
            else
            {
                metadata.CourseName = firstLine;
            }
        }

        // Look for yhp in second or third line
        foreach (var line in lines.Take(5))
        {
            var pointsMatch = Regex.Match(line, @"(\d+)\s*yhp", RegexOptions.IgnoreCase);
            if (pointsMatch.Success && string.IsNullOrEmpty(metadata.Points))
            {
                metadata.Points = pointsMatch.Groups[1].Value + " yhp";
            }
        }

        return metadata;
    }

    /// <summary>
    /// Parse document into named sections (Kunskaper, Färdigheter, Kompetenser).
    /// Excludes grading and assessment sections.
    /// </summary>
    private Dictionary<string, string> ParseDocumentIntoSections(string text)
    {
        var sections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = text.Split('\n');

        string? currentSection = null;
        var currentContent = new StringBuilder();

        // Swedish YH course plan sections to look for (only learning objectives)
        var sectionNames = new[] 
        { 
            "Kunskaper", "Kunskap", 
            "Färdigheter", "Färdighet", 
            "Kompetenser", "Kompetens" 
        };

        // Sections to EXCLUDE (grading, assessment, etc.)
        var excludedSections = new[]
        {
            "Former för kunskapskontroll",
            "Principer för bedömning",
            "Betyg",
            "Betygskriterier",
            "Icke godkänt",
            "Godkänt",
            "Väl godkänt"
        };

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            // Check if this is an excluded section - if so, stop processing
            if (excludedSections.Any(excluded => 
                trimmedLine.StartsWith(excluded, StringComparison.OrdinalIgnoreCase)))
            {
                // Save current section before stopping
                if (currentSection != null && currentContent.Length > 0)
                {
                    sections[currentSection] = currentContent.ToString().Trim();
                }
                // Stop processing - we've reached grading sections
                break;
            }

            // Check if this line is a section header
            var detectedSection = DetectSectionHeader(trimmedLine, sectionNames);

            if (detectedSection != null)
            {
                // Save previous section
                if (currentSection != null && currentContent.Length > 0)
                {
                    sections[currentSection] = currentContent.ToString().Trim();
                    currentContent.Clear();
                }
                currentSection = detectedSection;
            }
            else if (currentSection != null)
            {
                currentContent.AppendLine(line);
            }
        }

        // Save last section
        if (currentSection != null && currentContent.Length > 0)
        {
            sections[currentSection] = currentContent.ToString().Trim();
        }

        return sections;
    }

    /// <summary>
    /// Detect if a line is a section header.
    /// </summary>
    private string? DetectSectionHeader(string line, string[] sectionNames)
    {
        var trimmed = line.Trim();

        // Handle #SectionName format (e.g., #Kunskap, #Färdigheter)
        if (trimmed.StartsWith('#') && !trimmed.StartsWith("##"))
        {
            var afterHash = trimmed.TrimStart('#').Trim();
            foreach (var sectionName in sectionNames)
            {
                if (afterHash.Equals(sectionName, StringComparison.OrdinalIgnoreCase))
                {
                    return sectionName;
                }
            }
        }

        // Handle plain section names
        foreach (var sectionName in sectionNames)
        {
            if (trimmed.Equals(sectionName, StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals(sectionName + ":", StringComparison.OrdinalIgnoreCase))
            {
                return sectionName;
            }
        }

        return null;
    }

    /// <summary>
    /// Extract individual items from section text.
    /// Excludes assessment and grading content.
    /// </summary>
    private List<string> ExtractItemsFromText(string text)
    {
        var items = new List<string>();
        var lines = text.Split('\n');

        // Phrases that indicate assessment/grading content (not learning objectives)
        var excludedPhrases = new[]
        {
            "former för kunskapskontroll",
            "principer för bedömning",
            "betyg sätts",
            "icke godkänt",
            "godkänt (g)",
            "väl godkänt",
            "kunskapskontroller görs"
        };

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            // Skip empty lines
            if (string.IsNullOrWhiteSpace(trimmedLine))
                continue;

            // Skip if line contains excluded phrases
            var lowerLine = trimmedLine.ToLowerInvariant();
            if (excludedPhrases.Any(phrase => lowerLine.Contains(phrase)))
                continue;

            // Skip lines that look like headers
            if (trimmedLine.StartsWith('#') || 
                trimmedLine.Equals("Efter genomgången kurs ska den studerande ha följande:", StringComparison.OrdinalIgnoreCase) ||
                trimmedLine.Equals("Efter genomgången kurs ska den studerande kunna", StringComparison.OrdinalIgnoreCase) ||
                trimmedLine.Equals("Efter genomgången kurs ska den studerande ha färdigheter i att", StringComparison.OrdinalIgnoreCase) ||
                trimmedLine.Equals("Efter genomgången kurs ska den studerande ha kompetens att", StringComparison.OrdinalIgnoreCase))
                continue;

            // Remove bullet points and numbering
            var cleanedLine = RemoveListPrefix(trimmedLine);

            // Only add non-empty lines that are substantial (> 10 chars)
            // Increased from 5 to 10 to filter out very short fragments
            if (cleanedLine.Length > 10)
            {
                items.Add(CleanItemText(cleanedLine));
            }
        }

        return items;
    }

    /// <summary>
    /// Remove list prefix from item text (bullets, numbers, etc.).
    /// </summary>
    private string RemoveListPrefix(string text)
    {
        var trimmed = text.TrimStart();

        // Remove bullet points
        trimmed = Regex.Replace(trimmed, @"^[?•\-\*??]\s*", "");

        // Remove numbered lists
        trimmed = Regex.Replace(trimmed, @"^\d+[\.\)]\s*", "");

        // Remove letter lists
        trimmed = Regex.Replace(trimmed, @"^[a-z][\.\)]\s*", "", RegexOptions.IgnoreCase);

        return trimmed.Trim();
    }

    /// <summary>
    /// Clean item text for output.
    /// </summary>
    private string CleanItemText(string text)
    {
        // Remove multiple spaces
        var cleaned = Regex.Replace(text, @"\s+", " ").Trim();

        // Capitalize first letter
        if (cleaned.Length > 0)
        {
            cleaned = char.ToUpper(cleaned[0]) + cleaned.Substring(1);
        }

        return cleaned;
    }

    /// <summary>
    /// Get section code for numbering (K, F, Ko).
    /// </summary>
    private string GetSectionCode(string sectionName)
    {
        return sectionName.ToLowerInvariant() switch
        {
            "kunskaper" or "kunskap" => "K",
            "färdigheter" or "färdighet" => "F",
            "kompetenser" or "kompetens" => "Ko",
            _ => "X"
        };
    }

    /// <summary>
    /// Normalize section name to standard form.
    /// </summary>
    private string NormalizeSectionName(string sectionName)
    {
        return sectionName.ToLowerInvariant() switch
        {
            "kunskap" => "Kunskaper",
            "kunskaper" => "Kunskaper",
            "färdighet" => "Färdigheter",
            "färdigheter" => "Färdigheter",
            "kompetens" => "Kompetenser",
            "kompetenser" => "Kompetenser",
            _ => sectionName
        };
    }

    /// <summary>
    /// Format the result as clean text output (no codes, just sections).
    /// </summary>
    public string FormatResult(KursplanResult result)
    {
        var sb = new StringBuilder();

        // Course header
        if (!string.IsNullOrEmpty(result.Metadata?.CourseCode) && !string.IsNullOrEmpty(result.Metadata?.CourseName))
        {
            sb.AppendLine($"## {result.Metadata.CourseCode} - {result.Metadata.CourseName}");
        }
        else if (!string.IsNullOrEmpty(result.CourseName))
        {
            sb.AppendLine($"## {result.CourseName}");
        }

        if (result.Metadata?.Points != null)
        {
            sb.AppendLine($"*({result.Metadata.Points})*");
        }

        sb.AppendLine();

        // Sections without codes
        foreach (var section in result.Sections)
        {
            sb.AppendLine($"#{section.SectionName}");
            foreach (var item in section.Items)
            {
                sb.AppendLine(item.Text);
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Format the result with codes for tracking (K01, F02, Ko03, etc.).
    /// </summary>
    public string FormatResultWithCodes(KursplanResult result)
    {
        var sb = new StringBuilder();

        // Course header
        if (!string.IsNullOrEmpty(result.Metadata?.CourseCode) && !string.IsNullOrEmpty(result.Metadata?.CourseName))
        {
            sb.AppendLine($"## {result.Metadata.CourseCode} - {result.Metadata.CourseName}");
        }
        else if (!string.IsNullOrEmpty(result.CourseName))
        {
            sb.AppendLine($"## {result.CourseName}");
        }

        if (result.Metadata?.Points != null)
        {
            sb.AppendLine($"*({result.Metadata.Points})*");
        }

        sb.AppendLine();

        // Sections with codes
        foreach (var section in result.Sections)
        {
            sb.AppendLine($"#{section.SectionName}");
            foreach (var item in section.Items)
            {
                sb.AppendLine($"**{item.Code}** - {item.Text}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Create an AI prompt for enhanced extraction (fallback if parsing fails).
    /// </summary>
    public string CreateAiEnhancementPrompt(KursplanResult preliminaryResult, string rawText)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Du är en expert på svenska YH-kursplaner.");
        sb.AppendLine("Extrahera ALLA kursmål från följande dokument.");
        sb.AppendLine();
        sb.AppendLine("STRUKTUR:");
        sb.AppendLine("- Kunskaper (K) - vad den studerande ska kunna");
        sb.AppendLine("- Färdigheter (F) - praktiska färdigheter");
        sb.AppendLine("- Kompetenser (Ko) - övergripande kompetenser");
        sb.AppendLine();
        sb.AppendLine("FORMAT:");
        sb.AppendLine("## [Kursnamn]");
        sb.AppendLine();
        sb.AppendLine("#Kunskaper");
        sb.AppendLine("[Mål 1]");
        sb.AppendLine("[Mål 2]");
        sb.AppendLine();
        sb.AppendLine("#Färdigheter");
        sb.AppendLine("[Mål 1]");
        sb.AppendLine();
        sb.AppendLine("#Kompetenser");
        sb.AppendLine("[Mål 1]");
        sb.AppendLine();
        sb.AppendLine("DOKUMENT:");
        sb.AppendLine("---");
        sb.AppendLine(rawText.Length > 8000 ? rawText.Substring(0, 8000) + "..." : rawText);

        return sb.ToString();
    }
}

/// <summary>
/// Metadata extracted from a course plan document header.
/// </summary>
public class KursplanMetadata
{
    public string? CourseName { get; set; }
    public string? CourseCode { get; set; }
    public string? Points { get; set; }
    public string? Program { get; set; }
}

/// <summary>
/// Result of course plan analysis containing all extracted objectives.
/// </summary>
public class KursplanResult
{
    public List<KursmalSection> Sections { get; set; } = new();
    public int TotalItems { get; set; }
    public string? CourseName { get; set; }
    public string? CourseCode { get; set; }
    public KursplanMetadata? Metadata { get; set; }
    public DateTime ExtractedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A section of course objectives (Kunskaper, Färdigheter, or Kompetenser).
/// </summary>
public class KursmalSection
{
    public string SectionName { get; set; } = "";
    public string SectionCode { get; set; } = "";
    public List<KursmalItem> Items { get; set; } = new();
}

/// <summary>
/// A single course objective item with numbering and code.
/// </summary>
public class KursmalItem
{
    public int Number { get; set; }
    public string Code { get; set; } = ""; // e.g., "K01", "F02", "Ko03"
    public string Text { get; set; } = "";
    public string Section { get; set; } = "";
}
