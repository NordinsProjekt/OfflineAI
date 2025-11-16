using Microsoft.Extensions.DependencyInjection;
using Services.Repositories;

namespace Infrastructure.Data.Dapper;

/// <summary>
/// Extension methods for registering Dapper-based repository services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Dapper-based vector memory repository with the DI container.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="connectionString">Database connection string</param>
    /// <param name="tableName">Optional table name (defaults to "MemoryFragments")</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddDapperVectorMemoryRepository(
        this IServiceCollection services, 
        string connectionString,
        string tableName = "MemoryFragments")
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string cannot be null or empty", nameof(connectionString));
        }

        services.AddSingleton<IVectorMemoryRepository>(provider => 
            new VectorMemoryRepository(connectionString, tableName));

        return services;
    }

    /// <summary>
    /// Registers the Dapper-based game repository with the DI container.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="connectionString">Database connection string</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddDapperGameRepository(
        this IServiceCollection services,
        string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string cannot be null or empty", nameof(connectionString));
        }

        services.AddSingleton<IGameRepository>(sp => 
            new GameRepository(connectionString));
        
        return services;
    }

    /// <summary>
    /// Registers the Dapper-based knowledge domain repository with the DI container.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="connectionString">Database connection string</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddDapperKnowledgeDomainRepository(
        this IServiceCollection services,
        string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string cannot be null or empty", nameof(connectionString));
        }

        services.AddSingleton<IKnowledgeDomainRepository>(sp => 
            new KnowledgeDomainRepository(connectionString));
        
        return services;
    }
}
