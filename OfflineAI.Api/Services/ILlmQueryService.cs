using OfflineAI.Api.Models;

namespace OfflineAI.Api.Services;

/// <summary>
/// Service interface for LLM query operations.
/// </summary>
public interface ILlmQueryService
{
    /// <summary>
    /// Execute a query against the LLM with optional RAG context.
    /// </summary>
    /// <param name="request">Query request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Query response with answer and metadata</returns>
    Task<QueryResponse> QueryAsync(QueryRequest request, CancellationToken cancellationToken = default);
}
