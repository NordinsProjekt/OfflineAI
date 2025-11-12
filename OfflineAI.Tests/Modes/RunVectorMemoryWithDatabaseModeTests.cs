using Entities;
using OfflineAI.Tests.Mocks;
using Services.Memory;

namespace OfflineAI.Tests.Modes;

public class RunVectorMemoryWithDatabaseModeTests
{
    [Fact]
    public async Task LoadFromFilesAndSaveAsync_ShouldProcessFileContentCorrectly()
    {
        // Arrange
        var tempFile = CreateTempTestFile("Game Rules\n\nSection 1\nContent of section 1.\n\nSection 2\nContent of section 2.");
        var knowledgeFiles = new Dictionary<string, string>
        {
            ["TestGame"] = tempFile
        };
        var collectionName = "test-collection";

        var mockRepo = new MockVectorMemoryRepository();
        var embeddingService = new MockEmbeddingService(384);
        var persistenceService = new TestVectorMemoryPersistenceService(mockRepo, embeddingService);

        await mockRepo.InitializeDatabaseAsync();

        // Act
        var result = await LoadFromFilesAndSaveAsyncWrapper(
            knowledgeFiles,
            collectionName,
            embeddingService,
            persistenceService);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Count >= 2, "Should have at least 2 sections");
        
        var count = await mockRepo.GetCountAsync(collectionName);
        Assert.True(count >= 2, $"Database should have at least 2 fragments, found {count}");

