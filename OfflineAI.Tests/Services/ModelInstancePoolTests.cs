using Services;
using Xunit;
using FluentAssertions;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace OfflineAI.Tests.Services;

/// <summary>
/// Unit tests for ModelInstancePool.
/// Tests pool initialization, instance acquisition, concurrency, and resource management.
/// </summary>
public class ModelInstancePoolTests : IDisposable
{
    private readonly string _testLlmPath;
    private readonly string _testModelPath;
    private readonly string _tempDir;

    public ModelInstancePoolTests()
    {
        // Create temporary files for testing
        _tempDir = Path.Combine(Path.GetTempPath(), "OfflineAI_Tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        
        _testLlmPath = Path.Combine(_tempDir, "test-llama-cli.exe");
        _testModelPath = Path.Combine(_tempDir, "test-model.gguf");
        
        File.WriteAllText(_testLlmPath, "mock executable");
        File.WriteAllText(_testModelPath, "mock model");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_ShouldSucceed()
    {
        // Act
        var pool = new ModelInstancePool(_testLlmPath, _testModelPath, maxInstances: 3);

        // Assert
        pool.Should().NotBeNull();
        pool.MaxInstances.Should().Be(3);
        pool.AvailableCount.Should().Be(0); // Not initialized yet

        // Cleanup
        pool.Dispose();
    }

    [Fact]
    public void Constructor_WithDefaultParameters_ShouldUseDefaultMaxInstances()
    {
        // Act
        var pool = new ModelInstancePool(_testLlmPath, _testModelPath);

        // Assert
        pool.MaxInstances.Should().Be(3); // Default value

        // Cleanup
        pool.Dispose();
    }

    [Fact]
    public void Constructor_WithZeroInstances_ShouldThrowArgumentException()
    {
        // Act & Assert
        var act = () => new ModelInstancePool(_testLlmPath, _testModelPath, maxInstances: 0);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Must have at least 1 instance*");
    }

    [Fact]
    public void Constructor_WithNegativeInstances_ShouldThrowArgumentException()
    {
        // Act & Assert
        var act = () => new ModelInstancePool(_testLlmPath, _testModelPath, maxInstances: -1);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Must have at least 1 instance*");
    }

    [Fact]
    public void Constructor_WithNullLlmPath_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var act = () => new ModelInstancePool(null!, _testModelPath, maxInstances: 3);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("llmPath");
    }

    [Fact]
    public void Constructor_WithNullModelPath_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var act = () => new ModelInstancePool(_testLlmPath, null!, maxInstances: 3);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("modelPath");
    }

    #endregion

    #region InitializeAsync Tests

    [Fact]
    public async Task InitializeAsync_WithValidPaths_ShouldLoadAllInstances()
    {
        // Arrange
        var pool = new ModelInstancePool(_testLlmPath, _testModelPath, maxInstances: 3);

        // Act
        await pool.InitializeAsync();

        // Assert
        pool.AvailableCount.Should().Be(3);
        pool.MaxInstances.Should().Be(3);

        // Cleanup
        pool.Dispose();
    }

    [Fact]
    public async Task InitializeAsync_WithProgressCallback_ShouldInvokeCallback()
    {
        // Arrange
        var pool = new ModelInstancePool(_testLlmPath, _testModelPath, maxInstances: 2);
        var callbackInvocations = new List<(int current, int total)>();

        // Act
        await pool.InitializeAsync((current, total) =>
        {
            lock (callbackInvocations)
            {
                callbackInvocations.Add((current, total));
            }
        });

        // Wait a moment for async callbacks to complete
        await Task.Delay(100);

        // Assert
        callbackInvocations.Count.Should().BeGreaterThanOrEqualTo(1);
        callbackInvocations.All(c => c.total == 2).Should().BeTrue();

        // Cleanup
        pool.Dispose();
    }

