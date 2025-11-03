# Vector Memory Query Matching Tests

## Overview

`VectorMemoryQueryMatchingTests.cs` provides **integration tests** that vectorize actual game fragments and test how different queries match against them. These tests help you understand and validate the semantic matching behavior.

---

## Test Structure

### Setup Method
```csharp
SetupTreasureHuntFragments()
```

Creates a realistic game knowledge base with 8 fragments:
1. Setup instructions
2. Monster combat rules
3. Treasure card mechanics
4. Movement rules
5. Winning conditions
6. The Entrance special space
7. Special abilities
8. Game end conditions

---

## Test Categories

### 1. Query Matching Tests (6 tests)

Tests that specific queries return the correct sections:

? **Query_HowToFightMonsters_ShouldMatchMonsterSpace**
- Query: "How do I fight monsters?"
- Expected: Returns "Monster Spaces" section
- Validates: Correct section prioritization

? **Query_WhatAreTreasureCards_ShouldMatchTreasureSection**
- Query: "What are treasure cards for?"
- Expected: Returns "Treasure Cards" section

? **Query_HowToWin_ShouldMatchWinningConditions**
- Query: "How do I win the game?"
- Expected: Returns "Winning the Game" section

? **Query_HowToStart_ShouldMatchSetup**
- Query: "How do we start the game?"
- Expected: Returns "Setup" section

? **Query_HowToMove_ShouldMatchMovementRules**
- Query: "How does movement work?"
- Expected: Returns "Movement Rules" section

? **Query_SpecialPowers_ShouldMatchSpecialAbilities**
- Query: "What special powers can I use?"
- Expected: Returns "Special Abilities" section

### 2. Threshold Behavior Tests (3 tests)

Tests how different thresholds affect results:

? **Query_WithThreshold0_35_ShouldFilterIrrelevant**
- Validates: Threshold 0.35 filters unrelated content
- Shows: What passes vs what's filtered

? **Query_WithThreshold0_50_ShouldBeVeryStrict**
- Validates: Higher threshold (0.50) is more restrictive
- May return null if no strong matches

? **Query_WithThreshold0_30_ShouldBeMorePermissive**
- Validates: Lower threshold returns more results
- Compares: 0.30 vs 0.40 threshold

### 3. Edge Case Query Tests (4 tests)

Tests unusual or problematic queries:

? **Query_VagueQuestion_ShouldReturnSomething**
- Query: "Tell me about the game"
- Expected: Returns something (if above threshold)

? **Query_Irrelevant_ShouldReturnNull**
- Query: "How do I bake a cake?"
- Expected: Returns null (completely off-topic)

? **Query_Greeting_ShouldReturnNull**
- Query: "hello"
- Expected: Returns null (filtered by threshold)

? **Query_SpecificDetail_ShouldMatchCorrectSection**
- Query: "What happens if I roll equal to the monster power?"
- Expected: Returns "Monster Spaces" with specific detail

### 4. Comparative Query Tests (2 tests)

Tests that compare results from different queries:

? **Query_MonsterVsTreasure_DifferentResultsExpected**
- Compares: "How do monsters work?" vs "How do treasure cards work?"
- Validates: Different queries return different sections

? **Query_SimilarPhrasing_ShouldReturnSameSection**
- Compares: "How to defeat monsters?" vs "Fighting creatures in the game?"
- Validates: Similar meaning returns same section

### 5. Multi-Result Tests (3 tests)

Tests that validate multiple results:

? **Query_BroadTopic_ShouldReturnMultipleRelevantSections**
- Query: "What cards are in the game?"
- Expected: Returns both Monster and Treasure sections

? **Query_WithTopK1_ShouldReturnOnlyBestMatch**
- Validates: topK=1 returns exactly 1 result

? **Query_WithTopK5_ShouldReturnMultipleIfRelevant**
- Validates: topK=5 returns 1-5 results based on relevance

---

## Running the Tests

### Run All Vector Matching Tests
```bash
cd OfflineAI.Tests
dotnet test --filter "FullyQualifiedName~VectorMemoryQueryMatchingTests"
```

### Run Specific Category

**Query Matching Tests:**
```bash
dotnet test --filter "FullyQualifiedName~Query_HowTo"
```

**Threshold Tests:**
```bash
dotnet test --filter "FullyQualifiedName~Query_WithThreshold"
```

**Edge Cases:**
```bash
dotnet test --filter "FullyQualifiedName~Query_Irrelevant"
```

**Comparative Tests:**
```bash
dotnet test --filter "FullyQualifiedName~Query_MonsterVsTreasure"
```

### Run Single Test
```bash
dotnet test --filter "FullyQualifiedName~Query_HowToFightMonsters_ShouldMatchMonsterSpace"
```

---

## Understanding Test Output

### Console Output Format

Each test prints its results:

```
=== Query: How do I fight monsters? ===
[Relevance: 0.452]
Monster Spaces
When you land on a Monster space, draw a Monster card and fight! 
Roll the die. If you roll higher than the Monster's Power...

[Relevance: 0.367]
Game End Conditions
The game ends immediately when either: all Monster cards have been defeated...
```

### Interpreting Relevance Scores

| Score | Meaning | Example |
|-------|---------|---------|
| 0.70+ | Strong match | Exact phrase match |
| 0.50-0.70 | Good match | Same topic, different words |
| 0.40-0.50 | Moderate match | Related concepts |
| 0.35-0.40 | Weak match | Weakly related |
| < 0.35 | Filtered out | Not relevant enough |

---

## Creating Your Own Tests

### Template for New Query Test

