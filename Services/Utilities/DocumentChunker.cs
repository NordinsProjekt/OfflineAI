using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Services.Utilities;

/// <summary>
/// Smart chunking strategies for creating semantic embeddings from documents.
/// Ensures chunks preserve context and meaning for better RAG performance.
/// </summary>
public static class DocumentChunker
{
    public class ChunkOptions
    {
        /// <summary>Maximum characters per chunk (default: 1000)</summary>
        public int MaxChunkSize { get; set; } = 1000;
        
        /// <summary>Overlap between chunks to preserve context (default: 200)</summary>
        public int OverlapSize { get; set; } = 200;
        
        /// <summary>Minimum chunk size to avoid tiny fragments (default: 100)</summary>
        public int MinChunkSize { get; set; } = 100;
        
        /// <summary>Preserve section headers in each chunk</summary>
        public bool KeepHeaders { get; set; } = true;
        
        /// <summary>Add metadata to each chunk (page number, section)</summary>
        public bool AddMetadata { get; set; } = true;
    }

    public class DocumentChunk
    {
        public string Content { get; set; } = string.Empty;
        public int ChunkIndex { get; set; }
        public int PageNumber { get; set; }
        public string? SectionTitle { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new();
    }

    /// <summary>
    /// Chunks text using semantic boundaries (paragraphs, sentences) with overlap.
    /// Best for most documents including PDFs.
    /// </summary>
    public static List<DocumentChunk> ChunkBySemanticBoundaries(
        string text, 
        ChunkOptions? options = null)
    {
        options ??= new ChunkOptions();
        var chunks = new List<DocumentChunk>();
        
        if (string.IsNullOrWhiteSpace(text))
            return chunks;

        // Split by paragraphs first (double newlines)
        var paragraphs = SplitIntoParagraphs(text);
        
        var currentChunk = new StringBuilder();
        var chunkIndex = 0;
        
        foreach (var paragraph in paragraphs)
        {
            var paragraphLength = paragraph.Length;
            
            // If adding this paragraph would exceed max size
            if (currentChunk.Length + paragraphLength > options.MaxChunkSize)
            {
                // Save current chunk if it's substantial
                if (currentChunk.Length >= options.MinChunkSize)
                {
                    chunks.Add(CreateChunk(currentChunk.ToString(), chunkIndex++));
                    
                    // Start new chunk with overlap from previous
                    currentChunk.Clear();
                    if (options.OverlapSize > 0)
                    {
                        var overlap = GetOverlap(chunks[^1].Content, options.OverlapSize);
                        currentChunk.Append(overlap);
                    }
                }
                else
                {
                    currentChunk.Clear();
                }
            }
            
            // If single paragraph is too large, split by sentences
            if (paragraphLength > options.MaxChunkSize)
            {
                var sentences = SplitIntoSentences(paragraph);
                foreach (var sentence in sentences)
                {
                    if (currentChunk.Length + sentence.Length > options.MaxChunkSize)
                    {
                        if (currentChunk.Length >= options.MinChunkSize)
                        {
                            chunks.Add(CreateChunk(currentChunk.ToString(), chunkIndex++));
                            currentChunk.Clear();
                            
                            if (options.OverlapSize > 0)
                            {
                                var overlap = GetOverlap(chunks[^1].Content, options.OverlapSize);
                                currentChunk.Append(overlap);
                            }
                        }
                    }
                    
                    currentChunk.Append(sentence).Append(' ');
                }
            }
            else
            {
                currentChunk.Append(paragraph).Append("\n\n");
            }
        }
        
        // Add final chunk
        if (currentChunk.Length >= options.MinChunkSize)
        {
            chunks.Add(CreateChunk(currentChunk.ToString(), chunkIndex));
        }
        
        return chunks;
    }

