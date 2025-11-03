using Services;

namespace MemoryLibraryBenchmarks.TestMemoryModels;

/// <summary>
/// Test implementation using String.Join for concatenation.
/// Kept for benchmark documentation purposes.
/// Result: FASTER than StringBuilder approach.
/// </summary>
public class StringJoinMemory : ILlmMemory
{
    private readonly List<IMemoryFragment> _memory = new();

    public void ImportMemory(IMemoryFragment memoryFragment)
    {
        _memory.Add(memoryFragment);
    }

    public override string ToString()
    {
        return string.Join(Environment.NewLine, _memory);
    }
}
