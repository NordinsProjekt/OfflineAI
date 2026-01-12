using Application.AI.Chat;
using Application.AI.Models;
using Application.AI.Pooling;
using Entities;
using OfflineAI.Api.Models;
using Services.Configuration;
using Services.Interfaces;
using Services.Memory;
using Services.Repositories;
using Services.Language;
using Microsoft.SemanticKernel.Embeddings;
using System.Diagnostics;

namespace OfflineAI.Api.Services;

/// <summary>
/// Implementation of LLM query service using existing AiChatServicePooled.
/// Supports both manual context and automatic vector search RAG.
/// </summary>
public class LlmQueryService : ILlmQueryService
{
    private readonly IModelInstancePool _modelPool;
    private readonly ILogger<LlmQueryService> _logger;
    private readonly AppConfiguration _appConfig;
    private readonly ITextEmbeddingGenerationService? _embeddingService;
    private readonly IVectorMemoryRepository? _vectorRepository;
    private readonly ILanguageStopWordsService? _stopWordsService;

    public LlmQueryService(
        IModelInstancePool modelPool,
        ILogger<LlmQueryService> logger,
        AppConfiguration appConfig,
        ITextEmbeddingGenerationService? embeddingService = null,
        IVectorMemoryRepository? vectorRepository = null,
        ILanguageStopWordsService? stopWordsService = null)
    {
        _modelPool = modelPool;
        _logger = logger;
        _appConfig = appConfig;
        _embeddingService = embeddingService;
        _vectorRepository = vectorRepository;
        _stopWordsService = stopWordsService;
    }

    public async Task<QueryResponse> QueryAsync(QueryRequest request, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var warnings = new List<string>();

        try
        {
            // Get model settings
            var llmSettings = _appConfig.Llm;
            if (llmSettings == null || string.IsNullOrEmpty(llmSettings.ExecutablePath) || string.IsNullOrEmpty(llmSettings.ModelPath))
            {
                throw new InvalidOperationException(
                    "LLM is not configured. Please set ExecutablePath and ModelPath in User Secrets. " +
                    "Right-click the project > Manage User Secrets");
            }

            // Verify files exist
            if (!System.IO.File.Exists(llmSettings.ExecutablePath))
            {
                throw new InvalidOperationException($"LLM executable not found at: {llmSettings.ExecutablePath}");
            }

            if (!System.IO.File.Exists(llmSettings.ModelPath))
            {
                throw new InvalidOperationException($"LLM model file not found at: {llmSettings.ModelPath}");
            }

            // Create generation settings from request
            var generationSettings = new GenerationSettings
            {
                Temperature = (float)request.Temperature,
                MaxTokens = request.MaxTokens,
                TopK = 40,
                TopP = 0.95f,
                RepeatPenalty = 1.1f,
                PresencePenalty = 0.0f,
                FrequencyPenalty = 0.0f,
                RagTopK = request.TopK,
                RagMinRelevanceScore = request.MinRelevanceScore
            };

            // Determine RAG context and mode
            string? ragContext = request.Context;
            int documentsRetrieved = 0;
            bool usedVectorSearch = false;

            if (request.EnableRag)
            {
                if (string.IsNullOrEmpty(ragContext))
                {
                    // Auto RAG: Use vector search
                    (ragContext, documentsRetrieved) = await RetrieveContextFromVectorSearchAsync(
                        request.Question,
                        request.TopK,
                        request.MinRelevanceScore,
                        request.DomainFilter,
                        warnings);
                    usedVectorSearch = true;
                }
                else
                {
                    // Manual RAG: Use provided context
                    _logger.LogInformation("Using provided manual context ({Length} characters)", ragContext.Length);
                }
            }

            // Create memory instances (use SimpleMemory for now since DatabaseVectorMemory requires more setup)
            var memory = new SimpleMemory();
            var conversationMemory = new SimpleMemory();

            // Create chat service
            var chatService = new AiChatServicePooled(
                memory,
                conversationMemory,
                _modelPool,
                generationSettings,
                llmSettings,
                debugMode: false,
                enableRag: request.EnableRag && !string.IsNullOrEmpty(ragContext),
                showPerformanceMetrics: false);

            // Execute query
            _logger.LogInformation(
                "Executing LLM query. Question: '{Question}', RAG: {RagEnabled}, Vector Search: {VectorSearch}, Docs: {Docs}",
                request.Question,
                request.EnableRag,
                usedVectorSearch,
                documentsRetrieved);

            var answer = await chatService.SendMessageAsync(
                request.Question,
                cancellationToken);

            stopwatch.Stop();

            // Parse answer and estimate tokens
            var (promptTokens, completionTokens) = EstimateTokens(request.Question, answer, ragContext);

            var response = new QueryResponse
            {
                Answer = CleanAnswer(answer),
                Model = request.Model ?? llmSettings.ModelName ?? "local-llm",
                UsedRag = request.EnableRag && !string.IsNullOrEmpty(ragContext),
                DocumentsRetrieved = documentsRetrieved,
                ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens,
                TokensPerSecond = completionTokens / (stopwatch.ElapsedMilliseconds / 1000.0),
                Success = true,
                Warnings = warnings
            };

            _logger.LogInformation(
                "Query completed. Time: {Time}ms, Tokens: {Tokens}, RAG: {Rag}",
                response.ResponseTimeMs,
                response.TotalTokens,
                response.UsedRag);

            return response;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Configuration error");
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Query was cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing query");
            throw;
        }
    }

