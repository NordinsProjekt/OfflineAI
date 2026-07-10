using System.Text.Json;

namespace Services.Workspace;

/// <inheritdoc/>
/// <remarks>
/// Persists the workspace list and active selection as JSON, defaulting to
/// <c>%AppData%\OfflineAI\workspaces.json</c>. The first time the app runs (no settings file
/// yet), a single "Standard" workspace is seeded from the caller-supplied default path so
/// existing installs keep working exactly as before this feature existed.
/// </remarks>
public sealed class WorkspaceService : IWorkspaceService
{
    private const string DefaultWorkspaceName = "Standard";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _settingsFilePath;
    private readonly string _defaultWorkspacePath;
    private readonly string _workspaceRoot;
    private readonly object _syncRoot = new();

    private List<WorkspaceInfo> _workspaces = new();
    private string _activeWorkspaceName = DefaultWorkspaceName;

    /// <inheritdoc/>
    public event Action<WorkspaceInfo>? ActiveWorkspaceChanged;

    /// <param name="defaultWorkspacePath">
    /// Directory used to seed the first ("Standard") workspace the very first time the app
    /// runs, i.e. when no persisted workspaces file exists yet.
    /// </param>
    /// <param name="settingsFilePath">
    /// Full path to the JSON persistence file. Defaults to
    /// <c>%AppData%\OfflineAI\workspaces.json</c>.
    /// </param>
    /// <param name="workspaceRoot">
    /// Directory that every workspace path must resolve inside. Adding a workspace whose resolved
    /// path is outside this root is rejected, which prevents an untrusted API/UI caller from
    /// pointing the file agent at an arbitrary location on disk. When null/empty, the root
    /// defaults to the parent directory of <paramref name="defaultWorkspacePath"/> so the seeded
    /// default workspace and its siblings remain valid.
    /// </param>
    public WorkspaceService(string defaultWorkspacePath, string? settingsFilePath = null, string? workspaceRoot = null)
    {
        if (string.IsNullOrWhiteSpace(defaultWorkspacePath))
            throw new ArgumentNullException(nameof(defaultWorkspacePath));

        _defaultWorkspacePath = Path.GetFullPath(defaultWorkspacePath);
        _settingsFilePath = string.IsNullOrWhiteSpace(settingsFilePath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OfflineAI", "workspaces.json")
            : settingsFilePath;

        _workspaceRoot = ResolveWorkspaceRoot(workspaceRoot, _defaultWorkspacePath);

        Load();
    }

    /// <summary>
    /// Determines the containment root: the explicitly configured root when provided, otherwise
    /// the parent directory of the default workspace path (falling back to the default workspace
    /// path itself when it has no parent, e.g. a drive root).
    /// </summary>
    private static string ResolveWorkspaceRoot(string? configuredRoot, string defaultWorkspacePath)
    {
        if (!string.IsNullOrWhiteSpace(configuredRoot))
            return Path.GetFullPath(configuredRoot);

        var parent = Path.GetDirectoryName(defaultWorkspacePath);
        return string.IsNullOrWhiteSpace(parent) ? defaultWorkspacePath : Path.GetFullPath(parent);
    }

    /// <summary>
    /// Returns true when <paramref name="candidateFullPath"/> is the workspace root itself or a
    /// path nested inside it. The trailing-separator comparison prevents a sibling directory whose
    /// name merely starts with the root (e.g. root <c>C:\data</c> vs <c>C:\data-evil</c>) from
    /// being treated as contained.
    /// </summary>
    private bool IsWithinRoot(string candidateFullPath)
    {
        var root = Path.TrimEndingDirectorySeparator(_workspaceRoot);
        var candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidateFullPath));

        return candidate.Equals(root, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public IReadOnlyList<WorkspaceInfo> GetWorkspaces()
    {
        lock (_syncRoot)
        {
            return _workspaces.ToList();
        }
    }

    /// <inheritdoc/>
    public WorkspaceInfo GetActiveWorkspace()
    {
        lock (_syncRoot)
        {
            return _workspaces.First(w => NameEquals(w.Name, _activeWorkspaceName));
        }
    }

    /// <inheritdoc/>
    public Task<WorkspaceInfo> AddWorkspaceAsync(string name, string path)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Workspace name must not be empty.", nameof(name));
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Workspace path must not be empty.", nameof(path));

        var fullPath = Path.GetFullPath(path);

        // Confinement: the file agent operates inside the active workspace, so an arbitrary
        // workspace path would let a caller read/write anywhere the process can. Reject anything
        // that resolves outside the configured root before creating the directory.
        if (!IsWithinRoot(fullPath))
            throw new ArgumentException(
                "Workspace path must be inside the configured workspace root.", nameof(path));

        Directory.CreateDirectory(fullPath);

        WorkspaceInfo added;
        List<WorkspaceInfo> snapshot;
        string activeName;

        lock (_syncRoot)
        {
            if (_workspaces.Any(w => NameEquals(w.Name, name)))
                throw new InvalidOperationException($"En arbetsyta med namnet \"{name}\" finns redan.");

            added = new WorkspaceInfo(name, fullPath);
            _workspaces.Add(added);
            snapshot = _workspaces.ToList();
            activeName = _activeWorkspaceName;
        }

        Save(snapshot, activeName);
        return Task.FromResult(added);
    }

