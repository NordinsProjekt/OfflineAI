using System.Text;
using System.Diagnostics;
using Application.AI.Models;
using Application.AI.Pooling;
using Application.AI.Processing;
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
    IModelInstancePool modelPool,
    GenerationSettings generationSettings,
    LlmSettings? llmSettings = null,
    bool debugMode = false,
    bool enableRag = true,
    bool showPerformanceMetrics = false,
    IDomainDetector? domainDetector = null)
{
    private readonly ILlmMemory _memory = memory ?? throw new ArgumentNullException(nameof(memory));
    private readonly ILlmMemory _conversationMemory = conversationMemory ?? throw new ArgumentNullException(nameof(conversationMemory));
    private readonly IModelInstancePool _modelPool = modelPool ?? throw new ArgumentNullException(nameof(modelPool));
    private readonly GenerationSettings _generationSettings = generationSettings ?? throw new ArgumentNullException(nameof(generationSettings));

    private const int MaxContextChars = 1500;
    private const int MaxFragmentChars = 400;

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

        DisplayGenerationInfo(question);
        _conversationMemory.ImportMemory(new MemoryFragment("User", question));

        var systemPrompt = await PrepareSystemPromptAsync(question);
        if (IsErrorResponse(systemPrompt, out var errorMessage))
        {
            return errorMessage;
        }

        return await GenerateResponseAsync(systemPrompt, question, cancellationToken);
    }

    /// <summary>
    /// Display generation settings and user question.
    /// </summary>
    private void DisplayGenerationInfo(string question)
    {
        DisplayService.ShowGenerationSettings(_generationSettings, enableRag);
        DisplayService.WriteLine($"[*] User question: \"{question}\"");
    }

    /// <summary>
    /// Prepare the system prompt with RAG context or direct instruction.
    /// </summary>
    private async Task<string> PrepareSystemPromptAsync(string question)
    {
        if (!enableRag)
        {
            return "Answer the question directly in one paragraph.";
        }

        var ragResult = await BuildSystemPromptAsync(question);
        return ragResult ?? "NO_RAG_CONTEXT";
    }

    /// <summary>
    /// Check if the system prompt represents an error condition.
    /// </summary>
    private static bool IsErrorResponse(string systemPrompt, out string errorMessage)
    {
        if (systemPrompt == "NO_RAG_CONTEXT")
        {
            errorMessage = "I don't have any relevant information in my knowledge base to answer that question. " +
                          "Please make sure your question relates to the loaded documents, or add more knowledge files to the inbox folder.";
            return true;
        }

        if (systemPrompt.StartsWith("NO_RESULTS_FOR_DOMAIN:"))
        {
            var domainName = systemPrompt.Substring("NO_RESULTS_FOR_DOMAIN:".Length);
            errorMessage = $"I don't have information about {domainName} loaded in my knowledge base. " +
                          $"Please add documents for {domainName} to the inbox folder, or ask about a different topic.";
            return true;
        }

        errorMessage = string.Empty;
        return false;
    }

    /// <summary>
    /// Generate a response from the LLM using the prepared system prompt.
    /// </summary>
    private async Task<string> GenerateResponseAsync(string systemPrompt, string question, CancellationToken cancellationToken)
    {
        using var pooledInstance = await _modelPool.AcquireAsync(cancellationToken);

        try
        {
            DisplayQueryMode();
            
            var stopwatch = Stopwatch.StartNew();
            var response = await QueryLlmAsync(pooledInstance.Process, systemPrompt, question);
            stopwatch.Stop();

            response = CleanLlmResponse(response);
            RecordPerformanceMetrics(stopwatch, response, systemPrompt, question);

            if (string.IsNullOrWhiteSpace(response))
            {
                return "[WARNING] Model returned an empty response. The model may be overloaded or the context was too long. Try asking a more specific question.";
            }

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

    /// <summary>
    /// Display the query mode (RAG or direct).
    /// </summary>
    private void DisplayQueryMode()
    {
        var mode = enableRag ? "RAG context" : "direct mode";
        DisplayService.WriteLine($"[*] Querying with {mode}...");
    }

    /// <summary>
    /// Query the LLM process with the system prompt and question.
    /// </summary>
    private async Task<string> QueryLlmAsync(IPersistentLlmProcess process, string systemPrompt, string question)
    {
        return await process.QueryAsync(
            systemPrompt,
            question,
            maxTokens: _generationSettings.MaxTokens,
            temperature: _generationSettings.Temperature,
            topK: _generationSettings.TopK,
            topP: _generationSettings.TopP,
            repeatPenalty: _generationSettings.RepeatPenalty,
            presencePenalty: _generationSettings.PresencePenalty,
            frequencyPenalty: _generationSettings.FrequencyPenalty,
            useGpu: llmSettings?.UseGpu ?? false,
            gpuLayers: llmSettings?.GpuLayers ?? 0);
    }

    /// <summary>
    /// Clean EOS/EOF markers from LLM response.
    /// </summary>
    private static string CleanLlmResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return response;
        }

        var responseReport = EosEofDebugger.ScanForMarkers(response, "LLM Response");
        EosEofDebugger.LogReport(responseReport, onlyIfDirty: true);

        if (!responseReport.IsClean)
        {
            DisplayService.WriteLine("ℹ️  INFO: EOS/EOF markers found in LLM response (expected) - cleaning...");
            response = EosEofDebugger.CleanMarkers(response);
        }

        return response;
    }

    /// <summary>
    /// Record performance metrics and optionally display them.
    /// </summary>
    private void RecordPerformanceMetrics(Stopwatch stopwatch, string response, string systemPrompt, string question)
    {
        var metrics = new PerformanceMetrics
        {
            TotalTimeMs = stopwatch.Elapsed.TotalMilliseconds,
            CompletionTokens = string.IsNullOrWhiteSpace(response) ? 0 : EstimateTokenCount(response),
            PromptTokens = EstimateTokenCount(systemPrompt + " " + question),
            ModelName = null
        };

        LastMetrics = metrics;

        if (showPerformanceMetrics)
        {
            DisplayService.WriteLine(metrics.ToString());
        }
    }

    /// <summary>
    /// Build system prompt with RAG context from knowledge base.
    /// </summary>
    private async Task<string?> BuildSystemPromptAsync(string question)
    {
        const string basePrompt = "Answer the question using only the information below.\n";

        var detectedDomains = await DetectAndFilterDomainsAsync(question);
        var relevantMemory = await RetrieveRelevantContextAsync(question, detectedDomains);

        if (relevantMemory == null)
        {
            return await HandleMissingContext(detectedDomains);
        }

        relevantMemory = CleanAndValidateContext(relevantMemory);
        
        if (debugMode)
        {
            DisplayService.ShowSystemPromptDebug(relevantMemory, debug: true);
        }

        relevantMemory = TruncateContextIfNeeded(relevantMemory);

        return BuildFinalPrompt(basePrompt, relevantMemory);
    }

    /// <summary>
    /// Detect domains from query and add collection-based domain if applicable.
    /// </summary>
    private async Task<List<string>> DetectAndFilterDomainsAsync(string question)
    {
        var detectedDomains = new List<string>();

        if (domainDetector != null)
        {
            detectedDomains = await domainDetector.DetectDomainsAsync(question);
            await DisplayDetectedDomainsAsync(detectedDomains);
        }

        await AddCollectionBasedDomainAsync(detectedDomains);

        return detectedDomains;
    }

    /// <summary>
    /// Display detected domain names for debugging.
    /// </summary>
    private async Task DisplayDetectedDomainsAsync(List<string> detectedDomains)
    {
        if (detectedDomains.Count > 0 && domainDetector != null)
        {
            var domainNames = new List<string>();
            foreach (var domainId in detectedDomains)
            {
                domainNames.Add(await domainDetector.GetDisplayNameAsync(domainId));
            }
            DisplayService.WriteLine($"[*] Detected domain(s) from query: {string.Join(", ", domainNames)}");
        }
    }

    /// <summary>
    /// Add domain filter based on the current collection name if applicable.
    /// </summary>
    private async Task AddCollectionBasedDomainAsync(List<string> detectedDomains)
    {
        if (_memory is not DatabaseVectorMemory dbMemory || domainDetector == null)
        {
            return;
        }

        var collectionName = dbMemory.GetCollectionName();
        var allDomains = await domainDetector.GetAllDomainsAsync();
        
        var matchingDomain = FindMatchingDomain(allDomains, collectionName);

        if (matchingDomain != default && !detectedDomains.Contains(matchingDomain.DomainId))
        {
            detectedDomains.Add(matchingDomain.DomainId);
            DisplayService.WriteLine($"[*] Added domain filter from collection: {matchingDomain.DisplayName}");
        }
    }

    /// <summary>
    /// Find a domain that matches the collection name.
    /// </summary>
    private static (string DomainId, string DisplayName, string Category) FindMatchingDomain(
        List<(string DomainId, string DisplayName, string Category)> allDomains,
        string collectionName)
    {
        return allDomains.FirstOrDefault(d =>
            d.DisplayName.Equals(collectionName, StringComparison.OrdinalIgnoreCase) ||
            d.DomainId.Equals(collectionName.ToLowerInvariant().Replace(" ", "-"), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Retrieve relevant context from memory using vector search.
    /// </summary>
    private async Task<string?> RetrieveRelevantContextAsync(string question, List<string> detectedDomains)
    {
        DisplayService.WriteLine($"[*] Searching database with: '{question}'");

        if (_memory is not ISearchableMemory searchableMemory)
        {
            return null;
        }

        var relevantMemory = await searchableMemory.SearchRelevantMemoryAsync(
            question,
            topK: _generationSettings.RagTopK,
            minRelevanceScore: _generationSettings.RagMinRelevanceScore,
            domainFilter: detectedDomains.Count > 0 ? detectedDomains : null,
            maxCharsPerFragment: MaxFragmentChars,
            includeMetadata: false);

        return relevantMemory;
    }

    /// <summary>
    /// Handle the case when no relevant context is found.
    /// </summary>
    private async Task<string?> HandleMissingContext(List<string> detectedDomains)
    {
        if (detectedDomains.Count > 0 && domainDetector != null)
        {
            var domainNames = new List<string>();
            foreach (var domainId in detectedDomains)
            {
                domainNames.Add(await domainDetector.GetDisplayNameAsync(domainId));
            }
            return $"NO_RESULTS_FOR_DOMAIN:{string.Join(", ", domainNames)}";
        }

        DisplayService.WriteLine($"[!] Insufficient context found - returning null");
        return null;
    }

    /// <summary>
    /// Clean EOS/EOF markers from retrieved context and validate it.
    /// </summary>
    private static string CleanAndValidateContext(string relevantMemory)
    {
        var ragReport = EosEofDebugger.ScanForMarkers(relevantMemory, "RAG Retrieved Context");
        EosEofDebugger.LogReport(ragReport, onlyIfDirty: true);

        if (!ragReport.IsClean)
        {
            DisplayService.WriteLine("⚠️  WARNING: EOS/EOF markers found in RAG context - cleaning...");
            relevantMemory = EosEofDebugger.CleanMarkers(relevantMemory);

            var verifyReport = EosEofDebugger.ScanForMarkers(relevantMemory, "After Cleaning");
            EosEofDebugger.LogReport(verifyReport, onlyIfDirty: true);
        }

        return relevantMemory;
    }

    /// <summary>
    /// Truncate context if it exceeds the maximum length.
    /// </summary>
    private static string TruncateContextIfNeeded(string relevantMemory)
    {
        if (relevantMemory.Length > MaxContextChars)
        {
            DisplayService.WriteLine($"[*] Context truncated from {relevantMemory.Length} to {MaxContextChars} chars for LLM");
            return TruncateAtWordBoundary(relevantMemory, MaxContextChars);
        }

        return relevantMemory;
    }

    /// <summary>
    /// Build the final system prompt with context and validate it.
    /// </summary>
    private static string BuildFinalPrompt(string basePrompt, string relevantMemory)
    {
        var prompt = new StringBuilder();
        prompt.Append(basePrompt);
        prompt.AppendLine("\nInformation:");
        prompt.AppendLine(relevantMemory.Trim());

        var finalPrompt = prompt.ToString();

        ValidatePromptBeforeLlm(finalPrompt);
        CheckPromptLength(finalPrompt);

        return finalPrompt;
    }

    /// <summary>
    /// Validate that the prompt is clean before sending to LLM.
    /// </summary>
    private static void ValidatePromptBeforeLlm(string finalPrompt)
    {
        try
        {
            EosEofDebugger.ValidateCleanBeforeLlm(finalPrompt, "Final System Prompt");
            DisplayService.WriteLine("✅ System prompt validated - no EOS/EOF markers detected");
        }
        catch (InvalidOperationException ex)
        {
            DisplayService.WriteLine(ex.Message);

            finalPrompt = EosEofDebugger.CleanMarkers(finalPrompt);

            try
            {
                EosEofDebugger.ValidateCleanBeforeLlm(finalPrompt, "After Emergency Cleaning");
                DisplayService.WriteLine("✅ Emergency cleaning successful");
            }
            catch
            {
                throw new InvalidOperationException(
                    "CRITICAL: Unable to clean EOS/EOF markers from system prompt. " +
                    "This would corrupt the LLM input. Aborting query.");
            }
        }
    }

    /// <summary>
    /// Check if prompt length might cause issues.
    /// </summary>
    private static void CheckPromptLength(string finalPrompt)
    {
        if (finalPrompt.Length > 2500)
        {
            DisplayService.WriteLine($"[WARNING] Prompt is {finalPrompt.Length} chars, may cause issues");
        }
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

        if (lastSpace > maxLength - 50)
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

        return (int)Math.Ceiling(text.Length / 4.0);
    }
}
