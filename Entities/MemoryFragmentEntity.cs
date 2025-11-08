namespace Entities;

/// <summary>
/// Database entity for storing memory fragments with vector embeddings.
/// Maps to the MemoryFragments table in MSSQL.
/// </summary>
public class MemoryFragmentEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public string CollectionName { get; set; } = string.Empty;
    
    public string Category { get; set; } = string.Empty;
    
    public string Content { get; set; } = string.Empty;
    
    /// <summary>
    /// Vector embedding stored as byte array.
    /// Each float is 4 bytes, so a 384-dimension embedding = 1,536 bytes.
    /// </summary>
    public byte[]? Embedding { get; set; }
    
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
    /// Convert to in-memory MemoryFragment.
    /// </summary>
    public MemoryFragment ToMemoryFragment()
    {
        return new MemoryFragment(Category, Content);
    }
    
    /// <summary>
    /// Create entity from MemoryFragment and embedding.
    /// </summary>
    public static MemoryFragmentEntity FromMemoryFragment(
        MemoryFragment fragment, 
        ReadOnlyMemory<float> embedding,
        string collectionName,
        string? sourceFile = null,
        int? chunkIndex = null)
    {
        var entity = new MemoryFragmentEntity
        {
            CollectionName = collectionName,
            Category = fragment.Category,
            Content = fragment.Content,
            ContentLength = fragment.ContentLength,  // Store the calculated length
            SourceFile = sourceFile,
            ChunkIndex = chunkIndex
        };
        
        entity.SetEmbeddingFromMemory(embedding);
        
        return entity;
    }
}
