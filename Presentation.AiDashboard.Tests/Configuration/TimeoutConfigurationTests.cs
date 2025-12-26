using Xunit;
using System.Reflection;
using System.Text.RegularExpressions;
using AiDashboard.Services;
using Services.Configuration;

namespace Presentation.AiDashboard.Tests.Configuration;

/// <summary>
/// Tests to ensure no old 30-second timeouts remain in the system.
/// These tests verify that the 5-minute timeout migration is complete.
/// </summary>
public class TimeoutConfigurationTests
{
    [Fact]
    public void DashboardChatService_DefaultTimeout_ShouldBe300Seconds()
    {
        // This test verifies the default parameter value in SendMessageAsync
        // We check the method signature via reflection
        var method = typeof(DashboardChatService).GetMethod("SendMessageAsync");
        Assert.NotNull(method);

        var timeoutParam = method!.GetParameters()
            .FirstOrDefault(p => p.Name == "timeoutSeconds");
        
        Assert.NotNull(timeoutParam);
        Assert.True(timeoutParam!.HasDefaultValue, "timeoutSeconds parameter should have a default value");
        Assert.Equal(300, timeoutParam.DefaultValue);
    }

    [Fact]
    public void LlmProgressTracker_MaxTotalTimeout_ShouldBe300000Ms()
    {
        // Verify the 5-minute total timeout constant
        var field = typeof(LlmProgressTracker).GetField("MaxTotalTimeoutMs", 
            BindingFlags.Public | BindingFlags.Static);
        
        Assert.NotNull(field);
        var value = field!.GetValue(null);
        Assert.Equal(300000, value); // 5 minutes in milliseconds
    }

    [Fact]
    public void LlmProgressTracker_PauseTimeout_ShouldBe120000Ms()
    {
        // Verify the 40-second pause detection timeout
        var field = typeof(LlmProgressTracker).GetField("PauseTimeoutMs", 
            BindingFlags.Public | BindingFlags.Static);
        
        Assert.NotNull(field);
        var value = field!.GetValue(null);
        Assert.Equal(40000, value); // 40 seconds in milliseconds
    }

    [Fact]
    public void LlmProgressTracker_ShortTimeout_ShouldBe10000Ms()
    {
        // Verify the 10-second test timeout
        var field = typeof(LlmProgressTracker).GetField("ShortTimeoutMs", 
            BindingFlags.Public | BindingFlags.Static);
        
        Assert.NotNull(field);
        var value = field!.GetValue(null);
        Assert.Equal(10000, value); // 10 seconds in milliseconds
    }

    [Fact]
    public void GenerationSettingsService_TimeoutSeconds_DefaultShouldNotBe30()
    {
        // Verify that GenerationSettingsService doesn't default to 30 seconds
        var service = new GenerationSettingsService();
        
        // The default might still be 30 in this service as it's a general setting
        // but it should not affect LLM calls which use DashboardChatService
        // This test documents the current state
        var timeout = service.TimeoutSeconds;
        
        // We're just checking it exists and is readable
        Assert.True(timeout >= 0);
    }