    /// <summary>
    /// Chunks text by hierarchical structure (chapters, sections, subsections).
    /// Best for structured documents with clear headings.
    /// </summary>
    public static List<DocumentChunk> ChunkByHierarchy(
        string text,
        ChunkOptions? options = null)
    {
        options ??= new ChunkOptions();
        var chunks = new List<DocumentChunk>();
        
        // Detect sections by common heading patterns
        var sections = DetectSections(text);
        
        var chunkIndex = 0;
        foreach (var section in sections)
        {
            var sectionChunks = ChunkBySemanticBoundaries(section.Content, options);
            
            foreach (var chunk in sectionChunks)
            {
                chunk.ChunkIndex = chunkIndex++;
                chunk.SectionTitle = section.Title;
                
                if (options.KeepHeaders && !string.IsNullOrWhiteSpace(section.Title))
                {
                    chunk.Content = $"[{section.Title}]\n{chunk.Content}";
                }
                
                chunks.Add(chunk);
            }
        }
        
        return chunks.Count > 0 ? chunks : ChunkBySemanticBoundaries(text, options);
    }

    /// <summary>
    /// Chunks text by fixed character count with sentence boundary awareness.
    /// Simple but effective for uniform chunking.
    /// </summary>
    public static List<DocumentChunk> ChunkByFixedSize(
        string text,
        int chunkSize = 1000,
        int overlap = 200)
    {
        var chunks = new List<DocumentChunk>();
        if (string.IsNullOrWhiteSpace(text))
            return chunks;

        var chunkIndex = 0;
        var position = 0;
        
        while (position < text.Length)
        {
            var endPosition = Math.Min(position + chunkSize, text.Length);
            
            // Try to break at sentence boundary
            if (endPosition < text.Length)
            {
                var sentenceEnd = FindNearestSentenceBoundary(text, endPosition);
                if (sentenceEnd > position)
                {
                    endPosition = sentenceEnd;
                }
            }
            
            var chunk = text.Substring(position, endPosition - position).Trim();
            if (chunk.Length > 0)
            {
                chunks.Add(CreateChunk(chunk, chunkIndex++));
            }
            
            // Move forward with overlap
            position = endPosition - overlap;
            if (position >= text.Length - overlap)
                break;
        }
        
        return chunks;
    }

    /// <summary>
    /// Chunks text by token count (approximate) for LLM context windows.
    /// Useful when you know your embedding model's token limits.
    /// </summary>
    public static List<DocumentChunk> ChunkByTokenCount(
        string text,
        int maxTokens = 512,
        int overlapTokens = 50)
    {
        // Rough approximation: 1 token ? 4 characters for English
        var charsPerToken = 4;
        var maxChars = maxTokens * charsPerToken;
        var overlapChars = overlapTokens * charsPerToken;
        
        return ChunkByFixedSize(text, maxChars, overlapChars);
    }

    // Helper Methods

    private static List<string> SplitIntoParagraphs(string text)
    {
        return text
            .Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();
    }

    private static List<string> SplitIntoSentences(string text)
    {
        // Split on sentence boundaries (., !, ?) followed by space or newline
        var pattern = @"(?<=[.!?])\s+(?=[A-Z])";
        return Regex.Split(text, pattern)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }

    private static string GetOverlap(string text, int overlapSize)
    {
        if (text.Length <= overlapSize)
            return text;
        
        var overlap = text.Substring(text.Length - overlapSize);
        
        // Try to start overlap at sentence boundary
        var sentenceStart = overlap.IndexOf(". ", StringComparison.Ordinal);
        if (sentenceStart > 0 && sentenceStart < overlap.Length - 10)
        {
            overlap = overlap.Substring(sentenceStart + 2);
        }
        
        return overlap.Trim();
    }

