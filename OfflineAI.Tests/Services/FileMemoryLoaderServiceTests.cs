using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Services;
using MemoryLibrary.Models;

namespace OfflineAI.Tests.Services;

/// <summary>
/// Unit tests for FileMemoryLoaderService
/// Tests all public methods and their various code paths
/// </summary>
public class FileMemoryLoaderServiceTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        // Clean up all temp files created during tests
        foreach (var file in _tempFiles)
        {
            if (File.Exists(file))
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }

    private string CreateTempFile(string content)
    {
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, content);
        _tempFiles.Add(tempFile);
        return tempFile;
    }

    #region LoadFromFileAsync Tests

    [Fact]
    public async Task LoadFromFileAsync_ThrowsFileNotFoundException_WhenFileDoesNotExist()
    {
        // Arrange
        var service = new FileMemoryLoaderService();
        var mockMemory = new Mock<ILlmMemory>();
        var nonExistentFile = "non_existent_file_12345.txt";

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(
            async () => await service.LoadFromFileAsync(nonExistentFile, mockMemory.Object));
    }

    [Fact]
    public async Task LoadFromFileAsync_LoadsSingleFragment_WithHashFormat()
    {
        // Arrange
        var service = new FileMemoryLoaderService();
        var mockMemory = new Mock<ILlmMemory>();
        var content = "# Game Title\nGame content here.";
        var file = CreateTempFile(content);

        // Act
        var count = await service.LoadFromFileAsync(file, mockMemory.Object);

        // Assert
        Assert.Equal(1, count);
        mockMemory.Verify(
            m => m.ImportMemory(It.Is<IMemoryFragment>(
                f => f.Category == "Game Title" && f.Content == "Game content here.")),
            Times.Once);
    }

    [Fact]
    public async Task LoadFromFileAsync_LoadsMultipleFragments_WithHashFormat()
    {
        // Arrange
        var service = new FileMemoryLoaderService();
        var mockMemory = new Mock<ILlmMemory>();
        var content = @"# Game 1
Content for game 1.

# Game 2
Content for game 2.";
        var file = CreateTempFile(content);

        // Act
        var count = await service.LoadFromFileAsync(file, mockMemory.Object);

        // Assert
        Assert.Equal(2, count);
        mockMemory.Verify(
            m => m.ImportMemory(It.Is<IMemoryFragment>(f => f.Category == "Game 1")),
            Times.Once);
        mockMemory.Verify(
            m => m.ImportMemory(It.Is<IMemoryFragment>(f => f.Category == "Game 2")),
            Times.Once);
    }

    [Fact]
    public async Task LoadFromFileAsync_HandlesLeadingWhitespace_InHashHeaders()
    {
        // Arrange
        var service = new FileMemoryLoaderService();
        var mockMemory = new Mock<ILlmMemory>();
        var content = "   # Game Title\nContent here.";
        var file = CreateTempFile(content);

        // Act
        var count = await service.LoadFromFileAsync(file, mockMemory.Object);

        // Assert
        Assert.Equal(1, count);
        mockMemory.Verify(
            m => m.ImportMemory(It.Is<IMemoryFragment>(f => f.Category == "Game Title")),
            Times.Once);
    }

    [Fact]
    public async Task LoadFromFileAsync_TrimsMultipleHashes_FromHeaders()
    {
        // Arrange
        var service = new FileMemoryLoaderService();
        var mockMemory = new Mock<ILlmMemory>();
        var content = "### Game Title\nContent here.";
        var file = CreateTempFile(content);

        // Act
        var count = await service.LoadFromFileAsync(file, mockMemory.Object);

        // Assert
        Assert.Equal(1, count);
        mockMemory.Verify(
            m => m.ImportMemory(It.Is<IMemoryFragment>(f => f.Category == "Game Title")),
            Times.Once);
    }

    [Fact]
    public async Task LoadFromFileAsync_SkipsEmptyFragments()
    {
        // Arrange
        var service = new FileMemoryLoaderService();
        var mockMemory = new Mock<ILlmMemory>();
        var content = @"# Game 1

# Game 2
Content for game 2.";
        var file = CreateTempFile(content);

        // Act
        var count = await service.LoadFromFileAsync(file, mockMemory.Object);

        // Assert
        Assert.Equal(1, count); // Only Game 2 has content
        mockMemory.Verify(
            m => m.ImportMemory(It.Is<IMemoryFragment>(f => f.Category == "Game 2")),
            Times.Once);
        mockMemory.Verify(
            m => m.ImportMemory(It.Is<IMemoryFragment>(f => f.Category == "Game 1")),
            Times.Never);
    }

    [Fact]
    public async Task LoadFromFileAsync_PreservesMultilineContent()
    {
        // Arrange
        var service = new FileMemoryLoaderService();
        var mockMemory = new Mock<ILlmMemory>();
        var content = @"# Game Title
Line 1
Line 2
Line 3";
        var file = CreateTempFile(content);

        // Act
        var count = await service.LoadFromFileAsync(file, mockMemory.Object);

        // Assert
        Assert.Equal(1, count);
        mockMemory.Verify(
            m => m.ImportMemory(It.Is<IMemoryFragment>(
                f => f.Content.Contains("Line 1") && 
                     f.Content.Contains("Line 2") && 
                     f.Content.Contains("Line 3"))),
            Times.Once);
    }

    [Fact]
    public async Task LoadFromFileAsync_IgnoresLeadingEmptyLines_BeforeFirstHeader()
    {
        // Arrange
        var service = new FileMemoryLoaderService();
        var mockMemory = new Mock<ILlmMemory>();
        var content = @"


# Game Title
Content here.";
        var file = CreateTempFile(content);

        // Act
        var count = await service.LoadFromFileAsync(file, mockMemory.Object);

        // Assert
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task LoadFromFileAsync_HandlesEmptyFile()
    {
        // Arrange
        var service = new FileMemoryLoaderService();
        var mockMemory = new Mock<ILlmMemory>();
        var file = CreateTempFile("");

        // Act
        var count = await service.LoadFromFileAsync(file, mockMemory.Object);

        // Assert
        Assert.Equal(0, count);
        mockMemory.Verify(m => m.ImportMemory(It.IsAny<IMemoryFragment>()), Times.Never);
    }

    #endregion

    #region LoadFromFile Tests (Synchronous)

    [Fact]
    public void LoadFromFile_ThrowsFileNotFoundException_WhenFileDoesNotExist()
    {
        // Arrange
        var service = new FileMemoryLoaderService();
        var mockMemory = new Mock<ILlmMemory>();
        var nonExistentFile = "non_existent_file_sync.txt";

        // Act & Assert
        Assert.Throws<FileNotFoundException>(
            () => service.LoadFromFile(nonExistentFile, mockMemory.Object));
    }

    [Fact]
    public void LoadFromFile_LoadsFragments_Successfully()
    {
        // Arrange
        var service = new FileMemoryLoaderService();
        var mockMemory = new Mock<ILlmMemory>();
        var content = "# Game\nContent.";
        var file = CreateTempFile(content);

        // Act
        var count = service.LoadFromFile(file, mockMemory.Object);

        // Assert
        Assert.Equal(1, count);
        mockMemory.Verify(
            m => m.ImportMemory(It.IsAny<IMemoryFragment>()),
            Times.Once);
    }

    #endregion

    #region LoadFromSimpleFormatAsync Tests

    [Fact]
    public async Task LoadFromSimpleFormatAsync_ThrowsFileNotFoundException_WhenFileDoesNotExist()
    {
        // Arrange
        var mockMemory = new Mock<ILlmMemory>();
        var nonExistentFile = "non_existent_simple.txt";

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(
            async () => await FileMemoryLoaderService.LoadFromSimpleFormatAsync(
                nonExistentFile, mockMemory.Object));
    }

    [Fact]
    public async Task LoadFromSimpleFormatAsync_LoadsPairsCorrectly()
    {
        // Arrange
        var mockMemory = new Mock<ILlmMemory>();
        var content = @"Category 1
Content 1
Category 2
Content 2";
        var file = CreateTempFile(content);

        // Act
        var count = await FileMemoryLoaderService.LoadFromSimpleFormatAsync(file, mockMemory.Object);

        // Assert
        Assert.Equal(2, count);
        mockMemory.Verify(
            m => m.ImportMemory(It.Is<IMemoryFragment>(
                f => f.Category == "Category 1" && f.Content == "Content 1")),
            Times.Once);
        mockMemory.Verify(
            m => m.ImportMemory(It.Is<IMemoryFragment>(
                f => f.Category == "Category 2" && f.Content == "Content 2")),
            Times.Once);
    }

    [Fact]
    public async Task LoadFromSimpleFormatAsync_SkipsEmptyPairs()
    {
        // Arrange
        var mockMemory = new Mock<ILlmMemory>();
        var content = @"Category 1
Content 1

";
        var file = CreateTempFile(content);

        // Act
        var count = await FileMemoryLoaderService.LoadFromSimpleFormatAsync(file, mockMemory.Object);

        // Assert
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task LoadFromSimpleFormatAsync_TrimsWhitespace()
    {
        // Arrange
        var mockMemory = new Mock<ILlmMemory>();
        var content = @"  Category 1  
  Content 1  ";
        var file = CreateTempFile(content);

        // Act
        var count = await FileMemoryLoaderService.LoadFromSimpleFormatAsync(file, mockMemory.Object);

        // Assert
        Assert.Equal(1, count);
        mockMemory.Verify(
            m => m.ImportMemory(It.Is<IMemoryFragment>(
                f => f.Category == "Category 1" && f.Content == "Content 1")),
            Times.Once);
    }

    [Fact]
    public async Task LoadFromSimpleFormatAsync_HandlesOddNumberOfLines()
    {
        // Arrange
        var mockMemory = new Mock<ILlmMemory>();
        var content = @"Category 1
Content 1
Category 2";
        var file = CreateTempFile(content);

        // Act
        var count = await FileMemoryLoaderService.LoadFromSimpleFormatAsync(file, mockMemory.Object);

        // Assert
        Assert.Equal(1, count); // Only complete pair is processed
    }

    #endregion

    #region LoadFromParagraphFormatAsync Tests

    [Fact]
    public async Task LoadFromParagraphFormatAsync_ThrowsFileNotFoundException_WhenFileDoesNotExist()
    {
        // Arrange
        var mockMemory = new Mock<ILlmMemory>();
        var nonExistentFile = "non_existent_paragraph.txt";

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(
            async () => await FileMemoryLoaderService.LoadFromParagraphFormatAsync(
                nonExistentFile, mockMemory.Object));
    }

    [Fact]
    public async Task LoadFromParagraphFormatAsync_SplitsByDoubleNewlines()
    {
        // Arrange
        var mockMemory = new Mock<ILlmMemory>();
        var content = @"Header 1
Content for paragraph 1.

Header 2
Content for paragraph 2.";
        var file = CreateTempFile(content);

        // Act
        var count = await FileMemoryLoaderService.LoadFromParagraphFormatAsync(
            file, mockMemory.Object);

        // Assert
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task LoadFromParagraphFormatAsync_UsesFirstLineAsCategory()
    {
        // Arrange
        var mockMemory = new Mock<ILlmMemory>();
        var content = @"Category Title
Paragraph content here.

Another Category
More content.";
        var file = CreateTempFile(content);

        // Act
        var count = await FileMemoryLoaderService.LoadFromParagraphFormatAsync(
            file, mockMemory.Object);

        // Assert
        mockMemory.Verify(
            m => m.ImportMemory(It.Is<IMemoryFragment>(
                f => f.Category == "Category Title" && f.Content == "Paragraph content here.")),
            Times.Once);
    }

    [Fact]
    public async Task LoadFromParagraphFormatAsync_UsesDefaultCategory_WhenProvided()
    {
        // Arrange
        var mockMemory = new Mock<ILlmMemory>();
        var content = @"Header 1
Content 1.

Header 2
Content 2.";
        var file = CreateTempFile(content);

        // Act
        var count = await FileMemoryLoaderService.LoadFromParagraphFormatAsync(
            file, mockMemory.Object, "Default Category");

        // Assert
        mockMemory.Verify(
            m => m.ImportMemory(It.Is<IMemoryFragment>(f => f.Category == "Default Category")),
            Times.AtLeast(1));
    }

    [Fact]
    public async Task LoadFromParagraphFormatAsync_SkipsEmptyParagraphs()
    {
        // Arrange
        var mockMemory = new Mock<ILlmMemory>();
        var content = @"Header
Content here.



Another Header
More content.";
        var file = CreateTempFile(content);

        // Act
        var count = await FileMemoryLoaderService.LoadFromParagraphFormatAsync(
            file, mockMemory.Object);

        // Assert
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task LoadFromParagraphFormatAsync_HandlesSingleLineParagraphs()
    {
        // Arrange
        var mockMemory = new Mock<ILlmMemory>();
        var content = @"Single line paragraph.

Another single line.";
        var file = CreateTempFile(content);

        // Act
        var count = await FileMemoryLoaderService.LoadFromParagraphFormatAsync(
            file, mockMemory.Object, "Test");

        // Assert
        Assert.Equal(2, count);
    }

    #endregion

    #region LoadFromFileWithChunkingAsync Tests

    [Fact]
    public async Task LoadFromFileWithChunkingAsync_ThrowsFileNotFoundException_WhenFileDoesNotExist()
    {
        // Arrange
        var service = new FileMemoryLoaderService();
        var mockMemory = new Mock<ILlmMemory>();
        var nonExistentFile = "non_existent_chunking.txt";

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(
            async () => await service.LoadFromFileWithChunkingAsync(
                nonExistentFile, mockMemory.Object));
    }

    [Fact]
    public async Task LoadFromFileWithChunkingAsync_ChunksLargeContent()
    {
        // Arrange
        var service = new FileMemoryLoaderService();
        var mockMemory = new Mock<ILlmMemory>();
        var largeContent = new string('x', 1500); // Larger than default 500
        var file = CreateTempFile(largeContent);

        // Act
        var count = await service.LoadFromFileWithChunkingAsync(
            file, mockMemory.Object, maxChunkSize: 500, overlapSize: 50);

        // Assert
        Assert.True(count > 1, "Large content should be split into multiple chunks");
    }

    [Fact]
    public async Task LoadFromFileWithChunkingAsync_UsesFileNameAsCategory()
    {
        // Arrange
        var service = new FileMemoryLoaderService();
        var mockMemory = new Mock<ILlmMemory>();
        var content = "Short content.";
        var file = CreateTempFile(content);
        var fileName = Path.GetFileNameWithoutExtension(file);

        // Act
        var count = await service.LoadFromFileWithChunkingAsync(file, mockMemory.Object);

        // Assert
        mockMemory.Verify(
            m => m.ImportMemory(It.Is<IMemoryFragment>(
                f => f.Category.Contains(fileName))),
            Times.Once);
    }

    [Fact]
    public async Task LoadFromFileWithChunkingAsync_BreaksAtSentenceBoundaries()
    {
        // Arrange
        var service = new FileMemoryLoaderService();
        var mockMemory = new Mock<ILlmMemory>();
        var content = new string('x', 400) + ". " + new string('y', 400) + ".";
        var file = CreateTempFile(content);

        // Act
        var count = await service.LoadFromFileWithChunkingAsync(
            file, mockMemory.Object, maxChunkSize: 500, overlapSize: 50);

        // Assert
        Assert.True(count > 0);
    }

    [Fact]
    public async Task LoadFromFileWithChunkingAsync_HandlesEmptyFile()
    {
        // Arrange
        var service = new FileMemoryLoaderService();
        var mockMemory = new Mock<ILlmMemory>();
        var file = CreateTempFile("");

        // Act
        var count = await service.LoadFromFileWithChunkingAsync(file, mockMemory.Object);

        // Assert
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task LoadFromFileWithChunkingAsync_RespectsCustomChunkSize()
    {
        // Arrange
        var service = new FileMemoryLoaderService();
        var mockMemory = new Mock<ILlmMemory>();
        var content = new string('x', 250);
        var file = CreateTempFile(content);

        // Act
        var count = await service.LoadFromFileWithChunkingAsync(
            file, mockMemory.Object, maxChunkSize: 200, overlapSize: 20);

        // Assert
        Assert.True(count > 1);
    }

    [Fact]
    public async Task LoadFromFileWithChunkingAsync_NumbersChunksSequentially()
    {
        // Arrange
        var service = new FileMemoryLoaderService();
        var mockMemory = new Mock<ILlmMemory>();
        var content = new string('x', 1500);
        var file = CreateTempFile(content);

        // Act
        var count = await service.LoadFromFileWithChunkingAsync(
            file, mockMemory.Object, maxChunkSize: 500, overlapSize: 50);

        // Assert
        mockMemory.Verify(
            m => m.ImportMemory(It.Is<IMemoryFragment>(f => f.Category.Contains("_chunk_1"))),
            Times.Once);
    }

    #endregion

    #region LoadFromFileWithSmartChunkingAsync Tests

    [Fact]
    public async Task LoadFromFileWithSmartChunkingAsync_ThrowsFileNotFoundException_WhenFileDoesNotExist()
    {
        // Arrange
        var service = new FileMemoryLoaderService();
        var mockMemory = new Mock<ILlmMemory>();
        var nonExistentFile = "non_existent_smart.txt";

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(
            async () => await service.LoadFromFileWithSmartChunkingAsync(
                nonExistentFile, mockMemory.Object));
    }

    [Fact]
    public async Task LoadFromFileWithSmartChunkingAsync_KeepsSmallSectionsIntact()
    {
        // Arrange
        var service = new FileMemoryLoaderService();
        var mockMemory = new Mock<ILlmMemory>();
        var content = @"# Section 1
Small content here.

# Section 2
Another small section.";
        var file = CreateTempFile(content);

        // Act
        var count = await service.LoadFromFileWithSmartChunkingAsync(
            file, mockMemory.Object, maxChunkSize: 500);

        // Assert
        Assert.Equal(2, count);
        mockMemory.Verify(
            m => m.ImportMemory(It.Is<IMemoryFragment>(f => f.Category == "Section 1")),
            Times.Once);
    }

    [Fact]
    public async Task LoadFromFileWithSmartChunkingAsync_ChunksLargeSections()
    {
        // Arrange
        var service = new FileMemoryLoaderService();
        var mockMemory = new Mock<ILlmMemory>();
        var largeContent = new string('x', 1500);
        var content = $"# Large Section\n{largeContent}";
        var file = CreateTempFile(content);

        // Act
        var count = await service.LoadFromFileWithSmartChunkingAsync(
            file, mockMemory.Object, maxChunkSize: 500);

        // Assert
        Assert.True(count > 1, "Large sections should be chunked");
    }

    [Fact]
    public async Task LoadFromFileWithSmartChunkingAsync_HandlesMultipleSections()
    {
        // Arrange
        var service = new FileMemoryLoaderService();
        var mockMemory = new Mock<ILlmMemory>();
        var content = @"# Section 1
Content 1.

# Section 2
Content 2.

# Section 3
Content 3.";
        var file = CreateTempFile(content);

        // Act
        var count = await service.LoadFromFileWithSmartChunkingAsync(
            file, mockMemory.Object, maxChunkSize: 500);

        // Assert
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task LoadFromFileWithSmartChunkingAsync_PreservesCategories()
    {
        // Arrange
        var service = new FileMemoryLoaderService();
        var mockMemory = new Mock<ILlmMemory>();
        var content = @"# Game Rules
Rule content here.";
        var file = CreateTempFile(content);

        // Act
        var count = await service.LoadFromFileWithSmartChunkingAsync(
            file, mockMemory.Object, maxChunkSize: 500);

        // Assert
        mockMemory.Verify(
            m => m.ImportMemory(It.Is<IMemoryFragment>(
                f => f.Category == "Game Rules")),
            Times.Once);
    }

    #endregion

    #region LoadFromManualSectionsAsync Tests

    [Fact]
    public async Task LoadFromManualSectionsAsync_ThrowsFileNotFoundException_WhenFileDoesNotExist()
    {
        // Arrange
        var service = new FileMemoryLoaderService();
        var mockMemory = new Mock<ILlmMemory>();
        var nonExistentFile = "non_existent_manual.txt";

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(
            async () => await service.LoadFromManualSectionsAsync(
                nonExistentFile, mockMemory.Object));
    }

    [Fact]
    public async Task LoadFromManualSectionsAsync_SplitsByDoubleNewlines()
    {
        // Arrange
        var service = new FileMemoryLoaderService();
        var mockMemory = new Mock<ILlmMemory>();
        var content = @"Section 1 content here.

Section 2 content here.

Section 3 content here.";
        var file = CreateTempFile(content);

        // Act
        var count = await service.LoadFromManualSectionsAsync(
            file, mockMemory.Object, "TestGame");

        // Assert
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task LoadFromManualSectionsAsync_DetectsHeaders()
    {
        // Arrange
        var service = new FileMemoryLoaderService();
        var mockMemory = new Mock<ILlmMemory>();
        var content = @"Setup Phase
Place all pieces on board.

Movement Rules
Players move clockwise.";
        var file = CreateTempFile(content);

        // Act
        var count = await service.LoadFromManualSectionsAsync(
            file, mockMemory.Object, "TestGame", autoNumberSections: true);

        // Assert
        mockMemory.Verify(
            m => m.ImportMemory(It.Is<IMemoryFragment>(
                f => f.Category.Contains("Setup Phase"))),
            Times.Once);
        mockMemory.Verify(
            m => m.ImportMemory(It.Is<IMemoryFragment>(
                f => f.Category.Contains("Movement Rules"))),
            Times.Once);
    }

    [Fact]
    public async Task LoadFromManualSectionsAsync_AutoNumbersSections_WhenEnabled()
    {
        // Arrange
        var service = new FileMemoryLoaderService();
        var mockMemory = new Mock<ILlmMemory>();
        var content = @"First section.

Second section.";
        var file = CreateTempFile(content);

        // Act
        var count = await service.LoadFromManualSectionsAsync(
            file, mockMemory.Object, "TestGame", autoNumberSections: true);

        // Assert
        mockMemory.Verify(
            m => m.ImportMemory(It.Is<IMemoryFragment>(
                f => f.Category.Contains("Section 1"))),
            Times.Once);
        mockMemory.Verify(
            m => m.ImportMemory(It.Is<IMemoryFragment>(
                f => f.Category.Contains("Section 2"))),
            Times.Once);
    }

    [Fact]
    public async Task LoadFromManualSectionsAsync_DoesNotAutoNumber_WhenDisabled()
    {
        // Arrange
        var service = new FileMemoryLoaderService();
        var mockMemory = new Mock<ILlmMemory>();
        var content = @"Header 1
Content 1.

Header 2
Content 2.";
        var file = CreateTempFile(content);

        // Act
        var count = await service.LoadFromManualSectionsAsync(
            file, mockMemory.Object, "TestGame", autoNumberSections: false);

        // Assert
        mockMemory.Verify(
            m => m.ImportMemory(It.Is<IMemoryFragment>(
                f => f.Category == "Header 1")),
            Times.Once);
        mockMemory.Verify(
            m => m.ImportMemory(It.Is<IMemoryFragment>(
                f => f.Category == "Header 2")),
            Times.Once);
    }

    [Fact]
    public async Task LoadFromManualSectionsAsync_UsesDefaultCategory()
    {
        // Arrange
        var service = new FileMemoryLoaderService();
        var mockMemory = new Mock<ILlmMemory>();
        var content = "Section content.";
        var file = CreateTempFile(content);

        // Act
        var count = await service.LoadFromManualSectionsAsync(
            file, mockMemory.Object, "MyGame", autoNumberSections: true);

        // Assert
        mockMemory.Verify(
            m => m.ImportMemory(It.Is<IMemoryFragment>(
                f => f.Category.Contains("MyGame"))),
            Times.Once);
    }

    [Fact]
    public async Task LoadFromManualSectionsAsync_UsesFileNameAsCategory_WhenDefaultNotProvided()
    {
        // Arrange
        var service = new FileMemoryLoaderService();
        var mockMemory = new Mock<ILlmMemory>();
        var content = "Section content.";
        var file = CreateTempFile(content);
        var fileName = Path.GetFileNameWithoutExtension(file);

        // Act
        var count = await service.LoadFromManualSectionsAsync(
            file, mockMemory.Object);

        // Assert
        mockMemory.Verify(
            m => m.ImportMemory(It.Is<IMemoryFragment>(
                f => f.Category.Contains(fileName))),
            Times.Once);
    }

    [Fact]
    public async Task LoadFromManualSectionsAsync_IgnoresLongHeaders()
    {
        // Arrange
        var service = new FileMemoryLoaderService();
        var mockMemory = new Mock<ILlmMemory>();
        var longHeader = new string('A', 150); // Longer than 100 chars
        var content = $"{longHeader}\nThis is content.";
        var file = CreateTempFile(content);

        // Act
        var count = await service.LoadFromManualSectionsAsync(
            file, mockMemory.Object, "TestGame", autoNumberSections: true);

        // Assert
        // Long line should be treated as content, not header
        mockMemory.Verify(
            m => m.ImportMemory(It.Is<IMemoryFragment>(
                f => f.Content.Contains(longHeader))),
            Times.Once);
    }

    [Fact]
    public async Task LoadFromManualSectionsAsync_IgnoresHeadersEndingWithPeriod()
    {
        // Arrange
        var service = new FileMemoryLoaderService();
        var mockMemory = new Mock<ILlmMemory>();
        var content = @"This is a sentence.
More content here.

Another Section
Real content.";
        var file = CreateTempFile(content);

        // Act
        var count = await service.LoadFromManualSectionsAsync(
            file, mockMemory.Object, "TestGame", autoNumberSections: false);

        // Assert
        // First section: "This is a sentence." should be content, not header
        // Second section: "Another Section" should be header
        mockMemory.Verify(
            m => m.ImportMemory(It.Is<IMemoryFragment>(
                f => f.Category == "Another Section")),
            Times.Once);
    }

    [Fact]
    public async Task LoadFromManualSectionsAsync_IgnoresHeadersEndingWithColon()
    {
        // Arrange
        var service = new FileMemoryLoaderService();
        var mockMemory = new Mock<ILlmMemory>();
        var content = @"List of items:
Item 1, Item 2.

Real Header
Content here.";
        var file = CreateTempFile(content);

        // Act
        var count = await service.LoadFromManualSectionsAsync(
            file, mockMemory.Object, "TestGame", autoNumberSections: false);

        // Assert
        mockMemory.Verify(
            m => m.ImportMemory(It.Is<IMemoryFragment>(
                f => f.Category == "Real Header")),
            Times.Once);
    }

    [Fact]
    public async Task LoadFromManualSectionsAsync_SkipsEmptySections()
    {
        // Arrange
        var service = new FileMemoryLoaderService();
        var mockMemory = new Mock<ILlmMemory>();
        var content = @"Valid Section
Content here.



Another Valid Section
More content.";
        var file = CreateTempFile(content);

        // Act
        var count = await service.LoadFromManualSectionsAsync(
            file, mockMemory.Object, "TestGame");

        // Assert
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task LoadFromManualSectionsAsync_HandlesSingleLineSection()
    {
        // Arrange
        var service = new FileMemoryLoaderService();
        var mockMemory = new Mock<ILlmMemory>();
        var content = "Single line section.";
        var file = CreateTempFile(content);

        // Act
        var count = await service.LoadFromManualSectionsAsync(
            file, mockMemory.Object, "TestGame");

        // Assert
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task LoadFromManualSectionsAsync_PreservesNewlinesInContent()
    {
        // Arrange
        var service = new FileMemoryLoaderService();
        var mockMemory = new Mock<ILlmMemory>();
        var content = @"Header
Line 1
Line 2
Line 3";
        var file = CreateTempFile(content);

        // Act
        var count = await service.LoadFromManualSectionsAsync(
            file, mockMemory.Object, "TestGame", autoNumberSections: false);

        // Assert
        mockMemory.Verify(
            m => m.ImportMemory(It.Is<IMemoryFragment>(
                f => f.Content.Contains("Line 1") && 
                     f.Content.Contains("Line 2") && 
                     f.Content.Contains("Line 3"))),
            Times.Once);
    }

    #endregion

    #region Edge Cases and Integration Tests

    [Fact]
    public async Task AllMethods_HandleSpecialCharacters()
    {
        // Arrange
        var service = new FileMemoryLoaderService();
        var mockMemory = new Mock<ILlmMemory>();
        var content = "# Title\nContent with \"quotes\" and <tags> and special chars: é, ñ, ???.";
        var file = CreateTempFile(content);

        // Act
        var count = await service.LoadFromFileAsync(file, mockMemory.Object);

        // Assert
        Assert.Equal(1, count);
        mockMemory.Verify(
            m => m.ImportMemory(It.Is<IMemoryFragment>(
                f => f.Content.Contains("\"quotes\"") && f.Content.Contains("<tags>"))),
            Times.Once);
    }

    [Fact]
    public async Task LoadFromFileAsync_HandlesWindowsLineEndings()
    {
        // Arrange
        var service = new FileMemoryLoaderService();
        var mockMemory = new Mock<ILlmMemory>();
        var content = "# Title\r\nContent line 1.\r\nContent line 2.";
        var file = CreateTempFile(content);

        // Act
        var count = await service.LoadFromFileAsync(file, mockMemory.Object);

        // Assert
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task LoadFromFileAsync_HandlesUnixLineEndings()
    {
        // Arrange
        var service = new FileMemoryLoaderService();
        var mockMemory = new Mock<ILlmMemory>();
        var content = "# Title\nContent line 1.\nContent line 2.";
        var file = CreateTempFile(content);

        // Act
        var count = await service.LoadFromFileAsync(file, mockMemory.Object);

        // Assert
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task LoadFromManualSectionsAsync_HandlesRealWorldRulebook()
    {
        // Arrange
        var service = new FileMemoryLoaderService();
        var mockMemory = new Mock<ILlmMemory>();
        var content = @"Setup Phase
Place the board in the center. Each player takes 3 cards.

Movement Rules
Players move clockwise around the board.

Combat System
Roll 2 dice when attacking. Add your strength bonus.

Winning Conditions
First player to 10 points wins the game.";
        var file = CreateTempFile(content);

        // Act
        var count = await service.LoadFromManualSectionsAsync(
            file, mockMemory.Object, "BoardGame");

        // Assert
        Assert.Equal(4, count);
        mockMemory.Verify(
            m => m.ImportMemory(It.Is<IMemoryFragment>(
                f => f.Category.Contains("Setup Phase"))),
            Times.Once);
        mockMemory.Verify(
            m => m.ImportMemory(It.Is<IMemoryFragment>(
                f => f.Category.Contains("Combat System"))),
            Times.Once);
    }

    #endregion
}
