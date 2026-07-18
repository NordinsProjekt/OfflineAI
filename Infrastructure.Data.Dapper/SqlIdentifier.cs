using System.Text.RegularExpressions;

namespace Infrastructure.Data.Dapper;

/// <summary>
/// Central validation and quoting for SQL identifiers (table/column names) that must be embedded
/// in a command string because they cannot be passed as parameters. Values are never trusted:
/// every dynamic identifier must go through <see cref="Bracket"/> (or be checked with
/// <see cref="IsValid"/>) so the allow-list lives in exactly one place and a future caller can't
/// accidentally interpolate an unvalidated name and open a SQL-injection hole.
/// </summary>
internal static class SqlIdentifier
{
    // Letters, digits, and underscores only, starting with a letter or underscore. This excludes
    // spaces, brackets, semicolons, and quotes, so a bracket-quoted value cannot break out.
    private static readonly Regex ValidPattern = new(@"^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.Compiled);

    /// <summary>Returns true when <paramref name="name"/> is a safe, simple SQL identifier.</summary>
    public static bool IsValid(string? name) =>
        !string.IsNullOrWhiteSpace(name) && ValidPattern.IsMatch(name);

    /// <summary>
    /// Validates <paramref name="name"/> and returns it wrapped in square brackets for safe
    /// embedding in SQL Server command text. Throws <see cref="ArgumentException"/> for any name
    /// that is not a simple identifier.
    /// </summary>
    public static string Bracket(string name)
    {
        if (!IsValid(name))
            throw new ArgumentException(
                $"Invalid SQL identifier: '{name}'. Only letters, digits, and underscores are allowed, and it must start with a letter or underscore.",
                nameof(name));

        return $"[{name}]";
    }
}
