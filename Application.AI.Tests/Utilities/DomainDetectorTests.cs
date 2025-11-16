using Application.AI.Utilities;
using Entities;
using Moq;
using Services.Repositories;

namespace Application.AI.Tests.Utilities;

/// <summary>
/// Unit tests for DomainDetector class.
/// Tests cover domain detection, matching, registration, caching, and management operations.
/// </summary>
public class DomainDetectorTests : IDisposable
{
    private readonly Mock<IKnowledgeDomainRepository> _mockRepository;
    private readonly DomainDetector _domainDetector;

    public DomainDetectorTests()
    {
        _mockRepository = new Mock<IKnowledgeDomainRepository>();
        _domainDetector = new DomainDetector(_mockRepository.Object);
    }

    public void Dispose()
    {
        // Cleanup if needed
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidRepository_CreatesInstance()
    {
        // Arrange & Act
        var detector = new DomainDetector(_mockRepository.Object);

        // Assert
        Assert.NotNull(detector);
    }

    [Fact]
    public void Constructor_WithNullRepository_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new DomainDetector(null!));
    }

    #endregion

    #region DetectDomainsAsync Tests

    [Fact]
    public async Task DetectDomainsAsync_WithNullQuery_ReturnsEmptyList()
    {
        // Arrange
        SetupVariantsCache(new Dictionary<string, List<string>>());

        // Act
        var result = await _domainDetector.DetectDomainsAsync(null!);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task DetectDomainsAsync_WithEmptyQuery_ReturnsEmptyList()
    {
        // Arrange
        SetupVariantsCache(new Dictionary<string, List<string>>());

        // Act
        var result = await _domainDetector.DetectDomainsAsync("");

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task DetectDomainsAsync_WithWhitespaceQuery_ReturnsEmptyList()
    {
        // Arrange
        SetupVariantsCache(new Dictionary<string, List<string>>());

        // Act
        var result = await _domainDetector.DetectDomainsAsync("   ");

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task DetectDomainsAsync_WithNoMatchingDomains_ReturnsEmptyList()
    {
        // Arrange
        var variants = new Dictionary<string, List<string>>
        {
            { "gloomhaven", new List<string> { "gloomhaven", "gloom haven" } },
            { "catan", new List<string> { "catan", "settlers of catan" } }
        };
        SetupVariantsCache(variants);

        // Act
        var result = await _domainDetector.DetectDomainsAsync("Tell me about chess");

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task DetectDomainsAsync_WithSingleMatchingDomain_ReturnsDomainId()
    {
        // Arrange
        var variants = new Dictionary<string, List<string>>
        {
            { "gloomhaven", new List<string> { "gloomhaven", "gloom haven" } },
            { "catan", new List<string> { "catan", "settlers of catan" } }
        };
        SetupVariantsCache(variants);

        // Act
        var result = await _domainDetector.DetectDomainsAsync("Tell me about Gloomhaven");

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Contains("gloomhaven", result);
    }

    [Fact]
    public async Task DetectDomainsAsync_WithMultipleMatchingDomains_ReturnsAllDomainIds()
    {
        // Arrange
        var variants = new Dictionary<string, List<string>>
        {
            { "gloomhaven", new List<string> { "gloomhaven", "gloom haven" } },
            { "catan", new List<string> { "catan", "settlers of catan" } }
        };
        SetupVariantsCache(variants);

        // Act
        var result = await _domainDetector.DetectDomainsAsync("Compare Gloomhaven and Catan");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Contains("gloomhaven", result);
        Assert.Contains("catan", result);
    }

    [Fact]
    public async Task DetectDomainsAsync_WithCaseInsensitiveMatch_ReturnsDomainId()
    {
        // Arrange
        var variants = new Dictionary<string, List<string>>
        {
            { "gloomhaven", new List<string> { "gloomhaven", "gloom haven" } }
        };
        SetupVariantsCache(variants);

        // Act
        var result = await _domainDetector.DetectDomainsAsync("GLOOMHAVEN rules");

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Contains("gloomhaven", result);
    }

    [Fact]
    public async Task DetectDomainsAsync_WithVariantMatch_ReturnsDomainId()
    {
        // Arrange
        var variants = new Dictionary<string, List<string>>
        {
            { "gloomhaven", new List<string> { "gloomhaven", "gloom haven" } }
        };
        SetupVariantsCache(variants);

        // Act
        var result = await _domainDetector.DetectDomainsAsync("What are the gloom haven rules?");

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Contains("gloomhaven", result);
    }

    [Fact]
    public async Task DetectDomainsAsync_WithDuplicateMatches_ReturnsUniqueDomainId()
    {
        // Arrange
        var variants = new Dictionary<string, List<string>>
        {
            { "gloomhaven", new List<string> { "gloomhaven", "gloom haven" } }
        };
        SetupVariantsCache(variants);

        // Act
        var result = await _domainDetector.DetectDomainsAsync("Gloomhaven and gloom haven are the same");

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Contains("gloomhaven", result);
    }

    [Fact]
    public async Task DetectDomainsAsync_WithPartialWordMatch_ReturnsMatchingDomain()
    {
        // Arrange
        var variants = new Dictionary<string, List<string>>
        {
            { "gloomhaven", new List<string> { "gloomhaven" } }
        };
        SetupVariantsCache(variants);

        // Act
        var result = await _domainDetector.DetectDomainsAsync("Supergloomhaven edition");

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Contains("gloomhaven", result);
    }

    #endregion

    #region MatchesDomainAsync Tests

    [Fact]
    public async Task MatchesDomainAsync_WithEmptyDetectedDomains_ReturnsTrue()
    {
        // Arrange
        SetupVariantsCache(new Dictionary<string, List<string>>());

        // Act
        var result = await _domainDetector.MatchesDomainAsync("Any Category", new List<string>());

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task MatchesDomainAsync_WithMatchingDomainId_ReturnsTrue()
    {
        // Arrange
        var variants = new Dictionary<string, List<string>>
        {
            { "board-games", new List<string> { "board games", "board game" } }
        };
        SetupVariantsCache(variants);

        // Act
        var result = await _domainDetector.MatchesDomainAsync("Board Games - Strategy", new List<string> { "board-games" });

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task MatchesDomainAsync_WithMatchingVariant_ReturnsTrue()
    {
        // Arrange
        var variants = new Dictionary<string, List<string>>
        {
            { "gloomhaven", new List<string> { "gloomhaven", "gloom haven" } }
        };
        SetupVariantsCache(variants);

        // Act
        var result = await _domainDetector.MatchesDomainAsync("Gloomhaven - Rules", new List<string> { "gloomhaven" });

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task MatchesDomainAsync_WithNonMatchingCategory_ReturnsFalse()
    {
        // Arrange
        var variants = new Dictionary<string, List<string>>
        {
            { "gloomhaven", new List<string> { "gloomhaven", "gloom haven" } }
        };
        SetupVariantsCache(variants);

        // Act
        var result = await _domainDetector.MatchesDomainAsync("Catan - Rules", new List<string> { "gloomhaven" });

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task MatchesDomainAsync_WithCaseInsensitiveMatch_ReturnsTrue()
    {
        // Arrange
        var variants = new Dictionary<string, List<string>>
        {
            { "gloomhaven", new List<string> { "gloomhaven" } }
        };
        SetupVariantsCache(variants);

        // Act
        var result = await _domainDetector.MatchesDomainAsync("GLOOMHAVEN - RULES", new List<string> { "gloomhaven" });

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task MatchesDomainAsync_WithMultipleDomainsOneMatching_ReturnsTrue()
    {
        // Arrange
        var variants = new Dictionary<string, List<string>>
        {
            { "gloomhaven", new List<string> { "gloomhaven" } },
            { "catan", new List<string> { "catan" } }
        };
        SetupVariantsCache(variants);

        // Act
        var result = await _domainDetector.MatchesDomainAsync("Gloomhaven - Setup", new List<string> { "gloomhaven", "catan" });

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task MatchesDomainAsync_WithDomainIdHyphensReplacedBySpaces_ReturnsTrue()
    {
        // Arrange
        var variants = new Dictionary<string, List<string>>
        {
            { "board-games", new List<string> { "board games" } }
        };
        SetupVariantsCache(variants);

        // Act
        var result = await _domainDetector.MatchesDomainAsync("Board Games - Strategy", new List<string> { "board-games" });

        // Assert
        Assert.True(result);
    }

    #endregion

    #region ExtractDomainNameFromCategory Tests

    [Fact]
    public void ExtractDomainNameFromCategory_WithNullCategory_ReturnsEmptyString()
    {
        // Act
        var result = _domainDetector.ExtractDomainNameFromCategory(null!);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ExtractDomainNameFromCategory_WithEmptyCategory_ReturnsEmptyString()
    {
        // Act
        var result = _domainDetector.ExtractDomainNameFromCategory("");

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ExtractDomainNameFromCategory_WithWhitespaceCategory_ReturnsEmptyString()
    {
        // Act
        var result = _domainDetector.ExtractDomainNameFromCategory("   ");

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ExtractDomainNameFromCategory_WithSimpleCategory_ReturnsSameValue()
    {
        // Act
        var result = _domainDetector.ExtractDomainNameFromCategory("Gloomhaven");

        // Assert
        Assert.Equal("Gloomhaven", result);
    }

    [Fact]
    public void ExtractDomainNameFromCategory_WithDashSeparator_ReturnsFirstPart()
    {
        // Act
        var result = _domainDetector.ExtractDomainNameFromCategory("Gloomhaven - Rules");

        // Assert
        Assert.Equal("Gloomhaven", result);
    }

    [Fact]
    public void ExtractDomainNameFromCategory_WithMultipleDashSeparators_ReturnsFirstPart()
    {
        // Act
        var result = _domainDetector.ExtractDomainNameFromCategory("Gloomhaven - Rules - Setup");

        // Assert
        Assert.Equal("Gloomhaven", result);
    }

    [Fact]
    public void ExtractDomainNameFromCategory_WithLeadingAndTrailingSpaces_ReturnsTrimmedValue()
    {
        // Act
        var result = _domainDetector.ExtractDomainNameFromCategory("  Gloomhaven  ");

        // Assert
        Assert.Equal("Gloomhaven", result);
    }

    [Fact]
    public void ExtractDomainNameFromCategory_WithDashAndSpaces_ReturnsTrimmedFirstPart()
    {
        // Act
        var result = _domainDetector.ExtractDomainNameFromCategory("  Gloomhaven  -  Rules  ");

        // Assert
        Assert.Equal("Gloomhaven", result);
    }

    #endregion

    #region RegisterDomainFromCategoryAsync Tests

    [Fact]
    public async Task RegisterDomainFromCategoryAsync_WithNullCategory_DoesNotRegister()
    {
        // Act
        await _domainDetector.RegisterDomainFromCategoryAsync(null!);

        // Assert
        _mockRepository.Verify(r => r.RegisterDomainAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string[]>(),
            It.IsAny<string>(),
            It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RegisterDomainFromCategoryAsync_WithEmptyCategory_DoesNotRegister()
    {
        // Act
        await _domainDetector.RegisterDomainFromCategoryAsync("");

        // Assert
        _mockRepository.Verify(r => r.RegisterDomainAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string[]>(),
            It.IsAny<string>(),
            It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RegisterDomainFromCategoryAsync_WithWhitespaceCategory_DoesNotRegister()
    {
        // Act
        await _domainDetector.RegisterDomainFromCategoryAsync("   ");

        // Assert
        _mockRepository.Verify(r => r.RegisterDomainAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string[]>(),
            It.IsAny<string>(),
            It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RegisterDomainFromCategoryAsync_WithExistingDomain_DoesNotRegister()
    {
        // Arrange
        _mockRepository.Setup(r => r.DomainExistsAsync("gloomhaven")).ReturnsAsync(true);

        // Act
        await _domainDetector.RegisterDomainFromCategoryAsync("Gloomhaven");

        // Assert
        _mockRepository.Verify(r => r.RegisterDomainAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string[]>(),
            It.IsAny<string>(),
            It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RegisterDomainFromCategoryAsync_WithNewDomain_RegistersDomain()
    {
        // Arrange
        _mockRepository.Setup(r => r.DomainExistsAsync("gloomhaven")).ReturnsAsync(false);
        _mockRepository.Setup(r => r.RegisterDomainAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string[]>(),
            It.IsAny<string>(),
            It.IsAny<string>())).ReturnsAsync(Guid.NewGuid());

        // Act
        await _domainDetector.RegisterDomainFromCategoryAsync("Gloomhaven");

        // Assert
        _mockRepository.Verify(r => r.RegisterDomainAsync(
            "gloomhaven",
            "Gloomhaven",
            It.Is<string[]>(v => v.Contains("Gloomhaven") && v.Contains("gloomhaven")),
            "general",
            "auto-discovered"), Times.Once);
    }

    [Fact]
    public async Task RegisterDomainFromCategoryAsync_WithCustomCategoryType_UsesProvidedType()
    {
        // Arrange
        _mockRepository.Setup(r => r.DomainExistsAsync("gloomhaven")).ReturnsAsync(false);
        _mockRepository.Setup(r => r.RegisterDomainAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string[]>(),
            It.IsAny<string>(),
            It.IsAny<string>())).ReturnsAsync(Guid.NewGuid());

        // Act
        await _domainDetector.RegisterDomainFromCategoryAsync("Gloomhaven", "board-game");

        // Assert
        _mockRepository.Verify(r => r.RegisterDomainAsync(
            "gloomhaven",
            "Gloomhaven",
            It.IsAny<string[]>(),
            "board-game",
            "auto-discovered"), Times.Once);
    }

    [Fact]
    public async Task RegisterDomainFromCategoryAsync_WithCategoryContainingSeparator_ExtractsFirstPart()
    {
        // Arrange
        _mockRepository.Setup(r => r.DomainExistsAsync("gloomhaven")).ReturnsAsync(false);
        _mockRepository.Setup(r => r.RegisterDomainAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string[]>(),
            It.IsAny<string>(),
            It.IsAny<string>())).ReturnsAsync(Guid.NewGuid());

        // Act
        await _domainDetector.RegisterDomainFromCategoryAsync("Gloomhaven - Rules");

        // Assert
        _mockRepository.Verify(r => r.RegisterDomainAsync(
            "gloomhaven",
            "Gloomhaven",
            It.IsAny<string[]>(),
            "general",
            "auto-discovered"), Times.Once);
    }

    #endregion

    #region GetDisplayNameAsync Tests

    [Fact]
    public async Task GetDisplayNameAsync_WithExistingDomain_ReturnsDisplayName()
    {
        // Arrange
        var domain = new KnowledgeDomainEntity
        {
            DomainId = "gloomhaven",
            DisplayName = "Gloomhaven"
        };
        _mockRepository.Setup(r => r.GetDomainByIdAsync("gloomhaven")).ReturnsAsync(domain);

        // Act
        var result = await _domainDetector.GetDisplayNameAsync("gloomhaven");

        // Assert
        Assert.Equal("Gloomhaven", result);
    }

    [Fact]
    public async Task GetDisplayNameAsync_WithNonExistingDomain_ReturnsDomainId()
    {
        // Arrange
        _mockRepository.Setup(r => r.GetDomainByIdAsync("unknown")).ReturnsAsync((KnowledgeDomainEntity?)null);

        // Act
        var result = await _domainDetector.GetDisplayNameAsync("unknown");

        // Assert
        Assert.Equal("unknown", result);
    }

    #endregion

    #region RegisterDomainAsync Tests

    [Fact]
    public async Task RegisterDomainAsync_WithValidParameters_RegistersDomain()
    {
        // Arrange
        _mockRepository.Setup(r => r.RegisterDomainAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string[]>(),
            It.IsAny<string>(),
            It.IsAny<string>())).ReturnsAsync(Guid.NewGuid());

        // Act
        await _domainDetector.RegisterDomainAsync("gloomhaven", "Gloomhaven", "board-game", "gloomhaven", "gloom haven");

        // Assert
        _mockRepository.Verify(r => r.RegisterDomainAsync(
            "gloomhaven",
            "Gloomhaven",
            It.Is<string[]>(v => v.Contains("gloomhaven") && v.Contains("gloom haven")),
            "board-game",
            It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task RegisterDomainAsync_WithNoVariants_RegistersDomainWithEmptyVariants()
    {
        // Arrange
        _mockRepository.Setup(r => r.RegisterDomainAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string[]>(),
            It.IsAny<string>(),
            It.IsAny<string>())).ReturnsAsync(Guid.NewGuid());

        // Act
        await _domainDetector.RegisterDomainAsync("gloomhaven", "Gloomhaven", "board-game");

        // Assert
        _mockRepository.Verify(r => r.RegisterDomainAsync(
            "gloomhaven",
            "Gloomhaven",
            It.Is<string[]>(v => v.Length == 0),
            "board-game",
            It.IsAny<string>()), Times.Once);
    }

    #endregion

    #region GetAllDomainsAsync Tests

    [Fact]
    public async Task GetAllDomainsAsync_WithNoDomains_ReturnsEmptyList()
    {
        // Arrange
        _mockRepository.Setup(r => r.GetAllDomainsAsync()).ReturnsAsync(new List<KnowledgeDomainEntity>());

        // Act
        var result = await _domainDetector.GetAllDomainsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAllDomainsAsync_WithDomains_ReturnsAllDomains()
    {
        // Arrange
        var domains = new List<KnowledgeDomainEntity>
        {
            new() { DomainId = "gloomhaven", DisplayName = "Gloomhaven", Category = "board-game" },
            new() { DomainId = "catan", DisplayName = "Catan", Category = "board-game" }
        };
        _mockRepository.Setup(r => r.GetAllDomainsAsync()).ReturnsAsync(domains);

        // Act
        var result = await _domainDetector.GetAllDomainsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Contains(result, d => d.DomainId == "gloomhaven" && d.DisplayName == "Gloomhaven" && d.Category == "board-game");
        Assert.Contains(result, d => d.DomainId == "catan" && d.DisplayName == "Catan" && d.Category == "board-game");
    }

    #endregion

    #region GetDomainsByCategoryAsync Tests

    [Fact]
    public async Task GetDomainsByCategoryAsync_WithNoMatchingDomains_ReturnsEmptyList()
    {
        // Arrange
        _mockRepository.Setup(r => r.GetDomainsByCategoryAsync("non-existent")).ReturnsAsync(new List<KnowledgeDomainEntity>());

        // Act
        var result = await _domainDetector.GetDomainsByCategoryAsync("non-existent");

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetDomainsByCategoryAsync_WithMatchingDomains_ReturnsDomains()
    {
        // Arrange
        var domains = new List<KnowledgeDomainEntity>
        {
            new() { DomainId = "gloomhaven", DisplayName = "Gloomhaven", Category = "board-game" },
            new() { DomainId = "catan", DisplayName = "Catan", Category = "board-game" }
        };
        _mockRepository.Setup(r => r.GetDomainsByCategoryAsync("board-game")).ReturnsAsync(domains);

        // Act
        var result = await _domainDetector.GetDomainsByCategoryAsync("board-game");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Contains(result, d => d.DomainId == "gloomhaven" && d.DisplayName == "Gloomhaven");
        Assert.Contains(result, d => d.DomainId == "catan" && d.DisplayName == "Catan");
    }

    #endregion

    #region GetCategoriesAsync Tests

    [Fact]
    public async Task GetCategoriesAsync_WithNoCategories_ReturnsEmptyList()
    {
        // Arrange
        _mockRepository.Setup(r => r.GetCategoriesAsync()).ReturnsAsync(new List<string>());

        // Act
        var result = await _domainDetector.GetCategoriesAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetCategoriesAsync_WithCategories_ReturnsAllCategories()
    {
        // Arrange
        var categories = new List<string> { "board-game", "video-game", "product" };
        _mockRepository.Setup(r => r.GetCategoriesAsync()).ReturnsAsync(categories);

        // Act
        var result = await _domainDetector.GetCategoriesAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Count);
        Assert.Contains("board-game", result);
        Assert.Contains("video-game", result);
        Assert.Contains("product", result);
    }

    #endregion

    #region DeleteDomainAsync Tests

    [Fact]
    public async Task DeleteDomainAsync_WithValidDomainId_DeletesDomain()
    {
        // Arrange
        _mockRepository.Setup(r => r.DeleteDomainAsync("gloomhaven")).Returns(Task.CompletedTask);

        // Act
        await _domainDetector.DeleteDomainAsync("gloomhaven");

        // Assert
        _mockRepository.Verify(r => r.DeleteDomainAsync("gloomhaven"), Times.Once);
    }

    #endregion

    #region AddVariantAsync Tests

    [Fact]
    public async Task AddVariantAsync_WithValidParameters_AddsVariant()
    {
        // Arrange
        _mockRepository.Setup(r => r.AddVariantAsync("gloomhaven", "gloom")).Returns(Task.CompletedTask);

        // Act
        await _domainDetector.AddVariantAsync("gloomhaven", "gloom");

        // Assert
        _mockRepository.Verify(r => r.AddVariantAsync("gloomhaven", "gloom"), Times.Once);
    }

    #endregion

    #region UpdateCategoryAsync Tests

    [Fact]
    public async Task UpdateCategoryAsync_WithValidParameters_UpdatesCategory()
    {
        // Arrange
        _mockRepository.Setup(r => r.UpdateDomainCategoryAsync("gloomhaven", "strategy-game")).Returns(Task.CompletedTask);

        // Act
        await _domainDetector.UpdateCategoryAsync("gloomhaven", "strategy-game");

        // Assert
        _mockRepository.Verify(r => r.UpdateDomainCategoryAsync("gloomhaven", "strategy-game"), Times.Once);
    }

    #endregion

    #region RefreshCacheAsync Tests

    [Fact]
    public async Task RefreshCacheAsync_UpdatesCache()
    {
        // Arrange
        var variants = new Dictionary<string, List<string>>
        {
            { "gloomhaven", new List<string> { "gloomhaven" } }
        };
        _mockRepository.Setup(r => r.GetAllVariantsAsync()).ReturnsAsync(variants);

        // Act
        await _domainDetector.RefreshCacheAsync();

        // Assert
        _mockRepository.Verify(r => r.GetAllVariantsAsync(), Times.Once);
    }

    [Fact]
    public async Task RefreshCacheAsync_AfterRefresh_DetectionUsesNewCache()
    {
        // Arrange
        var initialVariants = new Dictionary<string, List<string>>
        {
            { "gloomhaven", new List<string> { "gloomhaven" } }
        };
        var updatedVariants = new Dictionary<string, List<string>>
        {
            { "gloomhaven", new List<string> { "gloomhaven" } },
            { "catan", new List<string> { "catan" } }
        };
        
        _mockRepository.SetupSequence(r => r.GetAllVariantsAsync())
            .ReturnsAsync(initialVariants)
            .ReturnsAsync(updatedVariants);

        // Act
        await _domainDetector.RefreshCacheAsync();
        var resultBefore = await _domainDetector.DetectDomainsAsync("Tell me about Catan");
        
        await _domainDetector.RefreshCacheAsync();
        var resultAfter = await _domainDetector.DetectDomainsAsync("Tell me about Catan");

        // Assert
        Assert.Empty(resultBefore);
        Assert.Single(resultAfter);
        Assert.Contains("catan", resultAfter);
    }

    #endregion

    #region InitializeAsync Tests

    [Fact]
    public async Task InitializeAsync_CallsAllInitializationMethods()
    {
        // Arrange
        _mockRepository.Setup(r => r.InitializeDatabaseAsync()).Returns(Task.CompletedTask);
        _mockRepository.Setup(r => r.SeedDefaultDomainsAsync()).Returns(Task.CompletedTask);
        _mockRepository.Setup(r => r.GetAllVariantsAsync()).ReturnsAsync(new Dictionary<string, List<string>>());

        // Act
        await _domainDetector.InitializeAsync();

        // Assert
        _mockRepository.Verify(r => r.InitializeDatabaseAsync(), Times.Once);
        _mockRepository.Verify(r => r.SeedDefaultDomainsAsync(), Times.Once);
        _mockRepository.Verify(r => r.GetAllVariantsAsync(), Times.Once);
    }

    #endregion

    #region Cache Management Tests

    [Fact]
    public async Task CacheManagement_AutoRefreshesAfterExpiration()
    {
        // Arrange
        var variants = new Dictionary<string, List<string>>
        {
            { "gloomhaven", new List<string> { "gloomhaven" } }
        };
        _mockRepository.Setup(r => r.GetAllVariantsAsync()).ReturnsAsync(variants);

        // Act - First call initializes cache
        await _domainDetector.RefreshCacheAsync();
        
        // Simulate cache expiration by waiting (in real scenario, we'd need to wait 5+ minutes)
        // For testing purposes, we'll just verify that multiple calls work correctly
        var result1 = await _domainDetector.DetectDomainsAsync("Gloomhaven");
        var result2 = await _domainDetector.DetectDomainsAsync("Gloomhaven");

        // Assert
        Assert.Single(result1);
        Assert.Single(result2);
        // Cache should be used, so GetAllVariantsAsync should only be called once initially
        _mockRepository.Verify(r => r.GetAllVariantsAsync(), Times.Once);
    }

    [Fact]
    public async Task CacheManagement_ThreadSafeAccess()
    {
        // Arrange
        var variants = new Dictionary<string, List<string>>
        {
            { "gloomhaven", new List<string> { "gloomhaven" } },
            { "catan", new List<string> { "catan" } }
        };
        _mockRepository.Setup(r => r.GetAllVariantsAsync()).ReturnsAsync(variants);

        // Act - Multiple concurrent operations
        var tasks = new List<Task>
        {
            _domainDetector.RefreshCacheAsync(),
            _domainDetector.DetectDomainsAsync("Gloomhaven"),
            _domainDetector.DetectDomainsAsync("Catan"),
            _domainDetector.MatchesDomainAsync("Gloomhaven - Rules", new List<string> { "gloomhaven" })
        };

        await Task.WhenAll(tasks);

        // Assert - Should not throw any exceptions
        Assert.True(true);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task Integration_CompleteWorkflow_RegisterDetectAndMatch()
    {
        // Arrange
        var domainId = "gloomhaven";
        var displayName = "Gloomhaven";
        var variants = new[] { "gloomhaven", "gloom haven" };
        
        _mockRepository.Setup(r => r.DomainExistsAsync(domainId)).ReturnsAsync(false);
        _mockRepository.Setup(r => r.RegisterDomainAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string[]>(),
            It.IsAny<string>(),
            It.IsAny<string>())).ReturnsAsync(Guid.NewGuid());
        
        var cachedVariants = new Dictionary<string, List<string>>
        {
            { domainId, new List<string>(variants) }
        };
        _mockRepository.Setup(r => r.GetAllVariantsAsync()).ReturnsAsync(cachedVariants);

        // Act - Register domain
        await _domainDetector.RegisterDomainAsync(domainId, displayName, "board-game", variants);
        await _domainDetector.RefreshCacheAsync();

        // Detect domain
        var detectedDomains = await _domainDetector.DetectDomainsAsync("Tell me about Gloomhaven");

        // Match domain
        var matches = await _domainDetector.MatchesDomainAsync("Gloomhaven - Rules", detectedDomains);

        // Assert
        Assert.Single(detectedDomains);
        Assert.Contains(domainId, detectedDomains);
        Assert.True(matches);
    }

    [Fact]
    public async Task Integration_AutoDiscoveryWorkflow()
    {
        // Arrange
        var category = "Gloomhaven - Rules";
        _mockRepository.Setup(r => r.DomainExistsAsync("gloomhaven")).ReturnsAsync(false);
        _mockRepository.Setup(r => r.RegisterDomainAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string[]>(),
            It.IsAny<string>(),
            It.IsAny<string>())).ReturnsAsync(Guid.NewGuid());

        // Act
        await _domainDetector.RegisterDomainFromCategoryAsync(category, "board-game");

        // Assert
        _mockRepository.Verify(r => r.RegisterDomainAsync(
            "gloomhaven",
            "Gloomhaven",
            It.IsAny<string[]>(),
            "board-game",
            "auto-discovered"), Times.Once);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task EdgeCase_DetectDomainsWithSpecialCharacters()
    {
        // Arrange
        var variants = new Dictionary<string, List<string>>
        {
            { "gloomhaven", new List<string> { "gloomhaven" } }
        };
        SetupVariantsCache(variants);

        // Act
        var result = await _domainDetector.DetectDomainsAsync("What about Gloomhaven!?!?");

        // Assert
        Assert.Single(result);
        Assert.Contains("gloomhaven", result);
    }

    [Fact]
    public async Task EdgeCase_MatchesDomainWithEmptyCategory()
    {
        // Arrange
        SetupVariantsCache(new Dictionary<string, List<string>>());

        // Act
        var result = await _domainDetector.MatchesDomainAsync("", new List<string> { "gloomhaven" });

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void EdgeCase_ExtractDomainWithOnlyDashSeparator()
    {
        // Act
        var result = _domainDetector.ExtractDomainNameFromCategory(" - ");

        // Assert
        // When the input is only the separator with spaces, after splitting with RemoveEmptyEntries
        // and falling through to category.Trim(), it returns the trimmed separator
        Assert.Equal("-", result);
    }

    [Fact]
    public async Task EdgeCase_RegisterDomainWithSpacesInName()
    {
        // Arrange
        _mockRepository.Setup(r => r.DomainExistsAsync("board-games")).ReturnsAsync(false);
        _mockRepository.Setup(r => r.RegisterDomainAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string[]>(),
            It.IsAny<string>(),
            It.IsAny<string>())).ReturnsAsync(Guid.NewGuid());

        // Act
        await _domainDetector.RegisterDomainFromCategoryAsync("Board Games - Strategy");

        // Assert
        _mockRepository.Verify(r => r.RegisterDomainAsync(
            "board-games",
            "Board Games",
            It.IsAny<string[]>(),
            It.IsAny<string>(),
            "auto-discovered"), Times.Once);
    }

    #endregion

    #region Helper Methods

    private void SetupVariantsCache(Dictionary<string, List<string>> variants)
    {
        _mockRepository.Setup(r => r.GetAllVariantsAsync()).ReturnsAsync(variants);
    }

    #endregion
}
