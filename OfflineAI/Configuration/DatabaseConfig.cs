namespace OfflineAI.Configuration;

/// <summary>
/// Console-app-specific database settings, bound from the top-level "DatabaseConfig" JSON
/// section (see appsettings.json / secrets.json) — distinct from
/// <c>Services.Configuration.AppConfiguration.DatabaseSettings</c>, which is bound under
/// "AppConfiguration:Database" and serves AiDashboard/OfflineAI.Api instead.
/// </summary>
public class DatabaseConfig
{
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Active table/collection name for vector memory. Mutable so /table switch can change it at runtime.</summary>
    public string ActiveTableName { get; set; } = "MemoryFragments";

    public bool UseDatabasePersistence { get; set; } = true;

    public bool AutoInitializeDatabase { get; set; } = true;
}
