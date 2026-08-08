using FluentAssertions;
using Services.Configuration;

namespace Services.Tests.Configuration;

/// <summary>
/// Unit tests for <see cref="UserSettingsStore"/>: the JSON file behind the dashboard's Settings
/// page. Loading must never throw — a corrupt file has to leave the app starting on its defaults
/// rather than failing — while saving must throw, because a silently skipped save looks exactly
/// like a successful one to the user.
/// </summary>
public sealed class UserSettingsStoreTests : IDisposable
{
    private readonly string _rootDir;
    private readonly string _settingsFilePath;

    public UserSettingsStoreTests()
    {
        _rootDir = Path.Combine(Path.GetTempPath(), "UserSettingsStoreTests_" + Guid.NewGuid());
        _settingsFilePath = Path.Combine(_rootDir, "settings", "settings.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDir))
            Directory.Delete(_rootDir, recursive: true);
    }

    private UserSettingsStore CreateSut() => new(_settingsFilePath);

    [Fact]
    public void Constructor_NoPath_DefaultsToAppDataFile()
    {
        var sut = new UserSettingsStore();

        sut.FilePath.Should().EndWith(Path.Combine("OfflineAI", "settings.json"));
    }

    [Fact]
    public void Load_NoFile_ReturnsNull()
    {
        var sut = CreateSut();

        sut.Load().Should().BeNull();
        sut.Exists.Should().BeFalse();
    }

    [Fact]
    public void Load_CorruptFile_ReturnsNullInsteadOfThrowing()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsFilePath)!);
        File.WriteAllText(_settingsFilePath, "{ this is not json");
        var sut = CreateSut();

        sut.Load().Should().BeNull();
    }

    [Fact]
    public void Save_CreatesMissingDirectoryAndRoundTrips()
    {
        var sut = CreateSut();
        var settings = new UserSettings();
        settings.Generation.Temperature = 0.42;
        settings.Generation.MaxTokens = 1234;
        settings.Agent.MaxIterations = 9;
        settings.Agent.VerificationTemperature = 0.15;
        settings.Gemma4.ContextSize = 4096;

        sut.Save(settings);
        var loaded = sut.Load();

        sut.Exists.Should().BeTrue();
        loaded.Should().NotBeNull();
        loaded!.Generation.Temperature.Should().Be(0.42);
        loaded.Generation.MaxTokens.Should().Be(1234);
        loaded.Agent.MaxIterations.Should().Be(9);
        loaded.Agent.VerificationTemperature.Should().Be(0.15);
        loaded.Gemma4.ContextSize.Should().Be(4096);
    }

    [Fact]
    public void Delete_RemovesTheFileAndReportsWhetherThereWasOne()
    {
        var sut = CreateSut();

        sut.Delete().Should().BeFalse();

        sut.Save(new UserSettings());
        sut.Delete().Should().BeTrue();
        sut.Exists.Should().BeFalse();
    }

    [Fact]
    public void CaptureThenApply_RoundTripsThroughTheLiveServices()
    {
        var generation = new GenerationSettingsService
        {
            Temperature = 0.65,
            MaxTokens = 777,
            TopK = 55,
            TopP = 0.8,
            RepeatPenalty = 1.25,
            PresencePenalty = 0.3,
            FrequencyPenalty = 0.4,
            TimeoutSeconds = 600,
            PauseTimeoutSeconds = 45,
            RagMode = false,
            PerformanceMetrics = true,
            DebugMode = true,
            RagTopK = 4,
            RagMinRelevanceScore = 0.7,
            UseGpu = false,
            GpuLayers = 30
        };
        var agent = new AgentSettingsService();
        agent.MaxIterations = 13;
        agent.VerificationTemperature = 0.25;
        agent.RequireApproval = false;
        agent.StallLimit = 5;

        var snapshot = UserSettingsStore.Capture(generation, agent);

        var restoredGeneration = new GenerationSettingsService();
        var restoredAgent = new AgentSettingsService();
        UserSettingsStore.ApplyTo(snapshot, restoredGeneration, restoredAgent);

        restoredGeneration.Should().BeEquivalentTo(generation, options => options
            .Including(s => s.Temperature)
            .Including(s => s.MaxTokens)
            .Including(s => s.TopK)
            .Including(s => s.TopP)
            .Including(s => s.RepeatPenalty)
            .Including(s => s.PresencePenalty)
            .Including(s => s.FrequencyPenalty)
            .Including(s => s.TimeoutSeconds)
            .Including(s => s.PauseTimeoutSeconds)
            .Including(s => s.RagMode)
            .Including(s => s.PerformanceMetrics)
            .Including(s => s.DebugMode)
            .Including(s => s.RagTopK)
            .Including(s => s.RagMinRelevanceScore)
            .Including(s => s.UseGpu)
            .Including(s => s.GpuLayers));
        restoredAgent.MaxIterations.Should().Be(13);
        restoredAgent.VerificationTemperature.Should().Be(0.25);
        restoredAgent.RequireApproval.Should().BeFalse();
        restoredAgent.StallLimit.Should().Be(5);
    }

    [Fact]
    public void ApplyTo_OutOfRangeFileValues_AreClampedByTheServices()
    {
        // A hand-edited settings.json must not be able to push the app into an invalid state.
        var settings = new UserSettings();
        settings.Agent.MaxIterations = 10_000;
        settings.Agent.VerificationTemperature = -4;
        settings.Generation.RagTopK = 99;
        settings.Generation.PauseTimeoutSeconds = 9999;

        var generation = new GenerationSettingsService();
        var agent = new AgentSettingsService();
        UserSettingsStore.ApplyTo(settings, generation, agent);

        agent.MaxIterations.Should().Be(AgentSettingsService.MaxAllowedIterations);
        agent.VerificationTemperature.Should().Be(0);
        generation.RagTopK.Should().Be(5);
        generation.PauseTimeoutSeconds.Should().Be(120);
    }
}
