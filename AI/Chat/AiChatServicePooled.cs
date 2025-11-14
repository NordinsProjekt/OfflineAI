using System.Text;
using Entities;
using Services.Interfaces;
using Services.Memory;
using Services.UI;
using Services.Utilities;

namespace Application.AI.Chat;

/// <summary>
/// AI Chat service that uses a pooled persistent LLM process.
/// Designed for web scenarios where the model should stay loaded in memory.
/// </summary>
public class AiChatServicePooled(
    ILlmMemory memory,
    ILlmMemory conversationMemory,
    Application.AI.Pooling.ModelInstancePool modelPool,
    bool debugMode = false,
    bool enableRag = true)
{
    private readonly ILlmMemory _memory = memory ?? throw new ArgumentNullException(nameof(memory));
    private readonly ILlmMemory _conversationMemory = conversationMemory ?? throw new ArgumentNullException(nameof(conversationMemory));
    private readonly Application.AI.Pooling.ModelInstancePool _modelPool = modelPool ?? throw new ArgumentNullException(nameof(modelPool));
    private readonly bool _enableRag = enableRag;

    // Performance tuning constants for TinyLlama
    private const int MaxContextChars = 1500;        // Reduced from ~2771 to prevent overload
    private const int MaxConversationChars = 500;   // Limit conversation history
    private const int MaxFragmentChars = 400;       // Truncate individual fragments
    private const int TopKResults = 3;              // Reduced from 5

    /// <summary>
    /// Send a message and get a response using a pooled LLM instance.
    /// </summary>
    public async Task<string> SendMessageAsync(string question, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        // Store user question in conversation history
        _conversationMemory.ImportMemory(new MemoryFragment("User", question));

        // Build system prompt with context (or skip RAG if disabled)
        string systemPromptResult;
        
        if (_enableRag)
        {
            // RAG mode: search knowledge base for context
            var ragResult = await BuildSystemPromptAsync(question);
            
            // Check if we found any relevant context
            if (ragResult == null)
            {
                return "I don't have any relevant information in my knowledge base to answer that question. " +
                       "Please make sure your question relates to the loaded documents, or add more knowledge files to the inbox folder.";
            }
            
            // Handle special case where game was detected but no results found
            if (ragResult.StartsWith("NO_RESULTS_FOR_GAME:"))
            {
                var gameName = ragResult.Substring("NO_RESULTS_FOR_GAME:".Length);
                return $"I don't have rules for {gameName} loaded in my knowledge base. " +
                       $"Please add the rulebook for {gameName} to the inbox folder, or ask about a different game.";
            }
            
            systemPromptResult = ragResult;
        }
        else
        {
            // Non-RAG mode: simple conversational prompt (no knowledge base search)
            systemPromptResult = "You are a helpful AI assistant. Answer the user's questions directly and concisely.";
        }

        // Acquire a process from the pool
        using var pooledInstance = await _modelPool.AcquireAsync(cancellationToken);

        try
        {
            // Query the persistent process
            if (_enableRag)
            {
                DisplayService.WriteLine("[*] Querying TinyLlama with RAG context... (this may take 10-30 seconds)");
            }
            else
            {
                DisplayService.WriteLine("[*] Querying TinyLlama in direct mode... (this may take 10-30 seconds)");
            }
            
            var response = await pooledInstance.Process.QueryAsync(systemPromptResult, question);

            // Check if response is empty or just whitespace
            if (string.IsNullOrWhiteSpace(response))
            {
                return "[WARNING] TinyLlama returned an empty response. The model may be overloaded or the context was too long. Try asking a more specific question.";
            }

            // Store AI response in conversation history
            _conversationMemory.ImportMemory(new MemoryFragment("AI", response));

            return response;
        }
        catch (TimeoutException tex)
        {
            return $"[ERROR] TinyLlama timed out after waiting for response: {tex.Message}";
        }
        catch (Exception ex)
        {
            return $"[ERROR] Failed to get response from TinyLlama: {ex.GetType().Name} - {ex.Message}";
        }
    }

    private async Task<string?> BuildSystemPromptAsync(string question)
    {
        // Simplified prompt optimized for tiny models
        // Removed verbose instructions that confuse small models
        const string basePrompt = "Answer the question using only the information below.\n";

        // Detect games mentioned in the query
        var detectedGames = GameDetector.DetectGames(question);
        
        if (detectedGames.Count > 0)
        {
            var gameNames = string.Join(", ", detectedGames.Select(GameDetector.GetDisplayName));
            DisplayService.WriteLine($"[*] Detected game(s): {gameNames}");
        }

        // Clean the query by removing game names for better semantic matching
        // This prevents query dilution (e.g., "how to win in Munchkin Panic" -> "how to win")
        var cleanedQuery = RemoveGameNamesFromQuery(question, detectedGames);
        
        if (cleanedQuery != question)
        {
            DisplayService.WriteLine($"[*] Cleaned query: '{question}' -> '{cleanedQuery}'");
        }

        // Use vector search if available with reduced topK
        string? relevantMemory = null;
        if (_memory is VectorMemory vectorMemory)
        {
            relevantMemory = await vectorMemory.SearchRelevantMemoryAsync(
                cleanedQuery,  // Use cleaned query without game names
                topK: TopKResults,  // Reduced from 5 to 3
                minRelevanceScore: 0.3,  // Lowered from 0.5 to 0.3 for better MPNet results
                gameFilter: detectedGames.Count > 0 ? detectedGames : null,
                maxCharsPerFragment: MaxFragmentChars,  // Limit individual fragment size
                includeMetadata: false);  // Don't include [Relevance] and [Category] - confuses LLM
        }
        
        if (relevantMemory == null)
        {
            // If we detected a game but found no results, provide helpful message
            if (detectedGames.Count > 0)
            {
                var gameNames = string.Join(", ", detectedGames.Select(GameDetector.GetDisplayName));
                return $"NO_RESULTS_FOR_GAME:{gameNames}";
            }
            return null;
        }

        // Truncate context if too long
        if (relevantMemory.Length > MaxContextChars)
        {
            DisplayService.WriteLine($"[*] Context truncated from {relevantMemory.Length} to {MaxContextChars} chars");
            relevantMemory = TruncateAtWordBoundary(relevantMemory, MaxContextChars);
        }

        // Show debug output if enabled
        DisplayService.ShowSystemPromptDebug(relevantMemory, debugMode);

        // Simple, direct prompt format for tiny models
        // No fancy formatting that confuses small LLMs
        var prompt = new StringBuilder();
        prompt.Append(basePrompt);
        prompt.AppendLine("\nInformation:");
        prompt.AppendLine(relevantMemory.Trim());

        var finalPrompt = prompt.ToString();
        
        // Final safety check
        if (finalPrompt.Length > 2500)
        {
            DisplayService.WriteLine($"[WARNING] Prompt is {finalPrompt.Length} chars, may cause issues");
        }

        return finalPrompt;
    }

    /// <summary>
    /// Removes detected game names from the query to improve semantic matching.
    /// Example: "how to win in Munchkin Panic" -> "how to win"
    /// </summary>
    private static string RemoveGameNamesFromQuery(string query, List<string> detectedGames)
    {
        if (detectedGames.Count == 0)
            return query;

        var cleanedQuery = query;
        
        foreach (var gameId in detectedGames)
        {
            var gameName = GameDetector.GetDisplayName(gameId);
            
            // Remove exact game name (case-insensitive)
            cleanedQuery = System.Text.RegularExpressions.Regex.Replace(
                cleanedQuery, 
                $@"\b{System.Text.RegularExpressions.Regex.Escape(gameName)}\b",
                "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            // Remove common game name variations (with dashes, spaces)
            cleanedQuery = System.Text.RegularExpressions.Regex.Replace(
                cleanedQuery,
                $@"\b{System.Text.RegularExpressions.Regex.Escape(gameId)}\b",
                "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        
        // Remove common connecting words that may be left over
        cleanedQuery = System.Text.RegularExpressions.Regex.Replace(
            cleanedQuery, 
            @"\b(in|for|about|regarding|concerning)\b",
            "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        // Clean up extra whitespace
        cleanedQuery = System.Text.RegularExpressions.Regex.Replace(cleanedQuery, @"\s+", " ").Trim();
        
        // Remove leading/trailing punctuation
        cleanedQuery = cleanedQuery.Trim(' ', ',', '.', '?', '!');
        
        return string.IsNullOrWhiteSpace(cleanedQuery) ? query : cleanedQuery;
    }

    /// <summary>
    /// Truncate text at word boundary to avoid cutting words in half.
    /// </summary>
    private static string TruncateAtWordBoundary(string text, int maxLength)
    {
        if (text.Length <= maxLength)
            return text;

        var truncated = text.Substring(0, maxLength);
        var lastSpace = truncated.LastIndexOf(' ');
        
        if (lastSpace > maxLength - 50) // Only use word boundary if not too far back
        {
            truncated = truncated.Substring(0, lastSpace);
        }

        return truncated + "...";
    }
}
