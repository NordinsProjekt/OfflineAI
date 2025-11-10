using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Data.EntityFramework;

/// <summary>
/// Background service that automatically applies pending EF Core migrations on application startup.
/// </summary>
public class DatabaseMigrationService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DatabaseMigrationService>? _logger;

    public DatabaseMigrationService(
        IServiceProvider serviceProvider,
        ILogger<DatabaseMigrationService>? logger = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Checking for pending database migrations...");
        Console.WriteLine("[*] Checking for pending database migrations...");

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<VectorMemoryDbContext>();

            // Check if database exists
            var canConnect = await context.Database.CanConnectAsync(cancellationToken);
            if (!canConnect)
            {
                _logger?.LogInformation("Database does not exist. Creating database and applying migrations...");
                Console.WriteLine("[*] Database does not exist. Creating database and applying migrations...");
            }

            // Get pending migrations
            var pendingMigrations = await context.Database.GetPendingMigrationsAsync(cancellationToken);
            var pendingList = pendingMigrations.ToList();

            if (pendingList.Any())
            {
                _logger?.LogInformation("Found {Count} pending migration(s). Applying...", pendingList.Count);
                Console.WriteLine($"[*] Found {pendingList.Count} pending migration(s). Applying...");
                
                foreach (var migration in pendingList)
                {
                    Console.WriteLine($"    - {migration}");
                }

                // Apply migrations
                await context.Database.MigrateAsync(cancellationToken);

                _logger?.LogInformation("Database migrations applied successfully");
                Console.WriteLine("[+] Database migrations applied successfully");
            }
            else
            {
                _logger?.LogInformation("Database is up to date. No pending migrations.");
                Console.WriteLine("[+] Database is up to date. No pending migrations.");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error applying database migrations");
            Console.WriteLine($"[!] Error applying database migrations: {ex.Message}");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
