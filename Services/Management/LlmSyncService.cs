using Services.Repositories;

namespace Services.Management;

/// <summary>
/// Service to manage LLM model synchronization with the database.
/// Scans the LLM folder and ensures all models are registered in the database.
/// </summary>
public class LlmSyncService
{
    private readonly ILlmRepository _llmRepository;
    private readonly string _llmFolderPath;

    public LlmSyncService(ILlmRepository llmRepository, string llmFolderPath)
    {
        _llmRepository = llmRepository ?? throw new ArgumentNullException(nameof(llmRepository));
        _llmFolderPath = llmFolderPath ?? throw new ArgumentNullException(nameof(llmFolderPath));
    }

    /// <summary>
    /// Scans the LLM folder for .gguf files and adds them to the database if they don't exist.
    /// </summary>
    public async Task<(int Added, int Existing, int Total)> SyncLlmsAsync()
    {
        Console.WriteLine($"\n[*] Scanning LLM folder: {_llmFolderPath}");

        if (!Directory.Exists(_llmFolderPath))
        {
            Console.WriteLine($"[!] LLM folder not found: {_llmFolderPath}");
            return (0, 0, 0);
        }

        // Find all .gguf files
        var ggufFiles = Directory.GetFiles(_llmFolderPath, "*.gguf", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(f => f != null)
            .Cast<string>()
            .OrderBy(f => f)
            .ToList();

        if (!ggufFiles.Any())
        {
            Console.WriteLine("[*] No GGUF model files found in folder");
            return (0, 0, 0);
        }

        Console.WriteLine($"[*] Found {ggufFiles.Count} GGUF model file(s)");

        int added = 0;
        int existing = 0;

        foreach (var modelFileName in ggufFiles)
        {
            var exists = await _llmRepository.LlmExistsAsync(modelFileName);
            
            if (exists)
            {
                existing++;
                Console.WriteLine($"    [EXISTS] {modelFileName}");
            }
            else
            {
                await _llmRepository.AddOrGetLlmAsync(modelFileName);
                added++;
                Console.WriteLine($"    [ADDED] {modelFileName}");
            }
        }

        Console.WriteLine($"[+] LLM sync complete: {added} added, {existing} existing, {ggufFiles.Count} total");

        return (added, existing, ggufFiles.Count);
    }

    /// <summary>
    /// Get the ID of an LLM by its model file name.
    /// </summary>
    public async Task<Guid?> GetLlmIdByNameAsync(string llmName)
    {
        if (string.IsNullOrWhiteSpace(llmName))
            return null;

        var llm = await _llmRepository.GetLlmByNameAsync(llmName);
        return llm?.Id;
    }

    /// <summary>
    /// Get or add an LLM by its model file name.
    /// Returns the LLM ID.
    /// </summary>
    public async Task<Guid> EnsureLlmExistsAsync(string llmName)
    {
        if (string.IsNullOrWhiteSpace(llmName))
            throw new ArgumentException("LLM name cannot be empty", nameof(llmName));

        return await _llmRepository.AddOrGetLlmAsync(llmName);
    }
}
