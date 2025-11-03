using MemoryLibrary.Models;
using System.Text;

namespace Services;

/// <summary>
/// AI Chat service that uses a pooled persistent LLM process.
/// Designed for web scenarios where the model should stay loaded in memory.
/// </summary>
public class AiChatServicePooled
{
    private readonly ILlmMemory _memory;
    private readonly ILlmMemory _conversationMemory;
    private readonly ModelInstancePool _modelPool;

    public AiChatServicePooled(
        ILlmMemory memory,
        ILlmMemory conversationMemory,
        ModelInstancePool modelPool)
    {
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _conversationMemory = conversationMemory ?? throw new ArgumentNullException(nameof(conversationMemory));
        _modelPool = modelPool ?? throw new ArgumentNullException(nameof(modelPool));
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
            "You are a helpful AI assistant. Answer questions accurately and concisely based on the provided context.";

        // Use vector search if available
        string? relevantMemory = null;
        if (_memory is VectorMemory vectorMemory)
        {
            relevantMemory = await vectorMemory.SearchRelevantMemoryAsync(
                question,
                topK: 5,
                minRelevanceScore: 0.5); // Raised to 0.5 to filter weak matches
        }
        
        // If no relevant fragments found with score >= 0.5, abort
        if (relevantMemory == null)
        {
            return null;
        }

        var prompt = new StringBuilder(basePrompt);
        prompt.AppendLine("\n\nContext:");
        prompt.AppendLine(relevantMemory);

        // Include conversation history
        var conversationHistoryText = _conversationMemory.ToString();
        if (!string.IsNullOrWhiteSpace(conversationHistoryText))
        {
            prompt.AppendLine("\n\nRecent conversation:");
            prompt.AppendLine(conversationHistoryText);
        }

        return prompt.ToString();
    }
}
