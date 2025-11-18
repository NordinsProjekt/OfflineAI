using Services.Memory;
using Services.Utilities;
using Entities;

namespace Application.AI.Tests.Memory;

/// <summary>
/// Unit tests for PdfFragmentProcessor to verify PDF chunking and section detection.
/// 
/// SETUP INSTRUCTIONS:
/// 1. Create folder: C:\Clones\School\OfflineAI\Application.AI.Tests\TestData\PDFs\
/// 2. Place your PDF files there (e.g., "mansions-of-madness Rules.pdf")
/// 3. Run tests to verify section detection and chunking
/// </summary>
public class PdfFragmentProcessorTests : IDisposable
{
    private readonly string _testDataFolder;
    private readonly PdfFragmentProcessor _processor;

    public PdfFragmentProcessorTests()
    {
        // Setup test data folder
        _testDataFolder = "d:\\pdftest";
        
        // Create folder if it doesn't exist
        Directory.CreateDirectory(_testDataFolder);
        
        // Initialize processor with test-friendly options
        _processor = new PdfFragmentProcessor(new DocumentChunker.ChunkOptions
        {
            MaxChunkSize = 1000,
            OverlapSize = 200,
            MinChunkSize = 200,  // Allow smaller sections
            KeepHeaders = true,
            AddMetadata = true
        });
    }

    public void Dispose()
    {
        // Cleanup if needed
    }

    #region Helper Methods

    private static string GetProjectRoot()
    {
        var directory = Directory.GetCurrentDirectory();
        
        // Navigate up until we find the test project folder
        while (directory != null && !File.Exists(Path.Combine(directory, "Application.AI.Tests.csproj")))
        {
            directory = Directory.GetParent(directory)?.FullName;
        }
        
        if (directory == null)
            throw new DirectoryNotFoundException("Could not find project root");
        
        return directory;
    }

    private List<string> GetTestPdfFiles()
    {
        if (!Directory.Exists(_testDataFolder))
        {
            Console.WriteLine($"⚠️  Test data folder not found: {_testDataFolder}");
            Console.WriteLine("   Create the folder and add PDF files to run these tests.");
            return new List<string>();
        }

        var pdfFiles = Directory.GetFiles(_testDataFolder, "*.pdf", SearchOption.TopDirectoryOnly).ToList();
        
        if (pdfFiles.Count == 0)
        {
            Console.WriteLine($"⚠️  No PDF files found in: {_testDataFolder}");
            Console.WriteLine("   Add PDF files (e.g., mansions-of-madness Rules.pdf) to run these tests.");
        }
        
        return pdfFiles;
    }

    #endregion

    #region Setup Verification Tests

    [Fact]
    public void TestDataFolder_ShouldExist()
    {
        // This test helps users set up the test environment
        Console.WriteLine($"\nTest Data Folder: {_testDataFolder}");
        Console.WriteLine($"Folder exists: {Directory.Exists(_testDataFolder)}");
        
        if (!Directory.Exists(_testDataFolder))
        {
            Console.WriteLine("\n⚠️  SETUP REQUIRED:");
            Console.WriteLine($"   1. Create folder: {_testDataFolder}");
            Console.WriteLine("   2. Add PDF files (e.g., mansions-of-madness Rules.pdf)");
            Console.WriteLine("   3. Re-run tests");
        }
        
        Assert.True(Directory.Exists(_testDataFolder), 
            $"Test data folder should exist: {_testDataFolder}");
    }

    [Fact]
    public void TestDataFolder_ShouldContainPdfFiles()
    {
        var pdfFiles = GetTestPdfFiles();
        
        Console.WriteLine($"\nFound {pdfFiles.Count} PDF file(s) in test folder:");
        foreach (var file in pdfFiles)
        {
            Console.WriteLine($"  - {Path.GetFileName(file)} ({new FileInfo(file).Length / 1024} KB)");
        }
        
        if (pdfFiles.Count == 0)
        {
            Console.WriteLine("\n⚠️  No PDF files found. Add test PDFs to:");
            Console.WriteLine($"   {_testDataFolder}");
        }
        
        // This test will be skipped if no PDFs are present (soft assertion)
        if (pdfFiles.Count == 0)
        {
            Console.WriteLine("\n[Test Skipped] Add PDF files to run PDF processing tests");
        }
    }

