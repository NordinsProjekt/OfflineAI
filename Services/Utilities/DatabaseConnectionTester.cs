using System;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Services.Utilities;

/// <summary>
/// Utility to test database connection and setup.
/// </summary>
public static class DatabaseConnectionTester
{
    /// <summary>
    /// Test if we can connect to the database server.
    /// Tests connection to 'master' database to verify SQL Server is accessible.
    /// </summary>
    public static async Task<bool> TestConnectionAsync(string connectionString)
    {
        try
        {
            // Parse connection string and connect to master database first
            // This avoids errors if the target database doesn't exist yet
            var builder = new SqlConnectionStringBuilder(connectionString);
            var targetDatabase = builder.InitialCatalog;
            builder.InitialCatalog = "master";
            
            using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            Console.WriteLine("? Successfully connected to SQL Server!");
            
            // Get server version
            var version = connection.ServerVersion;
            Console.WriteLine($"  Server version: {version}");
            
            if (!string.IsNullOrWhiteSpace(targetDatabase))
            {
                Console.WriteLine($"  Target database: {targetDatabase}");
            }
            
            return true;
        }
        catch (SqlException ex)
        {
            Console.WriteLine("? Failed to connect to database:");
            Console.WriteLine($"  Error: {ex.Message}");
            Console.WriteLine();
            Console.WriteLine("Troubleshooting:");
            
            if (ex.Message.Contains("server was not found"))
            {
                Console.WriteLine("  - Check if SQL Server LocalDB is installed");
                Console.WriteLine("  - Run: sqllocaldb info");
                Console.WriteLine("  - Start LocalDB: sqllocaldb start mssqllocaldb");
            }
            
            if (ex.Message.Contains("login failed"))
            {
                Console.WriteLine("  - Check connection string authentication");
                Console.WriteLine("  - Verify Windows Authentication is enabled");
                Console.WriteLine("  - For LocalDB, ensure you're using: Integrated Security=true");
            }
            
            Console.WriteLine();
            Console.WriteLine("See Docs/LocalDB-Setup.md for detailed help.");
            
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"? Unexpected error: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Test if a specific database exists.
    /// </summary>
    public static async Task<bool> DatabaseExistsAsync(string connectionString, string databaseName)
    {
        try
        {
            // Connect to master to check if database exists
            var builder = new SqlConnectionStringBuilder(connectionString)
            {
                InitialCatalog = "master"
            };
            
            using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            
            var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(1) FROM sys.databases WHERE name = @DatabaseName";
            command.Parameters.AddWithValue("@DatabaseName", databaseName);
            
            var exists = (int)await command.ExecuteScalarAsync() > 0;
            
            if (exists)
            {
                Console.WriteLine($"? Database '{databaseName}' exists");
            }
            else
            {
                Console.WriteLine($"? Database '{databaseName}' does not exist yet");
                Console.WriteLine("  (Will be created automatically on first run)");
            }
            
            return exists;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"? Error checking database: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Get LocalDB instance information.
    /// </summary>
    public static void ShowLocalDbInfo()
    {
        Console.WriteLine("=== LocalDB Instance Info ===");
        Console.WriteLine();
        Console.WriteLine("Your connection uses SQL Server LocalDB:");
        Console.WriteLine("  Instance: (localdb)\\mssqllocaldb");
        Console.WriteLine();
        Console.WriteLine("To check if LocalDB is running, use Command Prompt:");
        Console.WriteLine("  > sqllocaldb info mssqllocaldb");
        Console.WriteLine();
        Console.WriteLine("To start LocalDB:");
        Console.WriteLine("  > sqllocaldb start mssqllocaldb");
        Console.WriteLine();
        Console.WriteLine("To view in Visual Studio:");
        Console.WriteLine("  1. View ? SQL Server Object Explorer");
        Console.WriteLine("  2. Expand: SQL Server ? (localdb)\\mssqllocaldb ? Databases");
        Console.WriteLine();
    }
    
    /// <summary>
    /// Run all diagnostic checks.
    /// </summary>
    public static async Task<bool> RunDiagnosticsAsync(string connectionString, string databaseName)
    {
        Console.WriteLine("=== Database Connection Diagnostics ===");
        Console.WriteLine();
        
        ShowLocalDbInfo();
        
        Console.WriteLine("Testing connection...");
        var canConnect = await TestConnectionAsync(connectionString);
        
        if (!canConnect)
        {
            return false;
        }
        
        Console.WriteLine();
        Console.WriteLine("Checking database...");
        await DatabaseExistsAsync(connectionString, databaseName);
        
        Console.WriteLine();
        Console.WriteLine("=== Diagnostic Complete ===");
        Console.WriteLine();
        
        return canConnect;
    }
}
