using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Services.Models;

namespace OfflineAI.Tests.Mocks;

/// <summary>
/// Mock implementation of VectorMemoryRepository for testing without a real database.
/// This mimics the VectorMemoryRepository API without inheriting from it.
/// </summary>
public class MockVectorMemoryRepository
{
    private readonly Dictionary<string, List<MemoryFragmentEntity>> _collections = new();
    private bool _isInitialized = false;

    public MockVectorMemoryRepository()
    {
    }

    public Task InitializeDatabaseAsync()
    {
        _isInitialized = true;
        return Task.CompletedTask;
    }

    public Task<Guid> SaveAsync(MemoryFragmentEntity entity)
    {
        if (!_collections.ContainsKey(entity.CollectionName))
        {
            _collections[entity.CollectionName] = new List<MemoryFragmentEntity>();
        }

        if (entity.Id == Guid.Empty)
        {
            entity.Id = Guid.NewGuid();
        }

        _collections[entity.CollectionName].Add(entity);
        return Task.FromResult(entity.Id);
    }

    public Task BulkSaveAsync(IEnumerable<MemoryFragmentEntity> entities)
    {
        foreach (var entity in entities)
        {
            if (!_collections.ContainsKey(entity.CollectionName))
            {
                _collections[entity.CollectionName] = new List<MemoryFragmentEntity>();
            }

            if (entity.Id == Guid.Empty)
            {
                entity.Id = Guid.NewGuid();
            }

            _collections[entity.CollectionName].Add(entity);
        }

        return Task.CompletedTask;
    }

    public Task<List<MemoryFragmentEntity>> LoadByCollectionAsync(string collectionName)
    {
        if (_collections.TryGetValue(collectionName, out var fragments))
        {
            return Task.FromResult(fragments.ToList());
        }

        return Task.FromResult(new List<MemoryFragmentEntity>());
    }

    public Task<List<MemoryFragmentEntity>> LoadByCollectionPagedAsync(
        string collectionName,
        int pageNumber,
        int pageSize)
    {
        if (_collections.TryGetValue(collectionName, out var fragments))
        {
            var paged = fragments
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToList();
            return Task.FromResult(paged);
        }

        return Task.FromResult(new List<MemoryFragmentEntity>());
    }

    public Task<int> GetCountAsync(string collectionName)
    {
        if (_collections.TryGetValue(collectionName, out var fragments))
        {
            return Task.FromResult(fragments.Count);
        }

        return Task.FromResult(0);
    }

    public Task<bool> HasEmbeddingsAsync(string collectionName)
    {
        if (_collections.TryGetValue(collectionName, out var fragments))
        {
            return Task.FromResult(fragments.Any(f => f.Embedding != null && f.Embedding.Length > 0));
        }

        return Task.FromResult(false);
    }

    public Task<List<string>> GetCollectionsAsync()
    {
        return Task.FromResult(_collections.Keys.ToList());
    }

    public Task DeleteCollectionAsync(string collectionName)
    {
        _collections.Remove(collectionName);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id)
    {
        foreach (var collection in _collections.Values)
        {
            var entity = collection.FirstOrDefault(e => e.Id == id);
            if (entity != null)
            {
                collection.Remove(entity);
                break;
            }
        }

        return Task.CompletedTask;
    }

    public Task UpdateContentAsync(Guid id, string content)
    {
        foreach (var collection in _collections.Values)
        {
            var entity = collection.FirstOrDefault(e => e.Id == id);
            if (entity != null)
            {
                entity.Content = content;
                break;
            }
        }

        return Task.CompletedTask;
    }

    public Task<bool> CollectionExistsAsync(string collectionName)
    {
        return Task.FromResult(_collections.ContainsKey(collectionName));
    }

    public bool IsInitialized => _isInitialized;

    public void Clear()
    {
        _collections.Clear();
        _isInitialized = false;
    }
}