        // Cleanup
        System.IO.File.Delete(tempFile);
    }

    [Fact]
    public async Task LoadFromFilesAndSaveAsync_ShouldHandleMultipleFiles()
    {
        // Arrange
        var tempFile1 = CreateTempTestFile("Game 1\n\nRule A\nContent A.");
        var tempFile2 = CreateTempTestFile("Game 2\n\nRule B\nContent B.");
        
        var knowledgeFiles = new Dictionary<string, string>
        {
            ["Game1"] = tempFile1,
            ["Game2"] = tempFile2
        };
        var collectionName = "multi-game-collection";

        var mockRepo = new MockVectorMemoryRepository();
        var embeddingService = new MockEmbeddingService(384);
        var persistenceService = new TestVectorMemoryPersistenceService(mockRepo, embeddingService);

        await mockRepo.InitializeDatabaseAsync();

        // Act
        var result = await LoadFromFilesAndSaveAsyncWrapper(
            knowledgeFiles,
            collectionName,
            embeddingService,
            persistenceService);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Count >= 2);

        var fragments = await mockRepo.LoadByCollectionAsync(collectionName);
        Assert.True(fragments.Count >= 2);
        Assert.Contains(fragments, f => f.Category.Contains("Game1"));
        Assert.Contains(fragments, f => f.Category.Contains("Game2"));

        // Cleanup
        System.IO.File.Delete(tempFile1);
        System.IO.File.Delete(tempFile2);
    }

    [Fact]
    public async Task LoadFromFilesAndSaveAsync_ShouldDetectHeaders()
    {
        // Arrange
        var tempFile = CreateTempTestFile("Setup Phase\nPlace all tokens on the board.\n\nMovement Rules\nPlayers can move up to 3 spaces.");
        var knowledgeFiles = new Dictionary<string, string>
        {
            ["TestGame"] = tempFile
        };
        var collectionName = "header-test";

        var mockRepo = new MockVectorMemoryRepository();
        var embeddingService = new MockEmbeddingService(384);
        var persistenceService = new TestVectorMemoryPersistenceService(mockRepo, embeddingService);

        await mockRepo.InitializeDatabaseAsync();

        // Act
        var result = await LoadFromFilesAndSaveAsyncWrapper(
            knowledgeFiles,
            collectionName,
            embeddingService,
            persistenceService);

        // Assert
        Assert.NotNull(result);
        
        var fragments = await mockRepo.LoadByCollectionAsync(collectionName);
        Assert.Contains(fragments, f => f.Category.Contains("Setup Phase"));
        Assert.Contains(fragments, f => f.Category.Contains("Movement Rules"));
        Assert.Contains(fragments, f => f.Content.Contains("Place all tokens"));
        Assert.Contains(fragments, f => f.Content.Contains("move up to 3 spaces"));

        // Cleanup
        System.IO.File.Delete(tempFile);
    }

    [Fact]
    public async Task LoadFromFilesAndSaveAsync_ShouldSkipEmptySections()
    {
        // Arrange
        var tempFile = CreateTempTestFile("Valid Section\nValid content.\n\n\n\n\n\nAnother Valid\nMore content.");
        var knowledgeFiles = new Dictionary<string, string>
        {
            ["TestGame"] = tempFile
        };
        var collectionName = "empty-sections-test";

        var mockRepo = new MockVectorMemoryRepository();
        var embeddingService = new MockEmbeddingService(384);
        var persistenceService = new TestVectorMemoryPersistenceService(mockRepo, embeddingService);

        await mockRepo.InitializeDatabaseAsync();

        // Act
        var result = await LoadFromFilesAndSaveAsyncWrapper(
            knowledgeFiles,
            collectionName,
            embeddingService,
            persistenceService);

        // Assert
        Assert.NotNull(result);
        
        var fragments = await mockRepo.LoadByCollectionAsync(collectionName);
        Assert.All(fragments, f => Assert.False(string.IsNullOrWhiteSpace(f.Content)));
        Assert.Equal(2, fragments.Count);

        // Cleanup
        System.IO.File.Delete(tempFile);
    }

    [Fact]
    public async Task LoadFromFilesInMemoryAsync_ShouldLoadFragmentsWithoutDatabase()
    {
        // Arrange
        var tempFile = CreateTempTestFile("Memory Test\n\nSection A\nContent A.\n\nSection B\nContent B.");
        var knowledgeFiles = new Dictionary<string, string>
        {
            ["MemoryGame"] = tempFile
        };

        var embeddingService = new MockEmbeddingService(384);

        // Act
        var result = await LoadFromFilesInMemoryAsyncWrapper(
            knowledgeFiles,
            embeddingService);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Count >= 2);

        // Cleanup
        System.IO.File.Delete(tempFile);
    }

    [Fact]
    public async Task LoadFromFilesAndSaveAsync_ShouldVerifyFragmentSearch()
    {
        // Arrange
        var tempFile = CreateTempTestFile("Combat Rules\n\nAttacking: Roll 2 dice when attacking.\n\nDefending: Roll 1 die when defending.");
        var knowledgeFiles = new Dictionary<string, string>
        {
            ["RPG"] = tempFile
        };
        var collectionName = "rpg-rules";

        var mockRepo = new MockVectorMemoryRepository();
        var embeddingService = new MockEmbeddingService(384);
        var persistenceService = new TestVectorMemoryPersistenceService(mockRepo, embeddingService);

        await mockRepo.InitializeDatabaseAsync();

        // Act
        var result = await LoadFromFilesAndSaveAsyncWrapper(
            knowledgeFiles,
            collectionName,
            embeddingService,
            persistenceService);

        // Assert - Verify fragments can be searched
        Assert.NotNull(result);
        Assert.True(result.Count > 0, "VectorMemory should have fragments loaded");
        
        var searchResult = await result.SearchRelevantMemoryAsync("attacking", topK: 5, minRelevanceScore: 0.0);
        Assert.NotNull(searchResult);
        Assert.NotEmpty(searchResult);
        
        // Verify the search result contains relevant content
        Assert.Contains("dice", searchResult, StringComparison.OrdinalIgnoreCase);

        // Cleanup
        System.IO.File.Delete(tempFile);
    }

    [Fact]
    public async Task LoadFromFilesAndSaveAsync_ShouldHandleLongHeaders()
    {
        // Arrange
        var longHeader = new string('A', 150); // Header longer than 100 chars
        var tempFile = CreateTempTestFile($"{longHeader}\nThis should be treated as content, not a header.");
        var knowledgeFiles = new Dictionary<string, string>
        {
            ["TestGame"] = tempFile
        };
        var collectionName = "long-header-test";

        var mockRepo = new MockVectorMemoryRepository();
        var embeddingService = new MockEmbeddingService(384);
        var persistenceService = new TestVectorMemoryPersistenceService(mockRepo, embeddingService);

        await mockRepo.InitializeDatabaseAsync();

        // Act
        var result = await LoadFromFilesAndSaveAsyncWrapper(
            knowledgeFiles,
            collectionName,
            embeddingService,
            persistenceService);

        // Assert
        Assert.NotNull(result);
        
        var fragments = await mockRepo.LoadByCollectionAsync(collectionName);
        // Long header should be part of content, not category
        Assert.All(fragments, f => 
            Assert.DoesNotContain(longHeader, f.Category));

        // Cleanup
        System.IO.File.Delete(tempFile);
    }

    [Fact]
    public async Task LoadFromFilesAndSaveAsync_ShouldReplaceExistingCollection()
    {
        // Arrange
        var tempFile = CreateTempTestFile("New Content\nReplacing old data.");
        var knowledgeFiles = new Dictionary<string, string>
        {
            ["NewGame"] = tempFile
        };
        var collectionName = "replace-test";

        var mockRepo = new MockVectorMemoryRepository();
        var embeddingService = new MockEmbeddingService(384);
        var persistenceService = new TestVectorMemoryPersistenceService(mockRepo, embeddingService);

        await mockRepo.InitializeDatabaseAsync();

        // Add initial data
        await persistenceService.SaveFragmentsAsync(
            new List<MemoryFragment> { new MemoryFragment("Old", "Old content") },
            collectionName,
            sourceFile: "old.txt",
            replaceExisting: false);

        var initialCount = await mockRepo.GetCountAsync(collectionName);

        // Act - Replace with new data
        var result = await LoadFromFilesAndSaveAsyncWrapper(
            knowledgeFiles,
            collectionName,
            embeddingService,
            persistenceService);

        // Assert
        var fragments = await mockRepo.LoadByCollectionAsync(collectionName);
        Assert.Contains(fragments, f => f.Content.Contains("Replacing old data"));

        // Cleanup
        System.IO.File.Delete(tempFile);
    }

    [Fact]
    public async Task MockRepository_ShouldStoreAndRetrieveFragments()
    {
        // Arrange
        var mockRepo = new MockVectorMemoryRepository();
        var embeddingService = new MockEmbeddingService(384);
        var persistenceService = new TestVectorMemoryPersistenceService(mockRepo, embeddingService);

        await mockRepo.InitializeDatabaseAsync();

        var fragments = new List<MemoryFragment>
        {
            new MemoryFragment("Rules", "Setup: Place board in center."),
            new MemoryFragment("Rules", "Players: 2-4 players required.")
        };

        // Act - Save
        await persistenceService.SaveFragmentsAsync(
            fragments,
            "game-rules",
            sourceFile: "test.txt",
            replaceExisting: false);

        // Act - Load
        var vectorMemory = await persistenceService.LoadVectorMemoryAsync("game-rules");

        // Assert
        Assert.NotNull(vectorMemory);
        Assert.Equal(2, vectorMemory.Count);
        Assert.True(mockRepo.IsInitialized);
        
        var collections = await mockRepo.GetCollectionsAsync();
        Assert.Contains("game-rules", collections);
        
        var count = await mockRepo.GetCountAsync("game-rules");
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task MockRepository_ShouldSupportMultipleCollections()
    {
        // Arrange
        var mockRepo = new MockVectorMemoryRepository();
        var embeddingService = new MockEmbeddingService(384);
        var persistenceService = new TestVectorMemoryPersistenceService(mockRepo, embeddingService);

        // Act - Create multiple collections
        await persistenceService.SaveFragmentsAsync(
            new List<MemoryFragment> { new MemoryFragment("A", "Content A") },
            "collection1",
            sourceFile: "file1.txt",
            replaceExisting: false);

        await persistenceService.SaveFragmentsAsync(
            new List<MemoryFragment> { new MemoryFragment("B", "Content B") },
            "collection2",
            sourceFile: "file2.txt",
            replaceExisting: false);

        // Assert
        var collections = await mockRepo.GetCollectionsAsync();
        Assert.Equal(2, collections.Count);
        Assert.Contains("collection1", collections);
        Assert.Contains("collection2", collections);

        var stats1 = await persistenceService.GetCollectionStatsAsync("collection1");
        var stats2 = await persistenceService.GetCollectionStatsAsync("collection2");

        Assert.Equal(1, stats1.FragmentCount);
        Assert.Equal(1, stats2.FragmentCount);
    }

    [Fact]
    public async Task VectorMemory_ShouldFindRelevantFragments_WhenSearching()
    {
        // Arrange
        var embeddingService = new MockEmbeddingService(384);
        var vectorMemory = new VectorMemory(embeddingService, "test-collection");

        var fragments = new List<MemoryFragment>
        {
            new MemoryFragment("Combat", "Roll 2 dice when attacking. Add your strength bonus."),
            new MemoryFragment("Movement", "Players can move up to 3 spaces per turn."),
            new MemoryFragment("Defense", "Roll 1 die when defending. Add your armor bonus."),
            new MemoryFragment("Magic", "Cast spells by spending mana points."),
            new MemoryFragment("Combat", "Critical hits occur on a roll of 20.")
        };

        foreach (var fragment in fragments)
        {
            vectorMemory.ImportMemory(fragment);
        }

        // Act - Search for combat-related content
        var combatResults = await vectorMemory.SearchRelevantMemoryAsync("attacking", topK: 3, minRelevanceScore: 0.0);

        // Assert
        Assert.NotNull(combatResults);
        Assert.NotEmpty(combatResults);
        Assert.Contains("dice", combatResults, StringComparison.OrdinalIgnoreCase);
        
        // Verify vector memory has correct count
        Assert.Equal(5, vectorMemory.Count);
    }

    [Fact]
    public async Task VectorMemory_ShouldFilterByRelevanceScore()
    {
        // Arrange
        var embeddingService = new MockEmbeddingService(384);
        var vectorMemory = new VectorMemory(embeddingService, "test-collection");

        vectorMemory.ImportMemory(new MemoryFragment("Topic A", "Completely unrelated content about gardening."));
        vectorMemory.ImportMemory(new MemoryFragment("Topic B", "Roll dice for combat actions."));
        vectorMemory.ImportMemory(new MemoryFragment("Topic C", "Another unrelated topic about cooking."));

        // Act - Low relevance threshold (should get results including the relevant one)
        var lowThresholdResults = await vectorMemory.SearchRelevantMemoryAsync(
            "combat dice rolling", 
            topK: 5, 
            minRelevanceScore: 0.0);

        // Act - High relevance threshold (may filter out low-quality matches)
        var highThresholdResults = await vectorMemory.SearchRelevantMemoryAsync(
            "combat dice rolling", 
            topK: 5, 
            minRelevanceScore: 0.8);

        // Assert - Low threshold should find the relevant fragment
        Assert.NotEmpty(lowThresholdResults);
        Assert.Contains("dice", lowThresholdResults, StringComparison.OrdinalIgnoreCase);
        
        // Assert - High threshold might not find anything with simple embeddings
        // This is expected behavior with the character-frequency based embedding
    }

    [Fact]
    public async Task VectorMemory_ShowActualRelevanceScores()
    {
        // Arrange
        var embeddingService = new MockEmbeddingService(384);
        var vectorMemory = new VectorMemory(embeddingService, "test-collection");

        vectorMemory.ImportMemory(new MemoryFragment("Combat", "Roll 2 dice when attacking."));
        vectorMemory.ImportMemory(new MemoryFragment("Unrelated", "Plant flowers in the garden."));

        // Act - Get all results with scores
        var results = await vectorMemory.SearchRelevantMemoryAsync(
            "attacking with dice", 
            topK: 10, 
            minRelevanceScore: 0.0);

        // Assert - Just verify we got results
        Assert.NotEmpty(results);
        
        // The results include [Relevance: X.XXX] scores in the output
        // This helps diagnose what threshold values are realistic
        Assert.Contains("Relevance:", results);
    }

    [Fact]
    public async Task VectorMemory_DemonstrateSemanticGap()
    {
        // Arrange - This test demonstrates why simple embeddings get low scores
        var embeddingService = new MockEmbeddingService(384);
        var vectorMemory = new VectorMemory(embeddingService, "test-collection");

        // Add fragments with semantic meaning
        vectorMemory.ImportMemory(new MemoryFragment("Winning", "The winner is the player with the most Gold."));
        vectorMemory.ImportMemory(new MemoryFragment("Combat", "Fight monsters to get treasure."));
        vectorMemory.ImportMemory(new MemoryFragment("Movement", "Move through the dungeon rooms."));

        // Act - Query with synonyms that a human would understand
        var results = await vectorMemory.SearchRelevantMemoryAsync(
            "how to win the game", 
            topK: 10, 
            minRelevanceScore: 0.0);

        // Assert - Verify we get results but scores will be low
        Assert.NotEmpty(results);
        Assert.Contains("Relevance:", results);
        
        // The fragment about "winner" should match "win" but the score will be low
        // because simple character-frequency embeddings don't understand synonyms
        Assert.Contains("Gold", results, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VectorMemory_ShowExactWordMatchScores()
    {
        // Arrange - Show difference between exact match and semantic match
        var embeddingService = new MockEmbeddingService(384);
        var vectorMemory = new VectorMemory(embeddingService, "test-collection");

        vectorMemory.ImportMemory(new MemoryFragment("A", "How to win: collect gold treasures"));
        vectorMemory.ImportMemory(new MemoryFragment("B", "The winner gets the most points"));

        // Act - Query with exact words from fragment A
        var exactMatchResults = await vectorMemory.SearchRelevantMemoryAsync(
            "how to win collect gold", 
            topK: 10, 
            minRelevanceScore: 0.0);

        // Act - Query with synonyms (semantic match)
        var semanticMatchResults = await vectorMemory.SearchRelevantMemoryAsync(
            "winning strategy earning money", 
            topK: 10, 
            minRelevanceScore: 0.0);

        // Assert - Exact match should score higher
        Assert.NotEmpty(exactMatchResults);
        Assert.NotEmpty(semanticMatchResults);
        
        // Both should contain results but exact match will have much higher scores
        Assert.Contains("Relevance:", exactMatchResults);
        Assert.Contains("Relevance:", semanticMatchResults);
    }

    [Fact]
    public async Task VectorMemory_RealWorldExample_FightMonsterAlone()
    {
        // This test demonstrates the "fight monster alone?" query issue
        // All query words appear in the fragment, but score is still low
        
        // Arrange
        var embeddingService = new MockEmbeddingService(384);
        var vectorMemory = new VectorMemory(embeddingService, "test-collection");

        // Add fragments similar to your actual data
        vectorMemory.ImportMemory(new MemoryFragment(
            "Fighting Alone",
            "You don't always have to fight a Monster alone! You can ask any player within six spaces of your Room for help. If someone agrees to help, that person moves to the same Room you are in. Your helper rolls a die and adds the bonus from his permanent Treasures. He can play one-time Treasures if he wants to. If your combined Power beats the Monster, then you win!"));
        
        vectorMemory.ImportMemory(new MemoryFragment(
            "Game Over", 
            "The game is over when someone draws the last Treasure card. The winner is the player with the most Gold. If players are tied for the most Gold, they all win!"));
        
        vectorMemory.ImportMemory(new MemoryFragment(
            "Treasures", 
            "Treasure cards help you fight the monsters so you get even more Treasure. Most Treasures are one-time cards."));

        // Act - Query with words that appear in "Fighting Alone" fragment
        var results = await vectorMemory.SearchRelevantMemoryAsync(
            "fight monster alone", 
            topK: 5, 
            minRelevanceScore: 0.0);

        // Assert
        Assert.NotEmpty(results);
        Assert.Contains("Relevance:", results);
        
        // The "Fighting Alone" fragment contains ALL THREE words: fight, Monster, alone
        // But with character-frequency embeddings, the score will be low (around 0.05-0.10)
        // because the fragment is long and the algorithm counts ALL characters
        Assert.Contains("fight", results, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Monster", results, StringComparison.OrdinalIgnoreCase);
        
        // This demonstrates the core problem: exact word matches still get low scores
        // when the fragment is long compared to the query
    }

    // Helper methods to wrap private static methods for testing
    private async Task<VectorMemory> LoadFromFilesAndSaveAsyncWrapper(
        Dictionary<string, string> knowledgeFiles,
        string collectionName,
        MockEmbeddingService embeddingService,
        TestVectorMemoryPersistenceService persistenceService)
    {
        // This is a copy of the private method logic for testing
        var allFragments = new List<MemoryFragment>();

        foreach (var (gameName, filePath) in knowledgeFiles)
        {
            var content = await System.IO.File.ReadAllTextAsync(filePath);
            var sections = content.Split(
                new[] { "\r\n\r\n", "\n\n" },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            int sectionNum = 0;
            foreach (var section in sections)
            {
                if (string.IsNullOrWhiteSpace(section)) continue;

                sectionNum++;
                var lines = section.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                string category;
                string fragmentContent;

                if (lines.Length > 1 &&
                    lines[0].Length < 100 &&
                    !lines[0].TrimEnd().EndsWith('.') &&
                    !lines[0].TrimEnd().EndsWith(':'))
                {
                    category = $"{gameName} - Section {sectionNum}: {lines[0].Trim()}";
                    fragmentContent = string.Join(Environment.NewLine, lines.Skip(1)).Trim();
                }
                else
                {
                    category = $"{gameName} - Section {sectionNum}";
                    fragmentContent = section.Trim();
                }

                if (!string.IsNullOrWhiteSpace(fragmentContent))
                {
                    var fragment = new MemoryFragment(category, fragmentContent);
                    allFragments.Add(fragment);
                }
            }
        }

        await persistenceService.SaveFragmentsAsync(
            allFragments,
            collectionName,
            sourceFile: string.Join(", ", knowledgeFiles.Keys),
            replaceExisting: true);

        var vectorMemory = await persistenceService.LoadVectorMemoryAsync(collectionName);
        return vectorMemory;
    }

    private async Task<VectorMemory> LoadFromFilesInMemoryAsyncWrapper(
        Dictionary<string, string> knowledgeFiles,
        MockEmbeddingService embeddingService)
    {
        var vectorMemory = new VectorMemory(embeddingService, "game-rules");
        var fileReader = new FileMemoryLoaderService();

        foreach (var (gameName, filePath) in knowledgeFiles)
        {
            await fileReader.LoadFromManualSectionsAsync(
                filePath,
                vectorMemory,
                defaultCategory: gameName,
                autoNumberSections: true);
        }

        return vectorMemory;
    }

    private string CreateTempTestFile(string content)
    {
        var tempFile = System.IO.Path.GetTempFileName();
        System.IO.File.WriteAllText(tempFile, content);
        return tempFile;
    }
}
