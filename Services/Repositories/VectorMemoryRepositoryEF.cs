using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Services.Models;

namespace Services.Repositories;

/// <summary>
/// Entity Framework Core implementation of vector memory repository.
/// Handles CRUD operations and bulk inserts for vector embeddings.
/// </summary>
public class VectorMemoryRepositoryEF : IVectorMemoryRepository
{
    private readonly VectorMemoryDbContext _context;
    
    public VectorMemoryRepositoryEF(VectorMemoryDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }
    
    /// <summary>
    /// Initialize database schema. Call this once during setup.
    /// </summary>
    public async Task InitializeDatabaseAsync()
    {
        await _context.Database.EnsureCreatedAsync();
    }
    
    /// <summary>
    /// Save a single memory fragment with its vector embedding.
    /// </summary>
    public async Task<Guid> SaveAsync(MemoryFragmentEntity entity)
    {
        _context.MemoryFragments.Add(entity);
        await _context.SaveChangesAsync();
        
        return entity.Id;
    }
    
    /// <summary>
    /// Bulk insert for performance when loading many fragments.
    /// Significantly faster than individual inserts.
    /// </summary>
    public async Task BulkSaveAsync(IEnumerable<MemoryFragmentEntity> entities)
    {
        _context.MemoryFragments.AddRange(entities);
        await _context.SaveChangesAsync();
    }
    
    /// <summary>
    /// Load all fragments for a collection (including embeddings).
    /// </summary>
    public async Task<List<MemoryFragmentEntity>> LoadByCollectionAsync(string collectionName)
    {
        return await _context.MemoryFragments
            .Where(f => f.CollectionName == collectionName)
            .OrderBy(f => f.ChunkIndex)
            .ThenBy(f => f.CreatedAt)
            .ToListAsync();
    }
    
    /// <summary>
    /// Load fragments with pagination support.
    /// </summary>
    public async Task<List<MemoryFragmentEntity>> LoadByCollectionPagedAsync(
        string collectionName, 
        int pageNumber, 
        int pageSize)
    {
        return await _context.MemoryFragments
            .Where(f => f.CollectionName == collectionName)
            .OrderBy(f => f.ChunkIndex)
            .ThenBy(f => f.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }
    
    /// <summary>
    /// Get count of fragments in a collection.
    /// </summary>
    public async Task<int> GetCountAsync(string collectionName)
    {
        return await _context.MemoryFragments
            .Where(f => f.CollectionName == collectionName)
            .CountAsync();
    }
    
    /// <summary>
    /// Check if embeddings exist for a collection.
    /// </summary>
    public async Task<bool> HasEmbeddingsAsync(string collectionName)
    {
        return await _context.MemoryFragments
            .AnyAsync(f => f.CollectionName == collectionName && f.Embedding != null);
    }
    
    /// <summary>
    /// Get all unique collection names.
    /// </summary>
    public async Task<List<string>> GetCollectionsAsync()
    {
        return await _context.MemoryFragments
            .Select(f => f.CollectionName)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();
    }
    
    /// <summary>
    /// Delete all fragments in a collection.
    /// </summary>
    public async Task DeleteCollectionAsync(string collectionName)
    {
        var fragments = await _context.MemoryFragments
            .Where(f => f.CollectionName == collectionName)
            .ToListAsync();
        
        _context.MemoryFragments.RemoveRange(fragments);
        await _context.SaveChangesAsync();
    }
    
    /// <summary>
    /// Delete a specific fragment by ID.
    /// </summary>
    public async Task DeleteAsync(Guid id)
    {
        var fragment = await _context.MemoryFragments.FindAsync(id);
        if (fragment != null)
        {
            _context.MemoryFragments.Remove(fragment);
            await _context.SaveChangesAsync();
        }
    }
    
    /// <summary>
    /// Update fragment content (preserves embedding).
    /// </summary>
    public async Task UpdateContentAsync(Guid id, string content)
    {
        var fragment = await _context.MemoryFragments.FindAsync(id);
        if (fragment != null)
        {
            fragment.Content = content;
            fragment.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }
    
    /// <summary>
    /// Check if a collection exists.
    /// </summary>
    public async Task<bool> CollectionExistsAsync(string collectionName)
    {
        return await _context.MemoryFragments
            .AnyAsync(f => f.CollectionName == collectionName);
    }
}
