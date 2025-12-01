# Language-Specific Bot Personalities - Usage Guide

## Quick Start

### Creating a Swedish Bot Personality

```csharp
// Example: Swedish Recycling Assistant
var swedishRecyclingBot = new BotPersonalityEntity
{
    PersonalityId = "swedish-recycling-assistant",
    DisplayName = "Återvinningsassistent",
    Description = "Hjälper dig sortera och återvinna rätt",
    SystemPrompt = @"Du är en expert på återvinning och källsortering i Sverige. 
        Svara kort och tydligt på svenska baserat på den information som ges. 
        Om du inte vet svaret, säg att du inte har den informationen.",
    Language = "Swedish",  // ?? Key setting!
    Category = "support",
    Icon = "??",
    EnableRag = true,
    Temperature = 0.5f
};

await _personalityRepository.SaveAsync(swedishRecyclingBot);
```

### Creating an English Bot Personality

```csharp
// Example: English Board Game Rules Expert
var englishRulesBot = new BotPersonalityEntity
{
    PersonalityId = "board-game-rules-expert",
    DisplayName = "Rules Expert",
    Description = "Answers questions about board game rules",
    SystemPrompt = @"You are an expert in board game rules. 
        Provide clear, precise answers based on the rules documentation. 
        Cite specific rule sections when possible.",
    Language = "English",  // ?? Default but explicit
    Category = "games",
    Icon = "??",
    EnableRag = true,
    Temperature = 0.3f
};

await _personalityRepository.SaveAsync(englishRulesBot);
```

## Real-World Examples

### Example 1: Swedish Recycling Questions

**Query:** "Hur sorterar jag gamla adapter?"  
**Bot:** Swedish Recycling Assistant (Language: Swedish)

**Processing:**
```
1. Original query: "Hur sorterar jag gamla adapter?"
2. Language detected: Swedish (from bot personality)
3. Stop words applied: ["hur", "sorterar", "jag", "gamla"]
4. Filtered keywords: "adapter"
5. Database search: Focused on "adapter"
6. Result: High-quality Swedish content about adapter recycling
```

**Response:**
```
"Gamla adapter ska sorteras som elektronikavfall (e-avfall). 
Du kan lämna dem på återvinningscentralen eller i elektronikåtervinningen 
på många butiker. Adapter innehåller värdefulla metaller som kan återvinnas."
```

### Example 2: English Game Rules Questions

**Query:** "How do I win in Munchkin?"  
**Bot:** Rules Expert (Language: English)

**Processing:**
```
1. Original query: "How do I win in Munchkin?"
2. Language detected: English (from bot personality)
3. Important phrase detected: "how to win"
4. Light filtering applied (preserves "how to win")
5. Keywords: "how to win munchkin"
6. Database search: Multi-word phrase search
7. Result: Specific winning condition rules
```

**Response:**
```
"To win in Munchkin, you need to reach Level 10. You can reach Level 10 by:
1. Killing monsters and collecting treasure
2. Playing level-up cards
3. Using special abilities

The first player to reach Level 10 wins immediately."
```

### Example 3: Swedish Multi-Word Queries

**Query:** "Vad är skillnaden mellan glas och metallförpackningar?"  
**Bot:** Swedish Recycling Assistant

**Processing:**
```
1. Original query: "Vad är skillnaden mellan glas och metallförpackningar?"
2. Language: Swedish
3. Stop words removed: "vad", "är", "mellan", "och"
4. Keywords: "skillnaden glas metallförpackningar"
5. Database search: Finds content about both categories
6. Result: Comparative information
```

**Response:**
```
"Glas och metallförpackningar sorteras separat:

GLAS: Färgade och färglösa glasförpackningar (flaskor, burkar)
? Sorteras i glasåtervinning

METALLFÖRPACKNINGAR: Konservburkar, aluminiumfolie, lock
? Sorteras i metallåtervinning

Viktigt: Tvätta förpackningarna och ta bort lock innan återvinning."
```

## Setting Up Personalities in UI

### Blazor Dashboard Setup

```razor
@* PersonalityEditor.razor *@

<EditForm Model="@personality" OnValidSubmit="@HandleSave">
    <div class="form-group">
        <label>Personality ID:</label>
        <InputText @bind-Value="personality.PersonalityId" class="form-control" />
    </div>
    
    <div class="form-group">
        <label>Display Name:</label>
        <InputText @bind-Value="personality.DisplayName" class="form-control" />
    </div>
    
    <div class="form-group">
        <label>Language:</label>
        <InputSelect @bind-Value="personality.Language" class="form-control">
            <option value="English">English</option>
            <option value="Swedish">Swedish (Svenska)</option>
        </InputSelect>
    </div>
    
    <div class="form-group">
        <label>System Prompt:</label>
        <InputTextArea @bind-Value="personality.SystemPrompt" 
                       class="form-control" rows="5" />
    </div>
    
    <div class="form-group">
        <label>Temperature:</label>
        <InputNumber @bind-Value="personality.Temperature" 
                     class="form-control" step="0.1" />
    </div>
    
    <button type="submit" class="btn btn-primary">Save</button>
</EditForm>

@code {
    private BotPersonalityEntity personality = new();
    
    private async Task HandleSave()
    {
        await PersonalityService.SaveAsync(personality);
        NavigationManager.NavigateTo("/personalities");
    }
}
```

