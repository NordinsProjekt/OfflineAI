# How to Add Your Own Vector Matching Tests

## Quick Start

Want to test your own game rules? Follow this guide!

---

## Step 1: Create Your Test Class

```csharp
using Xunit;
using Services;
using MemoryLibrary.Models;
using System.Threading.Tasks;

namespace OfflineAI.Tests.Services;

public class MyGameQueryMatchingTests
{
    private readonly SemanticEmbeddingService _embeddingService;
    private VectorMemory _vectorMemory;

    public MyGameQueryMatchingTests()
    {
        var modelPath = @"d:\tinyllama\models\all-MiniLM-L6-v2\model.onnx";
        _embeddingService = new SemanticEmbeddingService(modelPath, embeddingDimension: 384);
        _vectorMemory = new VectorMemory(_embeddingService, "my_game");
    }

    // Add your setup method here (Step 2)
    // Add your tests here (Step 3)
}
```

---

## Step 2: Add Your Game Fragments

Replace this with YOUR actual game rules:

```csharp
private async Task SetupMyGameFragments()
{
    // YOUR GAME RULES GO HERE
    var fragments = new[]
    {
        new MemoryFragment(
            "Game Setup", 
            "Place the board in the center. Each player chooses a color and takes the matching pieces. Shuffle the deck and deal 5 cards to each player."),
        
        new MemoryFragment(
            "Turn Structure", 
            "On your turn: 1) Draw a card, 2) Play a card, 3) Move your piece, 4) Resolve any effects."),
        
        new MemoryFragment(
            "Winning Condition", 
            "The first player to reach the goal space with 10 points wins immediately."),
        
        // Add more fragments from YOUR game
    };

    // Import and vectorize
    foreach (var fragment in fragments)
    {
        _vectorMemory.ImportMemory(fragment);
    }

    // Initialize embeddings
    await _vectorMemory.SearchRelevantMemoryAsync("init", topK: 1, minRelevanceScore: 0.0);
    
    // Reset for clean testing
    _vectorMemory = new VectorMemory(_embeddingService, "my_game");
    foreach (var fragment in fragments)
    {
        _vectorMemory.ImportMemory(fragment);
    }
    await _vectorMemory.SearchRelevantMemoryAsync("init", topK: 1, minRelevanceScore: 0.0);
}
```

---

## Step 3: Add Your Query Tests

### Test Template

```csharp
[Fact]
public async Task Query_YourQuestionDescription_ShouldMatchExpectedSection()
{
    // Arrange
    await SetupMyGameFragments();
    var query = "Your actual question here";

    // Act
    var results = await _vectorMemory.SearchRelevantMemoryAsync(
        query, 
        topK: 3,           // Get top 3 matches
        minRelevanceScore: 0.35);  // Threshold

    // Assert
    Assert.NotNull(results);  // Should find something
    Assert.Contains("Expected Fragment Name", results);  // Should be in results
    
    // Print for analysis
    System.Console.WriteLine($"\n=== Query: {query} ===");
    System.Console.WriteLine(results);
}
```

---

## Example: Complete Test

