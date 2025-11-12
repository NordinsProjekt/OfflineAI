using System.Text;
using System.Text.Json;
using RAGTesting.Models;

namespace RAGTesting.Services;

/// <summary>
/// Generates reports from test results
/// </summary>
public class ReportGenerator
{
    private readonly string _outputPath;

    public ReportGenerator(string outputPath)
    {
        _outputPath = outputPath;
        Directory.CreateDirectory(_outputPath);
    }

    /// <summary>
    /// Generates all reports (console, JSON, markdown)
    /// </summary>
    public async Task GenerateAllReportsAsync(TestSummary summary)
    {
        // Console report
        DisplayConsoleSummary(summary);

        // JSON report
        await GenerateJsonReportAsync(summary);

        // Markdown report
        await GenerateMarkdownReportAsync(summary);

        // CSV report (for Excel)
        await GenerateCsvReportAsync(summary);
    }

    /// <summary>
    /// Displays summary to console
    /// </summary>
    public void DisplayConsoleSummary(TestSummary summary)
    {
        Console.WriteLine("\n" + new string('=', 80));
        Console.WriteLine("RAG QUALITY TEST SUMMARY");
        Console.WriteLine(new string('=', 80));
        Console.WriteLine($"Test Run: {summary.TestRunTime:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"Total Execution Time: {summary.TotalExecutionTime.TotalSeconds:F2}s");
        Console.WriteLine();

        Console.WriteLine("OVERALL RESULTS:");
        Console.WriteLine($"  Total Tests: {summary.TotalTests}");
        Console.WriteLine($"  Successful: {summary.SuccessfulTests} ({summary.SuccessRate:F1}%)");
        Console.WriteLine($"  Failed: {summary.FailedTests}");
        Console.WriteLine($"  Passed Threshold: {summary.PassedThreshold} ({summary.ThresholdPassRate:F1}%)");
        Console.WriteLine();

        Console.WriteLine("QUALITY METRICS:");
        Console.WriteLine($"  Average Relevance: {summary.AverageRelevanceAllTests:F3}");
        Console.WriteLine($"  Average Keyword Match: {summary.AverageKeywordMatchRate:F1}%");
        Console.WriteLine();

        Console.WriteLine("QUALITY DISTRIBUTION:");
        foreach (var kvp in summary.QualityDistribution.OrderByDescending(x => GetQualityScore(x.Key)))
        {
            var percentage = (double)kvp.Value / summary.SuccessfulTests * 100;
            Console.WriteLine($"  {kvp.Key}: {kvp.Value} ({percentage:F1}%)");
        }
        Console.WriteLine();

        Console.WriteLine("TOP PERFORMING QUERIES:");
        var topQueries = summary.TestResults
            .Where(r => r.Success)
            .OrderByDescending(r => r.MaxRelevance)
            .Take(3);

        foreach (var result in topQueries)
        {
            Console.WriteLine($"  ? {result.QueryId}: {result.MaxRelevance:F3} - {result.Query}");
        }
        Console.WriteLine();

        Console.WriteLine("QUERIES NEEDING IMPROVEMENT:");
        var worstQueries = summary.TestResults
            .Where(r => r.Success)
            .OrderBy(r => r.MaxRelevance)
            .Take(3);

        foreach (var result in worstQueries)
        {
            var icon = result.PassedMinimumThreshold ? "??" : "?";
            Console.WriteLine($"  {icon} {result.QueryId}: {result.MaxRelevance:F3} - {result.Query}");
        }
        Console.WriteLine();

        Console.WriteLine(new string('=', 80));
        Console.WriteLine($"Reports saved to: {Path.GetFullPath(_outputPath)}");
        Console.WriteLine(new string('=', 80));
    }

    /// <summary>
    /// Generates JSON report
    /// </summary>
    private async Task GenerateJsonReportAsync(TestSummary summary)
    {
        var timestamp = summary.TestRunTime.ToString("yyyyMMdd-HHmmss");
        var filename = Path.Combine(_outputPath, $"test-results-{timestamp}.json");

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var json = JsonSerializer.Serialize(summary, options);
        await File.WriteAllTextAsync(filename, json);

        Console.WriteLine($"?? JSON report: {filename}");
    }

    /// <summary>
    /// Generates Markdown report
    /// </summary>
    private async Task GenerateMarkdownReportAsync(TestSummary summary)
    {
        var timestamp = summary.TestRunTime.ToString("yyyyMMdd-HHmmss");
        var filename = Path.Combine(_outputPath, $"test-results-{timestamp}.md");

        var sb = new StringBuilder();

        sb.AppendLine("# RAG Quality Test Report");
        sb.AppendLine();
        sb.AppendLine($"**Test Run:** {summary.TestRunTime:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"**Total Execution Time:** {summary.TotalExecutionTime.TotalSeconds:F2}s");
        sb.AppendLine();

        sb.AppendLine("## Overall Results");
        sb.AppendLine();
        sb.AppendLine($"- **Total Tests:** {summary.TotalTests}");
        sb.AppendLine($"- **Successful:** {summary.SuccessfulTests} ({summary.SuccessRate:F1}%)");
        sb.AppendLine($"- **Failed:** {summary.FailedTests}");
        sb.AppendLine($"- **Passed Threshold:** {summary.PassedThreshold} ({summary.ThresholdPassRate:F1}%)");
        sb.AppendLine();

        sb.AppendLine("## Quality Metrics");
        sb.AppendLine();
        sb.AppendLine($"- **Average Relevance:** {summary.AverageRelevanceAllTests:F3}");
        sb.AppendLine($"- **Average Keyword Match:** {summary.AverageKeywordMatchRate:F1}%");
        sb.AppendLine();

        sb.AppendLine("## Quality Distribution");
        sb.AppendLine();
        sb.AppendLine("| Quality | Count | Percentage |");
        sb.AppendLine("|---------|-------|------------|");
        foreach (var kvp in summary.QualityDistribution.OrderByDescending(x => GetQualityScore(x.Key)))
        {
            var percentage = (double)kvp.Value / summary.SuccessfulTests * 100;
            sb.AppendLine($"| {kvp.Key} | {kvp.Value} | {percentage:F1}% |");
        }
        sb.AppendLine();

        sb.AppendLine("## Detailed Test Results");
        sb.AppendLine();
        sb.AppendLine("| ID | Query | Quality | Max Relevance | Keywords | Time |");
        sb.AppendLine("|----|-------|---------|---------------|----------|------|");

        foreach (var result in summary.TestResults.OrderByDescending(r => r.MaxRelevance))
        {
            var status = result.Success ? (result.PassedMinimumThreshold ? "?" : "??") : "?";
            var keywords = result.KeywordsExpected > 0 ? $"{result.KeywordsFound}/{result.KeywordsExpected}" : "N/A";
            sb.AppendLine($"| {status} {result.QueryId} | {result.Query} | {result.QualityRating} | {result.MaxRelevance:F3} | {keywords} | {result.ExecutionTime.TotalMilliseconds:F0}ms |");
        }
        sb.AppendLine();

        sb.AppendLine("## Top Results by Query");
        sb.AppendLine();

        foreach (var result in summary.TestResults.Where(r => r.Success))
        {
            sb.AppendLine($"### {result.QueryId}: {result.Query}");
            sb.AppendLine();
            sb.AppendLine($"**Quality:** {result.QualityRating} | **Max Relevance:** {result.MaxRelevance:F3}");
            sb.AppendLine();

            if (result.RetrievedFragments.Any())
            {
                sb.AppendLine("**Top Retrieved Fragments:**");
                sb.AppendLine();

                foreach (var fragment in result.RetrievedFragments.Take(3))
                {
                    sb.AppendLine($"- **[{fragment.Category}]** (Relevance: {fragment.RelevanceScore:F3})");
                    if (fragment.MatchedKeywords.Any())
                    {
                        sb.AppendLine($"  - Matched keywords: {string.Join(", ", fragment.MatchedKeywords)}");
                    }
                    sb.AppendLine($"  - Content: {TruncateString(fragment.Content, 150)}");
                    sb.AppendLine();
                }
            }
        }

        await File.WriteAllTextAsync(filename, sb.ToString());
        Console.WriteLine($"?? Markdown report: {filename}");
    }

    /// <summary>
    /// Generates CSV report for Excel
    /// </summary>
    private async Task GenerateCsvReportAsync(TestSummary summary)
    {
        var timestamp = summary.TestRunTime.ToString("yyyyMMdd-HHmmss");
        var filename = Path.Combine(_outputPath, $"test-results-{timestamp}.csv");

        var sb = new StringBuilder();

        // Header
        sb.AppendLine("ID,Query,Description,Quality,Max Relevance,Avg Relevance,Keywords Found,Keywords Expected,Keyword Match %,Passed Threshold,Execution Time (ms),Success,Error Message");

        // Data rows
        foreach (var result in summary.TestResults)
        {
            sb.AppendLine($"{EscapeCsv(result.QueryId)}," +
                         $"{EscapeCsv(result.Query)}," +
                         $"{EscapeCsv(result.Description)}," +
                         $"{EscapeCsv(result.QualityRating)}," +
                         $"{result.MaxRelevance:F3}," +
                         $"{result.AverageRelevance:F3}," +
                         $"{result.KeywordsFound}," +
                         $"{result.KeywordsExpected}," +
                         $"{result.KeywordMatchRate * 100:F1}," +
                         $"{result.PassedMinimumThreshold}," +
                         $"{result.ExecutionTime.TotalMilliseconds:F0}," +
                         $"{result.Success}," +
                         $"{EscapeCsv(result.ErrorMessage ?? "")}");
        }

        await File.WriteAllTextAsync(filename, sb.ToString());
        Console.WriteLine($"?? CSV report: {filename}");
    }

    private string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }

    private string TruncateString(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;

        return text.Substring(0, maxLength - 3) + "...";
    }

    private int GetQualityScore(string quality)
    {
        return quality switch
        {
            "Excellent" => 5,
            "Good" => 4,
            "Acceptable" => 3,
            "Poor" => 2,
            "Failing" => 1,
            _ => 0
        };
    }
}
