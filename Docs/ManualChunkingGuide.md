# Manual Chunking Guide for Better Vector Search

## Why Manual Chunking is Better

When you control section boundaries with **double newlines**, you get:

? **Complete semantic units** - Full rules, not fragments  
? **Better search accuracy** - Vector embeddings match complete thoughts  
? **No misleading info** - No cut-off sentences mid-rule  
? **Cleaner categories** - Each section has clear meaning  

## Recommended File Format

```
Section Title or First Sentence
Rest of the section content here.
Can span multiple lines.
Keep related information together.

Next Section Title
This is a new section because of the double newline above.
All content in this section stays together.

Another Complete Section
Make each section contain ONE complete topic or rule.
Don't split related information across sections.
```

## Example: Board Game Rules (GOOD ?)

```
Setup
Each player takes 5 cards from the deck. Place the board in the center of the table. Shuffle the resource tokens and place them face-down in a pile.

Turn Structure
On your turn: 1) Draw a card, 2) Play one action, 3) Discard down to 7 cards. You may trade with other players at any time during your turn.

Winning the Game
The first player to collect 10 victory points wins. Victory points are earned by: Building cities (2 points each), Holding the longest road (2 points), Playing victory point cards (1 point each).

Special Rules: Trading
You may trade resources with other players or with the bank. Bank trades require 4:1 ratio (4 of same resource for 1 of any other). Port trades offer better ratios if you control a port settlement.
```

## Example: Board Game Rules (BAD ?)

**Problem: Too much in one section**
```
Game Rules
Setup: Each player takes 5 cards. Turn structure: Draw, play, discard. Winning: Get 10 points. Trading: 4:1 with bank. Combat: Roll dice, higher wins. Movement: 2 spaces per turn. Special abilities: Each character has unique power.
```
*This creates ONE huge chunk that's hard for vector search to match specific questions.*

**Problem: Automatic 500-char chunking**
```
[Chunk 1 - 498 chars]
Setup: Each player takes 5 cards from the deck. Place the board in the center of the table. Shuffle the resource tokens and place them face-down in a pile. Turn Structure: On your turn: 1) Draw a card, 2) Play one action, 3) Discard down to 7 cards. You may trade with other players at any time during your turn. Winning the Game: The first player to collect 10 victory points wins. Victory points are earned by: Building cities (2 points each), Holding the longest r...

[Chunk 2 - 487 chars - OVERLAP]
...longest road (2 points), Playing victory point cards (1 point each). Special Rules: Trading You may trade resources with other players or with the bank. Bank trades require 4:1 ratio (4 of same resource for 1 of any other). Port trades offer better ratios if you control a port settlement. Combat: Roll 2 dice and add your attack value. Defender rolls 1 die and adds defense. Higher total wins. Ties go to defender.
```
*Creates confusing overlaps and splits "Winning the Game" explanation!*

## How to Structure Your Files

### Rule of Thumb
One section = One concept/rule/topic

### Good Section Sizes
- **Minimum**: 1-2 sentences (50+ chars)
- **Optimal**: 1-3 paragraphs (200-800 chars)
- **Maximum**: Avoid sections over 1000 chars (split into multiple sections)

### Category Naming

**Option 1: First line as title (auto-detected)**
```
Setup Instructions
Place the board on the table and shuffle cards.

Turn Actions
Draw, play, discard in that order.
```
? Creates categories: "Setup Instructions", "Turn Actions"

**Option 2: Content-only sections**
```
Place the board on the table and shuffle all cards face-down.

On each turn, draw one card, play one action, then discard to hand limit.
```
? Creates categories: "Game Rules - Section 1", "Game Rules - Section 2"

## Using the Loader

```csharp
// Load with manual sections
var chunksLoaded = await fileReader.LoadFromManualSectionsAsync(
    knowledgeFile, 
    vectorMemory, 
    defaultCategory: "Treasure Hunt Rules",  // Base category name
    autoNumberSections: true);               // Add "Section 1:", "Section 2:", etc.
```

## Testing Your Sections

Use the `/debug` command to see if your sections work well:

```
> /debug how do I win?

=== Relevant Memory Fragments ===
[Relevance: 0.876]
Treasure Hunt Rules - Section 3: Winning the Game
The first player to collect 10 victory points wins. Victory points are earned by...

[Relevance: 0.654]
Treasure Hunt Rules - Section 7: Special Victory Conditions
Alternative win: If you control all treasure locations simultaneously...
=================================
```

**Good signs:**
- High relevance scores (0.7+)
- Complete, coherent information
- Related sections appear together

**Bad signs:**
- Low relevance scores (< 0.5)
- Partial sentences
- Unrelated information in same section

## Converting Existing Files

### From Smart Chunking (500 chars)
**Before:**
```csharp
await fileReader.LoadFromFileWithSmartChunkingAsync(knowledgeFile, vectorMemory, maxChunkSize: 500);
```

**After:**
1. Format your file with double newlines between sections
2. Use manual loading:
```csharp
await fileReader.LoadFromManualSectionsAsync(knowledgeFile, vectorMemory);
```

### From # Header Format
You already have sections marked with `#` - just add double newlines!

**Before:**
```
# Setup
Content here...
# Turn Structure
More content...
```

**After:**
```
# Setup
Content here...

# Turn Structure
More content...
```

Then use:
```csharp
await fileReader.LoadFromManualSectionsAsync(knowledgeFile, vectorMemory);
```

## Pro Tips

1. **Test with `/debug`** - Verify sections make sense
2. **Keep related info together** - Don't split explanations
3. **Use descriptive first lines** - Helps with category detection
4. **Review your sections** - Make sure each is a complete thought
5. **Adjust `minRelevanceScore`** - Higher (0.5-0.6) with good sections

## Comparison

| Aspect | Smart Chunking (500 chars) | Manual Sections (Your Control) |
|--------|---------------------------|-------------------------------|
| **Semantic Boundaries** | ? Often broken | ? Preserved |
| **Complete Information** | ? May be partial | ? Always complete |
| **Misleading Context** | ?? High risk | ? Low risk |
| **Search Accuracy** | ?? Variable | ? Excellent |
| **Setup Effort** | Low (automatic) | Medium (manual formatting) |
| **Quality** | Lower | **Much Higher** |

## Conclusion

For board game rules and knowledge bases, **manual section control is worth the extra effort**. You get much better answers from your LLM because the vector search retrieves complete, accurate information instead of confusing fragments.
