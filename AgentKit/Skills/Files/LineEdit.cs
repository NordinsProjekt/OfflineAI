namespace AgentKit.Skills.Files;

/// <summary>
/// The kind of change a <see cref="LineEdit"/> represents.
/// </summary>
public enum LineEditKind
{
    /// <summary>Replace the inclusive line range <c>StartLine</c>-<c>EndLine</c> with new content.</summary>
    Replace,

    /// <summary>Insert new content immediately after line <c>StartLine</c>, without removing anything.</summary>
    InsertAfter,

    /// <summary>Insert new content immediately before line <c>StartLine</c>, without removing anything.</summary>
    InsertBefore,
}

/// <summary>
/// A single line-based change requested by the LLM in response to a <c>/redigera</c> command.
/// <para>
/// For <see cref="LineEditKind.Replace"/> (the default), lines <paramref name="StartLine"/>
/// through <paramref name="EndLine"/> (1-based, inclusive) in the target file are replaced with
/// <paramref name="NewContent"/>.
/// </para>
/// <para>
/// For <see cref="LineEditKind.InsertAfter"/> / <see cref="LineEditKind.InsertBefore"/>, no
/// existing lines are removed — <paramref name="NewContent"/> is spliced in immediately after
/// (or before) <paramref name="StartLine"/>. This is what the LLM should use when asked to add
/// brand-new code (e.g. a new method) rather than change existing lines, so it can append the
/// new block within the correct namespace/class instead of overwriting content. Use
/// <see cref="InsertAfterLine"/> / <see cref="InsertBeforeLine"/> to construct these.
/// </para>
/// </summary>
/// <param name="StartLine">
/// First line number (1-based, inclusive) to replace, or the anchor line for an insertion.
/// </param>
/// <param name="EndLine">
/// Last line number (1-based, inclusive) to replace. Equal to <paramref name="StartLine"/>
/// for a single-line edit. Ignored for insertions.
/// </param>
/// <param name="NewContent">
/// The text that should replace the given line range, or be inserted. May contain multiple lines.
/// </param>
/// <param name="Kind">Whether this is a replacement or an insertion. Defaults to <see cref="LineEditKind.Replace"/>.</param>
public sealed record LineEdit(int StartLine, int EndLine, string NewContent, LineEditKind Kind = LineEditKind.Replace)
{
    /// <summary>Creates an edit that inserts <paramref name="content"/> immediately after <paramref name="afterLine"/> (0 = insert at the very top of the file).</summary>
    public static LineEdit InsertAfterLine(int afterLine, string content) =>
        new(afterLine, afterLine, content, LineEditKind.InsertAfter);

    /// <summary>Creates an edit that inserts <paramref name="content"/> immediately before <paramref name="beforeLine"/> (lineCount + 1 = append at end of file).</summary>
    public static LineEdit InsertBeforeLine(int beforeLine, string content) =>
        new(beforeLine, beforeLine, content, LineEditKind.InsertBefore);
}