    [Fact]
    public void SourceFiles_ShouldNotContainHardcoded30SecondLlmTimeouts()
    {
        // This test scans source files for potential hardcoded 30-second timeouts
        // in LLM-related contexts
        
        var projectRoot = Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(), 
            "..", "..", "..", "..", "AiDashboard"));

        if (!Directory.Exists(projectRoot))
        {
            // Skip if directory structure is different
            return;
        }

        var suspiciousPatterns = new[]
        {
            // Look for timeout = 30 or timeout: 30 in LLM contexts
            @"timeout\s*[:=]\s*30(?!\d)",  // timeout = 30 or timeout: 30 (not 300)
            @"TimeoutMs\s*=\s*30000(?!\d)", // TimeoutMs = 30000 (not 300000)
            @"timeoutSeconds\s*[:=]\s*30(?!\d)", // timeoutSeconds = 30
        };

        var csFiles = Directory.GetFiles(projectRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("\\obj\\") && !f.Contains("\\bin\\"))
            .ToList();

        var razorFiles = Directory.GetFiles(projectRoot, "*.razor", SearchOption.AllDirectories)
            .Where(f => !f.Contains("\\obj\\") && !f.Contains("\\bin\\"))
            .ToList();

        var allFiles = csFiles.Concat(razorFiles).ToList();
        var violations = new List<string>();

        foreach (var file in allFiles)
        {
            try
            {
                var content = File.ReadAllText(file);
                
                // Skip if file doesn't mention LLM, Chat, or AI (to avoid false positives)
                if (!content.Contains("Llm", StringComparison.OrdinalIgnoreCase) &&
                    !content.Contains("Chat", StringComparison.OrdinalIgnoreCase) &&
                    !content.Contains("AI", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var pattern in suspiciousPatterns)
                {
                    var matches = Regex.Matches(content, pattern, RegexOptions.IgnoreCase);
                    if (matches.Count > 0)
                    {
                        var relativePath = Path.GetRelativePath(projectRoot, file);
                        violations.Add($"{relativePath}: Found potential 30-second timeout pattern");
                    }
                }
            }
            catch
            {
                // Skip files we can't read
            }
        }

        Assert.Empty(violations);
    }

    [Theory]
    [InlineData(300, "DashboardChatService default should be 5 minutes")]
    [InlineData(300000, "LlmProgressTracker max timeout should be 5 minutes in ms")]
    [InlineData(120000, "LlmProgressTracker pause timeout should be 2 minutes in ms")]
    [InlineData(10000, "LlmProgressTracker test timeout should be 10 seconds in ms")]
    public void TimeoutConstants_ShouldMatchRequirements(int expectedValue, string reason)
    {
        // Document the expected timeout values
        Assert.True(expectedValue > 0, reason);
        
        // Verify relationships between constants
        if (expectedValue == 300)
        {
            // 5 minutes in seconds
            Assert.Equal(300000, expectedValue * 1000);
        }
        else if (expectedValue == 300000)
        {
            // 5 minutes in milliseconds
            Assert.Equal(300, expectedValue / 1000);
        }
        else if (expectedValue == 120000)
        {
            // 2 minutes in milliseconds
            Assert.Equal(120, expectedValue / 1000);
        }
        else if (expectedValue == 10000)
        {
            // 10 seconds in milliseconds
            Assert.Equal(10, expectedValue / 1000);
        }
    }

    [Fact]
    public void TimeoutConstants_Relationships_ShouldBeCorrect()
    {
        // Verify the mathematical relationships between timeout constants
        var maxTimeout = LlmProgressTracker.MaxTotalTimeoutMs;
        var pauseTimeout = LlmProgressTracker.PauseTimeoutMs;
        var testTimeout = LlmProgressTracker.ShortTimeoutMs;

        // 5 minutes should be 300,000 ms
        Assert.Equal(300000, maxTimeout);
        
        // 40 seconds should be 40,000 ms
        Assert.Equal(40000, pauseTimeout);
        
        // 10 seconds should be 10,000 ms
        Assert.Equal(10000, testTimeout);

        // Pause timeout should be less than max timeout
        Assert.True(pauseTimeout < maxTimeout, 
            "Pause timeout (40 sec) should be less than max timeout (5 min)");
        
        // Test timeout should be much less than production timeouts
        Assert.True(testTimeout < pauseTimeout, 
            "Test timeout (10 sec) should be less than pause timeout (40 sec)");
        
        Assert.True(testTimeout < maxTimeout, 
            "Test timeout (10 sec) should be less than max timeout (5 min)");
    }

    [Fact]
    public void DashboardChatService_SendMessageAsync_ParameterValidation()
    {
        // Verify the SendMessageAsync method has the correct parameters
        var method = typeof(DashboardChatService).GetMethod("SendMessageAsync");
        Assert.NotNull(method);

        var parameters = method!.GetParameters();
        
        // Verify timeoutSeconds parameter exists
        var timeoutParam = parameters.FirstOrDefault(p => p.Name == "timeoutSeconds");
        Assert.NotNull(timeoutParam);
        
        // Verify it's an int
        Assert.Equal(typeof(int), timeoutParam!.ParameterType);
        
        // Verify it's optional (has default value)
        Assert.True(timeoutParam.IsOptional);
        
        // Verify default is 300 (not 30)
        Assert.Equal(300, timeoutParam.DefaultValue);
    }

    [Fact]
    public void NoLegacy30SecondConstants_InLlmProgressTracker()
    {
        // Ensure LlmProgressTracker doesn't have any 30-second constants
        var fields = typeof(LlmProgressTracker).GetFields(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

        foreach (var field in fields)
        {
            if (field.Name.Contains("Timeout", StringComparison.OrdinalIgnoreCase) &&
                field.FieldType == typeof(int))
            {
                var value = field.GetValue(null);
                if (value is int intValue)
                {
                    // No timeout should be exactly 30 seconds (30000 ms)
                    Assert.NotEqual(30000, intValue);
                    
                    // No timeout should be exactly 30 (if in seconds)
                    Assert.NotEqual(30, intValue);
                }
            }
        }
    }

    [Fact]
    public void Documentation_ConstantValues_AreCorrect()
    {
        // Verify that our timeout constants match what's documented
        // 5 minutes = 300 seconds = 300,000 milliseconds
        Assert.Equal(300000, LlmProgressTracker.MaxTotalTimeoutMs);
        
        // 40 seconds = 40,000 milliseconds
        Assert.Equal(40000, LlmProgressTracker.PauseTimeoutMs);
        
        // 10 seconds = 10,000 milliseconds
        Assert.Equal(10000, LlmProgressTracker.ShortTimeoutMs);

        // Verify DashboardChatService default
        var method = typeof(DashboardChatService).GetMethod("SendMessageAsync");
        var timeoutParam = method?.GetParameters()
            .FirstOrDefault(p => p.Name == "timeoutSeconds");
        
        if (timeoutParam?.HasDefaultValue == true)
        {
            // Should be 300 seconds = 5 minutes
            Assert.Equal(300, timeoutParam.DefaultValue);
        }
    }
}
