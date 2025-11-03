using System.Collections.Generic;
using System.Text;
using Services;

namespace BenchmarkSuite1.TestMemoryModels;

/// <summary>
/// Test implementation using StringBuilder for concatenation.
/// Kept for benchmark documentation purposes.
/// Result: SLOWER than String.Join approach.
/// </summary>
public class StringBuilderMemory : ILlmMemory
{
    private readonly List<IMemoryFragment> _memory = new();

    public void ImportMemory(IMemoryFragment memoryFragment)
    {
        _memory.Add(memoryFragment);
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        foreach (var fragment in _memory)
        {
            sb.AppendLine(fragment.Category);
            sb.AppendLine(fragment.Content);
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
