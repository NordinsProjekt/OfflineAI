using RAGTesting.Models;

namespace RAGTesting.Services;

/// <summary>
/// Service for handling console display in RAG testing system.
/// Centralizes all console UI logic for better maintainability.
/// </summary>
public static class DisplayService
{
    #region Headers and Banners

    public static void ShowHeader()
    {
        Console.WriteLine("???????????????????????????????????????????????????????????????????");
        Console.WriteLine("?           RAG Quality Testing System                       ?");
        Console.WriteLine("?           Testing Semantic Search & Retrieval             ?");
        Console.WriteLine("???????????????????????????????????????????????????????????????????");
        Console.WriteLine();
    }

    #endregion

    #region Configuration and Initialization

    public static void ShowEnvironment(string environment)
    {
        Console.WriteLine($"?? Environment: {environment}");
    }

    public static void ShowConfigurationSourcesHeader()
    {
        Console.WriteLine("?? Configuration sources:");
    }

    public static void ShowConfigurationSource(string providerName)
    {
        Console.WriteLine($"   - {providerName}");
    }

    public static void ShowConfigurationHeader()
    {
        Console.WriteLine($"\n?? Loaded configuration:");
    }

    public static void ShowConfigurationValue(string key, string value)
    {
        Console.WriteLine($"   {key}: '{value}'");
    }

    public static void ShowConfigurationValueInt(string key, int value)
    {
        Console.WriteLine($"   {key}: {value}");
    }

    public static void ShowConfigurationError(List<string> errors)
    {
        Console.WriteLine("\n? Configuration Error:");
        foreach (var error in errors)
        {
            Console.WriteLine($"   - {error}");
        }
        
        Console.WriteLine("\n?? Please configure using:");
        Console.WriteLine("   1. User Secrets: dotnet user-secrets set \"AppConfiguration:Embedding:ModelPath\" \"path\"");
        Console.WriteLine("   2. appsettings.json");
        Console.WriteLine("   3. Environment Variables");
    }

    #endregion

    #region Test Loading

    public static void ShowNoTestQueriesFound()
    {
        Console.WriteLine("? No test queries found in test-queries.json");
    }

    public static void ShowLoadedTestQueries(int count)
    {
        Console.WriteLine($"? Loaded {count} test queries");
        Console.WriteLine();
    }

    #endregion

    #region Database Loading

    public static void ShowLoadingEmbeddings(string collectionName)
    {
        Console.WriteLine($"?? Loading embeddings from database (collection: {collectionName})...");
    }

    public static void ShowNoFragmentsFound()
    {
        Console.WriteLine("??  No fragments found in database!");
        Console.WriteLine("   Run the main OfflineAI application to import game rules first.");
    }

    public static void ShowLoadedFragments(int count)
    {
        Console.WriteLine($"? Loaded {count} fragments with embeddings");
    }

    public static void ShowVectorMemoryReady(int count)
    {
        Console.WriteLine($"? Vector memory ready with {count} fragments");
        Console.WriteLine();
    }

    #endregion

    #region Test Execution

    public static void ShowRunningTestsHeader()
    {
        Console.WriteLine("?? Running RAG quality tests...");
        Console.WriteLine();
    }

    public static void ShowTestHeader(string id, string description, string query, string? expectedGame, double minimumRelevance)
    {
        Console.WriteLine($"\n[TEST] Running: {id} - {description}");
        Console.WriteLine($"   Query: \"{query}\"");
        Console.WriteLine($"   Expected Game: {expectedGame ?? "(any)"}");
        Console.WriteLine($"   Minimum Relevance: {minimumRelevance:F2}");
    }

    public static void ShowExecutingSearch()
    {
        Console.WriteLine($"   ?? Executing search...");
    }

    public static void ShowNoResults()
    {
        Console.WriteLine($"   ?? No results returned");
    }

    public static void ShowRetrievedCharacters(int length)
    {
        Console.WriteLine($"   ? Retrieved {length} characters");
    }

    public static void ShowParsedFragments(int count)
    {
        Console.WriteLine($"   ? Parsed {count} fragments");
    }

    public static void ShowTestException(Exception ex)
    {
        Console.WriteLine($"   ? Exception: {ex.Message}");
        Console.WriteLine($"   Stack trace: {ex.StackTrace}");
    }

    #endregion

    #region Test Results

    public static void ShowTestFailed(string errorMessage)
    {
        Console.WriteLine($"  ? FAILED: {errorMessage}");
    }

