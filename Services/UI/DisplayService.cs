using Services.Configuration;

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
        Console.WriteLine("\n╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  Vector Memory Mode with Database Persistence                ║");
        Console.WriteLine("║  AI BOT with Semantic Search + MSSQL Storage                 ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        Console.WriteLine("\nType your questions below\n");
    }

    public static void ShowVectorMemoryInMemoryHeader()
    {
        Console.WriteLine("\n╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  Vector Memory Mode (In-Memory)                              ║");
        Console.WriteLine("║  AI BOT with Semantic Search                                 ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        Console.WriteLine("\nType your questions below\n");
    }

    public static void ShowOriginalModeHeader()
    {
        Console.WriteLine("\n╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  Original CLI Mode                                           ║");
        Console.WriteLine("║  Best AI BOT EVER                                            ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        Console.WriteLine("\nType your questions below\n");
    }

    #endregion

    #region Status Messages

    public static void ShowInitializingEmbeddingService()
    {
        Console.WriteLine("[*] Initializing embedding service...");
    }

    public static void ShowTestingDatabaseConnection()
    {
        Console.WriteLine("\n[*] Testing database connection...");
    }

    public static void ShowDatabaseConnectionFailed()
    {
        Console.WriteLine("\n[!] Database connection failed. See Docs/LocalDB-Setup.md for help.");
        Console.WriteLine("Press any key to exit...");
    }

    public static void ShowInitializingDatabaseSchema()
    {
        Console.WriteLine("\n[*] Initializing database schema...");
    }

    public static void ShowDatabaseSchemaReady()
    {
        Console.WriteLine("[+] Database schema ready");
    }

    public static void ShowVectorMemoryInitialized(int fragmentCount)
    {
        Console.WriteLine("\n[+] Vector memory initialized!");
        Console.WriteLine($"    Total fragments loaded: {fragmentCount}");
        Console.WriteLine("    The AI will now use semantic search to find relevant information.");
    }

    #endregion
    
    #region Embedding Service Initialization
    
    public static void ShowAttemptingGpuAcceleration(string provider)
    {
        Console.WriteLine($"[*] Attempting to enable {provider} GPU acceleration...");
    }
    
    public static void ShowGpuAccelerationEnabled(string provider)
    {
        Console.WriteLine($"[+] {provider} GPU acceleration enabled!");
    }
    
    public static void ShowGpuAccelerationNotAvailable(string provider, string message)
    {
        Console.WriteLine($"[!] {provider} not available: {message}");
    }
    
    public static void ShowFallingBackToCpu()
    {
        Console.WriteLine("[*] Falling back to memory-optimized CPU processing");
    }
    
    public static void ShowGpuConfiguration()
    {
        Console.WriteLine("[*] GPU Configuration:");
        Console.WriteLine("    Optimization: Full");
        Console.WriteLine("    Memory Arena: Enabled");
    }
    
    public static void ShowCpuConfiguration()
    {
        Console.WriteLine("[*] Memory-Optimized CPU Configuration:");
        Console.WriteLine("    Target: < 2GB RAM usage");
        Console.WriteLine("    Memory Arena: DISABLED (saves ~500MB)");
        Console.WriteLine("    Threading: Single-threaded (saves ~200MB per thread)");
        Console.WriteLine("    Execution: Sequential (minimal memory footprint)");
        Console.WriteLine("    Optimization: Basic (reduced temporary allocations)");
        Console.WriteLine("    [!] WARNING: This will be SLOW but memory-safe");
    }
    
    public static void ShowEmbeddingServiceInitialized(string modelName, int embeddingDimension, bool isGpu)
    {
        Console.WriteLine("[+] REAL BERT embeddings initialized!");
        Console.WriteLine($"    Model: {modelName}");
        Console.WriteLine($"    Embedding dimension: {embeddingDimension}");
        Console.WriteLine($"    Execution: {(isGpu ? "GPU" : "CPU (memory-optimized)")}");
        Console.WriteLine($"    Processing: Sequential (one embedding at a time)");
    }
    
    public static void ShowEmbeddingError(string message)
    {
        Console.WriteLine($"[ERROR] BERT embedding failed: {message}");
    }
    
    #endregion

    #region Collections Display

    public static void ShowExistingCollections(int count)
    {
        Console.WriteLine($"\n[*] Existing collections in database: {count}");
    }

    public static void ShowCollectionInfo(string collectionName, int fragmentCount)
    {
        Console.WriteLine($"    - {collectionName}: {fragmentCount} fragments");
    }

    public static void ShowCollectionsList(List<string> collections, Dictionary<string, int> fragmentCounts)
    {
        Console.WriteLine("\n╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine($"║  Available Collections ({collections.Count})                             ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
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
        Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  OfflineAI - Select Mode                                     ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
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
        Console.WriteLine($"[!] Collection '{collectionName}' not found. Loading from files...");
    }

    #endregion

    #region Commands and Help

    public static void ShowAvailableCommands(bool debugMode = false)
    {
        Console.WriteLine("\n╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  Available Commands                                          ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        
        if (debugMode)
        {
            Console.WriteLine("  /debug <query>  - Show relevant memory fragments");
            Console.WriteLine("  /stats          - Show collection statistics");
            Console.WriteLine("  /lengths        - Show fragment length analysis");
            Console.WriteLine("  /collections    - List all collections");
            Console.WriteLine("  /pool           - Show model pool status");
        }
        
        Console.WriteLine("  /rag            - Toggle RAG mode (semantic search on/off)");
        Console.WriteLine("  /perf           - Toggle performance metrics (tokens/sec)");
        Console.WriteLine("  /temperature <value> or /temp <value> - Set temperature (0.0-2.0)");
        Console.WriteLine("  /tokens <value> or /maxtokens <value> - Set max tokens (1-2048)");
        Console.WriteLine("  /settings       - Show current settings");
        Console.WriteLine("  /table          - Manage RAG context tables (create/switch/list)");
        Console.WriteLine("  /reload         - Check inbox for new files and process them");
        Console.WriteLine("  exit            - Quit");
    }
    
    /// <summary>
    /// Display a prominent banner showing the currently active RAG table
    /// </summary>
    public static void ShowActiveTableBanner(string tableName, int fragmentCount)
    {
        var width = 67;
        var tableInfo = $"Active RAG Table: {tableName}";
        var fragmentInfo = $"({fragmentCount:N0} fragments loaded)";
        
        // Calculate padding for center alignment
        var totalTextLength = tableInfo.Length + fragmentInfo.Length + 1; // +1 for space
        var leftPadding = (width - totalTextLength - 2) / 2; // -2 for border chars
        var rightPadding = width - totalTextLength - 2 - leftPadding;
        
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine();
        Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine($"║{new string(' ', leftPadding)}{tableInfo} {fragmentInfo}{new string(' ', rightPadding)}║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        Console.ResetColor();
    }
    
    public static void ShowConfigurationInfo(string inboxFolder, string archiveFolder)
    {
        Console.WriteLine("\n╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  Configuration                                               ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        Console.WriteLine($"  📂 Inbox:   {inboxFolder}");
        Console.WriteLine($"  📦 Archive: {archiveFolder}");
    }

    #endregion
    
    #region Debug and Statistics

    public static void ShowRelevantMemoryHeader()
    {
        Console.WriteLine("\n╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  Relevant Memory Fragments                                   ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
    }

    public static void ShowRelevantMemoryFooter()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════\n");
    }

    public static void ShowCollectionStats(string collectionName, int fragmentCount, bool hasEmbeddings, int inMemoryCount)
    {
        Console.WriteLine($"\n[*] Collection: {collectionName}");
        Console.WriteLine($"    Fragments: {fragmentCount}");
        Console.WriteLine($"    Has Embeddings: {hasEmbeddings}");
        Console.WriteLine($"    In-Memory Count: {inMemoryCount}");
    }

    /// <summary>
    /// Shows the system prompt that will be sent to the LLM for debugging purposes.
    /// </summary>
    /// <param name="relevantMemory">The relevant context being sent</param>
    /// <param name="debug">If true, displays the debug information. Default is false.</param>
    public static void ShowSystemPromptDebug(string relevantMemory, bool debug = false)
    {
        if (!debug)
        {
            return;
        }

        Console.WriteLine("\n╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  SYSTEM PROMPT SENT TO LLM (DEBUG MODE)                      ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        Console.WriteLine($"Total Length: {relevantMemory.Length} characters");
        Console.WriteLine($"Estimated Tokens: ~{relevantMemory.Length / 4} tokens\n");
        Console.WriteLine("─────────────────────────────────────────────────────────────────");
        Console.WriteLine("FULL CONTEXT BEING SENT TO LLM:");
        Console.WriteLine("─────────────────────────────────────────────────────────────────\n");
        
        // Display the FULL relevant memory content without truncation
        Console.WriteLine(relevantMemory);
        
        Console.WriteLine("\n─────────────────────────────────────────────────────────────────");
        Console.WriteLine($"End of context ({relevantMemory.Length} chars)");
        Console.WriteLine("═══════════════════════════════════════════════════════════════\n");
    }

    #endregion

    #region Loading Progress

    public static void ShowLoadingFromFilesHeader()
    {
        Console.WriteLine("\n╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  Loading from Files and Saving to Database                  ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
    }

    public static void ShowLoadingInMemoryHeader()
    {
        Console.WriteLine("\n╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  Loading from Files (In-Memory Only)                        ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
    }

    public static void ShowReadingFilesHeader()
    {
        Console.WriteLine("\n[*] Reading files and collecting fragments...");
    }

    public static void ShowLoadingFile(string gameName, string filePath)
    {
        Console.WriteLine($"    Loading {gameName} from {filePath}...");
    }

    public static void ShowCollectedSections(int sectionCount, string gameName)
    {
        Console.WriteLine($"    Collected {sectionCount} sections from {gameName}");
    }

    public static void ShowTotalFragmentsCollected(int count)
    {
        Console.WriteLine($"\n[+] Total fragments collected: {count}");
    }

    public static void ShowSavingToDatabaseHeader()
    {
        Console.WriteLine("\n╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  Saving to Database with Embeddings                         ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
    }

    public static void ShowLoadingFromDatabaseHeader()
    {
        Console.WriteLine("\n╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  Loading from Database (with embeddings)                    ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
    }

    public static void ShowSuccessfullySavedAndLoaded(int count)
    {
        Console.WriteLine($"\n[+] Successfully saved and loaded {count} fragments with embeddings");
    }

    public static void ShowLoadedSections(int count, string gameName)
    {
        Console.WriteLine($"[+] Loaded {count} sections from {gameName}");
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

    #region System Ready

    public static void ShowSystemReady()
    {
        Console.WriteLine("\n╔════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  ✓ System Ready - You can now ask questions           ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════╝\n");
    }

    #endregion

    #region Response Formatting
    
    /// <summary>
    /// Format performance metrics to append to a response.
    /// </summary>
    /// <param name="totalTimeMs">Total time in milliseconds</param>
    /// <param name="promptTokens">Number of prompt tokens</param>
    /// <param name="completionTokens">Number of completion tokens</param>
    /// <returns>Formatted performance metrics string</returns>
    public static string FormatPerformanceMetrics(double totalTimeMs, int promptTokens, int completionTokens)
    {
        var tokensPerSec = completionTokens / (totalTimeMs / 1000.0);
        var totalTokens = promptTokens + completionTokens;
        
        return $"\n\n" +
               $"============================\n" +
               $"| **Performance Metrics**\n" +
               $"============================\n" +
               $"|  **Time:** {totalTimeMs / 1000.0:F2}s\n" +
               $"|  **Tokens:** {promptTokens} prompt + {completionTokens} completion = {totalTokens} total\n" +
               $"|  **Speed:** {tokensPerSec:F1} tokens/sec\n" +
               $"============================";
    }
    
    #endregion

    public static void ShowInstancePool()
    {
        Console.WriteLine("\n╔════════════════════════════════════════════════════════╗");
        Console.WriteLine("║         Initializing Model Instance Pool...            ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════╝");
        Console.WriteLine("\nThis keeps the model loaded in memory for faster responses.");
        Console.WriteLine("Pool size: 3 instances (supports 3-10 concurrent users)\n");
    }

    public static void ShowSmartFileProcessing()
    {
        Console.WriteLine("\n╔════════════════════════════════════════════════════════╗");
        Console.WriteLine("║           Smart File Auto-Processing                   ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════╝");
    }
    
    /// <summary>
    /// Display generation settings being used for the query
    /// </summary>
    public static void ShowGenerationSettings(GenerationSettings settings, bool enableRag)
    {
        WriteLine($"\n╔═══════════════════════════════════════════════════════════════╗");
        WriteLine($"║  Generation Settings for Query                                ║");
        WriteLine($"╚═══════════════════════════════════════════════════════════════╝");
        WriteLine($"  RAG Mode:            {(enableRag ? "ENABLED" : "DISABLED")}");
        WriteLine($"  Temperature:         {settings.Temperature:F2}");
        WriteLine($"  Max Tokens:          {settings.MaxTokens}");
        WriteLine($"  Top-K:               {settings.TopK}");
        WriteLine($"  Top-P:               {settings.TopP:F2}");
        WriteLine($"  Repeat Penalty:      {settings.RepeatPenalty:F2}");
        WriteLine($"  Presence Penalty:    {settings.PresencePenalty:F2}");
        WriteLine($"  Frequency Penalty:   {settings.FrequencyPenalty:F2}");
        WriteLine($"");
    }
}
