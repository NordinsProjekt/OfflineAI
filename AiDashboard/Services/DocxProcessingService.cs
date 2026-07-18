using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.AspNetCore.Components.Forms;

namespace AiDashboard.Services;

/// <summary>
/// Service for processing Microsoft Word DOCX files.
/// Extracts text, tables, headers, footers, and formatting information.
/// </summary>
public static class DocxProcessingService
{
    /// <summary>
    /// Extract all text from a DOCX file uploaded via Blazor InputFile.
    /// </summary>
    /// <param name="file">The uploaded DOCX file</param>
    /// <param name="maxFileSize">Maximum allowed file size in bytes (default 10MB)</param>
    /// <param name="includeHeaders">Include header text in output</param>
    /// <param name="includeFooters">Include footer text in output</param>
    /// <param name="includeTables">Include table text in output</param>
    /// <returns>Success status, extracted text, and any error message</returns>
    public static async Task<(bool Success, string Text, string Error)> ExtractTextAsync(
        IBrowserFile file,
        long maxFileSize = 10485760,
        bool includeHeaders = true,
        bool includeFooters = true,
        bool includeTables = true)
    {
        if (file.Size > maxFileSize)
        {
            return (false, "", $"File too large. Maximum size is {maxFileSize / 1024 / 1024}MB");
        }

        try
        {
            // Read file into memory stream
            using var memoryStream = new MemoryStream();
            await file.OpenReadStream(maxFileSize).CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            // Open the Word document
            using var doc = WordprocessingDocument.Open(memoryStream, false);
            
            if (doc.MainDocumentPart == null)
            {
                return (false, "", "Invalid DOCX file: No main document part found");
            }

            var textBuilder = new StringBuilder();

            // Extract headers if requested
            if (includeHeaders)
            {
                ExtractHeaderText(doc, textBuilder);
            }

            // Extract main body text
            ExtractBodyText(doc.MainDocumentPart, textBuilder, includeTables);

            // Extract footers if requested
            if (includeFooters)
            {
                ExtractFooterText(doc, textBuilder);
            }

            var extractedText = textBuilder.ToString().Trim();
            
            if (string.IsNullOrWhiteSpace(extractedText))
            {
                return (false, "", "No text content found in DOCX file");
            }

            return (true, extractedText, "");
        }
        catch (OpenXmlPackageException ex)
        {
            return (false, "", $"Invalid DOCX format: {ex.Message}");
        }
        catch (Exception ex)
        {
            return (false, "", $"Error reading DOCX file: {ex.Message}");
        }
    }

    /// <summary>
    /// Extract text from the main document body.
    /// </summary>
    private static void ExtractBodyText(MainDocumentPart mainPart, StringBuilder textBuilder, bool includeTables)
    {
        var body = mainPart.Document?.Body;
        if (body == null) return;

        foreach (var element in body.Elements())
        {
            switch (element)
            {
                case Paragraph paragraph:
                    ExtractParagraphText(paragraph, textBuilder);
                    break;

                case Table table when includeTables:
                    ExtractTableText(table, textBuilder);
                    break;
            }
        }
    }

    /// <summary>
    /// Extract text from a paragraph element.
    /// </summary>
    private static void ExtractParagraphText(Paragraph paragraph, StringBuilder textBuilder)
    {
        var paragraphText = GetParagraphText(paragraph);
        
        if (!string.IsNullOrWhiteSpace(paragraphText))
        {
            textBuilder.AppendLine(paragraphText);
        }
    }

    /// <summary>
    /// Get text from a paragraph including formatting.
    /// </summary>
    private static string GetParagraphText(Paragraph paragraph)
    {
        var textBuilder = new StringBuilder();

        foreach (var run in paragraph.Elements<Run>())
        {
            foreach (var textElement in run.Elements<Text>())
            {
                textBuilder.Append(textElement.Text);
            }
        }

        return textBuilder.ToString();
    }

    /// <summary>
    /// Extract text from a table.
    /// </summary>
    private static void ExtractTableText(Table table, StringBuilder textBuilder)
    {
        textBuilder.AppendLine("\n--- Table ---");

        foreach (var row in table.Elements<TableRow>())
        {
            var rowText = new StringBuilder();
            
            foreach (var cell in row.Elements<TableCell>())
            {
                var cellText = GetCellText(cell);
                if (!string.IsNullOrWhiteSpace(cellText))
                {
                    rowText.Append(cellText.Trim());
                    rowText.Append(" | ");
                }
            }

            var rowString = rowText.ToString().TrimEnd('|', ' ');
            if (!string.IsNullOrWhiteSpace(rowString))
            {
                textBuilder.AppendLine(rowString);
            }
        }

        textBuilder.AppendLine("--- End Table ---\n");
    }

