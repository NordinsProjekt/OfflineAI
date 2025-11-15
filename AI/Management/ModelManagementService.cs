using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Application.AI.Management;

/// <summary>
/// Service for managing LLM model discovery and selection.
/// Handles scanning for GGUF model files and model switching operations.
/// </summary>
public class ModelManagementService
{
    // Change notification for UI components
    public event Action? OnChange;

    private string _currentModel = "phi-3.5-mini-instruct.gguf";
    public string CurrentModel
    {
        get => _currentModel;
        set
        {
            if (_currentModel == value) return;
            _currentModel = value;
            NotifyStateChanged();
        }
    }

    private readonly List<string> _availableModels = new();
    public IReadOnlyList<string> AvailableModels => _availableModels.AsReadOnly();

    // Folder to search for GGUF model files
    public string ModelFolderPath { get; set; }

    // Persist the selected model full path
    public string? SelectedModelFullPath { get; private set; }

    /// <summary>
    /// Optional handler for performing the actual model switch in the backend.
    /// Signature: modelFullPath, progressCallback -> Task
    /// </summary>
    public Func<string, Action<int, int>?, Task>? SwitchModelHandler { get; set; }

    public ModelManagementService(string? modelFolderPath = null)
    {
        // Default model folder: try common locations, fall back to current directory
        if (!string.IsNullOrWhiteSpace(modelFolderPath))
        {
            ModelFolderPath = modelFolderPath;
        }
        else
        {
            var candidates = new[] { "d:/tinyllama", "c:/models", "./models" };
            ModelFolderPath = candidates.FirstOrDefault(Directory.Exists) ?? Directory.GetCurrentDirectory();
        }

        RefreshAvailableModels();
    }

    /// <summary>
    /// Scan ModelFolderPath for .gguf files and update AvailableModels.
    /// </summary>
    public Task RefreshAvailableModelsAsync()
    {
        RefreshAvailableModels();
        return Task.CompletedTask;
    }

    private void RefreshAvailableModels()
    {
        try
        {
            _availableModels.Clear();

            if (string.IsNullOrWhiteSpace(ModelFolderPath) || !Directory.Exists(ModelFolderPath))
            {
                NotifyStateChanged();
                return;
            }

            var files = Directory.EnumerateFiles(ModelFolderPath, "*.gguf", SearchOption.TopDirectoryOnly)
                .OrderBy(p => p)
                .Select(Path.GetFileName)
                .Where(f => f != null)
                .Cast<string>();

            _availableModels.AddRange(files);

            // If current model isn't in the list, and list is not empty, pick the first
            if (!_availableModels.Contains(CurrentModel, StringComparer.OrdinalIgnoreCase) && _availableModels.Count > 0)
            {
                CurrentModel = _availableModels[0];
            }

            NotifyStateChanged();
        }
        catch
        {
            // Ignore IO errors for UI - just leave the list empty
        }
    }

    /// <summary>
    /// Returns the full path to the currently selected model, if available.
    /// </summary>
    public string? GetCurrentModelFullPath()
    {
        if (string.IsNullOrWhiteSpace(CurrentModel)) return null;
        var candidate = Path.Combine(ModelFolderPath ?? string.Empty, CurrentModel);
        return File.Exists(candidate) ? candidate : null;
    }

    /// <summary>
    /// Switch to the selected model. Returns true if successful.
    /// </summary>
    public async Task<(bool Success, string Message)> SwitchModelAsync()
    {
        // Refresh available models and ensure CurrentModel is valid
        await RefreshAvailableModelsAsync();

        if (!_availableModels.Contains(CurrentModel, StringComparer.OrdinalIgnoreCase))
        {
            if (_availableModels.Count > 0)
            {
                CurrentModel = _availableModels[0];
            }
            else
            {
                return (false, $"No models found in {ModelFolderPath}");
            }
        }

        // Store selected model full path
        SelectedModelFullPath = GetCurrentModelFullPath();
        
        if (SelectedModelFullPath == null)
        {
            return (false, $"Model file not found: {CurrentModel}");
        }

        NotifyStateChanged();

        // If we have a handler assigned, invoke it to perform the actual backend switch
        if (SwitchModelHandler != null)
        {
            try
            {
                await SwitchModelHandler.Invoke(SelectedModelFullPath, null);
                return (true, $"Switched to model: {CurrentModel}");
            }
            catch (Exception ex)
            {
                return (false, $"Failed to switch model: {ex.Message}");
            }
        }

        // No handler - just update the selection
        return (true, $"Selected model: {CurrentModel}");
    }

    /// <summary>
    /// Check if a specific model file exists
    /// </summary>
    public bool ModelExists(string modelFileName)
    {
        if (string.IsNullOrWhiteSpace(modelFileName)) return false;
        var fullPath = Path.Combine(ModelFolderPath ?? string.Empty, modelFileName);
        return File.Exists(fullPath);
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}
