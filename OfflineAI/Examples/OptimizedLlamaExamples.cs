using Factories;
using Factories.Extensions;
using Services;
using System.Text;
using System.Diagnostics;

namespace OfflineAI.Examples;

public static class OptimizedLlamaExamples
{
}

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