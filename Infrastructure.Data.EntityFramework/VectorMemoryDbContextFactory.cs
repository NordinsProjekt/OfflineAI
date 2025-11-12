using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Infrastructure.Data.EntityFramework;

/// <summary>
/// Design-time factory for creating DbContext instances during migrations.
/// This is used by EF Core tools (dotnet ef migrations, etc.)
/// </summary>
public class VectorMemoryDbContextFactory : IDesignTimeDbContextFactory<VectorMemoryDbContext>
{
    public VectorMemoryDbContext CreateDbContext(string[] args)
    {
        // Default connection string for migrations
        // This can be overridden by command-line arguments or environment variables
        var connectionString = args.Length > 0 
            ? args[0] 
            : "Server=(localdb)\\mssqllocaldb;Database=VectorMemoryDB;Integrated Security=true;TrustServerCertificate=true;";

        var optionsBuilder = new DbContextOptionsBuilder<VectorMemoryDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new VectorMemoryDbContext(optionsBuilder.Options);
    }
}
