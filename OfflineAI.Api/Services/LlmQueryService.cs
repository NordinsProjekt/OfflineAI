using Application.AI.Chat;
using Application.AI.Models;
using Application.AI.Pooling;
using Entities;
using OfflineAI.Api.Models;
using Services.Configuration;
using System.Diagnostics;

namespace OfflineAI.Api.Services;

/// <summary>
/// Implementation of LLM query service using existing AiChatServicePooled.
/// </summary>
public class LlmQueryService : ILlmQueryService
{
    private readonly IModelInstancePool _modelPool;
    private readonly ILogger<LlmQueryService> _logger;
    private readonly AppConfiguration _appConfig;

    public LlmQueryService(
        IModelInstancePool modelPool,
        ILogger<LlmQueryService> logger,
        AppConfiguration appConfig)
    {
        _modelPool = modelPool;
        _logger = logger;
        _appConfig = appConfig;
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

            // Use provided context if available
            string? ragContext = request.Context;
            int documentsRetrieved = 0;

            if (request.EnableRag && string.IsNullOrEmpty(ragContext))
            {
                warnings.Add("RAG requested but no context provided and embedding service not configured");
            }

            // Create simple memory instances for the chat service
            var memory = new SimpleMemory();
            var conversationMemory = new SimpleMemory();

            // Create chat service with correct parameter order
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
            _logger.LogInformation("Executing LLM query for question: {Question}", request.Question);

            var answer = await chatService.SendMessageAsync(
                request.Question,
                cancellationToken);

            stopwatch.Stop();

            // Parse answer and estimate tokens
            var (promptTokens, completionTokens) = EstimateTokens(request.Question, answer);

            var response = new QueryResponse
            {
                Answer = CleanAnswer(answer),
                Model = request.Model ?? "tinyllama",
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
            throw new InvalidOperationException(ex.Message, ex);
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

    private static (int promptTokens, int completionTokens) EstimateTokens(string question, string answer)
    {
        // Simple estimation: ~4 characters per token
        var promptTokens = question.Length / 4;
        var completionTokens = answer.Length / 4;
        return (promptTokens, completionTokens);
    }
}
