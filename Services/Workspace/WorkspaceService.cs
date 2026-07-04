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
    public WorkspaceService(string defaultWorkspacePath, string? settingsFilePath = null)
    {
        if (string.IsNullOrWhiteSpace(defaultWorkspacePath))
            throw new ArgumentNullException(nameof(defaultWorkspacePath));

        _defaultWorkspacePath = Path.GetFullPath(defaultWorkspacePath);
        _settingsFilePath = string.IsNullOrWhiteSpace(settingsFilePath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OfflineAI", "workspaces.json")
            : settingsFilePath;

        Load();
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
                    _workspaces = dto.Workspaces
                        .Select(w => new WorkspaceInfo(w.Name, w.Path))
                        .ToList();
                    _activeWorkspaceName = _workspaces.Any(w => NameEquals(w.Name, dto.ActiveWorkspaceName))
                        ? dto.ActiveWorkspaceName
                        : _workspaces[0].Name;

                    foreach (var workspace in _workspaces)
                        Directory.CreateDirectory(workspace.Path);

                    return;
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
