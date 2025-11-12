using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Entities;
using Microsoft.SemanticKernel.Embeddings;
using OfflineAI.Tests.Mocks;
using Services.Memory;

namespace OfflineAI.Tests.Mocks;

/// <summary>
/// Test-friendly version of VectorMemoryPersistenceService that works with MockVectorMemoryRepository
/// </summary>
public class TestVectorMemoryPersistenceService
{
    private readonly MockVectorMemoryRepository _repository;
    private readonly ITextEmbeddingGenerationService _embeddingService;

    public TestVectorMemoryPersistenceService(
        MockVectorMemoryRepository repository,
        ITextEmbeddingGenerationService embeddingService)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
    }

    public async Task InitializeDatabaseAsync()
    {
        await _repository.InitializeDatabaseAsync();
    }

    public async Task SaveFragmentsAsync(
        List<MemoryFragment> fragments,
        string collectionName,
        string? sourceFile = null,
        bool replaceExisting = false)
    {
        if (replaceExisting && await _repository.CollectionExistsAsync(collectionName))
        {
            await _repository.DeleteCollectionAsync(collectionName);
        }

        var entities = new List<MemoryFragmentEntity>();

        for (int i = 0; i < fragments.Count; i++)
        {
            var fragment = fragments[i];

            // Generate embedding for the content
            var embedding = await _embeddingService.GenerateEmbeddingAsync(fragment.Content);

            var entity = MemoryFragmentEntity.FromMemoryFragment(
                fragment,
                embedding,
                collectionName,
                sourceFile,
                chunkIndex: i + 1);

            entities.Add(entity);
        }

        await _repository.BulkSaveAsync(entities);
    }

    public async Task<VectorMemory> LoadVectorMemoryAsync(string collectionName)
    {
        var entities = await _repository.LoadByCollectionAsync(collectionName);

        if (entities.Count == 0)
        {
            throw new InvalidOperationException(
                $"Collection '{collectionName}' not found or empty in database.");
        }

        var vectorMemory = new VectorMemory(_embeddingService, collectionName);

        foreach (var entity in entities)
        {
            // Import the fragment
            var fragment = entity.ToMemoryFragment();
            vectorMemory.ImportMemory(fragment);

            // Set the pre-computed embedding
            if (entity.Embedding != null)
            {
                var embedding = entity.GetEmbeddingAsMemory();
                vectorMemory.SetEmbeddingForLastFragment(embedding);
            }
        }

        return vectorMemory;
    }

    public async Task<bool> CollectionExistsAsync(string collectionName)
    {
        return await _repository.CollectionExistsAsync(collectionName);
    }

    public async Task<List<string>> GetCollectionsAsync()
    {
        return await _repository.GetCollectionsAsync();
    }

    public async Task DeleteCollectionAsync(string collectionName)
    {
        await _repository.DeleteCollectionAsync(collectionName);
    }

    public async Task<TestCollectionStats> GetCollectionStatsAsync(string collectionName)
    {
        var count = await _repository.GetCountAsync(collectionName);
        var hasEmbeddings = await _repository.HasEmbeddingsAsync(collectionName);

        return new TestCollectionStats
        {
            CollectionName = collectionName,
            FragmentCount = count,
            HasEmbeddings = hasEmbeddings
        };
    }
}

public record TestCollectionStats
{
    public string CollectionName { get; init; } = string.Empty;
    public int FragmentCount { get; init; }
    public bool HasEmbeddings { get; init; }
}
