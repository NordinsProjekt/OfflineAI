using Application.AI.Chat;
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
            var llmSettings = _appConfig.LlmSettings;
            if (llmSettings == null)
            {
                throw new InvalidOperationException("LLM settings not configured");
            }

            // Create generation settings from request
            var generationSettings = new GenerationSettingsService
            {
                Temperature = request.Temperature,
                MaxTokens = request.MaxTokens,
                TopK = 40, // Default
                TopP = 0.95f, // Default
                RepeatPenalty = 1.1f,
                PresencePenalty = 0.0f,
                FrequencyPenalty = 0.0f,
                TimeoutSeconds = 30,
                RagMode = request.EnableRag,
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

            // Build system prompt
            var systemPrompt = BuildSystemPrompt(ragContext);

            // Create chat service
            var chatService = new AiChatServicePooled(
                _modelPool,
                request.EnableRag && !string.IsNullOrEmpty(ragContext),
                llmSettings,
                generationSettings,
                ragContext);

            // Execute query
            _logger.LogInformation("Executing LLM query with timeout {Timeout}s", generationSettings.TimeoutSeconds);

            var answer = await chatService.SendMessageAsync(
                systemPrompt,
                request.Question,
                cancellationToken);

            stopwatch.Stop();

            // Parse answer and estimate tokens
            var (promptTokens, completionTokens) = EstimateTokens(systemPrompt, request.Question, answer);

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

    private static string BuildSystemPrompt(string? ragContext)
    {
        if (string.IsNullOrEmpty(ragContext))
        {
            return "You are a helpful AI assistant. Provide accurate and concise answers.";
        }

        return $@"You are a helpful AI assistant. Use the following context to answer the user's question.
If the answer cannot be found in the context, say so clearly.

Context:
{ragContext}

Provide accurate and concise answers based on the context above.";
    }

    private static string CleanAnswer(string answer)
    {
        // Remove common LLM artifacts
        answer = answer.Trim();

        // Remove [end of text] markers and similar
        var endMarkers = new[] { "[end of text]", "<|endoftext|>", "</s>", "[EOS]" };
        foreach (var marker in endMarkers)
        {
            var index = answer.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                answer = answer.Substring(0, index).Trim();
            }
        }

        return answer;
    }

    private static (int PromptTokens, int CompletionTokens) EstimateTokens(
        string systemPrompt,
        string question,
        string answer)
    {
        // Simple estimation: ~4 characters per token
        var promptTokens = (systemPrompt.Length + question.Length) / 4;
        var completionTokens = answer.Length / 4;

        return (promptTokens, completionTokens);
    }
}