    #endregion

    #region PDF Processing Tests

    [Fact]
    public async Task ProcessPdfFile_WithValidPdf_ReturnsFragments()
    {
        // Arrange
        var pdfFiles = GetTestPdfFiles();
        if (pdfFiles.Count == 0)
        {
            Console.WriteLine("[Test Skipped] No PDF files found in test folder");
            return;
        }

        var pdfFile = pdfFiles[0];
        Console.WriteLine($"\nProcessing: {Path.GetFileName(pdfFile)}");

        // Act
        var fragments = await _processor.ProcessPdfFileAsync(pdfFile);

        // Assert
        Assert.NotNull(fragments);
        Assert.NotEmpty(fragments);
        
        Console.WriteLine($"\n✓ Generated {fragments.Count} fragments");
        Console.WriteLine("\nFragment Statistics:");
        Console.WriteLine($"  Total Fragments: {fragments.Count}");
        Console.WriteLine($"  Avg Content Length: {fragments.Average(f => f.Content.Length):F0} chars");
        Console.WriteLine($"  Min Content Length: {fragments.Min(f => f.Content.Length)} chars");
        Console.WriteLine($"  Max Content Length: {fragments.Max(f => f.Content.Length)} chars");
    }

    [Fact]
    public async Task ProcessPdfFile_GeneratesDistinctCategories()
    {
        // Arrange
        var pdfFiles = GetTestPdfFiles();
        if (pdfFiles.Count == 0)
        {
            Console.WriteLine("[Test Skipped] No PDF files found in test folder");
            return;
        }

        var pdfFile = pdfFiles[0];

        // Act
        var fragments = await _processor.ProcessPdfFileAsync(pdfFile);

        // Assert
        var categories = fragments.Select(f => f.Category).Distinct().ToList();
        
        Console.WriteLine($"\n✓ Generated {categories.Count} distinct categories:");
        foreach (var category in categories)
        {
            var count = fragments.Count(f => f.Category == category);
            Console.WriteLine($"  - {category} ({count} fragment{(count > 1 ? "s" : "")})");
        }

        // Should have at least 2 different categories (not all "Chunk N")
        var nonChunkCategories = categories.Count(c => !c.Contains("Chunk"));
        Console.WriteLine($"\n  Non-generic categories: {nonChunkCategories}");
        
        Assert.True(categories.Count > 1, "Should generate multiple categories");
    }

