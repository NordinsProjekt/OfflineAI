namespace Services.UI;

/// <summary>
/// Service for handling console display and user interaction.
/// Centralizes all console UI logic for better maintainability.
/// </summary>
public static class DisplayService
{
    #region Headers and Banners

    public static void ShowVectorMemoryDatabaseHeader()
    {
        Console.WriteLine("\n=== Vector Memory Mode with Database Persistence ===");
        Console.WriteLine("=== AI BOT with Semantic Search + MSSQL Storage ===");
        Console.WriteLine("Type your questions\n");
    }

    public static void ShowVectorMemoryInMemoryHeader()
    {
        Console.WriteLine("\n=== Vector Memory Mode (In-Memory) ===");
        Console.WriteLine("=== AI BOT with Semantic Search ===");
        Console.WriteLine("Type your questions\n");
    }

    public static void ShowOriginalModeHeader()
    {
        Console.WriteLine("\n=== Original CLI Mode ===");
        Console.WriteLine("=== Best AI BOT EVER ===");
        Console.WriteLine("Type your questions\n");
    }

    #endregion

    #region Status Messages

    public static void ShowInitializingEmbeddingService()
    {
        Console.WriteLine("Initializing embedding service...");
    }

    public static void ShowTestingDatabaseConnection()
    {
        Console.WriteLine("\nTesting database connection...");
    }

    public static void ShowDatabaseConnectionFailed()
    {
        Console.WriteLine("\n? Database connection failed. See Docs/LocalDB-Setup.md for help.");
        Console.WriteLine("Press any key to exit...");
    }

    public static void ShowInitializingDatabaseSchema()
    {
        Console.WriteLine("\nInitializing database schema...");
    }

    public static void ShowDatabaseSchemaReady()
    {
        Console.WriteLine("? Database schema ready");
    }

    public static void ShowVectorMemoryInitialized(int fragmentCount)
    {
        Console.WriteLine("\n? Vector memory initialized!");
        Console.WriteLine($"Total fragments loaded: {fragmentCount}");
        Console.WriteLine("The AI will now use semantic search to find relevant information.");
    }

    #endregion

    #region Collections Display

    public static void ShowExistingCollections(int count)
    {
        Console.WriteLine($"\nExisting collections in database: {count}");
    }

    public static void ShowCollectionInfo(string collectionName, int fragmentCount)
    {
        Console.WriteLine($"  - {collectionName}: {fragmentCount} fragments");
    }

    public static void ShowCollectionsList(List<string> collections, Dictionary<string, int> fragmentCounts)
    {
        Console.WriteLine($"\n=== Available Collections ({collections.Count}) ===");
        foreach (var collection in collections)
        {
            var count = fragmentCounts.ContainsKey(collection) ? fragmentCounts[collection] : 0;
            Console.WriteLine($"  {collection}: {count} fragments");
        }
    }

    #endregion

    #region Menus and Prompts

    public static string ShowMainModeMenu()
    {
        Console.WriteLine("=== OfflineAI - Select Mode ===");
        Console.WriteLine("1. Original Mode (Load all memory into RAM)");
        Console.WriteLine("2. Vector Memory Mode (In-Memory Semantic Kernel)");
        Console.WriteLine("3. Vector Memory with Database (MSSQL Persistence)");
        Console.Write("\nSelect mode (1, 2, or 3): ");
        return Console.ReadLine() ?? string.Empty;
    }

    public static string ShowDataSourceMenu()
    {
        Console.WriteLine("\nOptions:");
        Console.WriteLine("1. Load from database (if exists)");
        Console.WriteLine("2. Load from files and save to database");
        Console.WriteLine("3. Use in-memory only (no database)");
        Console.Write("\nSelect option (1-3): ");
        return Console.ReadLine() ?? string.Empty;
    }

    public static void ShowCollectionNotFound(string collectionName)
    {
        Console.WriteLine($"Collection '{collectionName}' not found. Loading from files...");
    }

    #endregion

    #region Commands and Help

    public static void ShowAvailableCommands()
    {
        Console.WriteLine("\nCommands:");
        Console.WriteLine("  /debug <query> - Show relevant memory fragments");
        Console.WriteLine("  /stats - Show collection statistics");
        Console.WriteLine("  /collections - List all collections");
        Console.WriteLine("  /pool - Show model pool status");
        Console.WriteLine("  /reload - Check inbox for new files and process them");
        Console.WriteLine("  exit - Quit");
        Console.WriteLine("\nType your questions:");
    }

    #endregion

    #region Debug and Statistics

    public static void ShowRelevantMemoryHeader()
    {
        Console.WriteLine("\n=== Relevant Memory Fragments ===");
    }

    public static void ShowRelevantMemoryFooter()
    {
        Console.WriteLine("=================================\n");
    }

    public static void ShowCollectionStats(string collectionName, int fragmentCount, bool hasEmbeddings, int inMemoryCount)
    {
        Console.WriteLine($"\nCollection: {collectionName}");
        Console.WriteLine($"Fragments: {fragmentCount}");
        Console.WriteLine($"Has Embeddings: {hasEmbeddings}");
        Console.WriteLine($"In-Memory Count: {inMemoryCount}");
    }

    #endregion

    #region Loading Progress

    public static void ShowLoadingFromFilesHeader()
    {
        Console.WriteLine("\n=== Loading from Files and Saving to Database ===");
    }

    public static void ShowLoadingInMemoryHeader()
    {
        Console.WriteLine("\n=== Loading from Files (In-Memory Only) ===");
    }

    public static void ShowReadingFilesHeader()
    {
        Console.WriteLine("\nReading files and collecting fragments...");
    }

    public static void ShowLoadingFile(string gameName, string filePath)
    {
        Console.WriteLine($"  Loading {gameName} from {filePath}...");
    }

    public static void ShowCollectedSections(int sectionCount, string gameName)
    {
        Console.WriteLine($"  Collected {sectionCount} sections from {gameName}");
    }

    public static void ShowTotalFragmentsCollected(int count)
    {
        Console.WriteLine($"\nTotal fragments collected: {count}");
    }

    public static void ShowSavingToDatabaseHeader()
    {
        Console.WriteLine("\n=== Saving to Database with Embeddings ===");
    }

    public static void ShowLoadingFromDatabaseHeader()
    {
        Console.WriteLine("\n=== Loading from Database (with embeddings) ===");
    }

    public static void ShowSuccessfullySavedAndLoaded(int count)
    {
        Console.WriteLine($"\n? Successfully saved and loaded {count} fragments with embeddings");
    }

    public static void ShowLoadedSections(int count, string gameName)
    {
        Console.WriteLine($"Loaded {count} sections from {gameName}");
    }

    #endregion

    #region Input/Output

    public static string ReadInput(string prompt = "> ")
    {
        Console.Write(prompt);
        return Console.ReadLine() ?? string.Empty;
    }

    public static void ShowResponse(string response)
    {
        Console.Write("Response: ");
        Console.Write(response);
        Console.WriteLine("\n");
    }

    #endregion

    #region Utilities

    public static void WriteLine(string message = "")
    {
        Console.WriteLine(message);
    }

    public static void Write(string message)
    {
        Console.Write(message);
    }

    public static void WaitForKeyPress()
    {
        Console.ReadKey();
    }

    #endregion
}
