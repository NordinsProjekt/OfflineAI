using System.Diagnostics;
using System.Text.Json;
using Microsoft.SemanticKernel.Embeddings;
using Services.Memory;
using RAGTesting.Models;

namespace RAGTesting.Services;

/// <summary>
/// Service to execute RAG tests and evaluate quality
/// </summary>
public class RAGTestRunner
{
    private readonly VectorMemory _vectorMemory;
    private readonly EvaluationCriteria _criteria;
    private readonly double _minRelevanceThreshold;
    private readonly int _topK;

    public RAGTestRunner(
        VectorMemory vectorMemory,
        EvaluationCriteria criteria,
        double minRelevanceThreshold = 0.5,
        int topK = 5)
    {
        _vectorMemory = vectorMemory;
        _criteria = criteria;
        _minRelevanceThreshold = minRelevanceThreshold;
        _topK = topK;
    }

    /// <summary>
    /// Runs all test queries and returns aggregated results
    /// </summary>
    public async Task<TestSummary> RunAllTestsAsync(IEnumerable<TestQuery> testQueries)
    {
        var summary = new TestSummary
        {
            TestRunTime = DateTime.Now,
            TestResults = new List<TestResult>()
        };

        var overallStopwatch = Stopwatch.StartNew();

        foreach (var testQuery in testQueries)
        {
            Console.WriteLine($"\n[TEST] Running: {testQuery.Id} - {testQuery.Description}");
            Console.WriteLine($"   Query: \"{testQuery.Query}\"");
            Console.WriteLine($"   Expected Game: {testQuery.ExpectedGame ?? "(any)"}");
            Console.WriteLine($"   Minimum Relevance: {testQuery.MinimumRelevance:F2}");
            
            var result = await RunSingleTestAsync(testQuery);
            summary.TestResults.Add(result);

            // Display result
            DisplayTestResult(result);
        }

        overallStopwatch.Stop();
        summary.TotalExecutionTime = overallStopwatch.Elapsed;

        // Calculate summary statistics
        CalculateSummaryStatistics(summary);

        return summary;
    }

    /// <summary>
    /// Runs a single test query
    /// </summary>
    private async Task<TestResult> RunSingleTestAsync(TestQuery testQuery)
    {
        var result = new TestResult
        {
            QueryId = testQuery.Id,
            Query = testQuery.Query,
            Description = testQuery.Description,
            MinimumThreshold = testQuery.MinimumRelevance
        };

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Execute the RAG query
            Console.WriteLine($"   ? Executing search...");
            var retrievedText = await _vectorMemory.SearchRelevantMemoryAsync(
                testQuery.Query,
                topK: _topK,
                minRelevanceScore: _minRelevanceThreshold,
                gameFilter: testQuery.ExpectedGame != null ? new List<string> { testQuery.ExpectedGame } : null,
                maxCharsPerFragment: null,
                includeMetadata: true);

            stopwatch.Stop();
            result.ExecutionTime = stopwatch.Elapsed;

            if (string.IsNullOrEmpty(retrievedText))
            {
                result.Success = false;
                result.ErrorMessage = "No results returned from RAG system";
                Console.WriteLine($"   ?? No results returned");
                return result;
            }

            Console.WriteLine($"   ? Retrieved {retrievedText.Length} characters");

            // Parse the retrieved fragments
            result.RetrievedFragments = ParseRetrievedFragments(retrievedText);
            Console.WriteLine($"   ? Parsed {result.RetrievedFragments.Count} fragments");

            // Calculate metrics
            CalculateMetrics(result, testQuery);

            result.Success = true;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.ExecutionTime = stopwatch.Elapsed;
            result.Success = false;
            result.ErrorMessage = $"{ex.GetType().Name}: {ex.Message}";
            Console.WriteLine($"   ?? Exception: {ex.Message}");
            Console.WriteLine($"   Stack trace: {ex.StackTrace}");
        }

