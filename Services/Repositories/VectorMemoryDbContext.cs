using System;
using Microsoft.EntityFrameworkCore;
using Services.Models;

namespace Services.Repositories;

/// <summary>
/// Entity Framework Core DbContext for vector memory storage.
/// </summary>
public class VectorMemoryDbContext : DbContext
{
    public VectorMemoryDbContext(DbContextOptions<VectorMemoryDbContext> options)
        : base(options)
    {
    }

    public DbSet<MemoryFragmentEntity> MemoryFragments { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<MemoryFragmentEntity>(entity =>
        {
            entity.ToTable("MemoryFragments");
            
            entity.HasKey(e => e.Id);
            
            entity.Property(e => e.Id)
                .HasDefaultValueSql("NEWID()");
            
            entity.Property(e => e.CollectionName)
                .IsRequired()
                .HasMaxLength(255);
            
            entity.Property(e => e.Category)
                .IsRequired()
                .HasMaxLength(500);
            
            entity.Property(e => e.Content)
                .IsRequired()
                .HasColumnType("NVARCHAR(MAX)");
            
            entity.Property(e => e.Embedding)
                .HasColumnType("VARBINARY(MAX)");
            
            entity.Property(e => e.EmbeddingDimension);
            
            entity.Property(e => e.CreatedAt)
                .IsRequired()
                .HasDefaultValueSql("GETUTCDATE()");
            
            entity.Property(e => e.UpdatedAt)
                .IsRequired()
                .HasDefaultValueSql("GETUTCDATE()");
            
            entity.Property(e => e.SourceFile)
                .HasMaxLength(1000);
            
            entity.Property(e => e.ChunkIndex);
            
            // Indexes
            entity.HasIndex(e => e.CollectionName)
                .HasDatabaseName("IX_MemoryFragments_CollectionName");
            
            entity.HasIndex(e => e.Category)
                .HasDatabaseName("IX_MemoryFragments_Category");
            
            entity.HasIndex(e => e.CreatedAt)
                .HasDatabaseName("IX_MemoryFragments_CreatedAt");
        });
    }
}
