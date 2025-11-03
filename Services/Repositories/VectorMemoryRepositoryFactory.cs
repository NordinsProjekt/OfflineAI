using System;
using Microsoft.EntityFrameworkCore;
using Services.Configuration;

namespace Services.Repositories;

/// <summary>
/// Factory for creating vector memory repository instances.
/// Supports both Dapper (SQL) and EF Core implementations.
/// </summary>
public static class VectorMemoryRepositoryFactory
{
    public enum RepositoryType
    {
        Dapper,
        EntityFramework
    }
    
    /// <summary>
    /// Create a repository instance based on the specified type.
    /// </summary>
    public static IVectorMemoryRepository Create(
        string connectionString, 
        RepositoryType repositoryType = RepositoryType.Dapper)
    {
        return repositoryType switch
        {
            RepositoryType.Dapper => CreateDapperRepository(connectionString),
            RepositoryType.EntityFramework => CreateEFRepository(connectionString),
            _ => throw new ArgumentException($"Unknown repository type: {repositoryType}", nameof(repositoryType))
        };
    }
    
    /// <summary>
    /// Create a repository instance based on configuration.
    /// </summary>
    public static IVectorMemoryRepository Create(DatabaseConfig config)
    {
        var connectionString = config.GetConnectionString();
        var repositoryType = config.UseEntityFramework 
            ? RepositoryType.EntityFramework 
            : RepositoryType.Dapper;
        
        return Create(connectionString, repositoryType);
    }
    
    /// <summary>
    /// Create a Dapper-based repository.
    /// </summary>
    public static IVectorMemoryRepository CreateDapperRepository(string connectionString)
    {
        return new VectorMemoryRepository(connectionString);
    }
    
    /// <summary>
    /// Create an EF Core-based repository.
    /// </summary>
    public static IVectorMemoryRepository CreateEFRepository(string connectionString)
    {
        var optionsBuilder = new DbContextOptionsBuilder<VectorMemoryDbContext>();
        optionsBuilder.UseSqlServer(connectionString);
        
        var context = new VectorMemoryDbContext(optionsBuilder.Options);
        return new VectorMemoryRepositoryEF(context);
    }
}
