using System;

namespace Services.Configuration;

/// <summary>
/// Service for managing LLM generation settings with change notification support.
/// Can be used by any UI framework (Blazor, WPF, Console, etc.)
/// </summary>
public class GenerationSettingsService
{
    // Change notification for UI components
    public event Action? OnChange;

    private double _temperature = 0.7;
    public double Temperature
    {
        get => _temperature;
        set
        {
            if (Math.Abs(_temperature - value) < 0.0001) return;
            _temperature = value;
            NotifyStateChanged();
        }
    }

    private int _maxTokens = 512;
    public int MaxTokens
    {
        get => _maxTokens;
        set
        {
            if (_maxTokens == value) return;
            _maxTokens = value;
            NotifyStateChanged();
        }
    }

    private int _topK = 40;
    public int TopK
    {
        get => _topK;
        set
        {
            if (_topK == value) return;
            _topK = value;
            NotifyStateChanged();
        }
    }

    private double _topP = 0.95;
    public double TopP
    {
        get => _topP;
        set
        {
            if (Math.Abs(_topP - value) < 0.0001) return;
            _topP = value;
            NotifyStateChanged();
        }
    }

    private double _repeatPenalty = 1.1;
    public double RepeatPenalty
    {
        get => _repeatPenalty;
        set
        {
            if (Math.Abs(_repeatPenalty - value) < 0.0001) return;
            _repeatPenalty = value;
            NotifyStateChanged();
        }
    }

    private double _presencePenalty = 0.0;
    public double PresencePenalty
    {
        get => _presencePenalty;
        set
        {
            if (Math.Abs(_presencePenalty - value) < 0.0001) return;
            _presencePenalty = value;
            NotifyStateChanged();
        }
    }

    private double _frequencyPenalty = 0.0;
    public double FrequencyPenalty
    {
        get => _frequencyPenalty;
        set
        {
            if (Math.Abs(_frequencyPenalty - value) < 0.0001) return;
            _frequencyPenalty = value;
            NotifyStateChanged();
        }
    }

    private int _timeoutSeconds = 30;
    public int TimeoutSeconds
    {
        get => _timeoutSeconds;
        set
        {
            if (_timeoutSeconds == value) return;
            _timeoutSeconds = value;
            NotifyStateChanged();
        }
    }

    private bool _ragMode = true;
    public bool RagMode
    {
        get => _ragMode;
        set
        {
            if (_ragMode == value) return;
            _ragMode = value;
            NotifyStateChanged();
        }
    }

    private bool _performanceMetrics = false;
    public bool PerformanceMetrics
    {
        get => _performanceMetrics;
        set
        {
            if (_performanceMetrics == value) return;
            _performanceMetrics = value;
            NotifyStateChanged();
        }
    }

    private bool _debugMode = false;
    public bool DebugMode
    {
        get => _debugMode;
        set
        {
            if (_debugMode == value) return;
            _debugMode = value;
            NotifyStateChanged();
        }
    }

    private int _ragTopK = 3;
    public int RagTopK
    {
        get => _ragTopK;
        set
        {
            // Clamp between 1 and 5
            var clampedValue = Math.Clamp(value, 1, 5);
            if (_ragTopK == clampedValue) return;
            _ragTopK = clampedValue;
            NotifyStateChanged();
        }
    }

    private double _ragMinRelevanceScore = 0.5;
    public double RagMinRelevanceScore
    {
        get => _ragMinRelevanceScore;
        set
        {
            // Clamp between 0.3 and 0.8
            var clampedValue = Math.Clamp(value, 0.3, 0.8);
            if (Math.Abs(_ragMinRelevanceScore - clampedValue) < 0.001) return;
            _ragMinRelevanceScore = clampedValue;
            NotifyStateChanged();
        }
    }

    /// <summary>
    /// Create a GenerationSettings object from current settings
    /// </summary>
    public GenerationSettings ToGenerationSettings()
    {
        return new GenerationSettings
        {
            Temperature = (float)Temperature,
            MaxTokens = MaxTokens,
            TopK = TopK,
            TopP = (float)TopP,
            RepeatPenalty = (float)RepeatPenalty,
            PresencePenalty = (float)PresencePenalty,
            FrequencyPenalty = (float)FrequencyPenalty,
            RagTopK = RagTopK,
            RagMinRelevanceScore = RagMinRelevanceScore
        };
    }

    /// <summary>
    /// Load settings from a GenerationSettings object
    /// </summary>
    public void FromGenerationSettings(GenerationSettings settings)
    {
        Temperature = settings.Temperature;
        MaxTokens = settings.MaxTokens;
        TopK = settings.TopK;
        TopP = settings.TopP;
        RepeatPenalty = settings.RepeatPenalty;
        PresencePenalty = settings.PresencePenalty;
        FrequencyPenalty = settings.FrequencyPenalty;
        RagTopK = settings.RagTopK;
        RagMinRelevanceScore = settings.RagMinRelevanceScore;
    }

    /// <summary>
    /// Reset all settings to default values
    /// </summary>
    public void ResetToDefaults()
    {
        Temperature = 0.7;
        MaxTokens = 512;
        TopK = 40;
        TopP = 0.95;
        RepeatPenalty = 1.1;
        PresencePenalty = 0.0;
        FrequencyPenalty = 0.0;
        TimeoutSeconds = 30;
        RagMode = true;
        PerformanceMetrics = false;
        DebugMode = false;
        RagTopK = 3;
        RagMinRelevanceScore = 0.5;
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}
