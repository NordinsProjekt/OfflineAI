namespace Services.Repositories;

/// <summary>
/// Statistics about content lengths in a collection.
/// </summary>
public record ContentLengthStats
{
    public int TotalFragments { get; init; }
    public double AverageLength { get; init; }
    public int MinLength { get; init; }
    public int MaxLength { get; init; }
    public int LongFragments { get; init; }  // > 1000 chars
    public int ShortFragments { get; init; }  // < 200 chars
}
