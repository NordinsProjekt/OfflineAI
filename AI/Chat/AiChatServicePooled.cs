using System.Text;
using Entities;
using Services.Interfaces;
using Services.Memory;
using Services.UI;

namespace Application.AI.Chat;

/// <summary>
/// AI Chat service that uses a pooled persistent LLM process.
/// Designed for web scenarios where the model should stay loaded in memory.
/// </summary>
public class AiChatServicePooled
{
    private readonly ILlmMemory _memory;
    private readonly ILlmMemory _conversationMemory;
    private readonly Application.AI.Pooling.ModelInstancePool _modelPool;
    private readonly bool _debugMode;

    public AiChatServicePooled(
        ILlmMemory memory,
        ILlmMemory conversationMemory,
        Application.AI.Pooling.ModelInstancePool modelPool,
        bool debugMode = false)
    {
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _conversationMemory = conversationMemory ?? throw new ArgumentNullException(nameof(conversationMemory));
        _modelPool = modelPool ?? throw new ArgumentNullException(nameof(modelPool));
        _debugMode = debugMode;
    }

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
            "- Keep replies short and direct\n" +
            "- Format the answer with newlines\n";

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

        // Show debug output if enabled
        DisplayService.ShowSystemPromptDebug(relevantMemory, _debugMode);

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
}
