namespace Entities;

/// <summary>
/// Database entity for one goal-agent run (the Agent Mode page): what the run was asked to
/// achieve, where, with which model, and how it ended. Maps to the AgentRuns table in MSSQL.
/// <para>
/// Deliberately separate from <see cref="QuestionEntity"/>: the individual LLM round trips a run
/// performs are still saved as questions/answers (grouped under <see cref="ConversationId"/>),
/// but those rows carry no notion of a goal, an iteration, or a verdict. This table is the
/// run-level record that makes a history view possible.
/// </para>
/// </summary>
public class AgentRunEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The user's free-text description of the desired end result.</summary>
    public string GoalDescription { get; set; } = string.Empty;

    /// <summary>
    /// The workspace directory the run was confined to, so a run can be interpreted later even
    /// after the active workspace has been switched. Null when the agent ran without a file agent.
    /// </summary>
    public string? WorkspacePath { get; set; }

    /// <summary>
    /// Name of the LLM that served the run, as reported by the active backend. Null when the
    /// backend could not report one.
    /// </summary>
    public string? ModelName { get; set; }

    /// <summary>
    /// Links this run to the question/answer turns it produced in the Questions table. Null when
    /// no conversation was active. This is the join that makes the raw prompts of a run findable.
    /// </summary>
    public Guid? ConversationId { get; set; }

    /// <summary>The iteration cap the run started with.</summary>
    public int MaxIterations { get; set; }

    /// <summary>How many work → verify iterations the run actually executed.</summary>
    public int Iterations { get; set; }

    /// <summary>
    /// The run's phase, as the name of a <c>Services.GoalAgent.GoalAgentPhase</c> value. Stored as
    /// text rather than an ordinal so existing rows stay readable if the enum ever gains members.
    /// While a run is in flight this holds its current phase; it is rewritten to the terminal
    /// phase when the run ends.
    /// </summary>
    public string Phase { get; set; } = string.Empty;

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the run reached a terminal phase. Null for a run that is still executing — or one
    /// that was cut short by an app restart, which is the only way this stays null forever.
    /// </summary>
    public DateTime? CompletedAt { get; set; }
}