```csharp
using Xunit;
using Services;
using MemoryLibrary.Models;
using System.Threading.Tasks;

namespace OfflineAI.Tests.Services;

public class MyCardGameTests
{
    private readonly SemanticEmbeddingService _embeddingService;
    private VectorMemory _vectorMemory;

    public MyCardGameTests()
    {
        var modelPath = @"d:\tinyllama\models\all-MiniLM-L6-v2\model.onnx";
        _embeddingService = new SemanticEmbeddingService(modelPath, embeddingDimension: 384);
        _vectorMemory = new VectorMemory(_embeddingService, "my_card_game");
    }

    private async Task SetupCardGameFragments()
    {
        var fragments = new[]
        {
            new MemoryFragment("Setup", 
                "Shuffle the 52-card deck. Deal 7 cards to each player. Place remaining cards face-down as draw pile."),
            
            new MemoryFragment("Playing Cards", 
                "You can play a card if it matches the top discard pile card in either color or number. Special cards have unique effects."),
            
            new MemoryFragment("Drawing Cards", 
                "If you cannot play a card, draw one card from the draw pile. If it can be played, you may play it immediately."),
            
            new MemoryFragment("Winning", 
                "The first player to get rid of all their cards wins. Yell 'Done!' when playing your last card."),
        };

        foreach (var fragment in fragments)
        {
            _vectorMemory.ImportMemory(fragment);
        }
        
        await _vectorMemory.SearchRelevantMemoryAsync("init", topK: 1, minRelevanceScore: 0.0);
        
        _vectorMemory = new VectorMemory(_embeddingService, "my_card_game");
        foreach (var fragment in fragments)
        {
            _vectorMemory.ImportMemory(fragment);
        }
        await _vectorMemory.SearchRelevantMemoryAsync("init", topK: 1, minRelevanceScore: 0.0);
    }

    [Fact]
    public async Task Query_HowToStart_ShouldMatchSetup()
    {
        // Arrange
        await SetupCardGameFragments();
        var query = "How do we start the game?";

        // Act
        var results = await _vectorMemory.SearchRelevantMemoryAsync(query, topK: 2, minRelevanceScore: 0.35);

        // Assert
        Assert.NotNull(results);
        Assert.Contains("Setup", results);
        
        System.Console.WriteLine($"\n=== Query: {query} ===");
        System.Console.WriteLine(results);
    }

    [Fact]
    public async Task Query_WhenToDrawCard_ShouldMatchDrawingRules()
    {
        // Arrange
        await SetupCardGameFragments();
        var query = "When do I need to draw a card?";

        // Act
        var results = await _vectorMemory.SearchRelevantMemoryAsync(query, topK: 2, minRelevanceScore: 0.35);

        // Assert
        Assert.NotNull(results);
        Assert.Contains("Drawing Cards", results);
        
        System.Console.WriteLine($"\n=== Query: {query} ===");
        System.Console.WriteLine(results);
    }

    [Fact]
    public async Task Query_HowToWin_ShouldMatchWinningCondition()
    {
        // Arrange
        await SetupCardGameFragments();
        var query = "How do I win?";

        // Act
        var results = await _vectorMemory.SearchRelevantMemoryAsync(query, topK: 1, minRelevanceScore: 0.35);

        // Assert
        Assert.NotNull(results);
        Assert.Contains("Winning", results);
        
        System.Console.WriteLine($"\n=== Query: {query} ===");
        System.Console.WriteLine(results);
    }

    [Fact]
    public async Task Query_InvalidCard_ShouldReturnDrawingRule()
    {
        // Arrange
        await SetupCardGameFragments();
        var query = "What if I can't play any cards?";

        // Act
        var results = await _vectorMemory.SearchRelevantMemoryAsync(query, topK: 2, minRelevanceScore: 0.35);

        // Assert
        Assert.NotNull(results);
        Assert.Contains("Drawing Cards", results);
        
        System.Console.WriteLine($"\n=== Query: {query} ===");
        System.Console.WriteLine(results);
    }
}
```

---

## Running Your Tests

```bash
cd OfflineAI.Tests

# Run all your tests
dotnet test --filter "MyCardGameTests"

# Run specific test
dotnet test --filter "Query_HowToStart"
```

---

## Tips for Writing Good Tests

### 1. Use Real User Questions

? **Bad:** `"section 2 content"`  
? **Good:** `"How do I start the game?"`

### 2. Test Different Phrasings

```csharp
[Fact] public async Task Query_HowToWin_Phrasing1()
    { var query = "How do I win?"; }

[Fact] public async Task Query_HowToWin_Phrasing2()
    { var query = "What are the winning conditions?"; }

[Fact] public async Task Query_HowToWin_Phrasing3()
    { var query = "When does the game end?"; }
```

### 3. Test Edge Cases

```csharp
[Fact]
public async Task Query_VagueQuestion_ShouldReturnSomething()
{
    var query = "cards";  // Very vague
    // Test how system handles it
}

[Fact]
public async Task Query_Typo_ShouldStillMatch()
{
    var query = "How too win?";  // Typo
    // BERT is somewhat typo-tolerant
}

[Fact]
public async Task Query_Irrelevant_ShouldReturnNull()
{
    var query = "weather forecast";
    var results = await _vectorMemory.SearchRelevantMemoryAsync(query, topK: 3, minRelevanceScore: 0.35);
    Assert.Null(results);
}
```