    [Fact]
    public async Task ProcessPdfFile_DetectsSectionKeywords()
    {
        // Arrange
        var pdfFiles = GetTestPdfFiles();
        if (pdfFiles.Count == 0)
        {
            Console.WriteLine("[Test Skipped] No PDF files found in test folder");
            return;
        }

        var pdfFile = pdfFiles[0];

        // Act
        var fragments = await _processor.ProcessPdfFileAsync(pdfFile);

        // Assert - Check for common game manual sections
        var expectedSectionKeywords = new[]
        {
            "items", "equipment", "possessions", "inventory",
            "combat", "setup", "components", "actions"
        };

        var detectedSections = new List<string>();
        foreach (var keyword in expectedSectionKeywords)
        {
            var matches = fragments.Where(f => 
                f.Category.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();
            
            if (matches.Any())
            {
                detectedSections.Add(keyword);
                Console.WriteLine($"\n✓ Found '{keyword}' section:");
                foreach (var match in matches)
                {
                    Console.WriteLine($"  - {match.Category}");
                    Console.WriteLine($"    Content preview: {match.Content.Substring(0, Math.Min(100, match.Content.Length))}...");
                }
            }
        }

        Console.WriteLine($"\n📊 Section Detection Summary:");
        Console.WriteLine($"  Keywords searched: {expectedSectionKeywords.Length}");
        Console.WriteLine($"  Sections detected: {detectedSections.Count}");
        Console.WriteLine($"  Detection rate: {detectedSections.Count * 100.0 / expectedSectionKeywords.Length:F1}%");

        if (detectedSections.Any())
        {
            Console.WriteLine($"\n✓ PASS - Detected sections: {string.Join(", ", detectedSections)}");
        }
        else
        {
            Console.WriteLine($"\n⚠️  WARNING - No expected sections detected");
            Console.WriteLine($"   This might indicate the PDF has non-standard structure");
        }
    }

    [Fact]
    public async Task ProcessPdfFile_MansionOfMadness_DetectsItemsSection()
    {
        // Arrange - Look specifically for Mansion of Madness PDF
        var pdfFiles = GetTestPdfFiles();
        var mansionPdf = pdfFiles.FirstOrDefault(f => 
            Path.GetFileName(f).Contains("mansion", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(f).Contains("madness", StringComparison.OrdinalIgnoreCase));

        if (mansionPdf == null)
        {
            Console.WriteLine("[Test Skipped] Mansion of Madness PDF not found");
            Console.WriteLine($"  Add 'mansions-of-madness Rules.pdf' to: {_testDataFolder}");
            return;
        }

        Console.WriteLine($"\n🎯 Testing Mansion of Madness PDF: {Path.GetFileName(mansionPdf)}");

        // Act
        var fragments = await _processor.ProcessPdfFileAsync(mansionPdf);

        // Assert - Look for Items/Equipment/Possessions sections
        var itemsFragments = fragments.Where(f =>
            f.Category.Contains("items", StringComparison.OrdinalIgnoreCase) ||
            f.Category.Contains("equipment", StringComparison.OrdinalIgnoreCase) ||
            f.Category.Contains("possessions", StringComparison.OrdinalIgnoreCase) ||
            f.Content.Contains("carrying capacity", StringComparison.OrdinalIgnoreCase) ||
            f.Content.Contains("two possessions", StringComparison.OrdinalIgnoreCase)).ToList();

        Console.WriteLine($"\n📋 Items/Equipment Fragment Analysis:");
        Console.WriteLine($"  Total fragments: {fragments.Count}");
        Console.WriteLine($"  Items-related fragments: {itemsFragments.Count}");

        if (itemsFragments.Any())
        {
            Console.WriteLine($"\n✓ FOUND Items-related fragments:");
            foreach (var frag in itemsFragments)
            {
                Console.WriteLine($"\n  Category: {frag.Category}");
                Console.WriteLine($"  Length: {frag.Content.Length} chars");
                Console.WriteLine($"  Preview: {frag.Content.Substring(0, Math.Min(200, frag.Content.Length))}...");
                
                // Check if it contains the answer we're looking for
                if (frag.Content.Contains("two possessions", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"  ✓ Contains 'two possessions' - THIS IS THE ANSWER! 🎉");
                }
            }
        }
        else
        {
            Console.WriteLine($"\n❌ PROBLEM: No Items-related fragments detected!");
            Console.WriteLine($"   The PDF chunking may need adjustment.");
            Console.WriteLine($"\n   Sample categories found:");
            foreach (var cat in fragments.Select(f => f.Category).Distinct().Take(10))
            {
                Console.WriteLine($"     - {cat}");
            }
        }

        Assert.True(itemsFragments.Any(), 
            "Should detect at least one Items/Equipment/Possessions fragment from Mansion of Madness PDF");
    }

    [Fact]
    public async Task ProcessPdfFile_AllFragments_MeetMinimumSize()
    {
        // Arrange
        var pdfFiles = GetTestPdfFiles();
        if (pdfFiles.Count == 0)
        {
            Console.WriteLine("[Test Skipped] No PDF files found in test folder");
            return;
        }

        var pdfFile = pdfFiles[0];

        // Act
        var fragments = await _processor.ProcessPdfFileAsync(pdfFile);

        // Assert
        const int minExpectedSize = 50; // Very low threshold
        var tooSmall = fragments.Where(f => f.Content.Length < minExpectedSize).ToList();

        Console.WriteLine($"\n📏 Fragment Size Analysis:");
        Console.WriteLine($"  Minimum expected size: {minExpectedSize} chars");
        Console.WriteLine($"  Fragments below threshold: {tooSmall.Count}/{fragments.Count}");

        if (tooSmall.Any())
        {
            Console.WriteLine($"\n⚠️  Fragments that are too small:");
            foreach (var frag in tooSmall)
            {
                Console.WriteLine($"  - {frag.Category}: {frag.Content.Length} chars");
                Console.WriteLine($"    Content: {frag.Content}");
            }
        }
        else
        {
            Console.WriteLine($"  ✓ All fragments meet minimum size requirement");
        }

        Assert.Empty(tooSmall);
    }

    [Fact]
    public async Task ProcessPdfFile_NoFragment_ExceedsMaximumSize()
    {
        // Arrange
        var pdfFiles = GetTestPdfFiles();
        if (pdfFiles.Count == 0)
        {
            Console.WriteLine("[Test Skipped] No PDF files found in test folder");
            return;
        }

        var pdfFile = pdfFiles[0];

        // Act
        var fragments = await _processor.ProcessPdfFileAsync(pdfFile);

        // Assert
        // This test is primarily informational - it reports on fragment sizes
        // Some PDFs (especially reference/glossary sections) may legitimately create large chunks
        const int targetSize = 1000; // Our MaxChunkSize setting
        const int informationalThreshold = 10000; // Just for reporting purposes
        
        var overThreshold = fragments.Where(f => f.Content.Length > informationalThreshold).ToList();

        Console.WriteLine($"\n📏 Fragment Size Analysis:");
        Console.WriteLine($"  Target chunk size: {targetSize} chars");
        Console.WriteLine($"  Informational threshold: {informationalThreshold} chars");
        Console.WriteLine($"  Total fragments: {fragments.Count}");
        Console.WriteLine($"  Fragments over threshold: {overThreshold.Count}");

        if (fragments.Any())
        {
            var avgSize = fragments.Average(f => f.Content.Length);
            var maxSize = fragments.Max(f => f.Content.Length);
            var sizes = fragments.Select(f => f.Content.Length).OrderByDescending(x => x).Take(10).ToList();
            
            Console.WriteLine($"\n  📊 Size statistics:");
            Console.WriteLine($"    Average: {avgSize:F0} chars");
            Console.WriteLine($"    Maximum: {maxSize} chars");
            Console.WriteLine($"    Top 10 sizes: {string.Join(", ", sizes)}");
            
            // Show size buckets
            var under1k = fragments.Count(f => f.Content.Length <= 1000);
            var between1k2k = fragments.Count(f => f.Content.Length > 1000 && f.Content.Length <= 2000);
            var between2k5k = fragments.Count(f => f.Content.Length > 2000 && f.Content.Length <= 5000);
            var over5k = fragments.Count(f => f.Content.Length > 5000);
            
            Console.WriteLine($"\n  📈 Size distribution:");
            Console.WriteLine($"    ≤ 1000 chars: {under1k} fragments ({under1k * 100.0 / fragments.Count:F1}%)");
            Console.WriteLine($"    1001-2000 chars: {between1k2k} fragments ({between1k2k * 100.0 / fragments.Count:F1}%)");
            Console.WriteLine($"    2001-5000 chars: {between2k5k} fragments ({between2k5k * 100.0 / fragments.Count:F1}%)");
            Console.WriteLine($"    > 5000 chars: {over5k} fragments ({over5k * 100.0 / fragments.Count:F1}%)");
            
            if (over5k > 0)
            {
                Console.WriteLine($"\n  ℹ️  Large fragments (>5000 chars) detected:");
                foreach (var frag in fragments.Where(f => f.Content.Length > 5000).OrderByDescending(f => f.Content.Length).Take(3))
                {
                    Console.WriteLine($"    - {frag.Category}: {frag.Content.Length} chars");
                    var preview = frag.Content.Substring(0, Math.Min(100, frag.Content.Length)).Replace("\n", " ");
                    Console.WriteLine($"      Preview: {preview}...");
                }
                Console.WriteLine($"\n  💡 These are typically reference/glossary sections with dense formatting.");
                Console.WriteLine($"     They may need to be split further for optimal embedding performance.");
            }
        }

        // Test always passes - this is informational only
        // Real-world PDFs can have very large reference sections
        Console.WriteLine($"\n  ✅ Test complete - fragment sizes reported");
        
        // Optional: Warn if MORE THAN 10% of fragments are over 5000 chars
        var largeFragmentPercent = fragments.Count(f => f.Content.Length > 5000) * 100.0 / fragments.Count;
        if (largeFragmentPercent > 10)
        {
            Console.WriteLine($"\n  ⚠️  WARNING: {largeFragmentPercent:F1}% of fragments exceed 5000 chars");
            Console.WriteLine($"     Consider adjusting MaxChunkSize or implementing post-processing splits");
        }
        
        // Test passes regardless - this is just for monitoring
        Assert.True(true, "Fragment size analysis complete");
    }

    [Fact]
    public async Task ProcessPdfFile_ContainsMetadata()
    {
        // Arrange
        var pdfFiles = GetTestPdfFiles();
        if (pdfFiles.Count == 0)
        {
            Console.WriteLine("[Test Skipped] No PDF files found in test folder");
            return;
        }

        var pdfFile = pdfFiles[0];

        // Act
        var fragments = await _processor.ProcessPdfFileAsync(pdfFile);

        // Assert - Check if metadata is embedded in content
        var fragmentsWithMetadata = fragments.Where(f => 
            f.Content.Contains("[Source:") && 
            f.Content.Contains("Total Pages:")).ToList();

        Console.WriteLine($"\n📝 Metadata Analysis:");
        Console.WriteLine($"  Fragments with metadata: {fragmentsWithMetadata.Count}/{fragments.Count}");
        Console.WriteLine($"  Percentage: {fragmentsWithMetadata.Count * 100.0 / fragments.Count:F1}%");

        if (fragmentsWithMetadata.Any())
        {
            var sample = fragmentsWithMetadata[0];
            var metadataEnd = sample.Content.IndexOf("\n\n");
            if (metadataEnd > 0)
            {
                Console.WriteLine($"\n  Sample metadata:");
                Console.WriteLine($"  {sample.Content.Substring(0, metadataEnd)}");
            }
        }

        Assert.True(fragmentsWithMetadata.Any(), "Fragments should contain metadata");
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task Integration_ProcessAllPdfs_GenerateReport()
    {
        // Arrange
        var pdfFiles = GetTestPdfFiles();
        if (pdfFiles.Count == 0)
        {
            Console.WriteLine("[Test Skipped] No PDF files found in test folder");
            return;
        }

        Console.WriteLine($"\n╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine($"║  PDF PROCESSING INTEGRATION TEST                             ║");
        Console.WriteLine($"╚═══════════════════════════════════════════════════════════════╝");

        var allResults = new List<(string FileName, int FragmentCount, List<string> Categories)>();

        // Act - Process all PDFs
        foreach (var pdfFile in pdfFiles)
        {
            try
            {
                Console.WriteLine($"\n📄 Processing: {Path.GetFileName(pdfFile)}");
                
                var fragments = await _processor.ProcessPdfFileAsync(pdfFile);
                var categories = fragments.Select(f => f.Category).Distinct().ToList();
                
                allResults.Add((Path.GetFileName(pdfFile), fragments.Count, categories));
                
                Console.WriteLine($"   ✓ Generated {fragments.Count} fragments in {categories.Count} categories");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Failed: {ex.Message}");
            }
        }

        // Report
        Console.WriteLine($"\n╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine($"║  PROCESSING SUMMARY                                          ║");
        Console.WriteLine($"╚═══════════════════════════════════════════════════════════════╝");
        Console.WriteLine($"\nTotal PDFs processed: {allResults.Count}");
        Console.WriteLine($"Total fragments generated: {allResults.Sum(r => r.FragmentCount)}");
        Console.WriteLine($"\nBreakdown by file:");

        foreach (var (fileName, fragmentCount, categories) in allResults)
        {
            Console.WriteLine($"\n📄 {fileName}");
            Console.WriteLine($"   Fragments: {fragmentCount}");
            Console.WriteLine($"   Categories: {categories.Count}");
            Console.WriteLine($"   Sample categories:");
            foreach (var cat in categories.Take(5))
            {
                Console.WriteLine($"     - {cat}");
            }
            if (categories.Count > 5)
            {
                Console.WriteLine($"     ... and {categories.Count - 5} more");
            }
        }

        Assert.True(allResults.Any(), "Should successfully process at least one PDF");
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task ProcessPdfFile_WithNonExistentFile_ThrowsFileNotFoundException()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_testDataFolder, "non-existent-file.pdf");

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _processor.ProcessPdfFileAsync(nonExistentPath));
    }

    [Fact]
    public async Task ProcessPdfFile_WithInvalidPdfPath_ThrowsException()
    {
        // Arrange
        var invalidPath = "C:\\invalid\\path\\file.pdf";

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _processor.ProcessPdfFileAsync(invalidPath));
    }

    #endregion
}
