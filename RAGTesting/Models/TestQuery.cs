namespace RAGTesting.Models;

/// <summary>
/// Represents a test query with expected results for RAG evaluation
/// </summary>
public class TestQuery
{
    public required string Id { get; set; }
    public required string Query { get; set; }
    public string[]? ExpectedKeywords { get; set; }
    public string? ExpectedGame { get; set; }
    public double MinimumRelevance { get; set; }
    public required string Description { get; set; }
}

/// <summary>
/// Configuration for test queries from JSON
/// </summary>
public class TestQueriesConfig
{
    public TestQuery[] TestQueries { get; set; } = Array.Empty<TestQuery>();
    public EvaluationCriteria? EvaluationCriteria { get; set; }
}

/// <summary>
/// Evaluation criteria for different quality levels
/// </summary>
public class EvaluationCriteria
{
    public QualityLevel? Excellent { get; set; }
    public QualityLevel? Good { get; set; }
    public QualityLevel? Acceptable { get; set; }
    public QualityLevel? Poor { get; set; }
    public QualityLevel? Failing { get; set; }
}

public class QualityLevel
{
    public double? MinRelevance { get; set; }
    public double? MaxRelevance { get; set; }
    public string? Description { get; set; }
}