```csharp
[Fact]
public async Task Query_YourTestName_ShouldMatchExpectedSection()
{
    // Arrange
    await SetupTreasureHuntFragments();
    var query = "Your question here";

    // Act
    var results = await _vectorMemory.SearchRelevantMemoryAsync(
        query, 
        topK: 3, 
        minRelevanceScore: 0.35);

    // Assert
    Assert.NotNull(results);
    Assert.Contains("Expected Section Name", results);
    
    System.Console.WriteLine($"\n=== Query: {query} ===");
    System.Console.WriteLine(results);
}
```

### Template for Threshold Test

```csharp
[Fact]
public async Task Query_YourQuery_WithThreshold_X()
{
    // Arrange
    await SetupTreasureHuntFragments();
    var query = "Your question";

    // Act
    var results = await _vectorMemory.SearchRelevantMemoryAsync(
        query, 
        topK: 5, 
        minRelevanceScore: 0.40);  // Try different thresholds

    // Assert & Output
    if (results != null)
    {
        System.Console.WriteLine($"\n=== Query: {query} (threshold 0.40) ===");
        System.Console.WriteLine(results);
    }
    else
    {
        System.Console.WriteLine("No results above threshold");
    }
}
```

---

## Test Scenarios to Add

### Recommended Additional Tests

1. **Your Actual Game Rules**
   ```csharp
   // Replace SetupTreasureHuntFragments() with your actual rules
   private async Task SetupYourGameFragments()
   {
       var fragments = new[]
       {
           new MemoryFragment("Section 1", "Your content..."),
           // ...
       };
       // ...
   }
   ```

2. **Common User Questions**
   ```csharp
   [Fact]
   public async Task Query_CommonUserQuestion_ShouldWork()
   {
       // Test actual questions users will ask
   }
   ```

3. **Ambiguous Queries**
   ```csharp
   [Fact]
   public async Task Query_Ambiguous_ShouldReturnBestGuess()
   {
       var query = "cards"; // Ambiguous - treasure or monster?
       // Test how system handles ambiguity
   }
   ```

4. **Multi-Word Queries**
   ```csharp
   [Fact]
   public async Task Query_LongQuestion_ShouldStillMatch()
   {
       var query = "Can you please explain to me how the fighting mechanics work when I encounter a monster space?";
       // Test long, natural language queries
   }
   ```

---

## Debugging Failed Tests

### If Query Doesn't Match Expected Section

1. **Check Console Output**
   - Look at actual relevance scores
   - See what sections were returned

2. **Lower Threshold Temporarily**
   ```csharp
   minRelevanceScore: 0.0  // See all matches
   ```

3. **Check Fragment Content**
   - Ensure fragments contain relevant keywords
   - Verify fragment text is clear

4. **Try Rephrasing Query**
   - Use words that appear in the fragment
   - Be more specific or more general

### If Too Many/Few Results

**Too Many Results:**
- Increase threshold: 0.35 ? 0.40
- Reduce topK: 5 ? 3
- Make query more specific

**Too Few Results:**
- Decrease threshold: 0.35 ? 0.30
- Increase topK: 3 ? 5
- Make query more general

---

## Performance Notes

### Test Execution Time

- **First test:** ~20-30 seconds (loads BERT model)
- **Subsequent tests:** ~2-5 seconds each (model cached)
- **Total suite:** ~2-3 minutes (25 tests)

### Memory Usage

- Peak: ~500 MB (during vectorization)
- Steady: ~200 MB (after GC)

---

## Integration with Your Workflow

### Step 1: Test Your Questions

Create tests for questions YOUR users will ask:

```csharp
[Fact]
public async Task Query_ActualUserQuestion1()
{
    var query = "how many players"; // Real question from users
    // ...test it...
}
```

### Step 2: Validate Threshold

Run tests with different thresholds:

```csharp
// Try 0.30, 0.35, 0.40, 0.45
minRelevanceScore: 0.35
```

Find the sweet spot for your content.

### Step 3: Monitor in Production

Compare test results with production queries:
- Log actual query scores
- Compare to test expectations
- Adjust threshold if needed

---

## Example Test Session

```bash
$ dotnet test --filter "VectorMemoryQueryMatchingTests"

Starting test execution...

? Query_HowToFightMonsters_ShouldMatchMonsterSpace
   Output: Relevance: 0.452 - Monster Spaces

? Query_WhatAreTreasureCards_ShouldMatchTreasureSection
   Output: Relevance: 0.478 - Treasure Cards

? Query_Greeting_ShouldReturnNull
   Output: Correctly returned null (greeting filtered out)

? Query_Irrelevant_ShouldReturnNull
   Output: Correctly returned null (no relevant results)

Test Run Successful.
Total tests: 25
     Passed: 25
```

---

## Continuous Testing

### Add to CI/CD Pipeline

```yaml
- name: Run Vector Memory Tests
  run: |
    dotnet test --filter "VectorMemoryQueryMatchingTests" \
      --logger "console;verbosity=detailed"
```

### Run Before Deployment

```bash
# Full test suite
dotnet test

# Just vector matching
dotnet test --filter "VectorMemoryQueryMatching"
```

---

## Conclusion

These tests help you:
- ? Understand how queries match fragments
- ? Validate threshold settings
- ? Test real user questions
- ? Debug matching issues
- ? Document expected behavior

**Add your own tests to build confidence in your semantic search!** ??

---

## Quick Reference

| Task | Command |
|------|---------|
| Run all tests | `dotnet test --filter "VectorMemoryQueryMatching"` |
| Run one test | `dotnet test --filter "Query_HowToFightMonsters"` |
| See output | Tests print to console automatically |
| Debug | Set `minRelevanceScore: 0.0` to see all matches |
| Add test | Copy template, modify query and assertions |

**Happy testing!** ??
