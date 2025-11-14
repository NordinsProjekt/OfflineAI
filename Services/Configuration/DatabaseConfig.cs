namespace Services.Configuration;

/// <summary>
/// Configuration settings for database connection.
/// </summary>
public class DatabaseConfig
{
    /// <summary>
    /// SQL Server connection string.
    /// Example: "Server=localhost;Database=VectorMemoryDB;Integrated Security=true;"
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;
    
    /// <summary>
    /// Whether to use database persistence (false = in-memory only).
    /// </summary>
    public bool UseDatabasePersistence { get; set; } = false;
    
    /// <summary>
    /// Auto-initialize database schema on startup.
    /// Note: Only Dapper is supported. Entity Framework Core has been removed.
    /// </summary>
    public bool AutoInitializeDatabase { get; set; } = true;
    
    /// <summary>
    /// Get the connection string for database operations.
    /// </summary>
    public string GetConnectionString() => ConnectionString;
}
