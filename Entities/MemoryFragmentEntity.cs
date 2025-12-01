namespace Entities;

/// <summary>
/// Database entity for storing memory fragments with vector embeddings.
/// Maps to the MemoryFragments table in MSSQL.
/// Supports multiple embedding strategies for improved semantic search.
/// </summary>
public class MemoryFragmentEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public string CollectionName { get; set; } = string.Empty;
    
    public string Category { get; set; } = string.Empty;
    
    public string Content { get; set; } = string.Empty;
    
    /// <summary>
    /// Primary embedding: Combined category + content (legacy/default).
    /// Vector embedding stored as byte array.
    /// Each float is 4 bytes, so a 768-dimension embedding = 3,072 bytes.
    /// </summary>
    public byte[]? Embedding { get; set; }
    
    /// <summary>
    /// Category-only embedding for better category matching.
    /// Helps match queries that focus on domain/topic names.
    /// </summary>
    public byte[]? CategoryEmbedding { get; set; }
    
    /// <summary>
    /// Content-only embedding for semantic content matching.
    /// Helps match queries about specific details.
    /// </summary>
    public byte[]? ContentEmbedding { get; set; }
    
    public int? EmbeddingDimension { get; set; }
    
    // Metadata
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    public string? SourceFile { get; set; }
    
    public int? ChunkIndex { get; set; }
    
    /// <summary>
    /// Length of the content in characters (for debugging and statistics).
    /// </summary>
    public int ContentLength { get; set; }
    
    
    // --- Helper Methods ---
    
    /// <summary>
    /// Convert ReadOnlyMemory&lt;float&gt; to byte array for database storage.
    /// </summary>
    public void SetEmbeddingFromMemory(ReadOnlyMemory<float> embedding)
    {
        var span = embedding.Span;
        Embedding = new byte[span.Length * sizeof(float)];
        EmbeddingDimension = span.Length;
        
        Buffer.BlockCopy(span.ToArray(), 0, Embedding, 0, Embedding.Length);
    }
    
    /// <summary>
    /// Set category-only embedding.
    /// </summary>
    public void SetCategoryEmbeddingFromMemory(ReadOnlyMemory<float> embedding)
    {
        var span = embedding.Span;
        CategoryEmbedding = new byte[span.Length * sizeof(float)];
        
        Buffer.BlockCopy(span.ToArray(), 0, CategoryEmbedding, 0, CategoryEmbedding.Length);
    }
    
    /// <summary>
    /// Set content-only embedding.
    /// </summary>
    public void SetContentEmbeddingFromMemory(ReadOnlyMemory<float> embedding)
    {
        var span = embedding.Span;
        ContentEmbedding = new byte[span.Length * sizeof(float)];
        
        Buffer.BlockCopy(span.ToArray(), 0, ContentEmbedding, 0, ContentEmbedding.Length);
    }
    
    /// <summary>
    /// Convert byte array back to ReadOnlyMemory&lt;float&gt; for vector operations.
    /// </summary>
    public ReadOnlyMemory<float> GetEmbeddingAsMemory()
    {
        if (Embedding == null || Embedding.Length == 0)
            return ReadOnlyMemory<float>.Empty;
        
        var floats = new float[Embedding.Length / sizeof(float)];
        Buffer.BlockCopy(Embedding, 0, floats, 0, Embedding.Length);
        
        return new ReadOnlyMemory<float>(floats);
    }
    
    /// <summary>
    /// Get category-only embedding as memory.
    /// </summary>
    public ReadOnlyMemory<float> GetCategoryEmbeddingAsMemory()
    {
        if (CategoryEmbedding == null || CategoryEmbedding.Length == 0)
            return ReadOnlyMemory<float>.Empty;
        
        var floats = new float[CategoryEmbedding.Length / sizeof(float)];
        Buffer.BlockCopy(CategoryEmbedding, 0, floats, 0, CategoryEmbedding.Length);
        
        return new ReadOnlyMemory<float>(floats);
    }
    
    /// <summary>
    /// Get content-only embedding as memory.
    /// </summary>
    public ReadOnlyMemory<float> GetContentEmbeddingAsMemory()
    {
        if (ContentEmbedding == null || ContentEmbedding.Length == 0)
            return ReadOnlyMemory<float>.Empty;
        
        var floats = new float[ContentEmbedding.Length / sizeof(float)];
        Buffer.BlockCopy(ContentEmbedding, 0, floats, 0, ContentEmbedding.Length);
        
        return new ReadOnlyMemory<float>(floats);
    }
    
    /// <summary>
    /// Convert to in-memory MemoryFragment.
    /// </summary>
    public MemoryFragment ToMemoryFragment()
    {
        return new MemoryFragment(Category, Content);
    }
    
    /// <summary>
    /// Create entity from MemoryFragment with multiple embeddings for improved matching.
    /// </summary>
    public static MemoryFragmentEntity FromMemoryFragment(
        MemoryFragment fragment, 
        ReadOnlyMemory<float> combinedEmbedding,
        ReadOnlyMemory<float>? categoryEmbedding = null,
        ReadOnlyMemory<float>? contentEmbedding = null,
        string collectionName = "",
        string? sourceFile = null,
        int? chunkIndex = null)
    {
        var entity = new MemoryFragmentEntity
        {
            CollectionName = collectionName,
            Category = fragment.Category,
            Content = fragment.Content,
            ContentLength = fragment.ContentLength,
            SourceFile = sourceFile,
            ChunkIndex = chunkIndex
        };
        
        // Set primary (combined) embedding
        entity.SetEmbeddingFromMemory(combinedEmbedding);
        
        // Set optional separate embeddings for weighted matching
        if (categoryEmbedding.HasValue)
        {
            entity.SetCategoryEmbeddingFromMemory(categoryEmbedding.Value);
        }
        
        if (contentEmbedding.HasValue)
        {
            entity.SetContentEmbeddingFromMemory(contentEmbedding.Value);
        }
        
        return entity;
    }
}
