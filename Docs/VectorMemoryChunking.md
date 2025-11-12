# Vector Memory Chunking Strategies

## The Problem

When you load an entire file into a single memory fragment, vector search becomes ineffective:
- ? Query: "How do I attack an enemy?"
- ? Memory: [ENTIRE 50-PAGE RULEBOOK]
- ? Result: AI gets overwhelmed with irrelevant information

## The Solution: Text Chunking

Break large documents into smaller, focused chunks so semantic search can find **exactly** the relevant information.

---

## Chunking Methods Available

### 1. **LoadFromFileAsync** (Original - Section-based)
```csharp
await fileReader.LoadFromFileAsync(filePath, vectorMemory);
```

**Best for:** Well-structured files with `#` headers

**Format:**
```
# Combat Rules
When attacking, roll 2d6...

# Movement Rules
Each turn you can move up to 5 spaces...

# Victory Conditions
First player to 10 points wins...
```

**Result:** 3 memory fragments (one per section)

**Pros:**
- ? Preserves document structure
- ? Good for hierarchical knowledge bases

**Cons:**
- ? Sections might still be too large (multiple pages)
- ? Won't work if file has no `#` headers

---

### 2. **LoadFromFileWithChunkingAsync** (Character-based)
```csharp
await fileReader.LoadFromFileWithChunkingAsync(
    filePath, 
    vectorMemory, 
    maxChunkSize: 500,    // characters per chunk
    overlapSize: 50   // overlap for context
);
```

**Best for:** Unstructured text files, long paragraphs

**Example Input:**
```
Combat in Treasure Hunt involves rolling dice and comparing attack values. 
The attacker rolls 2d6 and adds their attack bonus. The defender rolls 1d6 
and adds their defense. If attack exceeds defense, deal damage equal to the 
difference. Critical hits occur on natural 12s and deal double damage.

Movement is simple. Each character has a movement value printed on their card...
```

**Result with maxChunkSize=200:**
```
Chunk 1: "Combat in Treasure Hunt involves rolling dice and comparing attack 
values. The attacker rolls 2d6 and adds their attack bonus. The defender rolls 
1d6 and adds their defense."

Chunk 2 (with overlap): "...defense. If attack exceeds defense, deal damage equal 
to the difference. Critical hits occur on natural 12s and deal double damage."

Chunk 3: "Movement is simple. Each character has a movement value printed on..."
```

**Features:**
- ? Breaks at sentence boundaries (looks for `.`, `!`, `?`)
- ? Falls back to word boundaries if no sentence found
- ? **Overlap** preserves context between chunks (last 50 chars repeat)
- ? Works on any text file

**Recommended Settings:**
```csharp
// For detailed Q&A (e.g., game rules)
maxChunkSize: 500, overlapSize: 50

// For longer context (e.g., stories, tutorials)
maxChunkSize: 1000, overlapSize: 100

// For short facts (e.g., FAQ)
maxChunkSize: 200, overlapSize: 20
```

---

### 3. **LoadFromFileWithSmartChunkingAsync** (Hybrid - RECOMMENDED ?)
```csharp
await fileReader.LoadFromFileWithSmartChunkingAsync(
  filePath, 
    vectorMemory, 
    maxChunkSize: 500
);
```

**Best for:** Mixed content (headers + long sections)

