namespace Entities;

/// <summary>
/// Categories for <see cref="AgentRunEventEntity.EventType"/>. Plain string constants rather than
/// an enum so the stored value is readable straight out of a SQL query.
/// </summary>
public static class AgentRunEventTypes
{
    /// <summary>The run entered a new phase (generating requirements, working, verifying, done...).</summary>
    public const string Phase = "Phase";

    /// <summary>A tool command was executed, or a work step executed none at all.</summary>
    public const string Tool = "Tool";

    /// <summary>A requirement was judged passed or failed.</summary>
    public const string Verdict = "Verdict";

    /// <summary>Anything else worth showing in the activity log.</summary>
    public const string Info = "Info";
}

/// <summary>
/// Database entity for one entry in a run's activity log — the same lines the Agent Mode page
/// shows live, kept so a finished run can be reviewed later. Maps to the AgentRunEvents table.
/// <para>
/// This is the activity log, not the transcript: the full prompts and raw LLM replies stay in
/// <c>agentlogg.txt</c> in the workspace, since a long run's prompts carry injected file content
/// and would dominate the table.
/// </para>
/// </summary>
public class AgentRunEventEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RunId { get; set; }

    /// <summary>
    /// Ordering within the run, assigned when the event is logged. Needed because events are
    /// written to the database in batches and <see cref="CreatedAt"/> alone is too coarse to
    /// separate entries logged in the same millisecond.
    /// </summary>
    public int Sequence { get; set; }

    /// <summary>One of the <see cref="AgentRunEventTypes"/> constants.</summary>
    public string EventType { get; set; } = AgentRunEventTypes.Info;

    /// <summary>The work → verify iteration this event belongs to, or null for events outside the loop.</summary>
    public int? Iteration { get; set; }

    /// <summary>The activity log line.</summary>
    public string Message { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
