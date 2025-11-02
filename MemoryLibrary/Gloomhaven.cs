using MemoryLibrary.Models;
using Services;

namespace MemoryLibrary;

public class Gloomhaven : ILlmMemory
{
    private List<IMemoryFragment> Memory = new List<IMemoryFragment>();

    public Gloomhaven()
    {
        Memory.Add(new MemoryFragment("Gloomhaven information",
            "Gloomhaven is a cooperative game of battling monsters and advancing a player’s own individual goals."));
    }

    public override string ToString()
    {
        return string.Join(Environment.NewLine, Memory);
    }

    public void ImportMemory(IMemoryFragment section)
    {
        Memory.Add(section);
    }
}