    /// <summary>
    /// Retrieve context from vector database using semantic search.
    /// </summary>
    private async Task<(string? context, int documentsRetrieved)> RetrieveContextFromVectorSearchAsync(
        string question,
        int topK,
        double minRelevanceScore,
        List<string>? domainFilter,
        List<string> warnings)
    {
        if (_embeddingService == null || _vectorRepository == null || _stopWordsService == null)
        {
            warnings.Add("RAG requested but vector search is not configured. Install embedding service and database to enable automatic context retrieval.");
            _logger.LogWarning("Vector search requested but embedding service, repository, or stop words service is not available");
            return (null, 0);
        }

        try
        {
            _logger.LogInformation(
                "Performing vector search: TopK={TopK}, MinScore={MinScore}, Domains={Domains}",
                topK,
                minRelevanceScore,
                domainFilter != null ? string.Join(", ", domainFilter) : "all");

            var dbConfig = _appConfig.Database ?? new DatabaseSettings();
            var memory = new DatabaseVectorMemory(
                _embeddingService,
                _vectorRepository,
                _stopWordsService,
                dbConfig.ActiveTableName ?? "MemoryFragments");

            var context = await memory.SearchRelevantMemoryAsync(
                question,
                topK: topK,
                minRelevanceScore: minRelevanceScore,
                domainFilter: domainFilter,
                maxCharsPerFragment: 400,
                includeMetadata: false,
                language: "English");

            if (string.IsNullOrEmpty(context))
            {
                warnings.Add($"No relevant documents found in knowledge base (min score: {minRelevanceScore})");
                _logger.LogInformation("Vector search returned no results");
                return (null, 0);
            }

            // Count documents (rough estimate based on fragment separators)
            var docCount = context.Split(new[] { "\n\n", "---" }, StringSplitOptions.RemoveEmptyEntries).Length;

            _logger.LogInformation("Vector search found {Count} relevant fragments ({Length} chars)", docCount, context.Length);
            return (context, docCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during vector search");
            warnings.Add($"Vector search failed: {ex.Message}");
            return (null, 0);
        }
    }

    private static string CleanAnswer(string answer)
    {
        // Remove common LLM artifacts
        answer = answer.Trim();

        // Remove [end of text] markers and similar
        var endMarkers = new[] { "[end of text]", "<|endoftext|>", "</s>", "[EOS]" };
        foreach (var marker in endMarkers)
        {
            answer = answer.Replace(marker, "", StringComparison.OrdinalIgnoreCase);
        }

        return answer.Trim();
    }

    private static (int promptTokens, int completionTokens) EstimateTokens(string question, string answer, string? context = null)
    {
        // Simple estimation: ~4 characters per token
        var questionTokens = question.Length / 4;
        var contextTokens = context?.Length / 4 ?? 0;
        var promptTokens = questionTokens + contextTokens;
        var completionTokens = answer.Length / 4;
        return (promptTokens, completionTokens);
    }
}
