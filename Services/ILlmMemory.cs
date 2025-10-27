namespace Services;

public interface ILlmMemory
{
    string ExportMemory();
    void ImportMemory(string section);
}
