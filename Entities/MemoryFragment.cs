namespace Entities;

public class MemoryFragment(string category, string content) : IMemoryFragment
{
    public string Category { get; set; } = category;
    public string Content { get; set; } = content;
    
    /// <summary>
    /// Gets the character length of the content for debugging and validation.
    /// </summary>
    public int ContentLength => Content?.Length ?? 0;

    public override string ToString()
    {
        // Clean format for LLM with game name and section
        // Category format: "Game Title - Section Name"
        return $"## {Category}\n\n{Content}";
    }
}