    private static int FindNearestSentenceBoundary(string text, int position)
    {
        // Look backward for sentence ending punctuation
        var searchStart = Math.Max(0, position - 200);
        var searchText = text.Substring(searchStart, position - searchStart);
        
        var lastPeriod = searchText.LastIndexOf(". ", StringComparison.Ordinal);
        var lastQuestion = searchText.LastIndexOf("? ", StringComparison.Ordinal);
        var lastExclamation = searchText.LastIndexOf("! ", StringComparison.Ordinal);
        
        var boundary = Math.Max(lastPeriod, Math.Max(lastQuestion, lastExclamation));
        
        return boundary > 0 ? searchStart + boundary + 2 : position;
    }

    private static List<(string Title, string Content)> DetectSections(string text)
    {
        var sections = new List<(string Title, string Content)>();
        
        // Common heading patterns
        var headingPatterns = new[]
        {
            @"^#{1,6}\s+(.+)$",                           // Markdown headings
            @"^([A-Z][A-Za-z\s]{2,40}):$",               // Title Case with colon (2-40 chars)
            @"^(\d+\.\s+[A-Z].+)$",                      // 1. Numbered headings
            @"^([A-Z][A-Z\s]{4,50})$",                   // ALL CAPS headings (4-50 chars)
            @"^(Chapter\s+\d+.*)$",                      // Chapter N
            @"^(Section\s+\d+.*)$",                      // Section N
            @"^([A-Z][a-z]+\s+[A-Z][a-z]+(?:\s+[A-Z][a-z]+)*)$",  // Title Case multi-word (e.g., "Monster Attack")
            @"^---\s*Page\s+\d+\s*---$"                  // Page markers (ignore these)
        };
        
        var lines = text.Split('\n');
        string? currentTitle = null; // Changed from "Introduction" to null
        var currentContent = new StringBuilder();
        
        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            
            // Skip empty lines
            if (string.IsNullOrWhiteSpace(trimmedLine))
            {
                currentContent.AppendLine(line);
                continue;
            }
            
            var isHeading = false;
            
            // Skip page markers
            if (Regex.IsMatch(trimmedLine, @"^---\s*Page\s+\d+\s*---$"))
            {
                currentContent.AppendLine(line);
                continue;
            }
            
            foreach (var pattern in headingPatterns)
            {
                var match = Regex.Match(trimmedLine, pattern, RegexOptions.Multiline);
                if (match.Success)
                {
                    // Additional validation for ALL CAPS pattern
                    if (pattern.Contains("[A-Z][A-Z\\s]+"))
                    {
                        // Must be at least 2 words and not too long (likely a heading, not a sentence)
                        var words = trimmedLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (words.Length < 2 || trimmedLine.Length > 60)
                        {
                            continue;
                        }
                    }
                    
                    // Save previous section
                    if (currentContent.Length > 0)
                    {
                        sections.Add((currentTitle ?? "General", currentContent.ToString().Trim()));
                        currentContent.Clear();
                    }
                    
                    currentTitle = match.Groups[1].Value.Trim();
                    isHeading = true;
                    break;
                }
            }
            
            if (!isHeading)
            {
                currentContent.AppendLine(line);
            }
        }
        
        // Add final section
        if (currentContent.Length > 0)
        {
            sections.Add((currentTitle ?? "General", currentContent.ToString().Trim()));
        }
        
        return sections;
    }

    private static DocumentChunk CreateChunk(string content, int index)
    {
        return new DocumentChunk
        {
            Content = content.Trim(),
            ChunkIndex = index,
            Metadata = new Dictionary<string, string>
            {
                ["length"] = content.Length.ToString(),
                ["created"] = DateTime.UtcNow.ToString("O")
            }
        };
    }

    /// <summary>
    /// Estimates the number of tokens in text (rough approximation).
    /// </summary>
    public static int EstimateTokenCount(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;
        
        // Rough estimates:
        // - English: ~4 chars per token
        // - Code: ~3 chars per token
        // - Numbers/symbols: ~2 chars per token
        
        var charCount = text.Length;
        var wordCount = text.Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        
        // Average approach
        return (int)Math.Ceiling(wordCount * 1.3); // ~1.3 tokens per word on average
    }
}
