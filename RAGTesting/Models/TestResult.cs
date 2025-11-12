namespace RAGTesting.Models;

/// <summary>
/// Result from a single test query execution
/// </summary>
public class TestResult
{
    public required string QueryId { get; set; }
    public required string Query { get; set; }
    public required string Description { get; set; }
    public List<RetrievedFragment> RetrievedFragments { get; set; } = new();
    public double AverageRelevance { get; set; }
    public double MaxRelevance { get; set; }
    public int KeywordsFound { get; set; }
    public int KeywordsExpected { get; set; }
    public double KeywordMatchRate => KeywordsExpected > 0 ? (double)KeywordsFound / KeywordsExpected : 0;
    public bool PassedMinimumThreshold { get; set; }
    public double MinimumThreshold { get; set; }
    public string QualityRating { get; set; } = "Unknown";
    public TimeSpan ExecutionTime { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// A single retrieved fragment with metadata
/// </summary>
public class RetrievedFragment
{
    public required string Category { get; set; }
    public required string Content { get; set; }
    public double RelevanceScore { get; set; }
    public List<string> MatchedKeywords { get; set; } = new();
}

/// <summary>
/// Overall summary of all test results
/// </summary>
public class TestSummary
{
    public DateTime TestRunTime { get; set; }
    public int TotalTests { get; set; }
    public int SuccessfulTests { get; set; }
    public int FailedTests { get; set; }
    public int PassedThreshold { get; set; }
    public double AverageRelevanceAllTests { get; set; }
    public double AverageKeywordMatchRate { get; set; }
    public TimeSpan TotalExecutionTime { get; set; }
    public Dictionary<string, int> QualityDistribution { get; set; } = new();
    public List<TestResult> TestResults { get; set; } = new();
    
    public double SuccessRate => TotalTests > 0 ? (double)SuccessfulTests / TotalTests * 100 : 0;
    public double ThresholdPassRate => TotalTests > 0 ? (double)PassedThreshold / TotalTests * 100 : 0;
}
