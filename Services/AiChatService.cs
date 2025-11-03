using Factories;
using Factories.Extensions;
using System.Diagnostics;
using System.Text;
using MemoryLibrary.Models;

namespace Services;

public class AiChatService(
    ILlmMemory memory,
    ILlmMemory conversationMemory,
    string filePath,
    string modelPath,
    int timeoutMs = 30000)
{
    private ILlmMemory Memory { get; } = memory;
    private ILlmMemory ConversationHistory { get; } = conversationMemory;
    private readonly LlmProcessExecutor _processExecutor = new(timeoutMs, conversationMemory);

    public async Task<string> SendMessageStreamAsync(string question)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ConversationHistory.ImportMemory(new MemoryFragment("User", question));

        var systemPrompt = await BuildSystemPromptAsync(question);

        if (systemPrompt.Equals("NoData")) return "Found no matching fragments";
        
        var process = LlmFactory.CreateForBoardGame(filePath, modelPath, maxTokens: 200, temperature: 0.4f)
            .SetLlmContext(systemPrompt)
            .SetPrompt(question)
            .Build();

        return await _processExecutor.ExecuteAsync(process);
    }

    private async Task<string> BuildSystemPromptAsync(string question)
    {
        const string basePrompt =
            "You are a helpful AI assistant. Answer questions accurately and concisely based on the provided context.";

        var prompt = new StringBuilder(basePrompt);

        prompt.AppendLine("\n\nContext:");

        // Use vector search if available
        if (Memory is VectorMemory vectorMemory)
        {
            // Lower threshold to 0.35 - BERT baseline is 0.25-0.30 for unrelated words
            // Scores above 0.35 indicate at least weak semantic relationship
            // Adjusted after investigation showed 0.4 was too restrictive
            var relevantMemory = await vectorMemory.SearchRelevantMemoryAsync(
                question, 
                topK: 5, 
                minRelevanceScore: 0.35);
            
            if (string.IsNullOrWhiteSpace(relevantMemory))
            {
                return "NoData";
            }
            
            prompt.AppendLine(relevantMemory);
        }
        else
        {
            prompt.AppendLine(Memory.ToString());
        }

        // Include conversation history if it exists
        var conversationHistoryText = ConversationHistory.ToString();
        if (!string.IsNullOrWhiteSpace(conversationHistoryText))
        {
            prompt.AppendLine("\n\nRecent conversation:");
            prompt.AppendLine(conversationHistoryText);
        }

        return prompt.ToString();
    }
}