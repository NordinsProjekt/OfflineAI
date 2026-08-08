using System.Globalization;
using System.Text.RegularExpressions;
using AgentKit.Skills.Files;

namespace Services.Workspace;

/// <inheritdoc/>
/// <remarks>
/// Backups live in a subdirectory of the workspace itself, so they travel with it and are obvious
/// to anyone looking at the folder. Only files the agent could plausibly have written are copied:
/// binaries, bulk formats and anything over <see cref="MaxFileBytes"/> are skipped, because a
/// workspace with a 50 MB PDF in it would otherwise copy that PDF on every single work step.
/// </remarks>
public sealed class WorkspaceBackupService : IWorkspaceBackupService
{
    /// <summary>Default number of backups kept; older ones are deleted as new ones are taken.</summary>
    public const int DefaultRetainCount = 8;

    /// <summary>Largest file copied into a backup. Above this it is treated as an asset, not agent output.</summary>
    private const long MaxFileBytes = 1024 * 1024;

    /// <summary>Extensions never copied — binary or bulk formats the agent does not author.</summary>
    private static readonly HashSet<string> SkippedExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".pdf", ".exe", ".dll", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".zip", ".gguf", ".mp4", ".mp3" };

    /// <summary>Folder-name format: sortable, and parseable back into the creation time.</summary>
    private const string TimestampFormat = "yyyyMMdd-HHmmss-fff";

    private static readonly Regex LabelSanitiser = new("[^A-Za-z0-9-]", RegexOptions.Compiled);

    private readonly IFileAgentService _fileAgent;
    private readonly string[] _neverBackedUpNames;
    private readonly int _retainCount;

    /// <param name="fileAgent">
    /// Supplies the active workspace directory. Read on every call rather than captured, so
    /// switching workspaces switches which one gets backed up.
    /// </param>
    /// <param name="neverBackedUpNames">
    /// Filenames to leave out regardless of extension — the goal agent's own transcript, which is
    /// rewritten constantly and is a log of the run rather than part of its result.
    /// </param>
    /// <param name="retainCount">How many backups to keep. Non-positive falls back to <see cref="DefaultRetainCount"/>.</param>
    public WorkspaceBackupService(
        IFileAgentService fileAgent,
        IEnumerable<string>? neverBackedUpNames = null,
        int retainCount = DefaultRetainCount)
    {
        _fileAgent = fileAgent ?? throw new ArgumentNullException(nameof(fileAgent));
        _neverBackedUpNames = neverBackedUpNames?.ToArray() ?? Array.Empty<string>();
        _retainCount = retainCount > 0 ? retainCount : DefaultRetainCount;
    }

    /// <inheritdoc/>
    public string BackupFolderName => ".agent-backup";

    private string BackupRoot => Path.Combine(_fileAgent.BaseDirectory, BackupFolderName);

    /// <inheritdoc/>
    public WorkspaceBackupInfo? Create(string label)
    {
        try
        {
            var sources = GetBackupableFiles();
            if (sources.Count == 0)
                return null; // an empty workspace has nothing to protect

            var timestamp = DateTime.Now;
            var id = $"{timestamp.ToString(TimestampFormat, CultureInfo.InvariantCulture)}_{Sanitise(label)}";
            var directory = Path.Combine(BackupRoot, id);
            Directory.CreateDirectory(directory);

            long totalBytes = 0;
            foreach (var source in sources)
            {
                File.Copy(source, Path.Combine(directory, Path.GetFileName(source)), overwrite: true);
                totalBytes += new FileInfo(source).Length;
            }

            Prune();
            return new WorkspaceBackupInfo(id, ParseLabel(id), timestamp, sources.Count, totalBytes);
        }
        catch (Exception)
        {
            // Insurance that fails to write must not take down the thing it was insuring.
            return null;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<WorkspaceBackupInfo> GetBackups()
    {
        try
        {
            if (!Directory.Exists(BackupRoot))
                return Array.Empty<WorkspaceBackupInfo>();

            return Directory.GetDirectories(BackupRoot)
                .Select(Describe)
                .Where(info => info is not null)
                .Select(info => info!)
                .OrderByDescending(info => info.CreatedAt)
                .ToList();
        }
        catch (Exception)
        {
            return Array.Empty<WorkspaceBackupInfo>();
        }
    }

    /// <inheritdoc/>
    public int Restore(string backupId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupId);

        // Only ever a folder name directly under the backup root — never a path from elsewhere.
        var safeId = Path.GetFileName(backupId);
        var directory = Path.Combine(BackupRoot, safeId);
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Säkerhetskopian \"{backupId}\" finns inte längre.");

        var workspace = _fileAgent.BaseDirectory;
        var restored = 0;
        foreach (var file in Directory.GetFiles(directory))
        {
            File.Copy(file, Path.Combine(workspace, Path.GetFileName(file)), overwrite: true);
            restored++;
        }

        return restored;
    }

    /// <summary>
    /// The workspace's editable files: the root directory only (the backup folder itself is a
    /// subdirectory and is therefore never picked up), minus binaries, oversized assets and the
    /// excluded names.
    /// </summary>
    private List<string> GetBackupableFiles()
    {
        var workspace = _fileAgent.BaseDirectory;
        if (!Directory.Exists(workspace))
            return new List<string>();

        return Directory.GetFiles(workspace)
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                if (_neverBackedUpNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                    return false;
                if (SkippedExtensions.Contains(Path.GetExtension(name)))
                    return false;
                return new FileInfo(path).Length <= MaxFileBytes;
            })
            .ToList();
    }

    /// <summary>Deletes the oldest backups beyond the retention count.</summary>
    private void Prune()
    {
        var stale = GetBackups().Skip(_retainCount);
        foreach (var backup in stale)
        {
            try
            {
                Directory.Delete(Path.Combine(BackupRoot, backup.Id), recursive: true);
            }
            catch (Exception)
            {
                // A locked or already-removed folder just means one extra backup sticks around.
            }
        }
    }

    private static WorkspaceBackupInfo? Describe(string directory)
    {
        var id = Path.GetFileName(directory);
        if (!TryParseTimestamp(id, out var createdAt))
            return null; // not one of ours

        var files = Directory.GetFiles(directory);
        return new WorkspaceBackupInfo(
            id,
            ParseLabel(id),
            createdAt,
            files.Length,
            files.Sum(f => new FileInfo(f).Length));
    }

    private static bool TryParseTimestamp(string id, out DateTime createdAt)
    {
        var separator = id.IndexOf('_');
        var timestampPart = separator < 0 ? id : id[..separator];

        return DateTime.TryParseExact(
            timestampPart, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out createdAt);
    }

    private static string ParseLabel(string id)
    {
        var separator = id.IndexOf('_');
        return separator < 0 || separator == id.Length - 1 ? string.Empty : id[(separator + 1)..];
    }

    private static string Sanitise(string label) =>
        string.IsNullOrWhiteSpace(label) ? "backup" : LabelSanitiser.Replace(label.Trim(), "-");
}
