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
    /// </summary>
    public bool AutoInitializeDatabase { get; set; } = true;
    
    /// <summary>
    /// Whether to use Entity Framework Core (true) or Dapper (false).
    /// Default: false (Dapper).
    /// </summary>
    public bool UseEntityFramework { get; set; } = false;
    
    /// <summary>
    /// Get the connection string for database operations.
    /// </summary>
    public string GetConnectionString() => ConnectionString;
}
