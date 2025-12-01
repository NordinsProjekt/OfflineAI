using Entities;
using Services.Repositories;

namespace Services.Management;

/// <summary>
/// Service for managing bot personalities.
/// Handles CRUD operations and personality selection.
/// </summary>
public class BotPersonalityService(IBotPersonalityRepository repository)
{
    private readonly IBotPersonalityRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    
    // Change notification for UI components
    public event Action? OnChange;
    
    private List<BotPersonalityEntity> _availablePersonalities = new();
    public IReadOnlyList<BotPersonalityEntity> AvailablePersonalities => _availablePersonalities.AsReadOnly();
    
    private BotPersonalityEntity? _currentPersonality;
    public BotPersonalityEntity? CurrentPersonality
    {
        get => _currentPersonality;
        set
        {
            if (_currentPersonality == value) return;
            _currentPersonality = value;
            NotifyStateChanged();
        }
    }

    /// <summary>
    /// Refresh the list of available personalities from the database.
    /// </summary>
    public async Task<(bool Success, string Message)> RefreshPersonalitiesAsync()
    {
        try
        {
            _availablePersonalities.Clear();
            var personalities = await _repository.GetAllActiveAsync();
            _availablePersonalities.AddRange(personalities);
            NotifyStateChanged();
            return (true, $"Found {personalities.Count} bot personalit{(personalities.Count == 1 ? "y" : "ies")}");
        }
        catch (Exception ex)
        {
            return (false, $"Failed to refresh personalities: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Get all personalities including inactive ones.
    /// </summary>
    public async Task<List<BotPersonalityEntity>> GetAllPersonalitiesAsync(bool includeInactive = false)
    {
        return includeInactive 
            ? await _repository.GetAllAsync() 
            : await _repository.GetAllActiveAsync();
    }
    
    /// <summary>
    /// Select a personality by ID.
    /// </summary>
    public async Task<(bool Success, string Message)> SelectPersonalityAsync(string personalityId)
    {
        if (string.IsNullOrWhiteSpace(personalityId))
        {
            CurrentPersonality = null;
            return (true, "No personality selected");
        }
        
        try
        {
            var personality = await _repository.GetByPersonalityIdAsync(personalityId);
            if (personality == null)
            {
                return (false, $"Personality '{personalityId}' not found");
            }
            
            if (!personality.IsActive)
            {
                return (false, $"Personality '{personality.DisplayName}' is not active");
            }
            
            CurrentPersonality = personality;
            return (true, $"Selected: {personality.DisplayName}");
        }
        catch (Exception ex)
        {
            return (false, $"Failed to select personality: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Delete a personality.
    /// </summary>
    public async Task<(bool Success, string Message)> DeletePersonalityAsync(string personalityId)
    {
        if (string.IsNullOrWhiteSpace(personalityId))
        {
            return (false, "Personality ID is required");
        }
        
        try
        {
            var exists = await _repository.ExistsAsync(personalityId);
            if (!exists)
            {
                return (false, $"Personality '{personalityId}' not found");
            }
            
            await _repository.DeleteAsync(personalityId);
            
            // Clear current personality if it was deleted
            if (CurrentPersonality?.PersonalityId == personalityId)
            {
                CurrentPersonality = null;
            }
            
            await RefreshPersonalitiesAsync();
            return (true, $"Deleted personality: {personalityId}");
        }
        catch (Exception ex)
        {
            return (false, $"Failed to delete personality: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Initialize and seed default personalities.
    /// </summary>
    public async Task InitializeAsync()
    {
        await _repository.InitializeDatabaseAsync();
        await _repository.SeedDefaultPersonalitiesAsync();
    }
    
    private void NotifyStateChanged() => OnChange?.Invoke();
}