### 4. Test Threshold Boundaries

```csharp
[Fact]
public async Task Query_WithDifferentThresholds()
{
    await SetupMyGameFragments();
    var query = "game rules";

    // Test multiple thresholds
    var results030 = await _vectorMemory.SearchRelevantMemoryAsync(query, topK: 5, minRelevanceScore: 0.30);
    var results035 = await _vectorMemory.SearchRelevantMemoryAsync(query, topK: 5, minRelevanceScore: 0.35);
    var results040 = await _vectorMemory.SearchRelevantMemoryAsync(query, topK: 5, minRelevanceScore: 0.40);

    // Compare counts
    var count030 = results030?.Split("Relevance:").Length - 1 ?? 0;
    var count035 = results035?.Split("Relevance:").Length - 1 ?? 0;
    var count040 = results040?.Split("Relevance:").Length - 1 ?? 0;

    System.Console.WriteLine($"0.30: {count030} results");
    System.Console.WriteLine($"0.35: {count035} results");
    System.Console.WriteLine($"0.40: {count040} results");
}
```

---

## Common Patterns

### Pattern 1: Test Question ? Expected Section

```csharp
[Theory]
[InlineData("How to start?", "Setup")]
[InlineData("How to win?", "Winning")]
[InlineData("When to draw?", "Drawing Cards")]
public async Task Query_ShouldMatchExpectedSection(string query, string expectedSection)
{
    await SetupMyGameFragments();
    
    var results = await _vectorMemory.SearchRelevantMemoryAsync(query, topK: 1, minRelevanceScore: 0.35);
    
    Assert.NotNull(results);
    Assert.Contains(expectedSection, results);
}
```

### Pattern 2: Compare Two Queries

```csharp
[Fact]
public async Task Query_SimilarQuestions_ShouldReturnSameSection()
{
    await SetupMyGameFragments();
    
    var query1 = "How to win the game?";
    var query2 = "What are the victory conditions?";
    
    var results1 = await _vectorMemory.SearchRelevantMemoryAsync(query1, topK: 1, minRelevanceScore: 0.35);
    var results2 = await _vectorMemory.SearchRelevantMemoryAsync(query2, topK: 1, minRelevanceScore: 0.35);
    
    // Both should mention winning
    Assert.Contains("Winning", results1 ?? "");
    Assert.Contains("Winning", results2 ?? "");
}
```

### Pattern 3: Test Top Results Order

```csharp
[Fact]
public async Task Query_ShouldPrioritizeMostRelevant()
{
    await SetupMyGameFragments();
    var query = "What cards can I play?";
    
    var results = await _vectorMemory.SearchRelevantMemoryAsync(query, topK: 3, minRelevanceScore: 0.0);
    
    // First result should be most relevant
    // Extract relevance scores and verify ordering
    System.Console.WriteLine(results);
}
```

---

## Troubleshooting

### Test Fails: "Results is null"

**Cause:** No fragments above threshold

**Fix:**
1. Lower threshold: `0.35` ? `0.30`
2. Check fragment content has relevant keywords
3. Rephrase query to match fragment words

### Test Fails: Wrong section returned

**Cause:** Another section has higher similarity

**Fix:**
1. Check console output to see actual scores
2. Add more specific content to expected fragment
3. Make query more specific

### Test is Slow

**Cause:** First test loads BERT model (~20s)

**Fix:**
- Normal! Subsequent tests are fast (~2-5s)
- Model is cached after first load

---

## Best Practices

? **DO:**
- Test actual user questions
- Test with your real game fragments
- Print results to console for analysis
- Test multiple phrasings of same question
- Test edge cases (vague, irrelevant, typos)

? **DON'T:**
- Use artificial test data
- Expect perfect scores (0.70+ is rare)
- Set threshold too high (> 0.45)
- Test without printing output
- Assume all queries will match

---

## Next Steps

1. ? Copy the example code
2. ? Replace fragments with YOUR game rules
3. ? Replace queries with YOUR user questions
4. ? Run tests: `dotnet test --filter "YourTestClass"`
5. ? Analyze console output
6. ? Adjust threshold based on results

**Now go test your game! ??**
