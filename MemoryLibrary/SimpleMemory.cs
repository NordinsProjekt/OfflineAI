using System.Text;
using Services;

namespace OfflineAI;

public class SimpleMemory : ILlmMemory
{
    private readonly StringBuilder _memory = new();

    public string ExportMemory() => _memory.ToString();

    public void ImportMemory(string section)
    {
        if (!string.IsNullOrWhiteSpace(section))
        {
            _memory.AppendLine(section);
        }
    }
}