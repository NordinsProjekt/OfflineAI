using System.Text;
using Microsoft.AspNetCore.Components.Forms;

namespace AiDashboard.Services;

/// <summary>
/// Service for analyzing documents with AI.
/// Supports PDF, DOCX, and TXT files with automatic chunking and semantic search.
/// </summary>
public class DocumentAnalysisService : IDocumentAnalysisService
{
    private const int MaxChunkSize = 4000; // Characters per chunk
    private const int ChunkOverlap = 200; // Overlap between chunks for context

    /// <summary>
    /// Extract text from uploaded file based on file type.
    /// </summary>
    public async Task<(bool Success, string Text, string Error)> ExtractTextAsync(IBrowserFile file, long maxFileSize = 10485760)
    {
        if (file.Size > maxFileSize)
        {
            return (false, "", $"File too large. Maximum size is {maxFileSize / 1024 / 1024}MB");
        }

        try
        {
            var extension = Path.GetExtension(file.Name).ToLowerInvariant();

            return extension switch
            {
                ".txt" => await ExtractFromTxtAsync(file, maxFileSize),
                ".pdf" => await ExtractFromPdfAsync(file, maxFileSize),
                ".doc" or ".docx" => await ExtractFromDocxAsync(file),
                _ => (false, "", $"Unsupported file type: {extension}")
            };
        }
        catch (Exception ex)
        {
            return (false, "", $"Error reading file: {ex.Message}");
        }
    }

    /// <summary>
    /// Extract text from TXT file.
    /// </summary>
    private static async Task<(bool Success, string Text, string Error)> ExtractFromTxtAsync(IBrowserFile file, long maxFileSize)
    {
        try
        {
            using var stream = file.OpenReadStream(maxAllowedSize: maxFileSize);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var text = await reader.ReadToEndAsync();
            return (true, text, "");
        }
        catch (Exception ex)
        {
            return (false, "", $"Error reading TXT file: {ex.Message}");
        }
    }

    /// <summary>
    /// Extract text from PDF file using UglyToad.PdfPig.
    /// </summary>
    private static async Task<(bool Success, string Text, string Error)> ExtractFromPdfAsync(IBrowserFile file, long maxFileSize)
    {
        try
        {
            using var memoryStream = new MemoryStream();
            await file.OpenReadStream(maxAllowedSize: maxFileSize).CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            using var pdf = UglyToad.PdfPig.PdfDocument.Open(memoryStream);
            var textBuilder = new StringBuilder();

            foreach (var page in pdf.GetPages())
            {
                textBuilder.AppendLine(page.Text);
            }

            var extractedText = textBuilder.ToString().Trim();

            if (string.IsNullOrWhiteSpace(extractedText))
            {
                return (false, "", "No text content found in the PDF file.");
            }

            return (true, extractedText, "");
        }
        catch (Exception ex)
        {
            return (false, "", $"Error reading PDF file: {ex.Message}");
        }
    }

    /// <summary>
    /// Extract text from DOCX file using DocxProcessingService.
    /// </summary>
    private static async Task<(bool Success, string Text, string Error)> ExtractFromDocxAsync(IBrowserFile file)
    {
        return await DocxProcessingService.ExtractTextAsync(
            file, 
            includeHeaders: true, 
            includeFooters: true, 
            includeTables: true);
    }

    /// <summary>
    /// Extract metadata from DOCX file.
    /// </summary>
    public static async Task<DocxMetadata> ExtractDocxMetadataAsync(IBrowserFile file)
    {
        return await DocxProcessingService.ExtractMetadataAsync(file);
    }

