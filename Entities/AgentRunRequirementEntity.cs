namespace Entities;

/// <summary>
/// Database entity for one requirement within an <see cref="AgentRunEntity"/>: the checkable
/// "test" the LLM derived from the goal, and how it stood when the run ended. Maps to the
/// AgentRunRequirements table in MSSQL.
/// </summary>
public class AgentRunRequirementEntity
{
    /// <summary>
    /// Matches the in-memory <c>Services.GoalAgent.GoalRequirement.Id</c> of the run, so a
    /// requirement's row can be updated as its verdict changes without a lookup table.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RunId { get; set; }

    /// <summary>1-based position in the requirement list, preserving the order the LLM listed them.</summary>
    public int Ordinal { get; set; }

    /// <summary>The requirement text (one "KRAV:" line).</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// The requirement's state, as the name of a <c>Services.GoalAgent.RequirementStatus</c>
    /// value. Stored as text for the same reason as <see cref="AgentRunEntity.Phase"/>.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Motivation from the most recent verification that failed it, or null.</summary>
    public string? LastVerdict { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