    /// <inheritdoc/>
    public Task RemoveWorkspaceAsync(string name)
    {
        List<WorkspaceInfo> snapshot;
        string activeName;
        WorkspaceInfo? newActive = null;

        lock (_syncRoot)
        {
            var toRemove = _workspaces.FirstOrDefault(w => NameEquals(w.Name, name));
            if (toRemove is null)
                return Task.CompletedTask;

            _workspaces.Remove(toRemove);

            if (_workspaces.Count == 0)
            {
                Directory.CreateDirectory(_defaultWorkspacePath);
                _workspaces.Add(new WorkspaceInfo(DefaultWorkspaceName, _defaultWorkspacePath));
            }

            if (NameEquals(_activeWorkspaceName, name))
            {
                _activeWorkspaceName = _workspaces[0].Name;
                newActive = _workspaces[0];
            }

            activeName = _activeWorkspaceName;
            snapshot = _workspaces.ToList();
        }

        Save(snapshot, activeName);

        if (newActive is not null)
            ActiveWorkspaceChanged?.Invoke(newActive);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task SetActiveWorkspaceAsync(string name)
    {
        WorkspaceInfo active;
        List<WorkspaceInfo> snapshot;

        lock (_syncRoot)
        {
            active = _workspaces.FirstOrDefault(w => NameEquals(w.Name, name))
                ?? throw new InvalidOperationException($"Ingen arbetsyta med namnet \"{name}\" hittades.");

            _activeWorkspaceName = active.Name;
            snapshot = _workspaces.ToList();
        }

        Save(snapshot, active.Name);
        ActiveWorkspaceChanged?.Invoke(active);
        return Task.CompletedTask;
    }

    private static bool NameEquals(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private void Load()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                var dto = JsonSerializer.Deserialize<WorkspaceFileDto>(json);
                if (dto is not null && dto.Workspaces.Count > 0)
                {
                    // Drop any persisted workspace that resolves outside the confinement root, so a
                    // tampered/legacy settings file can't reintroduce an out-of-root workspace.
                    var loaded = dto.Workspaces
                        .Where(w => !string.IsNullOrWhiteSpace(w.Path) && IsWithinRoot(w.Path))
                        .Select(w => new WorkspaceInfo(w.Name, w.Path))
                        .ToList();

                    if (loaded.Count > 0)
                    {
                        _workspaces = loaded;
                        _activeWorkspaceName = _workspaces.Any(w => NameEquals(w.Name, dto.ActiveWorkspaceName))
                            ? dto.ActiveWorkspaceName
                            : _workspaces[0].Name;

                        foreach (var workspace in _workspaces)
                            Directory.CreateDirectory(workspace.Path);

                        return;
                    }
                }
            }
        }
        catch
        {
            // Corrupt or unreadable settings file — fall back to seeding a default workspace below.
        }

        Directory.CreateDirectory(_defaultWorkspacePath);
        _workspaces = new List<WorkspaceInfo> { new(DefaultWorkspaceName, _defaultWorkspacePath) };
        _activeWorkspaceName = DefaultWorkspaceName;

        Save(_workspaces, _activeWorkspaceName);
    }

    private void Save(List<WorkspaceInfo> workspaces, string activeWorkspaceName)
    {
        var dto = new WorkspaceFileDto
        {
            ActiveWorkspaceName = activeWorkspaceName,
            Workspaces = workspaces.Select(w => new WorkspaceDto { Name = w.Name, Path = w.Path }).ToList()
        };

        var directory = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(dto, JsonOptions);
        File.WriteAllText(_settingsFilePath, json);
    }

    private sealed class WorkspaceFileDto
    {
        public string ActiveWorkspaceName { get; set; } = string.Empty;
        public List<WorkspaceDto> Workspaces { get; set; } = new();
    }

    private sealed class WorkspaceDto
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
    }
}