**How it works:**
1. First, splits file by `#` headers (like method #1)
2. Then, if a section is > maxChunkSize, further chunks it (like method #2)
3. Preserves category names for each chunk

**Example:**

Input file:
```
# Combat Rules
Combat involves rolling dice. The attacker rolls 2d6 and adds their attack 
bonus from equipment and abilities. The defender rolls 1d6 and adds defense. 
Compare totals. If attack is higher, deal damage equal to the difference. 
Critical hits occur on natural 12s and deal double damage. Fumbles occur on 
natural 2s and you drop your weapon.

# Movement
Move up to your speed value.
```

Result:
```
Fragment 1: [Combat Rules_chunk_1]
"Combat involves rolling dice. The attacker rolls 2d6 and adds their attack 
bonus from equipment and abilities. The defender rolls 1d6 and adds defense."

Fragment 2: [Combat Rules_chunk_2]
"Compare totals. If attack is higher, deal damage equal to the difference. 
Critical hits occur on natural 12s and deal double damage. Fumbles occur on 
natural 2s and you drop your weapon."

Fragment 3: [Movement]
"Move up to your speed value."
```

**Why it's the best:**
- ? Respects document structure (keeps headers)
- ? Automatically chunks oversized sections
- ? Small sections stay intact (no unnecessary splitting)
- ? Preserves semantic meaning with category labels

---

## Recommended Usage

### For Game Rulebooks (Multiple Files)
```csharp
var fileReader = new FileMemoryLoaderService();
var vectorMemory = new VectorMemory(embeddingService, "game-rules");

// Load multiple rulebooks with smart chunking
await fileReader.LoadFromFileWithSmartChunkingAsync(
    @"d:\tinyllama\trhunt_rules.txt", 
  vectorMemory, 
    maxChunkSize: 500
);

await fileReader.LoadFromFileWithSmartChunkingAsync(
    @"d:\tinyllama\munchkin-panic-rulebook.txt", 
    vectorMemory, 
    maxChunkSize: 500
);
```

**Why 500 characters?**
- Small enough for focused semantic search
- Large enough to contain complete thoughts
- Works well with most embedding models (384-1536 dimensions)

### For FAQs or Short Facts
```csharp
// Use smaller chunks for precise answers
await fileReader.LoadFromFileWithChunkingAsync(
    @"d:\tinyllama\faq.txt", 
    vectorMemory, 
    maxChunkSize: 200,
 overlapSize: 20
);
```

### For Long Documents (Books, Tutorials)
```csharp
// Use larger chunks for context-heavy content
await fileReader.LoadFromFileWithSmartChunkingAsync(
    @"d:\tinyllama\campaign_guide.txt", 
    vectorMemory, 
    maxChunkSize: 1000
);
```

---

## How Chunking Improves Vector Search

### Without Chunking (Bad ?)
```
User: "How do I attack?"

Memory Fragments:
1. [ENTIRE 10,000-word rulebook]

Vector Search Result:
- Fragment 1: Score 0.65 (contains attack rules + 9,950 other words)

AI Prompt: [ENTIRE RULEBOOK]
AI Response: "Um, there's something about attacking on page 42..."
```

### With Smart Chunking (Good ?)
```
User: "How do I attack?"

Memory Fragments:
1. [Combat Rules_chunk_1]: "Combat involves rolling dice..."
2. [Combat Rules_chunk_2]: "The attacker rolls 2d6..."
3. [Combat Rules_chunk_3]: "Critical hits deal double damage..."
4. [Movement Rules]: "Move up to speed value..."
5. [Victory Conditions]: "First to 10 points wins..."
... (50 more focused chunks)

Vector Search Results:
- Fragment 2: Score 0.92 ? (Perfect match!)
- Fragment 3: Score 0.87 ? (Also relevant)
- Fragment 1: Score 0.78 ? (Good context)

AI Prompt: [Only the 3 combat-related chunks]
AI Response: "To attack, roll 2d6 and add your attack bonus. If your total 
exceeds the defender's roll plus defense, you deal damage equal to the 
difference. Critical hits on natural 12s deal double damage!"
```

---

## Performance Comparison

| Method | Chunks Created | Search Accuracy | Context Quality |
|--------|---------------|-----------------|-----------------|
| No Chunking | 1 | ????? | ????? (too much) |
| Section-based | 5-20 | ????? | ????? |
| Character-based | 50-200 | ????? | ????? |
| **Smart Chunking** | 20-100 | ????? | ????? |

---

## Testing Your Chunks

Use the `/debug` command to see which chunks are being retrieved:

```sh
> /debug How do combat critical hits work?

=== Relevant Memory Fragments ===
[Relevance: 0.924]
[Combat Rules_chunk_3]
Critical hits occur on natural 12s and deal double damage.

[Relevance: 0.856]
[Combat Rules_chunk_2]
The attacker rolls 2d6 and adds their attack bonus. Compare totals.

[Relevance: 0.732]
[Advanced Combat_chunk_1]
Some weapons have critical hit multipliers that stack with base crit damage.
=================================
```

**Good signs:**
- ? High relevance scores (> 0.7)
- ? Multiple related chunks retrieved
- ? Each chunk contains focused, relevant information

**Bad signs:**
- ? Low scores (< 0.5) - chunks might be too large or poorly split
- ? Only 1 chunk retrieved - sections might be too big
- ? Irrelevant chunks with high scores - need better chunking boundaries

---

## Advanced: Custom Chunking Logic

If you need specialized chunking (e.g., by paragraphs, bullet points, or custom delimiters), extend the `FileMemoryLoaderService`:

```csharp
public async Task<int> LoadFromBulletListAsync(string filePath, ILlmMemory memory)
{
    var content = await File.ReadAllTextAsync(filePath);
    var bullets = content.Split(new[] { "\n- ", "\n* ", "\n• " }, 
StringSplitOptions.RemoveEmptyEntries);
    
    int count = 0;
    foreach (var bullet in bullets)
    {
if (!string.IsNullOrWhiteSpace(bullet))
    {
    memory.ImportMemory(new MemoryFragment("BulletPoint", bullet.Trim()));
            count++;
   }
    }
    return count;
}
```

---

## Summary

? **Use `LoadFromFileWithSmartChunkingAsync`** for most scenarios  
? **Set `maxChunkSize: 500`** as a starting point  
? **Test with `/debug`** to verify chunk quality  
? **Adjust chunk size** based on your content type  

This ensures your vector memory provides precise, relevant context to your LLM instead of overwhelming it with entire documents.
