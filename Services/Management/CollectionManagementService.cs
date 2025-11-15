using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Services.Repositories;
using Services.Interfaces;
using Services.Memory;

namespace Services.Management;

/// <summary>
/// Service for managing vector memory collections.
/// Handles CRUD operations for collections in the vector database.
/// </summary>
public class CollectionManagementService
{
    // Change notification for UI components
    public event Action? OnChange;

    private readonly IVectorMemoryRepository _repository;
    private readonly VectorMemoryPersistenceService _persistenceService;

    private List<string> _availableCollections = new();
    public IReadOnlyList<string> AvailableCollections => _availableCollections.AsReadOnly();

    private string _currentCollection = "game-rules-mpnet";
    public string CurrentCollection
    {
        get => _currentCollection;
        set
        {
            if (_currentCollection == value) return;
            _currentCollection = value;
            NotifyStateChanged();
        }
    }

    public CollectionManagementService(
        IVectorMemoryRepository repository,
        VectorMemoryPersistenceService persistenceService)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _persistenceService = persistenceService ?? throw new ArgumentNullException(nameof(persistenceService));
    }

    /// <summary>
    /// Refresh the list of available collections from the database
    /// </summary>
    public async Task<(bool Success, string Message)> RefreshCollectionsAsync()
    {
        try
        {
            _availableCollections.Clear();
            var collections = await _repository.GetCollectionsAsync();
            _availableCollections.AddRange(collections.OrderBy(c => c));
            NotifyStateChanged();
            return (true, $"Found {collections.Count} collection(s)");
        }
        catch (Exception ex)
        {
            return (false, $"Failed to refresh collections: {ex.Message}");
        }
    }

    /// <summary>
    /// Get information about a specific collection
    /// </summary>
    public async Task<(bool Success, string Message, int Count, bool HasEmbeddings)> GetCollectionInfoAsync(string collectionName)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
        {
            return (false, "Collection name is required", 0, false);
        }

        try
        {
            var exists = await _repository.CollectionExistsAsync(collectionName);
            if (!exists)
            {
                return (false, $"Collection '{collectionName}' does not exist", 0, false);
            }

            var count = await _repository.GetCountAsync(collectionName);
            var hasEmbeddings = await _repository.HasEmbeddingsAsync(collectionName);

            return (true, $"Collection: {collectionName} | Fragments: {count} | Embeddings: {(hasEmbeddings ? "Yes" : "No")}", count, hasEmbeddings);
        }
        catch (Exception ex)
        {
            return (false, $"Failed to get collection info: {ex.Message}", 0, false);
        }
    }

    /// <summary>
    /// Delete a collection from the database
    /// </summary>
    public async Task<(bool Success, string Message)> DeleteCollectionAsync(string collectionName)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
        {
            return (false, "Collection name is required");
        }

        try
        {
            var exists = await _repository.CollectionExistsAsync(collectionName);
            if (!exists)
            {
                return (false, $"Collection '{collectionName}' does not exist");
            }

            await _repository.DeleteCollectionAsync(collectionName);
            
            // Refresh collections list
            await RefreshCollectionsAsync();
            
            return (true, $"Deleted collection: {collectionName}");
        }
        catch (Exception ex)
        {
            return (false, $"Failed to delete collection: {ex.Message}");
        }
    }

    /// <summary>
    /// Load a collection from the database into vector memory for RAG queries
    /// </summary>
    public async Task<(bool Success, string Message, ILlmMemory? Memory)> LoadCollectionAsync(string collectionName)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
        {
            return (false, "Collection name is required", null);
        }

        try
        {
            var exists = await _repository.CollectionExistsAsync(collectionName);
            if (!exists)
            {
                return (false, $"Collection '{collectionName}' does not exist", null);
            }

            var vectorMemory = await _persistenceService.LoadVectorMemoryAsync(collectionName);
            CurrentCollection = collectionName;
            NotifyStateChanged();
            
            return (true, $"Loaded collection '{collectionName}' with {vectorMemory.Count} fragments", vectorMemory);
        }
        catch (Exception ex)
        {
            return (false, $"Failed to load collection: {ex.Message}", null);
        }
    }

    /// <summary>
    /// Check if a collection exists
    /// </summary>
    public async Task<bool> CollectionExistsAsync(string collectionName)
    {
        if (string.IsNullOrWhiteSpace(collectionName)) return false;
        
        try
        {
            return await _repository.CollectionExistsAsync(collectionName);
        }
        catch
        {
            return false;
        }
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}
