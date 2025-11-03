using MemoryLibrary.Models;
using System.Text;

namespace Services;

public class FileMemoryLoaderService
{
    /// <summary>
    /// Loads a text file and parses it into MemoryFragments.
    /// Expected format:
    /// # Board Game Title
    /// Paragraph content here...
    /// 
    /// # Another Board Game Title
    /// More paragraph content...
    /// </summary>
    /// <param name="filePath">Path to the text file</param>
    /// <param name="memory">ILlmMemory instance to import fragments into</param>
    /// <returns>Number of fragments loaded</returns>
    public async Task<int> LoadFromFileAsync(string filePath, ILlmMemory memory)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}");
        }

        var lines = await File.ReadAllLinesAsync(filePath);
        return ParseAndImport(lines, memory);
    }

    /// <summary>
    /// Synchronous version of LoadFromFileAsync
    /// </summary>
    public int LoadFromFile(string filePath, ILlmMemory memory)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}");
        }

        var lines = File.ReadAllLines(filePath);
        return ParseAndImport(lines, memory);
    }

    private static int ParseAndImport(string[] lines, ILlmMemory memory)
    {
        string? currentCategory = null;
        var currentContent = new List<string>();
        int fragmentCount = 0;

        foreach (var line in lines)
        {
            // Check if this is a category/title line (starts with #)
            if (line.TrimStart().StartsWith("#"))
            {
                // Save previous fragment if exists
                if (currentCategory != null && currentContent.Count > 0)
                {
                    var content = string.Join(Environment.NewLine, currentContent).Trim();
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        memory.ImportMemory(new MemoryFragment(currentCategory, content));
                        fragmentCount++;
                    }
                }

                // Start new fragment
                currentCategory = line.TrimStart().TrimStart('#').Trim();
                currentContent.Clear();
            }
            else
            {
                // Add content line to current fragment
                if (!string.IsNullOrWhiteSpace(line) || currentContent.Count > 0)
                {
                    currentContent.Add(line);
                }
            }
        }

        // Don't forget the last fragment
        if (currentCategory != null && currentContent.Count > 0)
        {
            var content = string.Join(Environment.NewLine, currentContent).Trim();
            if (!string.IsNullOrWhiteSpace(content))
            {
                memory.ImportMemory(new MemoryFragment(currentCategory, content));
                fragmentCount++;
            }
        }

        return fragmentCount;
    }

    /// <summary>
    /// Loads from a simple format where each line alternates between category and content
    /// Line 1: Category
    /// Line 2: Content
    /// Line 3: Category
    /// Line 4: Content
    /// etc.
    /// </summary>
    public static async Task<int> LoadFromSimpleFormatAsync(string filePath, ILlmMemory memory)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}");
        }

        var lines = await File.ReadAllLinesAsync(filePath);
        int fragmentCount = 0;

        for (int i = 0; i < lines.Length - 1; i += 2)
        {
            var category = lines[i].Trim();
            var content = lines[i + 1].Trim();

            if (!string.IsNullOrWhiteSpace(category) && !string.IsNullOrWhiteSpace(content))
            {
                memory.ImportMemory(new MemoryFragment(category, content));
                fragmentCount++;
            }
        }

        return fragmentCount;
    }

    /// <summary>
    /// Loads from a format where paragraphs are separated by blank lines,
    /// and the first line of each paragraph is the category
    /// </summary>
    public static async Task<int> LoadFromParagraphFormatAsync(string filePath, ILlmMemory memory,
        string? defaultCategory = null)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}");
        }

        var content = await File.ReadAllTextAsync(filePath);
        var paragraphs = content.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
        int fragmentCount = 0;

        foreach (var paragraph in paragraphs)
        {
            var trimmedParagraph = paragraph.Trim();
            if (string.IsNullOrWhiteSpace(trimmedParagraph))
                continue;

            var lines = trimmedParagraph.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            if (lines.Length > 0)
            {
                var category = defaultCategory ?? lines[0].Trim();
                var paragraphContent = lines.Length > 1
                    ? string.Join(Environment.NewLine, lines.Skip(1)).Trim()
                    : trimmedParagraph;

                if (!string.IsNullOrWhiteSpace(paragraphContent))
                {
                    memory.ImportMemory(new MemoryFragment(category, paragraphContent));
                    fragmentCount++;
                }
            }
        }

        return fragmentCount;
    }

    /// <summary>
    /// Loads a text file and chunks it into smaller sections for better vector search.
    /// This is optimized for semantic search where smaller, focused chunks work better.
    /// </summary>
    /// <param name="filePath">Path to the text file</param>
    /// <param name="memory">ILlmMemory instance to import fragments into</param>
    /// <param name="maxChunkSize">Maximum characters per chunk (default: 500)</param>
    /// <param name="overlapSize">Number of characters to overlap between chunks (default: 50)</param>
    /// <returns>Number of fragments loaded</returns>
    public async Task<int> LoadFromFileWithChunkingAsync(
        string filePath,
        ILlmMemory memory,
        int maxChunkSize = 500,
        int overlapSize = 50)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}");
        }

        var content = await File.ReadAllTextAsync(filePath);
        var fileName = Path.GetFileNameWithoutExtension(filePath);

        return ChunkAndImport(content, memory, fileName, maxChunkSize, overlapSize);
    }

    /// <summary>
    /// Chunks text into smaller pieces with optional overlap for better context preservation.
    /// </summary>
    private static int ChunkAndImport(
        string content,
        ILlmMemory memory,
        string category,
        int maxChunkSize,
        int overlapSize)
    {
        if (string.IsNullOrWhiteSpace(content))
            return 0;

        int fragmentCount = 0;
        int startIndex = 0;

        while (startIndex < content.Length)
        {
            int chunkSize = Math.Min(maxChunkSize, content.Length - startIndex);

            // Try to break at sentence or paragraph boundaries
            if (startIndex + chunkSize < content.Length)
            {
                // Look for sentence ending within the last 100 characters of the chunk
                int searchStart = Math.Max(0, chunkSize - 100);
                int lastPeriod = content.LastIndexOfAny(new[] { '.', '!', '?' }, startIndex + chunkSize,
                    chunkSize - searchStart);

                if (lastPeriod > startIndex)
                {
                    chunkSize = lastPeriod - startIndex + 1;
                }
                else
                {
                    // Fallback: break at last space
                    int lastSpace = content.LastIndexOf(' ', startIndex + chunkSize, chunkSize);
                    if (lastSpace > startIndex)
                    {
                        chunkSize = lastSpace - startIndex;
                    }
                }
            }

            var chunk = content.Substring(startIndex, chunkSize).Trim();

            if (!string.IsNullOrWhiteSpace(chunk))
            {
                memory.ImportMemory(new MemoryFragment($"{category}_chunk_{fragmentCount + 1}", chunk));
                fragmentCount++;
            }

            // Move forward with overlap
            startIndex += chunkSize - overlapSize;

            // Prevent infinite loop on small overlaps
            if (startIndex + overlapSize >= content.Length)
                break;
        }

        return fragmentCount;
    }

    /// <summary>
    /// Loads a file and chunks it by sections (marked with #) and further sub-chunks large sections.
    /// Best for knowledge bases with headers and large content blocks.
    /// </summary>
    public async Task<int> LoadFromFileWithSmartChunkingAsync(
        string filePath,
        ILlmMemory memory,
        int maxChunkSize = 500)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}");
        }

        var lines = await File.ReadAllLinesAsync(filePath);
        return ParseAndImportWithChunking(lines, memory, maxChunkSize);
    }

    private static int ParseAndImportWithChunking(string[] lines, ILlmMemory memory, int maxChunkSize)
    {
        string? currentCategory = null;
        var currentContent = new StringBuilder();
        int fragmentCount = 0;

        foreach (var line in lines)
        {
            // Check if this is a category/title line (starts with #)
            if (line.TrimStart().StartsWith("#"))
            {
                // Save previous fragment(s) if exists
                if (currentCategory != null && currentContent.Length > 0)
                {
                    fragmentCount +=
                        ChunkAndImportSection(currentContent.ToString(), currentCategory, memory, maxChunkSize);
                }

                // Start new section
                currentCategory = line.TrimStart().TrimStart('#').Trim();
                currentContent.Clear();
            }
            else
            {
                // Add content line to current section
                currentContent.AppendLine(line);
            }
        }

        // Don't forget the last section
        if (currentCategory != null && currentContent.Length > 0)
        {
            fragmentCount += ChunkAndImportSection(currentContent.ToString(), currentCategory, memory, maxChunkSize);
        }

        return fragmentCount;
    }

    private static int ChunkAndImportSection(string content, string category, ILlmMemory memory, int maxChunkSize)
    {
        content = content.Trim();

        if (string.IsNullOrWhiteSpace(content))
            return 0;

        // If content is small enough, import as single fragment
        if (content.Length <= maxChunkSize)
        {
            memory.ImportMemory(new MemoryFragment(category, content));
            return 1;
        }

        // Otherwise, chunk it
        return ChunkAndImport(content, memory, category, maxChunkSize, overlapSize: 50);
    }

    /// <summary>
    /// Loads from a format where YOU control the sections with double newlines.
    /// This is BETTER for rulebooks where you want complete, meaningful chunks.
    /// 
    /// Format:
    /// Section content here...
    /// Can span multiple lines.
    /// 
    /// New section starts after double newline.
    /// This preserves semantic boundaries.
    /// </summary>
    /// <param name="filePath">Path to the text file</param>
    /// <param name="memory">ILlmMemory instance to import fragments into</param>
    /// <param name="defaultCategory">Category name for all sections (e.g., "Game Rules")</param>
    /// <param name="autoNumberSections">Add section numbers to categories</param>
    /// <returns>Number of fragments loaded</returns>
    public async Task<int> LoadFromManualSectionsAsync(
        string filePath,
        ILlmMemory memory,
        string? defaultCategory = null,
        bool autoNumberSections = true)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}");
        }

        var content = await File.ReadAllTextAsync(filePath);
        var fileName = defaultCategory ?? Path.GetFileNameWithoutExtension(filePath);

        // Split by double newlines (your manual section boundaries)
        var sections = content.Split(
            new[] { "\r\n\r\n", "\n\n" },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        int fragmentCount = 0;

        foreach (var section in sections)
        {
            var trimmedSection = section.Trim();
            if (string.IsNullOrWhiteSpace(trimmedSection))
                continue;

            // Extract first line as potential title/category
            var lines = trimmedSection.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            string category;
            string content_value;

            // Check if first line looks like a header (short, no punctuation at end)
            if (lines.Length > 1 &&
                lines[0].Length < 100 &&
                !lines[0].TrimEnd().EndsWith('.') &&
                !lines[0].TrimEnd().EndsWith(':'))
            {
                // Use first line as category
                category = autoNumberSections
                    ? $"{fileName} - Section {fragmentCount + 1}: {lines[0].Trim()}"
                    : lines[0].Trim();
                content_value = string.Join(Environment.NewLine, lines.Skip(1)).Trim();
            }
            else
            {
                // Use entire section as content
                category = autoNumberSections
                    ? $"{fileName} - Section {fragmentCount + 1}"
                    : fileName;
                content_value = trimmedSection;
            }

            if (!string.IsNullOrWhiteSpace(content_value))
            {
                memory.ImportMemory(new MemoryFragment(category, content_value));
                fragmentCount++;
            }
        }

        return fragmentCount;
    }

    // ...existing code...
}