    /// <summary>
    /// Get text from a table cell.
    /// </summary>
    private static string GetCellText(TableCell cell)
    {
        var textBuilder = new StringBuilder();

        foreach (var paragraph in cell.Elements<Paragraph>())
        {
            var paragraphText = GetParagraphText(paragraph);
            if (!string.IsNullOrWhiteSpace(paragraphText))
            {
                textBuilder.Append(paragraphText);
                textBuilder.Append(" ");
            }
        }

        return textBuilder.ToString().Trim();
    }

    /// <summary>
    /// Extract text from document headers.
    /// </summary>
    private static void ExtractHeaderText(WordprocessingDocument doc, StringBuilder textBuilder)
    {
        if (doc.MainDocumentPart?.HeaderParts == null) return;

        foreach (var header in doc.MainDocumentPart.HeaderParts.Select(headerPart => headerPart.Header).OfType<Header>())
        {
            textBuilder.AppendLine("--- Header ---");

            foreach (var paragraph in header.Elements<Paragraph>())
            {
                var paragraphText = GetParagraphText(paragraph);
                if (!string.IsNullOrWhiteSpace(paragraphText))
                {
                    textBuilder.AppendLine(paragraphText);
                }
            }
            
            textBuilder.AppendLine("--- End Header ---\n");
        }
    }

    /// <summary>
    /// Extract text from document footers.
    /// </summary>
    private static void ExtractFooterText(WordprocessingDocument doc, StringBuilder textBuilder)
    {
        if (doc.MainDocumentPart?.FooterParts == null) return;

        foreach (var footer in doc.MainDocumentPart.FooterParts.Select(footerPart => footerPart.Footer).OfType<Footer>())
        {
            textBuilder.AppendLine("\n--- Footer ---");

            foreach (var paragraph in footer.Elements<Paragraph>())
            {
                var paragraphText = GetParagraphText(paragraph);
                if (!string.IsNullOrWhiteSpace(paragraphText))
                {
                    textBuilder.AppendLine(paragraphText);
                }
            }
            
            textBuilder.AppendLine("--- End Footer ---");
        }
    }

    /// <summary>
    /// Extract document metadata (properties).
    /// </summary>
    public static async Task<DocxMetadata> ExtractMetadataAsync(IBrowserFile file, long maxFileSize = 10485760)
    {
        var metadata = new DocxMetadata();

        try
        {
            using var memoryStream = new MemoryStream();
            await file.OpenReadStream(maxFileSize).CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            using var doc = WordprocessingDocument.Open(memoryStream, false);
            
            var coreProps = doc.PackageProperties;
            
            metadata.Title = coreProps.Title ?? "";
            metadata.Subject = coreProps.Subject ?? "";
            metadata.Creator = coreProps.Creator ?? "";
            metadata.Keywords = coreProps.Keywords ?? "";
            metadata.Description = coreProps.Description ?? "";
            metadata.LastModifiedBy = coreProps.LastModifiedBy ?? "";
            metadata.Created = coreProps.Created;
            metadata.Modified = coreProps.Modified;
            
            // Count pages, paragraphs, etc.
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body != null)
            {
                metadata.ParagraphCount = body.Elements<Paragraph>().Count();
                metadata.TableCount = body.Elements<Table>().Count();
            }
        }
        catch (Exception ex)
        {
            metadata.Error = $"Error extracting metadata: {ex.Message}";
        }

        return metadata;
    }

    /// <summary>
    /// Check if a file is a valid DOCX file without fully parsing it.
    /// </summary>
    public static async Task<bool> IsValidDocxAsync(IBrowserFile file, long maxFileSize = 10485760)
    {
        try
        {
            using var memoryStream = new MemoryStream();
            await file.OpenReadStream(maxFileSize).CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            using var doc = WordprocessingDocument.Open(memoryStream, false);
            return doc.MainDocumentPart != null;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Metadata extracted from a DOCX file.
/// </summary>
public class DocxMetadata
{
    public string Title { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Creator { get; set; } = "";
    public string Keywords { get; set; } = "";
    public string Description { get; set; } = "";
    public string LastModifiedBy { get; set; } = "";
    public DateTimeOffset? Created { get; set; }
    public DateTimeOffset? Modified { get; set; }
    public int ParagraphCount { get; set; }
    public int TableCount { get; set; }
    public string Error { get; set; } = "";
}