    /// <summary>
    /// Split large documents into smaller chunks with overlap.
    /// </summary>
    public List<DocumentChunk> SplitIntoChunks(string text, int maxChunkSize = MaxChunkSize, int overlap = ChunkOverlap)
    {
        var chunks = new List<DocumentChunk>();
        
        if (string.IsNullOrWhiteSpace(text))
        {
            return chunks;
        }

        // Split by paragraphs first
        var paragraphs = text.Split(["\r\n\r\n", "\n\n"], StringSplitOptions.RemoveEmptyEntries);
        
        var currentChunk = new StringBuilder();
        var chunkIndex = 0;
        var startPosition = 0;

        foreach (var paragraph in paragraphs)
        {
            var paragraphWithNewline = paragraph + "\n\n";
            
            // If adding this paragraph would exceed max size, save current chunk
            if (currentChunk.Length + paragraphWithNewline.Length > maxChunkSize && currentChunk.Length > 0)
            {
                chunks.Add(new DocumentChunk
                {
                    Index = chunkIndex++,
                    Text = currentChunk.ToString().Trim(),
                    StartPosition = startPosition,
                    EndPosition = startPosition + currentChunk.Length
                });

                // Keep last 'overlap' characters for context
                var overlapText = currentChunk.Length > overlap 
                    ? currentChunk.ToString().Substring(currentChunk.Length - overlap) 
                    : currentChunk.ToString();
                
                startPosition += currentChunk.Length - overlapText.Length;
                currentChunk.Clear();
                currentChunk.Append(overlapText);
            }

            currentChunk.Append(paragraphWithNewline);
        }

        // Add final chunk
        if (currentChunk.Length > 0)
        {
            chunks.Add(new DocumentChunk
            {
                Index = chunkIndex,
                Text = currentChunk.ToString().Trim(),
                StartPosition = startPosition,
                EndPosition = startPosition + currentChunk.Length
            });
        }

        return chunks;
    }

    /// <summary>
    /// Search for specific terms in chunks using simple text search.
    /// For semantic search, integrate with your embedding service.
    /// </summary>
    public static List<DocumentChunk> SearchChunks(List<DocumentChunk> chunks, string searchTerm, bool caseSensitive = false)
    {
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        
        return chunks
            .Where(chunk => chunk.Text.Contains(searchTerm, comparison))
            .Select(chunk =>
            {
                // Calculate relevance score based on frequency
                var occurrences = CountOccurrences(chunk.Text, searchTerm, comparison);
                chunk.RelevanceScore = occurrences;
                return chunk;
            })
            .OrderByDescending(chunk => chunk.RelevanceScore)
            .ToList();
    }

    /// <summary>
    /// Search for multiple terms (e.g., "Kunskapsmål", "Learning Objectives", etc.)
    /// </summary>
    public List<DocumentChunk> SearchMultipleTerms(List<DocumentChunk> chunks, string[] searchTerms, bool caseSensitive = false)
    {
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        
        return chunks
            .Where(chunk => searchTerms.Any(term => chunk.Text.Contains(term, comparison)))
            .Select(chunk =>
            {
                // Calculate relevance score based on total occurrences of all terms
                var totalOccurrences = searchTerms.Sum(term => CountOccurrences(chunk.Text, term, comparison));
                chunk.RelevanceScore = totalOccurrences;
                chunk.MatchedTerms = searchTerms.Where(term => chunk.Text.Contains(term, comparison)).ToList();
                return chunk;
            })
            .OrderByDescending(chunk => chunk.RelevanceScore)
            .ToList();
    }

    /// <summary>
    /// Create a formatted prompt for AI analysis of search results.
    /// </summary>
    public string CreateAnalysisPrompt(List<DocumentChunk> matchedChunks, string searchTerm, string outputFormat)
    {
        var prompt = new StringBuilder();
        
        prompt.AppendLine($"Analyze the following document sections that contain '{searchTerm}'.");
        prompt.AppendLine($"Extract and format the information according to this format:\n");
        prompt.AppendLine(outputFormat);
        prompt.AppendLine("\nDocument sections:\n");
        prompt.AppendLine("---");

        foreach (var chunk in matchedChunks)
        {
            prompt.AppendLine($"\n[Section {chunk.Index + 1}]");
            prompt.AppendLine(chunk.Text);
            prompt.AppendLine("---");
        }

        return prompt.ToString();
    }

    /// <summary>
    /// Count occurrences of a term in text.
    /// </summary>
    private static int CountOccurrences(string text, string term, StringComparison comparison)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(term))
            return 0;

        int count = 0;
        int index = 0;

        while ((index = text.IndexOf(term, index, comparison)) != -1)
        {
            count++;
            index += term.Length;
        }

        return count;
    }
}

/// <summary>
/// Represents a chunk of a document.
/// </summary>
public class DocumentChunk
{
    public int Index { get; set; }
    public string Text { get; set; } = "";
    public int StartPosition { get; set; }
    public int EndPosition { get; set; }
    public double RelevanceScore { get; set; }
    public List<string> MatchedTerms { get; set; } = new();
}
