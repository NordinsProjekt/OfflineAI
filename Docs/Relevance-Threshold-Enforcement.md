# Relevance Threshold Enforcement

## Overview

The system now enforces a **minimum relevance threshold of 0.5** for all queries. If no fragments meet this threshold, the AI will **refuse to answer** rather than use its built-in knowledge.

---

## Why This Matters

### Problem (Before)

When you asked a question with no relevant knowledge:

```
> What is the capital of France?
```

**Bad behavior:**
1. Vector search finds no relevant fragments (scores < 0.5)
2. System falls back to returning ALL fragments
3. LLM uses its **built-in training knowledge** to answer
4. Response: "Paris is the capital of France" ? (from LLM's training data, not your documents!)

### Solution (After)

Now with enforcement:

```
> What is the capital of France?
Response: I don't have any relevant information in my knowledge base to answer that question. 
Please make sure your question relates to the loaded documents, or add more knowledge files to the inbox folder.
```

? **AI only answers from YOUR documents**  
? **No hallucinations from training data**  
? **Clear feedback when knowledge is missing**

---

## Technical Implementation

### 1. VectorMemory.SearchRelevantMemoryAsync

**Returns `null`** when no fragments meet threshold:

```csharp
public async Task<string?> SearchRelevantMemoryAsync(
    string query, 
    int topK = 5, 
    double minRelevanceScore = 0.5)
{
    // ... search logic ...
    
    // Calculate similarity scores
    var results = _entries
        .Select(entry => new { Entry = entry, Score = ... })
        .Where(x => x.Score >= minRelevanceScore)
        .OrderByDescending(x => x.Score)
        .Take(topK)
        .ToList();

    // ? Return null if no fragments meet the threshold
    if (results.Count == 0)
    {
        return null;
    }

    // Build result string...
}
```

### 2. AiChatServicePooled.SendMessageAsync

**Aborts** when context is missing:

```csharp
public async Task<string> SendMessageAsync(string question, ...)
{
    // Build system prompt with context
    var systemPromptResult = await BuildSystemPromptAsync(question);
    
    // ? Check if we found any relevant context
    if (systemPromptResult == null)
    {
        return "I don't have any relevant information in my knowledge base to answer that question. " +
               "Please make sure your question relates to the loaded documents, or add more knowledge files to the inbox folder.";
    }

    // Only proceed to LLM if we have relevant context
    using var pooledInstance = await _modelPool.AcquireAsync(cancellationToken);
    var response = await pooledInstance.Process.QueryAsync(systemPromptResult, question);
    
    return response;
}
```

### 3. Debug Command

Shows clear feedback:

```csharp
> /debug What is the capital of France?

=== Relevant Memory Fragments ===
?? No relevant fragments found with relevance >= 0.5
The query does not match any content in the knowledge base.
=================================
```

---

## Behavior Examples

### Example 1: Query with Good Match

```
> How do I fight a monster?
```

**Search results:**
```
[Relevance: 0.847] ? Above threshold!
Treasure Hunt - Section 12: Monster Combat
When you encounter a monster, fight it!
Roll dice and add your power value...
```

**Response:**
```
To fight a monster, roll a die and add your Power from permanent Treasures.
Compare your total to the Monster's Power. If yours is higher, you win!
```

---

### Example 2: Query with No Match

```
> What is the weather today?
```

**Search results:**
```
Best match: [Relevance: 0.089] ? Below 0.5 threshold
```

**Response:**
```
I don't have any relevant information in my knowledge base to answer that question.
Please make sure your question relates to the loaded documents, or add more knowledge files to the inbox folder.
```

---

### Example 3: Query with Weak Match (Filtered Out)

```
> hello
```

**Search results:**
```
Best match: [Relevance: 0.329] ? Below 0.5 threshold
(weak semantic match, filtered out)
```

**Response:**
```
I don't have any relevant information in my knowledge base to answer that question.
Please make sure your question relates to the loaded documents, or add more knowledge files to the inbox folder.
```

---

## Relevance Score Guide

| Score Range | Meaning | Action |
|-------------|---------|--------|
| **0.8 - 1.0** | Excellent match | ? High confidence answer |
| **0.5 - 0.8** | Good match | ? Answer with context |
| **0.3 - 0.5** | Weak match | ? **Refuse (too weak)** |
| **0.0 - 0.3** | Poor/no match | ? **Refuse to answer** |

**Current threshold:** `0.5` (filters out weak matches like 0.329)

---

## Testing the Threshold

### Test 1: On-Topic Query

```bash
> How do I win the game?
```

**Expected:**
- ? Relevance score > 0.5
- ? Answer based on your documents

---

### Test 2: Off-Topic Query

```bash
> What is quantum physics?
```

**Expected:**
- ? Relevance score < 0.5
- ? Refusal message displayed

---

### Test 3: Debug Off-Topic Query

```bash
> /debug What is quantum physics?
```

**Expected output:**
```
=== Relevant Memory Fragments ===
?? No relevant fragments found with relevance >= 0.5
The query does not match any content in the knowledge base.
=================================
```

---

## Benefits

### 1. **No Hallucinations**
- LLM cannot invent answers from training data
- All answers come from YOUR documents

### 2. **Clear Feedback**
- Users know when knowledge is missing
- Prompts to add more documents

### 3. **Quality Control**
- Only high-relevance matches used
- Prevents weak connections

### 4. **Debugging**
- `/debug` command shows why query failed
- See actual relevance scores

---

## Configuration

### Change Threshold

**Lower threshold (0.2)** - More permissive:
```csharp
// In VectorMemory search calls
await vectorMemory.SearchRelevantMemoryAsync(query, topK: 5, minRelevanceScore: 0.2);
```

**Higher threshold (0.5)** - More strict:
```csharp
await vectorMemory.SearchRelevantMemoryAsync(query, topK: 5, minRelevanceScore: 0.5);
```

**Current default:** `0.5` (filters out weak matches)

---

## User Guidance Message

When threshold not met:

```
I don't have any relevant information in my knowledge base to answer that question.
Please make sure your question relates to the loaded documents, or add more knowledge files to the inbox folder.
```

**Suggests:**
1. ? Check your question relates to loaded documents
2. ? Add more knowledge files to `inbox\` folder
3. ? Use `/collections` to see what's loaded

---

## Troubleshooting

### "Getting too many refusals"

**Cause:** Threshold too high (0.5) or poor embeddings

**Solutions:**
1. Lower threshold to 0.4:
   ```csharp
   minRelevanceScore: 0.4
   ```

2. Verify BERT embeddings:
   ````
   ? SemanticEmbeddingService initialized with BERT tokenizer
   ````

3. Check query phrasing:
   - ? "monster" (too vague)
   - ? "How do I fight a monster?" (specific)

---

### "Not refusing when it should"

**Cause:** Threshold too low or weak fragments

**Solutions:**
1. Raise threshold to 0.6:
   ```csharp
   minRelevanceScore: 0.6
   ```

2. Test with `/debug`:
   ````
   > /debug What is Python?
   ````
   Check relevance scores - should be < 0.5

---

### "False positives"

**Cause:** Weak semantic connections

**Example:**
```
Query: "How do I cook pasta?"
Match: [Relevance: 0.312] "Recipe: Monster Stew"
```

**Solutions:**
1. Raise threshold to 0.6
2. Improve document quality (more specific content)
3. Add domain-specific knowledge files

---

## Code Changes Summary

| File | Change | Purpose |
|------|--------|---------|
| `VectorMemory.cs` | Return `null` when no matches | Signal no relevant context |
| `AiChatServicePooled.cs` | Check for `null` context | Abort before LLM call |
| `RunVectorMemoryWithDatabaseMode.cs` | Handle `null` in `/debug` | Show clear feedback |

---

## Related Commands

### Check What's Loaded

```bash
> /collections
```

Shows all knowledge collections in database.

### View Statistics

```bash
> /stats
```

Shows fragment count and embedding status.

### Debug Search

```bash
> /debug <query>
```

Shows relevance scores for each fragment.

---

## Summary

? **Enforces 0.5 minimum relevance threshold**  
? **Refuses to answer when no match found**  
? **Prevents hallucinations from training data**  
? **Provides clear user feedback**  
? **Only uses YOUR document knowledge**

**Your AI assistant now stays truthful to your documents!** ??
