using FluentAssertions;
using Services.Configuration;

namespace Services.Tests.Configuration;

/// <summary>
/// Unit tests for <see cref="AgentSettingsService"/>: the shared, live Agent Mode settings. The
/// values here decide how long a goal-agent run is allowed to keep working and how deterministic
/// its verifications are, and they are edited from two different pages, so the clamping and the
/// change notification are what keep those pages honest.
/// </summary>
public sealed class AgentSettingsServiceTests
{
    [Fact]
    public void Constructor_UsesConfiguredIterationCap()
    {
        var sut = new AgentSettingsService(configuredMaxIterations: 7);

        sut.MaxIterations.Should().Be(7);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Constructor_NonPositiveCap_FallsBackToDefault(int configured)
    {
        var sut = new AgentSettingsService(configured);

        sut.MaxIterations.Should().Be(AgentSettingsService.DefaultMaxIterations);
    }

    [Fact]
    public void Constructor_CapAboveAllowedMaximum_IsClamped()
    {
        var sut = new AgentSettingsService(configuredMaxIterations: 5000);

        sut.MaxIterations.Should().Be(AgentSettingsService.MaxAllowedIterations);
    }

    [Fact]
    public void Constructor_UsesDefaultVerificationTemperature()
    {
        var sut = new AgentSettingsService();

        sut.VerificationTemperature.Should().Be(AgentSettingsService.DefaultVerificationTemperature);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(500, AgentSettingsService.MaxAllowedIterations)]
    public void MaxIterations_OutOfRange_IsClamped(int value, int expected)
    {
        var sut = new AgentSettingsService();

        sut.MaxIterations = value;

        sut.MaxIterations.Should().Be(expected);
    }

    [Theory]
    [InlineData(-1.0, 0.0)]
    [InlineData(9.0, 2.0)]
    public void VerificationTemperature_OutOfRange_IsClamped(double value, double expected)
    {
        var sut = new AgentSettingsService();

        sut.VerificationTemperature = value;

        sut.VerificationTemperature.Should().Be(expected);
    }

    [Fact]
    public void SettingAValue_RaisesOnChange()
    {
        var sut = new AgentSettingsService();
        var notifications = 0;
        sut.OnChange += () => notifications++;

        sut.MaxIterations = 5;
        sut.VerificationTemperature = 0.9;

        notifications.Should().Be(2);
    }

    [Fact]
    public void SettingTheSameValue_DoesNotRaiseOnChange()
    {
        var sut = new AgentSettingsService(configuredMaxIterations: 12);
        var notifications = 0;
        sut.OnChange += () => notifications++;

        sut.MaxIterations = 12;
        sut.VerificationTemperature = AgentSettingsService.DefaultVerificationTemperature;

        notifications.Should().Be(0);
    }

    [Fact]
    public void RequireApproval_DefaultsToOn()
    {
        // A misread requirement list is the cheapest failure to catch before any work starts.
        new AgentSettingsService().RequireApproval.Should().BeTrue();
    }

    [Fact]
    public void StallLimit_DefaultsToTheStandardValue()
    {
        new AgentSettingsService().StallLimit.Should().Be(AgentSettingsService.DefaultStallLimit);
    }

    [Theory]
    [InlineData(-4, 0)]
    [InlineData(0, 0)]
    [InlineData(500, AgentSettingsService.MaxAllowedStallLimit)]
    public void StallLimit_OutOfRange_IsClamped(int value, int expected)
    {
        var sut = new AgentSettingsService();

        sut.StallLimit = value;

        // 0 is a legitimate setting — it turns the check off — so it is clamped to, not away from.
        sut.StallLimit.Should().Be(expected);
    }

    [Fact]
    public void ResetToDefaults_RestoresConfiguredCapNotTheHardcodedOne()
    {
        var sut = new AgentSettingsService(configuredMaxIterations: 8);
        sut.MaxIterations = 42;
        sut.VerificationTemperature = 1.5;
        sut.RequireApproval = false;
        sut.StallLimit = 0;

        sut.ResetToDefaults();

        sut.MaxIterations.Should().Be(8);
        sut.VerificationTemperature.Should().Be(AgentSettingsService.DefaultVerificationTemperature);
        sut.RequireApproval.Should().BeTrue();
        sut.StallLimit.Should().Be(AgentSettingsService.DefaultStallLimit);
    }
}
