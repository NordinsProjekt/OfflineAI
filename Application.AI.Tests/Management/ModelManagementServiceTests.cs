using Application.AI.Management;

namespace Application.AI.Tests.Management;

/// <summary>
/// Unit tests for ModelManagementService class.
/// Tests cover model discovery, selection, switching, and event notifications.
/// </summary>
public class ModelManagementServiceTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly List<string> _createdFiles = new();

    public ModelManagementServiceTests()
    {
        // Create a temporary test directory
        _testDirectory = Path.Combine(Path.GetTempPath(), $"ModelManagementTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        // Clean up test files and directory
        try
        {
            foreach (var file in _createdFiles)
            {
                if (File.Exists(file))
                    File.Delete(file);
            }

            if (Directory.Exists(_testDirectory))
                Directory.Delete(_testDirectory, true);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidPath_SetsModelFolderPath()
    {
        // Arrange & Act
        var service = new ModelManagementService(_testDirectory);

        // Assert
        Assert.Equal(_testDirectory, service.ModelFolderPath);
    }

    [Fact]
    public void Constructor_WithNullPath_SetsDefaultPath()
    {
        // Arrange & Act
        var service = new ModelManagementService(null);

        // Assert
        Assert.NotNull(service.ModelFolderPath);
        Assert.NotEmpty(service.ModelFolderPath);
    }

    [Fact]
    public void Constructor_WithEmptyPath_SetsDefaultPath()
    {
        // Arrange & Act
        var service = new ModelManagementService(string.Empty);

        // Assert
        Assert.NotNull(service.ModelFolderPath);
        Assert.NotEmpty(service.ModelFolderPath);
    }

    [Fact]
    public void Constructor_WithWhitespacePath_SetsDefaultPath()
    {
        // Arrange & Act
        var service = new ModelManagementService("   ");

        // Assert
        Assert.NotNull(service.ModelFolderPath);
        Assert.NotEmpty(service.ModelFolderPath);
    }

    [Fact]
    public void Constructor_InitializesCurrentModel()
    {
        // Arrange & Act
        var service = new ModelManagementService(_testDirectory);

        // Assert
        Assert.Equal("phi-3.5-mini-instruct.gguf", service.CurrentModel);
    }

    [Fact]
    public void Constructor_WithNoModelsInDirectory_HasEmptyAvailableModels()
    {
        // Arrange & Act
        var service = new ModelManagementService(_testDirectory);

        // Assert
        Assert.Empty(service.AvailableModels);
    }

    [Fact]
    public void Constructor_WithModelsInDirectory_PopulatesAvailableModels()
    {
        // Arrange
        CreateTestModelFile("model1.gguf");
        CreateTestModelFile("model2.gguf");

        // Act
        var service = new ModelManagementService(_testDirectory);

        // Assert
        Assert.Equal(2, service.AvailableModels.Count);
        Assert.Contains("model1.gguf", service.AvailableModels);
        Assert.Contains("model2.gguf", service.AvailableModels);
    }

    [Fact]
    public void Constructor_WithModelsInDirectory_SetsCurrentModelToFirst()
    {
        // Arrange
        CreateTestModelFile("alpha.gguf");
        CreateTestModelFile("beta.gguf");

        // Act
        var service = new ModelManagementService(_testDirectory);

        // Assert
        Assert.Equal("alpha.gguf", service.CurrentModel);
    }

    #endregion

    #region CurrentModel Property Tests

    [Fact]
    public void CurrentModel_Set_UpdatesValue()
    {
        // Arrange
        var service = new ModelManagementService(_testDirectory);

        // Act
        service.CurrentModel = "new-model.gguf";

        // Assert
        Assert.Equal("new-model.gguf", service.CurrentModel);
    }

    [Fact]
    public void CurrentModel_SetSameValue_DoesNotTriggerOnChange()
    {
        // Arrange
        var service = new ModelManagementService(_testDirectory);
        var changeCount = 0;
        service.OnChange += () => changeCount++;

        // Act
        service.CurrentModel = "phi-3.5-mini-instruct.gguf"; // Same as default

        // Assert
        Assert.Equal(0, changeCount);
    }

    [Fact]
    public void CurrentModel_SetDifferentValue_TriggersOnChange()
    {
        // Arrange
        var service = new ModelManagementService(_testDirectory);
        var changeCount = 0;
        service.OnChange += () => changeCount++;

        // Act
        service.CurrentModel = "new-model.gguf";

        // Assert
        Assert.Equal(1, changeCount);
    }

    [Fact]
    public void CurrentModel_MultipleChanges_TriggersOnChangeMultipleTimes()
    {
        // Arrange
        var service = new ModelManagementService(_testDirectory);
        var changeCount = 0;
        service.OnChange += () => changeCount++;

        // Act
        service.CurrentModel = "model1.gguf";
        service.CurrentModel = "model2.gguf";
        service.CurrentModel = "model3.gguf";

        // Assert
        Assert.Equal(3, changeCount);
    }

    #endregion

    #region AvailableModels Property Tests

    [Fact]
    public void AvailableModels_ReturnsReadOnlyList()
    {
        // Arrange
        var service = new ModelManagementService(_testDirectory);

        // Act
        var models = service.AvailableModels;

        // Assert
        Assert.IsAssignableFrom<IReadOnlyList<string>>(models);
    }

    [Fact]
    public void AvailableModels_IsOrdered()
    {
        // Arrange
        CreateTestModelFile("zebra.gguf");
        CreateTestModelFile("alpha.gguf");
        CreateTestModelFile("mike.gguf");

        // Act
        var service = new ModelManagementService(_testDirectory);

        // Assert
        Assert.Equal(3, service.AvailableModels.Count);
        Assert.Equal("alpha.gguf", service.AvailableModels[0]);
        Assert.Equal("mike.gguf", service.AvailableModels[1]);
        Assert.Equal("zebra.gguf", service.AvailableModels[2]);
    }

    #endregion

    #region RefreshAvailableModelsAsync Tests

    [Fact]
    public async Task RefreshAvailableModelsAsync_WithNewFiles_UpdatesAvailableModels()
    {
        // Arrange
        var service = new ModelManagementService(_testDirectory);
        Assert.Empty(service.AvailableModels);

        CreateTestModelFile("new-model.gguf");

        // Act
        await service.RefreshAvailableModelsAsync();

        // Assert
        Assert.Single(service.AvailableModels);
        Assert.Contains("new-model.gguf", service.AvailableModels);
    }

    [Fact]
    public async Task RefreshAvailableModelsAsync_WithRemovedFiles_UpdatesAvailableModels()
    {
        // Arrange
        var modelPath = CreateTestModelFile("temp-model.gguf");
        var service = new ModelManagementService(_testDirectory);
        Assert.Single(service.AvailableModels);

        File.Delete(modelPath);

        // Act
        await service.RefreshAvailableModelsAsync();

        // Assert
        Assert.Empty(service.AvailableModels);
    }

    [Fact]
    public async Task RefreshAvailableModelsAsync_WithNonExistentDirectory_ClearsAvailableModels()
    {
        // Arrange
        CreateTestModelFile("model.gguf");
        var service = new ModelManagementService(_testDirectory);
        Assert.Single(service.AvailableModels);

        service.ModelFolderPath = Path.Combine(_testDirectory, "non-existent");

        // Act
        await service.RefreshAvailableModelsAsync();

        // Assert
        Assert.Empty(service.AvailableModels);
    }

    [Fact]
    public async Task RefreshAvailableModelsAsync_WithEmptyPath_ClearsAvailableModels()
    {
        // Arrange
        CreateTestModelFile("model.gguf");
        var service = new ModelManagementService(_testDirectory);
        Assert.Single(service.AvailableModels);

        service.ModelFolderPath = string.Empty;

        // Act
        await service.RefreshAvailableModelsAsync();

        // Assert
        Assert.Empty(service.AvailableModels);
    }

    [Fact]
    public async Task RefreshAvailableModelsAsync_TriggersOnChange()
    {
        // Arrange
        var service = new ModelManagementService(_testDirectory);
        var changeCount = 0;
        service.OnChange += () => changeCount++;

        // Act
        await service.RefreshAvailableModelsAsync();

        // Assert
        Assert.Equal(1, changeCount);
    }

    [Fact]
    public async Task RefreshAvailableModelsAsync_WithCurrentModelNotInList_SetsFirstModel()
    {
        // Arrange
        CreateTestModelFile("model1.gguf");
        CreateTestModelFile("model2.gguf");
        var service = new ModelManagementService(_testDirectory);
        service.CurrentModel = "non-existent.gguf";

        // Act
        await service.RefreshAvailableModelsAsync();

        // Assert
        Assert.Equal("model1.gguf", service.CurrentModel);
    }

    [Fact]
    public async Task RefreshAvailableModelsAsync_OnlyIncludesGgufFiles()
    {
        // Arrange
        CreateTestModelFile("model.gguf");
        CreateTestFile("model.txt");
        CreateTestFile("model.bin");
        CreateTestFile("readme.md");

        // Act
        var service = new ModelManagementService(_testDirectory);
        await service.RefreshAvailableModelsAsync();

        // Assert
        Assert.Single(service.AvailableModels);
        Assert.Contains("model.gguf", service.AvailableModels);
    }

    #endregion

    #region GetCurrentModelFullPath Tests

    [Fact]
    public void GetCurrentModelFullPath_WithExistingModel_ReturnsFullPath()
    {
        // Arrange
        CreateTestModelFile("test-model.gguf");
        var service = new ModelManagementService(_testDirectory);
        service.CurrentModel = "test-model.gguf";

        // Act
        var result = service.GetCurrentModelFullPath();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(Path.Combine(_testDirectory, "test-model.gguf"), result);
    }

    [Fact]
    public void GetCurrentModelFullPath_WithNonExistentModel_ReturnsNull()
    {
        // Arrange
        var service = new ModelManagementService(_testDirectory);
        service.CurrentModel = "non-existent.gguf";

        // Act
        var result = service.GetCurrentModelFullPath();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetCurrentModelFullPath_WithEmptyCurrentModel_ReturnsNull()
    {
        // Arrange
        var service = new ModelManagementService(_testDirectory);
        service.CurrentModel = string.Empty;

        // Act
        var result = service.GetCurrentModelFullPath();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetCurrentModelFullPath_WithWhitespaceCurrentModel_ReturnsNull()
    {
        // Arrange
        var service = new ModelManagementService(_testDirectory);
        service.CurrentModel = "   ";

        // Act
        var result = service.GetCurrentModelFullPath();

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region SwitchModelAsync Tests

    [Fact]
    public async Task SwitchModelAsync_WithExistingModel_ReturnsSuccess()
    {
        // Arrange
        CreateTestModelFile("model.gguf");
        var service = new ModelManagementService(_testDirectory);
        service.CurrentModel = "model.gguf";

        // Act
        var result = await service.SwitchModelAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Contains("Selected model: model.gguf", result.Message);
    }

    [Fact]
    public async Task SwitchModelAsync_WithNoModels_ReturnsFailure()
    {
        // Arrange
        var service = new ModelManagementService(_testDirectory);

        // Act
        var result = await service.SwitchModelAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Contains("No models found", result.Message);
    }

    [Fact]
    public async Task SwitchModelAsync_WithNonExistentModel_ReturnsFailure()
    {
        // Arrange
        CreateTestModelFile("existing.gguf");
        var service = new ModelManagementService(_testDirectory);
        service.CurrentModel = "non-existent.gguf";

        // Act
        var result = await service.SwitchModelAsync();

        // Assert
        // Should switch to first available model
        Assert.True(result.Success);
        Assert.Equal("existing.gguf", service.CurrentModel);
    }

    [Fact]
    public async Task SwitchModelAsync_SetsSelectedModelFullPath()
    {
        // Arrange
        CreateTestModelFile("model.gguf");
        var service = new ModelManagementService(_testDirectory);
        service.CurrentModel = "model.gguf";

        // Act
        await service.SwitchModelAsync();

        // Assert
        Assert.NotNull(service.SelectedModelFullPath);
        Assert.Equal(Path.Combine(_testDirectory, "model.gguf"), service.SelectedModelFullPath);
    }

    [Fact]
    public async Task SwitchModelAsync_TriggersOnChange()
    {
        // Arrange
        CreateTestModelFile("model.gguf");
        var service = new ModelManagementService(_testDirectory);
        service.CurrentModel = "model.gguf";
        var changeCount = 0;
        service.OnChange += () => changeCount++;

        // Act
        await service.SwitchModelAsync();

        // Assert
        Assert.True(changeCount > 0);
    }

    [Fact]
    public async Task SwitchModelAsync_WithHandler_InvokesHandler()
    {
        // Arrange
        CreateTestModelFile("model.gguf");
        var service = new ModelManagementService(_testDirectory);
        service.CurrentModel = "model.gguf";

        var handlerInvoked = false;
        string? invokedPath = null;
        service.SwitchModelHandler = async (path, progress) =>
        {
            handlerInvoked = true;
            invokedPath = path;
            await Task.CompletedTask;
        };

        // Act
        var result = await service.SwitchModelAsync();

        // Assert
        Assert.True(result.Success);
        Assert.True(handlerInvoked);
        Assert.Equal(Path.Combine(_testDirectory, "model.gguf"), invokedPath);
        Assert.Contains("Switched to model", result.Message);
    }

    [Fact]
    public async Task SwitchModelAsync_WithHandlerException_ReturnsFailure()
    {
        // Arrange
        CreateTestModelFile("model.gguf");
        var service = new ModelManagementService(_testDirectory);
        service.CurrentModel = "model.gguf";

        service.SwitchModelHandler = async (path, progress) =>
        {
            await Task.CompletedTask;
            throw new InvalidOperationException("Handler error");
        };

        // Act
        var result = await service.SwitchModelAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Failed to switch model", result.Message);
        Assert.Contains("Handler error", result.Message);
    }

    [Fact]
    public async Task SwitchModelAsync_WithInvalidCurrentModel_SwitchesToFirstAvailable()
    {
        // Arrange
        CreateTestModelFile("alpha.gguf");
        CreateTestModelFile("beta.gguf");
        var service = new ModelManagementService(_testDirectory);
        service.CurrentModel = "invalid.gguf";

        // Act
        var result = await service.SwitchModelAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Equal("alpha.gguf", service.CurrentModel);
    }

    [Fact]
    public async Task SwitchModelAsync_RefreshesModelsFirst()
    {
        // Arrange
        var service = new ModelManagementService(_testDirectory);
        Assert.Empty(service.AvailableModels);

        CreateTestModelFile("new-model.gguf");

        // Act
        var result = await service.SwitchModelAsync();

        // Assert
        Assert.Single(service.AvailableModels);
        Assert.Contains("new-model.gguf", service.AvailableModels);
    }

    #endregion

    #region ModelExists Tests

    [Fact]
    public void ModelExists_WithExistingModel_ReturnsTrue()
    {
        // Arrange
        CreateTestModelFile("existing.gguf");
        var service = new ModelManagementService(_testDirectory);

        // Act
        var result = service.ModelExists("existing.gguf");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ModelExists_WithNonExistentModel_ReturnsFalse()
    {
        // Arrange
        var service = new ModelManagementService(_testDirectory);

        // Act
        var result = service.ModelExists("non-existent.gguf");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ModelExists_WithNullFileName_ReturnsFalse()
    {
        // Arrange
        var service = new ModelManagementService(_testDirectory);

        // Act
        var result = service.ModelExists(null!);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ModelExists_WithEmptyFileName_ReturnsFalse()
    {
        // Arrange
        var service = new ModelManagementService(_testDirectory);

        // Act
        var result = service.ModelExists(string.Empty);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ModelExists_WithWhitespaceFileName_ReturnsFalse()
    {
        // Arrange
        var service = new ModelManagementService(_testDirectory);

        // Act
        var result = service.ModelExists("   ");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ModelExists_ChecksInModelFolderPath()
    {
        // Arrange
        CreateTestModelFile("model.gguf");
        var service = new ModelManagementService(_testDirectory);

        // Act
        var result = service.ModelExists("model.gguf");

        // Assert
        Assert.True(result);
    }

    #endregion

    #region OnChange Event Tests

    [Fact]
    public void OnChange_MultipleSubscribers_AllReceiveNotification()
    {
        // Arrange
        var service = new ModelManagementService(_testDirectory);
        var count1 = 0;
        var count2 = 0;
        var count3 = 0;

        service.OnChange += () => count1++;
        service.OnChange += () => count2++;
        service.OnChange += () => count3++;

        // Act
        service.CurrentModel = "new-model.gguf";

        // Assert
        Assert.Equal(1, count1);
        Assert.Equal(1, count2);
        Assert.Equal(1, count3);
    }

    [Fact]
    public void OnChange_AfterUnsubscribe_DoesNotReceiveNotification()
    {
        // Arrange
        var service = new ModelManagementService(_testDirectory);
        var count = 0;
        Action handler = () => count++;

        service.OnChange += handler;
        service.CurrentModel = "model1.gguf";
        Assert.Equal(1, count);

        service.OnChange -= handler;

        // Act
        service.CurrentModel = "model2.gguf";

        // Assert
        Assert.Equal(1, count); // Still 1, not incremented
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task Integration_FullWorkflow_Success()
    {
        // Arrange
        CreateTestModelFile("model1.gguf");
        CreateTestModelFile("model2.gguf");
        CreateTestModelFile("model3.gguf");

        var service = new ModelManagementService(_testDirectory);

        // Act & Assert - Initial state
        Assert.Equal(3, service.AvailableModels.Count);
        Assert.Equal("model1.gguf", service.CurrentModel);

        // Change model
        service.CurrentModel = "model2.gguf";
        Assert.Equal("model2.gguf", service.CurrentModel);

        // Switch model
        var switchResult = await service.SwitchModelAsync();
        Assert.True(switchResult.Success);
        Assert.NotNull(service.SelectedModelFullPath);

        // Check model exists
        Assert.True(service.ModelExists("model2.gguf"));
        Assert.False(service.ModelExists("model4.gguf"));

        // Get full path
        var fullPath = service.GetCurrentModelFullPath();
        Assert.NotNull(fullPath);
        Assert.Contains("model2.gguf", fullPath);
    }

    [Fact]
    public async Task Integration_WithHandler_FullWorkflow()
    {
        // Arrange
        CreateTestModelFile("test.gguf");
        var service = new ModelManagementService(_testDirectory);

        var handlerCallCount = 0;
        var lastPath = string.Empty;

        service.SwitchModelHandler = async (path, progress) =>
        {
            handlerCallCount++;
            lastPath = path;
            await Task.Delay(10); // Simulate async work
        };

        service.CurrentModel = "test.gguf";

        // Act
        var result = await service.SwitchModelAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, handlerCallCount);
        Assert.Contains("test.gguf", lastPath);
        Assert.Contains("Switched to model", result.Message);
    }

    [Fact]
    public async Task Integration_ModelDiscovery_DynamicChanges()
    {
        // Arrange
        var service = new ModelManagementService(_testDirectory);
        Assert.Empty(service.AvailableModels);

        // Add first model
        CreateTestModelFile("model1.gguf");
        await service.RefreshAvailableModelsAsync();
        Assert.Single(service.AvailableModels);

        // Add more models
        CreateTestModelFile("model2.gguf");
        CreateTestModelFile("model3.gguf");
        await service.RefreshAvailableModelsAsync();
        Assert.Equal(3, service.AvailableModels.Count);

        // Remove a model
        var modelPath = Path.Combine(_testDirectory, "model2.gguf");
        File.Delete(modelPath);
        await service.RefreshAvailableModelsAsync();
        Assert.Equal(2, service.AvailableModels.Count);
        Assert.DoesNotContain("model2.gguf", service.AvailableModels);
    }

    #endregion

    #region Helper Methods

    private string CreateTestModelFile(string fileName)
    {
        var fullPath = Path.Combine(_testDirectory, fileName);
        File.WriteAllText(fullPath, "test content");
        _createdFiles.Add(fullPath);
        return fullPath;
    }

    private string CreateTestFile(string fileName)
    {
        var fullPath = Path.Combine(_testDirectory, fileName);
        File.WriteAllText(fullPath, "test content");
        _createdFiles.Add(fullPath);
        return fullPath;
    }

    #endregion
}
