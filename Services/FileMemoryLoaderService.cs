using MemoryLibrary.Models;

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
}