        return result;
    }

    /// <summary>
    /// Parses the retrieved text into structured fragments
    /// </summary>
    private List<RetrievedFragment> ParseRetrievedFragments(string retrievedText)
    {
        var fragments = new List<RetrievedFragment>();
        var lines = retrievedText.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        RetrievedFragment? currentFragment = null;
        double currentRelevance = 0;
        string? currentCategory = null;

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            // Parse relevance score: [Relevance: 0.850]
            if (trimmedLine.StartsWith("[Relevance:"))
            {
                var relevanceStr = trimmedLine.Remove(0, "[Relevance:".Length).TrimEnd(']', ' ');
                if (double.TryParse(relevanceStr, out var relevance))
                {
                    currentRelevance = relevance;
                }
            }
            // Parse category: [Game Name - Section Title]
            else if (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
            {
                currentCategory = trimmedLine.Trim('[', ']');
            }
            // Content line
            else if (!string.IsNullOrWhiteSpace(trimmedLine))
            {
                if (currentFragment == null && currentCategory != null)
                {
                    currentFragment = new RetrievedFragment
                    {
                        Category = currentCategory,
                        Content = trimmedLine,
                        RelevanceScore = currentRelevance
                    };
                }
                else if (currentFragment != null)
                {
                    currentFragment.Content += " " + trimmedLine;
                }
            }
            // Empty line - end of fragment
            else if (currentFragment != null)
            {
                fragments.Add(currentFragment);
                currentFragment = null;
                currentCategory = null;
                currentRelevance = 0;
            }
        }

        // Add last fragment if exists
        if (currentFragment != null)
        {
            fragments.Add(currentFragment);
        }

        return fragments;
    }

    /// <summary>
    /// Calculates all metrics for a test result
    /// </summary>
    private void CalculateMetrics(TestResult result, TestQuery testQuery)
    {
        // Average and max relevance
        if (result.RetrievedFragments.Any())
        {
            result.AverageRelevance = result.RetrievedFragments.Average(f => f.RelevanceScore);
            result.MaxRelevance = result.RetrievedFragments.Max(f => f.RelevanceScore);
        }

        // Keyword matching
        if (testQuery.ExpectedKeywords != null && testQuery.ExpectedKeywords.Length > 0)
        {
            result.KeywordsExpected = testQuery.ExpectedKeywords.Length;
            var allContent = string.Join(" ", result.RetrievedFragments.Select(f => f.Content + " " + f.Category)).ToLower();

            foreach (var keyword in testQuery.ExpectedKeywords)
            {
                if (allContent.Contains(keyword.ToLower()))
                {
                    result.KeywordsFound++;

                    // Mark which fragments contain this keyword
                    foreach (var fragment in result.RetrievedFragments)
                    {
                        var fragmentText = (fragment.Content + " " + fragment.Category).ToLower();
                        if (fragmentText.Contains(keyword.ToLower()))
                        {
                            fragment.MatchedKeywords.Add(keyword);
                        }
                    }
                }
            }
        }

        // Threshold check
        result.PassedMinimumThreshold = result.MaxRelevance >= testQuery.MinimumRelevance;

        // Quality rating
        result.QualityRating = DetermineQualityRating(result.MaxRelevance);
    }

    /// <summary>
    /// Determines quality rating based on relevance score
    /// </summary>
    private string DetermineQualityRating(double relevanceScore)
    {
        if (_criteria.Excellent?.MinRelevance != null && relevanceScore >= _criteria.Excellent.MinRelevance)
            return "Excellent";
        if (_criteria.Good?.MinRelevance != null && relevanceScore >= _criteria.Good.MinRelevance)
            return "Good";
        if (_criteria.Acceptable?.MinRelevance != null && relevanceScore >= _criteria.Acceptable.MinRelevance)
            return "Acceptable";
        if (_criteria.Poor?.MinRelevance != null && relevanceScore >= _criteria.Poor.MinRelevance)
            return "Poor";
        return "Failing";
    }

    /// <summary>
    /// Calculates summary statistics
    /// </summary>
    private void CalculateSummaryStatistics(TestSummary summary)
    {
        summary.TotalTests = summary.TestResults.Count;
        summary.SuccessfulTests = summary.TestResults.Count(r => r.Success);
        summary.FailedTests = summary.TotalTests - summary.SuccessfulTests;
        summary.PassedThreshold = summary.TestResults.Count(r => r.PassedMinimumThreshold);

        var successfulResults = summary.TestResults.Where(r => r.Success).ToList();
        if (successfulResults.Any())
        {
            summary.AverageRelevanceAllTests = successfulResults.Average(r => r.MaxRelevance);
            summary.AverageKeywordMatchRate = successfulResults.Average(r => r.KeywordMatchRate) * 100;
        }

        // Quality distribution
        foreach (var result in successfulResults)
        {
            if (!summary.QualityDistribution.ContainsKey(result.QualityRating))
            {
                summary.QualityDistribution[result.QualityRating] = 0;
            }
            summary.QualityDistribution[result.QualityRating]++;
        }
    }

    /// <summary>
    /// Displays a single test result to console
    /// </summary>
    private void DisplayTestResult(TestResult result)
    {
        if (!result.Success)
        {
            Console.WriteLine($"  ? FAILED: {result.ErrorMessage}");
            return;
        }

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
}
