using System.Text;
using Entities;
using Services.Interfaces;
using Services.Memory;
using Services.Pooling;

namespace Services.AI.Chat;

/// <summary>
/// AI Chat service that uses a pooled persistent LLM process.
/// Designed for web scenarios where the model should stay loaded in memory.
/// </summary>
public class AiChatServicePooled(
    ILlmMemory memory,
    ILlmMemory conversationMemory,
    ModelInstancePool modelPool)
{
    private readonly ILlmMemory _memory = memory ?? throw new ArgumentNullException(nameof(memory));
    private readonly ILlmMemory _conversationMemory = conversationMemory ?? throw new ArgumentNullException(nameof(conversationMemory));
    private readonly ModelInstancePool _modelPool = modelPool ?? throw new ArgumentNullException(nameof(modelPool));

    /// <summary>
    /// Send a message and get a response using a pooled LLM instance.
    /// </summary>
    public async Task<string> SendMessageAsync(string question, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        // Store user question in conversation history
        _conversationMemory.ImportMemory(new MemoryFragment("User", question));

        // Build system prompt with context
        var systemPromptResult = await BuildSystemPromptAsync(question);
        
        // Check if we found any relevant context
        if (systemPromptResult == null)
        {
            return "I don't have any relevant information in my knowledge base to answer that question. " +
                   "Please make sure your question relates to the loaded documents, or add more knowledge files to the inbox folder.";
        }

        // Acquire a process from the pool
        using var pooledInstance = await _modelPool.AcquireAsync(cancellationToken);

        try
        {
            // Query the persistent process
            var response = await pooledInstance.Process.QueryAsync(systemPromptResult, question);

            // Store AI response in conversation history
            if (!string.IsNullOrWhiteSpace(response))
            {
                _conversationMemory.ImportMemory(new MemoryFragment("AI", response));
            }

            return response;
        }
        catch (Exception ex)
        {
            return $"[ERROR] Failed to get response: {ex.Message}";
        }
    }

    private async Task<string?> BuildSystemPromptAsync(string question)
    {
        const string basePrompt =
            "You are a helpful game rule master. Answer questions accurately and concisely based ONLY on the provided context below. " +
            "CRITICAL RULES:\n" +
            "- ONLY use information from the Context section below\n" +
            "- DO NOT add information from your training data\n" +
            "- DO NOT infer or guess rules that aren't explicitly stated\n" +
            "- If the context doesn't contain the answer, say 'I don't have that information in the provided rules'\n" +
            "- Keep replies short and direct\n";

        // Use vector search if available
        string? relevantMemory = null;
        if (_memory is VectorMemory vectorMemory)
        {
            relevantMemory = await vectorMemory.SearchRelevantMemoryAsync(
                question,
                topK: 5,
                minRelevanceScore: 0.6);
        }
        
        if (relevantMemory == null)
        {
            return null;
        }

        // DEBUG: Show exactly what context is being sent to the LLM
        Console.WriteLine("\n╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  SYSTEM PROMPT SENT TO LLM                                   ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        Console.WriteLine($"Relevant Memory Length: {relevantMemory.Length} characters");
        Console.WriteLine($"\nFirst 500 chars of context:");
        Console.WriteLine(relevantMemory.Substring(0, Math.Min(500, relevantMemory.Length)));
        Console.WriteLine($"\n... (total {relevantMemory.Length} chars)");
        Console.WriteLine("═══════════════════════════════════════════════════════════════\n");

        var prompt = new StringBuilder(basePrompt);
        prompt.AppendLine("\n=== CONTEXT (Use ONLY this information) ===");
        prompt.AppendLine(relevantMemory);
        prompt.AppendLine("=== END OF CONTEXT ===");

        // Include conversation history
        var conversationHistoryText = _conversationMemory.ToString();
        if (!string.IsNullOrWhiteSpace(conversationHistoryText))
        {
            prompt.AppendLine("\n=== RECENT CONVERSATION ===");
            prompt.AppendLine(conversationHistoryText);
            prompt.AppendLine("=== END OF CONVERSATION ===");
        }

        return prompt.ToString();
    }

    /// <summary>
    /// Extracts section numbers from text (e.g., "Section 14" -> 14)
    /// </summary>
    private static HashSet<int> ExtractSectionNumbers(string text)
    {
        var sections = new HashSet<int>();
        var regex = new System.Text.RegularExpressions.Regex(@"Section\s+(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var matches = regex.Matches(text);
        
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            if (int.TryParse(match.Groups[1].Value, out int sectionNum))
            {
                sections.Add(sectionNum);
            }
        }
        
        return sections;
    }
}
    