## Testing Your Language-Specific Bots

### Test Suite for Swedish Bot

```csharp
[Fact]
public async Task SwedishBot_FiltersSwedishStopWords()
{
    // Arrange
    var personality = new BotPersonalityEntity
    {
        PersonalityId = "test-swedish",
        Language = "Swedish"
    };
    
    var chatService = CreateChatService(personality);
    
    // Act
    var response = await chatService.SendMessageAsync(
        "Hur sorterar jag batterier?",
        ragMode: true,
        personality: personality);
    
    // Assert
    // Should search for "batterier" not "hur sorterar jag batterier"
    Assert.Contains("batteri", response, StringComparison.OrdinalIgnoreCase);
}

[Fact]
public async Task EnglishBot_FiltersEnglishStopWords()
{
    // Arrange
    var personality = new BotPersonalityEntity
    {
        PersonalityId = "test-english",
        Language = "English"
    };
    
    var chatService = CreateChatService(personality);
    
    // Act
    var response = await chatService.SendMessageAsync(
        "How do I play Munchkin?",
        ragMode: true,
        personality: personality);
    
    // Assert
    // Should preserve "how to play" phrase
    Assert.Contains("play", response, StringComparison.OrdinalIgnoreCase);
}
```

## Best Practices

### 1. Match Language to Content

```csharp
// ? DON'T: Swedish bot with English content
var badSetup = new BotPersonalityEntity
{
    DisplayName = "Recycling Helper",
    SystemPrompt = "You are a recycling expert...",  // English
    Language = "Swedish"  // ? Mismatch!
};

// ? DO: Match language
var goodSetup = new BotPersonalityEntity
{
    DisplayName = "Återvinningshjälp",
    SystemPrompt = "Du är en återvinningsexpert...",  // Swedish
    Language = "Swedish"  // ? Match!
};
```

### 2. Set Appropriate Temperature

```csharp
// Information retrieval (recycling, rules, FAQs)
Temperature = 0.3f - 0.5f;  // More deterministic

// Creative tasks (brainstorming, storytelling)
Temperature = 0.7f - 0.9f;  // More creative
```

### 3. Use Clear System Prompts

```csharp
// ? Good Swedish system prompt
SystemPrompt = @"Du är en återvinningsexpert för Sverige. 
    Svara kort och tydligt på svenska. 
    Använd informationen nedan för att svara. 
    Om informationen saknas, säg att du inte vet.";

// ? Good English system prompt
SystemPrompt = @"You are a board game rules expert. 
    Answer clearly and precisely in English. 
    Use the information below to answer. 
    If you don't know, say so.";
```

### 4. Test With Real Queries

```csharp
// Swedish test queries
var swedishTests = new[]
{
    "Hur sorterar jag gamla batterier?",
    "Var kan jag lämna elektronik?",
    "Vad betyder återvinningssymbolen?"
};

// English test queries
var englishTests = new[]
{
    "How do I win the game?",
    "What are the setup instructions?",
    "Can I play this card?"
};
```

## Troubleshooting

### Issue: Swedish Query Returns English Content

**Problem:**
```
Query: "Hur sorterar jag adapter?"
Response: "To recycle adapters, take them to..."  ?
```

**Solution:**
1. Check bot personality language setting
2. Verify database contains Swedish content
3. Ensure collection has Swedish fragments

```csharp
// Verify personality
var personality = await repository.GetByPersonalityIdAsync("swedish-bot");
Console.WriteLine($"Language: {personality.Language}");  // Should be "Swedish"

// Check collection content
var fragments = await repository.LoadByCollectionAsync("recycling");
var swedishFragments = fragments.Where(f => 
    f.Content.Contains("återvinn") || 
    f.Content.Contains("sorterar"));
Console.WriteLine($"Swedish fragments: {swedishFragments.Count()}");
```

### Issue: Important Phrases Get Filtered

**Problem:**
```
Query: "How to win in Munchkin"
Filtered to: "munchkin"  ? (lost "how to win")
```

**Solution:** The system automatically preserves these phrases:
- "how to win", "how to play", "how to setup", etc.
- If you need more, add to `LanguageStopWordsService`:

```csharp
var importantPhrases = new[]
{
    "how to win", "how to play", "how to setup",
    "how to fight",  // Add more as needed
    "winning condition",
    "setup instructions"
};
```

### Issue: Database Migration Didn't Run

**Problem:**
```
Error: Invalid column name 'Language'
```

**Solution:**
1. Ensure `InitializeDatabaseAsync()` is called on startup
2. Check database connection
3. Verify SQL permissions

```csharp
// In Program.cs, ensure this runs:
Task.Run(async () =>
{
    using var scope = app.Services.CreateScope();
    var repository = scope.ServiceProvider.GetService<IBotPersonalityRepository>();
    if (repository != null)
    {
        await repository.InitializeDatabaseAsync();  // ?? This adds the column
        Console.WriteLine("[+] Bot personalities table initialized");
    }
});
```

## Summary

Key points for using language-specific personalities:

1. **Set `Language` property** when creating personalities
2. **Match language** with system prompt and content language
3. **Test thoroughly** with real queries in target language
4. **Use appropriate temperature** for the task type
5. **Monitor query filtering** in logs to verify correct stop words

The system will automatically handle language-specific filtering once the personality is configured correctly!
