using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Entities;
using Services.Utilities;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Services.Memory;

/// <summary>
/// Processes PDF files into semantic chunks suitable for embedding.
/// Extracts text using UglyToad.PdfPig, preserves structure, and creates meaningful fragments.
/// </summary>
public class PdfFragmentProcessor
{
    private readonly DocumentChunker.ChunkOptions _chunkOptions;

    public PdfFragmentProcessor(DocumentChunker.ChunkOptions? options = null)
    {
        _chunkOptions = options ?? new DocumentChunker.ChunkOptions
        {
            MaxChunkSize = 1000,      // Good size for embeddings
            OverlapSize = 200,        // Preserve context between chunks
            MinChunkSize = 100,       // Avoid tiny fragments
            KeepHeaders = true,       // Include section titles
            AddMetadata = true        // Track page numbers
        };
    }

    /// <summary>
    /// Process a PDF file into memory fragments ready for embedding.
    /// </summary>
    public async Task<List<MemoryFragment>> ProcessPdfFileAsync(
        string pdfPath, 
        string? collectionName = null)
    {
        if (!File.Exists(pdfPath))
            throw new FileNotFoundException($"PDF file not found: {pdfPath}");

        var fileName = Path.GetFileNameWithoutExtension(pdfPath);
        collectionName ??= fileName;

        // Extract text from PDF
        var pdfText = await ExtractTextFromPdfAsync(pdfPath);
        
        // Extract metadata
        var metadata = await ExtractPdfMetadataAsync(pdfPath);
        
        // Chunk the text using semantic boundaries
        var chunks = DocumentChunker.ChunkByHierarchy(pdfText, _chunkOptions);
        
        // Convert to MemoryFragments
        var fragments = new List<MemoryFragment>();
        
        foreach (var chunk in chunks)
        {
            // Encode metadata in the category field
            var category = $"{collectionName} - Chunk {chunk.ChunkIndex + 1}";
            
            // Only use section title if it's meaningful (not the default fallback)
            if (!string.IsNullOrWhiteSpace(chunk.SectionTitle) && 
                chunk.SectionTitle != "General")
            {
                category = $"{collectionName} - {chunk.SectionTitle}";
            }
            
            // Optionally prepend metadata to content for better context
            var enhancedContent = chunk.Content;
            if (_chunkOptions.AddMetadata && metadata.TotalPages > 0)
            {
                var metadataHeader = $"[Source: {fileName}.pdf, Total Pages: {metadata.TotalPages}]\n\n";
                enhancedContent = metadataHeader + chunk.Content;
            }
            
            var fragment = new MemoryFragment(
                category: category,
                content: enhancedContent
            );
            
            fragments.Add(fragment);
        }
        
        Console.WriteLine($"[?] Extracted {fragments.Count} chunks from {metadata.TotalPages} pages: {fileName}.pdf");
        
        return fragments;
    }

    /// <summary>
    /// Process multiple PDF files from a directory
    /// </summary>
    public async Task<List<MemoryFragment>> ProcessPdfDirectoryAsync(
        string directoryPath,
        string searchPattern = "*.pdf",
        bool recursive = false)
    {
        if (!Directory.Exists(directoryPath))
            throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var pdfFiles = Directory.GetFiles(directoryPath, searchPattern, searchOption);

        var allFragments = new List<MemoryFragment>();

        foreach (var pdfFile in pdfFiles)
        {
            try
            {
                var fragments = await ProcessPdfFileAsync(pdfFile);
                allFragments.AddRange(fragments);
                
                Console.WriteLine($"[?] Processed: {Path.GetFileName(pdfFile)} ({fragments.Count} chunks)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[?] Failed to process {Path.GetFileName(pdfFile)}: {ex.Message}");
            }
        }

        return allFragments;
    }

    /// <summary>
    /// Extract text from PDF using UglyToad.PdfPig
    /// </summary>
    private async Task<string> ExtractTextFromPdfAsync(string pdfPath)
    {
        return await Task.Run(() =>
        {
            using var document = PdfDocument.Open(pdfPath);
            var text = new StringBuilder();
            
            foreach (var page in document.GetPages())
            {
                // Add page marker for better context
                text.AppendLine($"\n--- Page {page.Number} ---\n");
                
                // Extract text with proper spacing
                var pageText = page.Text;
                
                // Clean up common PDF extraction issues
                pageText = CleanPdfText(pageText);
                
                text.AppendLine(pageText);
                text.AppendLine();
            }
            
            return text.ToString();
        });
    }

    /// <summary>
    /// Clean up common PDF text extraction issues
    /// </summary>
    private string CleanPdfText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        
        var cleaned = text;
        
        // Fix hyphenated words at line breaks
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"(\w+)-\s*\r?\n\s*(\w+)", "$1$2");
        
        // Remove excessive whitespace
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"[ \t]+", " ");
        
        // Fix multiple newlines (keep maximum of 2)
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\r?\n\s*\r?\n\s*\r?\n+", "\n\n");
        
        return cleaned.Trim();
    }

    /// <summary>
    /// Extract PDF metadata (title, author, page count, etc.)
    /// </summary>
    private async Task<PdfMetadata> ExtractPdfMetadataAsync(string pdfPath)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var document = PdfDocument.Open(pdfPath);
                var info = document.Information;
                
                // Try to parse creation date if available
                DateTime? creationDate = null;
                if (!string.IsNullOrWhiteSpace(info.CreationDate))
                {
                    if (DateTime.TryParse(info.CreationDate, out var parsedDate))
                    {
                        creationDate = parsedDate;
                    }
                }
                
                return new PdfMetadata
                {
                    TotalPages = document.NumberOfPages,
                    Title = info.Title ?? Path.GetFileNameWithoutExtension(pdfPath),
                    Author = info.Author ?? "Unknown",
                    Subject = info.Subject,
                    CreationDate = creationDate,
                    Producer = info.Producer,
                    Creator = info.Creator
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Failed to extract PDF metadata: {ex.Message}");
                
                return new PdfMetadata
                {
                    TotalPages = 0,
                    Title = Path.GetFileNameWithoutExtension(pdfPath),
                    Author = "Unknown",
                    Subject = null
                };
            }
        });
    }

    private class PdfMetadata
    {
        public int TotalPages { get; set; }
        public string? Title { get; set; }
        public string? Author { get; set; }
        public string? Subject { get; set; }
        public DateTime? CreationDate { get; set; }
        public string? Producer { get; set; }
        public string? Creator { get; set; }
    }
}
