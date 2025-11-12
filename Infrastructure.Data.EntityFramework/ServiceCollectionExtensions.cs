using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Services.Repositories;

namespace Infrastructure.Data.EntityFramework;

/// <summary>
/// Extension methods for registering Entity Framework Core-based repository services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the EF Core-based vector memory repository with the DI container.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="connectionString">Database connection string</param>
    /// <param name="autoMigrate">If true, automatically applies pending migrations on startup</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddEntityFrameworkVectorMemoryRepository(
        this IServiceCollection services, 
        string connectionString,
        bool autoMigrate = true)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string cannot be null or empty", nameof(connectionString));
        }

        // Register DbContext
        services.AddDbContext<VectorMemoryDbContext>(options =>
            options.UseSqlServer(connectionString));

        // Register repository
        services.AddScoped<IVectorMemoryRepository, VectorMemoryRepositoryEF>();

        // Optionally apply migrations automatically
        if (autoMigrate)
        {
            services.AddHostedService<DatabaseMigrationService>();
        }

        return services;
    }
}
