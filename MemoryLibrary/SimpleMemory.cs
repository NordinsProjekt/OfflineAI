using System.Text;
using Services;

namespace OfflineAI;

public class SimpleMemory : ILlmMemory
{
    private List<IMemoryFragment> _memory = new List<IMemoryFragment>();
    
    public void ImportMemory(IMemoryFragment section)
    {
        _memory.Add(section);
    }
    
    public override string ToString()
    {
        var sb = new StringBuilder();
        foreach (var fragment in _memory)
        {
            sb.AppendLine(fragment.ToString());
        }
        return sb.ToString();
    }
}