    public static void ShowTestResult(TestResult result)
    {
        var icon = result.PassedMinimumThreshold ? "?" : "??";
        Console.WriteLine($"  {icon} Quality: {result.QualityRating}");
        Console.WriteLine($"     Max Relevance: {result.MaxRelevance:F3} (threshold: {result.MinimumThreshold:F2})");
        Console.WriteLine($"     Avg Relevance: {result.AverageRelevance:F3}");
        Console.WriteLine($"     Keyword Match: {result.KeywordsFound}/{result.KeywordsExpected} ({result.KeywordMatchRate:P0})");
        Console.WriteLine($"     Execution: {result.ExecutionTime.TotalMilliseconds:F0}ms");

        if (result.RetrievedFragments.Any())
        {
            Console.WriteLine($"     Top Result: {result.RetrievedFragments[0].Category}");
        }
    }

    #endregion

    #region Summary Reports

    public static void ShowSummaryHeader(TestSummary summary)
    {
        Console.WriteLine("\n" + new string('=', 80));
        Console.WriteLine("RAG QUALITY TEST SUMMARY");
        Console.WriteLine(new string('=', 80));
        Console.WriteLine($"Test Run: {summary.TestRunTime:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"Total Execution Time: {summary.TotalExecutionTime.TotalSeconds:F2}s");
        Console.WriteLine();
    }

    public static void ShowOverallResults(TestSummary summary)
    {
        Console.WriteLine("OVERALL RESULTS:");
        Console.WriteLine($"  Total Tests: {summary.TotalTests}");
        Console.WriteLine($"  Successful: {summary.SuccessfulTests} ({summary.SuccessRate:F1}%)");
        Console.WriteLine($"  Failed: {summary.FailedTests}");
        Console.WriteLine($"  Passed Threshold: {summary.PassedThreshold} ({summary.ThresholdPassRate:F1}%)");
        Console.WriteLine();
    }

    public static void ShowQualityMetrics(TestSummary summary)
    {
        Console.WriteLine("QUALITY METRICS:");
        Console.WriteLine($"  Average Relevance: {summary.AverageRelevanceAllTests:F3}");
        Console.WriteLine($"  Average Keyword Match: {summary.AverageKeywordMatchRate:F1}%");
        Console.WriteLine();
    }

    public static void ShowQualityDistribution(TestSummary summary, Func<string, int> getQualityScore)
    {
        Console.WriteLine("QUALITY DISTRIBUTION:");
        foreach (var kvp in summary.QualityDistribution.OrderByDescending(x => getQualityScore(x.Key)))
        {
            var percentage = (double)kvp.Value / summary.SuccessfulTests * 100;
            Console.WriteLine($"  {kvp.Key}: {kvp.Value} ({percentage:F1}%)");
        }
        Console.WriteLine();
    }

    public static void ShowTopPerformingQueries(IEnumerable<TestResult> topQueries)
    {
        Console.WriteLine("TOP PERFORMING QUERIES:");
        foreach (var result in topQueries)
        {
            Console.WriteLine($"  ? {result.QueryId}: {result.MaxRelevance:F3} - {result.Query}");
        }
        Console.WriteLine();
    }

    public static void ShowQueriesNeedingImprovement(IEnumerable<TestResult> worstQueries)
    {
        Console.WriteLine("QUERIES NEEDING IMPROVEMENT:");
        foreach (var result in worstQueries)
        {
            var icon = result.PassedMinimumThreshold ? "??" : "?";
            Console.WriteLine($"  {icon} {result.QueryId}: {result.MaxRelevance:F3} - {result.Query}");
        }
        Console.WriteLine();
    }

    public static void ShowReportsSaved(string outputPath)
    {
        Console.WriteLine(new string('=', 80));
        Console.WriteLine($"Reports saved to: {Path.GetFullPath(outputPath)}");
        Console.WriteLine(new string('=', 80));
    }

    public static void ShowReportGenerated(string reportType, string filename)
    {
        Console.WriteLine($"?? {reportType} report: {filename}");
    }

    #endregion

    #region Exit Messages

    public static void ShowTestsFailedMessage()
    {
        Console.WriteLine("\n??  Some tests failed. Check the reports for details.");
    }

    public static void ShowLowPassRateMessage(double passRate)
    {
        Console.WriteLine($"\n??  Only {passRate:F1}% of tests passed their minimum threshold.");
    }

    public static void ShowAllTestsPassedMessage()
    {
        Console.WriteLine("\n? All tests passed!");
    }

    public static void ShowFatalError(Exception ex)
    {
        Console.WriteLine($"\n? Fatal error: {ex.Message}");
        Console.WriteLine(ex.StackTrace);
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

    #endregion
}
