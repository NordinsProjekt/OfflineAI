using System.Text;
using System.Diagnostics;
using Application.AI.Models;
using Application.AI.Utilities;
using Entities;
using Services.Configuration;
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
    GenerationSettings generationSettings,
    bool debugMode = false,
    bool enableRag = true,
    bool showPerformanceMetrics = false,
    DomainDetector? domainDetector = null)
{
    private readonly ILlmMemory _memory = memory ?? throw new ArgumentNullException(nameof(memory));
    private readonly ILlmMemory _conversationMemory = conversationMemory ?? throw new ArgumentNullException(nameof(conversationMemory));
    private readonly Application.AI.Pooling.ModelInstancePool _modelPool = modelPool ?? throw new ArgumentNullException(nameof(modelPool));
    private readonly GenerationSettings _generationSettings = generationSettings ?? throw new ArgumentNullException(nameof(generationSettings));
    private readonly DomainDetector? _domainDetector = domainDetector;
    private readonly bool _enableRag = enableRag;
    private readonly bool _debugMode = debugMode;
    private readonly bool _showPerformanceMetrics = showPerformanceMetrics;

    // Performance tuning constants for TinyLlama
    private const int MaxContextChars = 1500;        // Reduced from ~2771 to prevent overload
    private const int MaxFragmentChars = 400;       // Truncate individual fragments
    private const int TopKResults = 3;              // Reduced from 5

    /// <summary>
    /// Last performance metrics from generation
    /// </summary>
    public PerformanceMetrics? LastMetrics { get; private set; }

    /// <summary>
    /// Send a message and get a response using a pooled LLM instance.
    /// </summary>
    public async Task<string> SendMessageAsync(string question, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        // Display generation settings being used for this query
        DisplayService.ShowGenerationSettings(_generationSettings, enableRag);
        DisplayService.WriteLine($"[*] User question: \"{question}\"");

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
            if (ragResult.StartsWith("NO_RESULTS_FOR_DOMAIN:"))
            {
                var domainName = ragResult.Substring("NO_RESULTS_FOR_DOMAIN:".Length);
                return $"I don't have information about {domainName} loaded in my knowledge base. " +
                       $"Please add documents for {domainName} to the inbox folder, or ask about a different topic.";
            }
            
            systemPromptResult = ragResult;
        }
        else
        {
            // Non-RAG mode: Simple, direct instruction
            // Balance between preventing dialogues and allowing useful responses
            systemPromptResult = "Answer the question directly in one paragraph.";
        }

        // Acquire a process from the pool
        using var pooledInstance = await _modelPool.AcquireAsync(cancellationToken);

        try
        {
            // Use configured max tokens - don't limit for non-RAG mode
            var maxTokens = _generationSettings.MaxTokens;
            
            // Query the persistent process
            if (_enableRag)
            {
                DisplayService.WriteLine("[*] Querying with RAG context...");
            }
            else
            {
                DisplayService.WriteLine("[*] Querying in direct mode...");
            }
            
            // Start performance tracking
            var stopwatch = Stopwatch.StartNew();
            
            var response = await pooledInstance.Process.QueryAsync(
                systemPromptResult, 
                question,
                maxTokens: maxTokens,
                temperature: _generationSettings.Temperature,
                topK: _generationSettings.TopK,
                topP: _generationSettings.TopP,
                repeatPenalty: _generationSettings.RepeatPenalty,
                presencePenalty: _generationSettings.PresencePenalty,
                frequencyPenalty: _generationSettings.FrequencyPenalty);

            stopwatch.Stop();

            // Calculate performance metrics (do this before checking response validity)
            var metrics = new PerformanceMetrics
            {
                TotalTimeMs = stopwatch.Elapsed.TotalMilliseconds,
                CompletionTokens = string.IsNullOrWhiteSpace(response) ? 0 : EstimateTokenCount(response),
                PromptTokens = EstimateTokenCount(systemPromptResult + " " + question),
                ModelName = null // Model name not available from PersistentLlmProcess
            };
            
            LastMetrics = metrics;
            
            // Display performance metrics if enabled (even for empty responses)
            if (_showPerformanceMetrics)
            {
                DisplayService.WriteLine(metrics.ToString());
            }

            // Check if response is empty or just whitespace
            if (string.IsNullOrWhiteSpace(response))
            {
                return "[WARNING] Model returned an empty response. The model may be overloaded or the context was too long. Try asking a more specific question.";
            }

            // Store AI response in conversation history
            _conversationMemory.ImportMemory(new MemoryFragment("AI", response));

            return response;
        }
        catch (TimeoutException tex)
        {
            return $"[ERROR] TinyLamma timed out after waiting for response: {tex.Message}";
        }
        catch (Exception ex)
        {
            return $"[ERROR] Failed to get response from TinyLlama: {ex.GetType().Name} - {ex.Message}";
        }
    }

    private async Task<string?> BuildSystemPromptAsync(string question)
    {
        // Simplified prompt optimized for tiny models
        const string basePrompt = "Answer the question using only the information below.\n";

        // Detect domains mentioned in the query
        List<string> detectedDomains = new();
        
        if (_domainDetector != null)
        {
            detectedDomains = await _domainDetector.DetectDomainsAsync(question);
            
            if (detectedDomains.Count > 0)
            {
                var domainNames = new List<string>();
                foreach (var domainId in detectedDomains)
                {
                    domainNames.Add(await _domainDetector.GetDisplayNameAsync(domainId));
                }
                DisplayService.WriteLine($"[*] Detected domain(s): {string.Join(", ", domainNames)}");
            }
        }

        // IMPORTANT: Do NOT remove domain names from the query before database search
        // Domain names are crucial for matching titles in embeddings
        // The database search needs the full query INCLUDING domain names to match properly
        string queryForSearch = question;
        
        DisplayService.WriteLine($"[*] Searching database with: '{queryForSearch}'");

        // Use vector search if available
        string? relevantMemory = null;
        if (_memory is ISearchableMemory searchableMemory)
        {
            relevantMemory = await searchableMemory.SearchRelevantMemoryAsync(
                queryForSearch,
                topK: TopKResults,
                minRelevanceScore: 0.3,
                gameFilter: detectedDomains.Count > 0 ? detectedDomains : null,
                maxCharsPerFragment: MaxFragmentChars,
                includeMetadata: false);
        }
        
        if (relevantMemory == null)
        {
            // If we detected a domain but found no results
            if (detectedDomains.Count > 0 && _domainDetector != null)
            {
                var domainNames = new List<string>();
                foreach (var domainId in detectedDomains)
                {
                    domainNames.Add(await _domainDetector.GetDisplayNameAsync(domainId));
                }
                return $"NO_RESULTS_FOR_DOMAIN:{string.Join(", ", domainNames)}";
            }
            return null;
        }

        // DON'T clean the query - domain names provide important context for the LLM
        // The domain filtering already happened during the vector search above
        // Removing domain names can create grammatically incorrect queries
        
        // Example: "How to win in Gloomhaven?" is clearer than "How to win?"
        // The retrieved fragments already contain Gloomhaven-specific context

        // Truncate context if too long
        if (relevantMemory.Length > MaxContextChars)
        {
            DisplayService.WriteLine($"[*] Context truncated from {relevantMemory.Length} to {MaxContextChars} chars");
            relevantMemory = TruncateAtWordBoundary(relevantMemory, MaxContextChars);
        }

        // Show debug output if enabled
        DisplayService.ShowSystemPromptDebug(relevantMemory, debugMode);

        // Simple prompt format - use the ORIGINAL question with domain name intact
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

    /// <summary>
    /// Estimate token count from text (rough approximation: 1 token ≈ 4 characters for English)
    /// This is an approximation since we don't have access to the actual tokenizer
    /// </summary>
    private static int EstimateTokenCount(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;
        
        // Rough estimate: ~4 characters per token for English text
        // This varies by tokenizer but gives a reasonable approximation
        return (int)Math.Ceiling(text.Length / 4.0);
    }
}
