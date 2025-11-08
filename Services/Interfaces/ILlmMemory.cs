using Entities;

namespace Services.Interfaces;

public interface ILlmMemory
{
    void ImportMemory(IMemoryFragment section);
}