    [Fact]
    public async Task InitializeAsync_WithInvalidPaths_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var invalidPath = Path.Combine(_tempDir, "nonexistent.exe");
        var pool = new ModelInstancePool(invalidPath, _testModelPath, maxInstances: 1);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await pool.InitializeAsync());

        // Cleanup
        pool.Dispose();
    }

    [Fact]
    public async Task InitializeAsync_SingleInstance_ShouldLoadOneInstance()
    {
        // Arrange
        var pool = new ModelInstancePool(_testLlmPath, _testModelPath, maxInstances: 1);

        // Act
        await pool.InitializeAsync();

        // Assert
        pool.AvailableCount.Should().Be(1);
        pool.MaxInstances.Should().Be(1);

        // Cleanup
        pool.Dispose();
    }

    #endregion

    #region AcquireAsync Tests

    [Fact]
    public async Task AcquireAsync_AfterInitialization_ShouldReturnPooledInstance()
    {
        // Arrange
        var pool = new ModelInstancePool(_testLlmPath, _testModelPath, maxInstances: 2);
        await pool.InitializeAsync();

        // Act
        using var instance = await pool.AcquireAsync();

        // Assert
        instance.Should().NotBeNull();
        instance.Process.Should().NotBeNull();
        pool.AvailableCount.Should().Be(1); // One acquired, one remaining

        // Cleanup
        pool.Dispose();
    }

    [Fact]
    public async Task AcquireAsync_DisposedPool_ShouldThrowObjectDisposedException()
    {
        // Arrange
        var pool = new ModelInstancePool(_testLlmPath, _testModelPath, maxInstances: 1);
        await pool.InitializeAsync();
        pool.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await pool.AcquireAsync());
    }

    [Fact]
    public async Task AcquireAsync_MultipleAcquisitions_ShouldReduceAvailableCount()
    {
        // Arrange
        var pool = new ModelInstancePool(_testLlmPath, _testModelPath, maxInstances: 3);
        await pool.InitializeAsync();

        // Act
        var instance1 = await pool.AcquireAsync();
        var instance2 = await pool.AcquireAsync();

        // Assert
        pool.AvailableCount.Should().Be(1); // 3 - 2 = 1
        
        // Cleanup
        instance1.Dispose();
        instance2.Dispose();
        pool.Dispose();
    }

    [Fact]
    public async Task AcquireAsync_WithCancellation_ShouldRespectCancellationToken()
    {
        // Arrange
        var pool = new ModelInstancePool(_testLlmPath, _testModelPath, maxInstances: 1);
        await pool.InitializeAsync();
        
        // Acquire the only instance
        var instance = await pool.AcquireAsync();
        
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert - TaskCanceledException inherits from OperationCanceledException
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await pool.AcquireAsync(cts.Token));

        // Cleanup
        instance.Dispose();
        pool.Dispose();
    }

    #endregion

    #region PooledInstance Tests

    [Fact]
    public async Task PooledInstance_Dispose_ShouldReturnToPool()
    {
        // Arrange
        var pool = new ModelInstancePool(_testLlmPath, _testModelPath, maxInstances: 2);
        await pool.InitializeAsync();

        // Act
        var instanceBefore = pool.AvailableCount;
        var pooledInstance = await pool.AcquireAsync();
        var instanceDuring = pool.AvailableCount;
        pooledInstance.Dispose();
        var instanceAfter = pool.AvailableCount;

        // Assert
        instanceBefore.Should().Be(2);
        instanceDuring.Should().Be(1); // One acquired
        instanceAfter.Should().Be(2);  // Returned to pool

        // Cleanup
        pool.Dispose();
    }

    [Fact]
    public async Task PooledInstance_UsingStatement_ShouldAutoReturn()
    {
        // Arrange
        var pool = new ModelInstancePool(_testLlmPath, _testModelPath, maxInstances: 2);
        await pool.InitializeAsync();

        // Act
        var beforeCount = pool.AvailableCount;
        using (var instance = await pool.AcquireAsync())
        {
            var duringCount = pool.AvailableCount;
            duringCount.Should().Be(1); // One in use
        }
        var afterCount = pool.AvailableCount;

        // Assert
        beforeCount.Should().Be(2);
        afterCount.Should().Be(2); // Automatically returned

        // Cleanup
        pool.Dispose();
    }

    [Fact]
    public async Task PooledInstance_MultipleDisposes_ShouldBeIdempotent()
    {
        // Arrange
        var pool = new ModelInstancePool(_testLlmPath, _testModelPath, maxInstances: 1);
        await pool.InitializeAsync();

        // Act
        var instance = await pool.AcquireAsync();
        instance.Dispose();
        var act = () => instance.Dispose();

        // Assert
        act.Should().NotThrow();
        pool.AvailableCount.Should().Be(1);

        // Cleanup
        pool.Dispose();
    }

    #endregion

    #region Concurrency Tests

    [Fact]
    public async Task AcquireAsync_Concurrent_ShouldHandleMultipleThreads()
    {
        // Arrange
        var pool = new ModelInstancePool(_testLlmPath, _testModelPath, maxInstances: 3);
        await pool.InitializeAsync();

        var tasks = new List<Task>();
        var acquiredInstances = new List<ModelInstancePool.PooledInstance>();
        var lockObj = new object();

        // Act - Acquire 3 instances concurrently
        for (int i = 0; i < 3; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                var instance = await pool.AcquireAsync();
                lock (lockObj)
                {
                    acquiredInstances.Add(instance);
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert
        acquiredInstances.Should().HaveCount(3);
        pool.AvailableCount.Should().Be(0);

        // Cleanup
        foreach (var instance in acquiredInstances)
        {
            instance.Dispose();
        }
        pool.Dispose();
    }

    [Fact]
    public async Task AcquireAsync_MoreThanMaxConcurrent_ShouldBlock()
    {
        // Arrange
        var pool = new ModelInstancePool(_testLlmPath, _testModelPath, maxInstances: 2);
        await pool.InitializeAsync();

        var instance1 = await pool.AcquireAsync();
        var instance2 = await pool.AcquireAsync();

        // Act - Try to acquire third (should block)
        var acquireTask = pool.AcquireAsync();
        await Task.Delay(100); // Give it time to block

        // Assert
        acquireTask.IsCompleted.Should().BeFalse(); // Still waiting

        // Release one
        instance1.Dispose();
        await Task.Delay(100);

        // Now it should complete
        acquireTask.IsCompleted.Should().BeTrue();

        // Cleanup
        instance2.Dispose();
        (await acquireTask).Dispose();
        pool.Dispose();
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public async Task Dispose_ShouldReleaseAllInstances()
    {
        // Arrange
        var pool = new ModelInstancePool(_testLlmPath, _testModelPath, maxInstances: 3);
        await pool.InitializeAsync();

        // Act
        pool.Dispose();

        // Assert
        pool.AvailableCount.Should().Be(0); // All instances disposed

        // Further operations should fail
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await pool.AcquireAsync());
    }

    [Fact]
    public async Task Dispose_MultipleTimesShould_BeIdempotent()
    {
        // Arrange
        var pool = new ModelInstancePool(_testLlmPath, _testModelPath, maxInstances: 1);
        await pool.InitializeAsync();

        // Act
        pool.Dispose();
        var act = () => pool.Dispose();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public async Task Dispose_WithAcquiredInstances_ShouldStillDispose()
    {
        // Arrange
        var pool = new ModelInstancePool(_testLlmPath, _testModelPath, maxInstances: 2);
        await pool.InitializeAsync();
        var instance = await pool.AcquireAsync();

        // Act
        pool.Dispose();

        // Assert
        // Instance should still be usable until explicitly disposed
        instance.Should().NotBeNull();
        
        // But trying to return it should not add back to pool
        instance.Dispose();
        pool.AvailableCount.Should().Be(0);
    }

    #endregion

    #region Property Tests

    [Fact]
    public void MaxInstances_ShouldReturnConfiguredValue()
    {
        // Arrange & Act
        var pool1 = new ModelInstancePool(_testLlmPath, _testModelPath, maxInstances: 5);
        var pool2 = new ModelInstancePool(_testLlmPath, _testModelPath, maxInstances: 10);

        // Assert
        pool1.MaxInstances.Should().Be(5);
        pool2.MaxInstances.Should().Be(10);

        // Cleanup
        pool1.Dispose();
        pool2.Dispose();
    }

    [Fact]
    public async Task AvailableCount_ShouldReflectPoolState()
    {
        // Arrange
        var pool = new ModelInstancePool(_testLlmPath, _testModelPath, maxInstances: 3);

        // Initially not initialized
        pool.AvailableCount.Should().Be(0);

        // After initialization
        await pool.InitializeAsync();
        pool.AvailableCount.Should().Be(3);

        // After acquiring one
        var instance = await pool.AcquireAsync();
        pool.AvailableCount.Should().Be(2);

        // After returning
        instance.Dispose();
        pool.AvailableCount.Should().Be(3);

        // Cleanup
        pool.Dispose();
    }

    #endregion

    #region Integration Scenario Tests

    [Fact]
    public async Task Scenario_TypicalWebRequest_ShouldAcquireUseReturn()
    {
        // Arrange - Simulates a web application with 3 instance pool
        var pool = new ModelInstancePool(_testLlmPath, _testModelPath, maxInstances: 3);
        await pool.InitializeAsync();

        // Act - Simulate multiple requests
        var request1 = SimulateWebRequestAsync(pool, delayMs: 50);
        var request2 = SimulateWebRequestAsync(pool, delayMs: 50);
        var request3 = SimulateWebRequestAsync(pool, delayMs: 50);

        await Task.WhenAll(request1, request2, request3);

        // Assert - All requests completed and instances returned
        pool.AvailableCount.Should().Be(3);

        // Cleanup
        pool.Dispose();
    }

    [Fact]
    public async Task Scenario_BurstTraffic_ShouldHandleGracefully()
    {
        // Arrange
        var pool = new ModelInstancePool(_testLlmPath, _testModelPath, maxInstances: 3);
        await pool.InitializeAsync();

        // Act - Simulate 10 requests with only 3 instances
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => SimulateWebRequestAsync(pool, delayMs: 20))
            .ToList();

        await Task.WhenAll(tasks);

        // Assert - All requests completed successfully
        tasks.Should().AllSatisfy(t => t.IsCompletedSuccessfully.Should().BeTrue());
        pool.AvailableCount.Should().Be(3); // All returned

        // Cleanup
        pool.Dispose();
    }

    private async Task SimulateWebRequestAsync(ModelInstancePool pool, int delayMs)
    {
        using var instance = await pool.AcquireAsync();
        await Task.Delay(delayMs); // Simulate processing time
        // Instance automatically returned on dispose
    }

    #endregion
}
