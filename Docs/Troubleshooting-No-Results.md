# Troubleshooting Guide: No Search Results

## Current Symptoms

BERT model is working (embeddings generated), but **all queries return no results**.

```
> fight monster
[DEBUG] BERT output shape: [1, 128, 384]
Response: I don't have any relevant information...
```

## Possible Causes

### 1. Database is Empty

**Check:**
```sql
SELECT COUNT(*) FROM MemoryFragments WHERE CollectionName = 'game-rules'
```

**Expected:** > 0 fragments

**Fix if empty:**
```
1. Add .txt files to: d:\tinyllama\inbox\
2. Restart program (auto-processes files)
3. Or use /reload command
```

---

### 2. Embeddings Not in Database

**Check:**
```sql
SELECT COUNT(*) FROM MemoryFragments 
WHERE CollectionName = 'game-rules' 
AND EmbeddingJson IS NOT NULL
```

**Expected:** Same count as total fragments

**Fix if null:**
- Database has fragments but no embeddings
- Need to regenerate embeddings with BERT
- Delete collection and reload files

---

### 3. Embeddings Using Old Format

**Check:**
- Were embeddings generated with old statistical method?
- Database created before BERT implementation?

**Fix:**
```sql
-- Delete old collection
DELETE FROM MemoryFragments WHERE CollectionName = 'game-rules'

-- Reload files (program will use BERT)
-- Place files in inbox folder and restart
```

---

### 4. Threshold Too High (0.5)

**Check debug output:**
```
[DEBUG] Top scores:
  0.635 - Treasure Hunt - Section 12
  0.421 - Treasure Hunt - Section 5
  0.389 - Treasure Hunt - Section 11
```

**If all scores < 0.5:**
- BERT might be working but scores naturally lower
- Try threshold = 0.3

**Fix:** In `AiChatServicePooled.cs`:
```csharp
await vectorMemory.SearchRelevantMemoryAsync(
    question,
    topK: 5,
    minRelevanceScore: 0.3); // Lowered from 0.5
```

---

## Debug Commands

### Check What's Loaded

```
> /stats
Collection: game-rules
Fragments: 45
Has Embeddings: True
In-Memory Count: 45
```

**If count = 0:** Database is empty, add files to inbox

---

### Check Collections

```
> /collections
=== Available Collections (1) ===
  game-rules: 45 fragments
```

**If no collections:** No data loaded

---

### Debug Search

```
> /debug fight monster
[DEBUG] Searching 45 fragments for: 'fight monster'
[DEBUG] Top scores:
  0.635 - Treasure Hunt - Section 12
  0.589 - Treasure Hunt - Section 13
  0.521 - Treasure Hunt - Section 8
```

**What to look for:**
- Fragment count should be > 0
- Top scores should show actual numbers
- At least one score should be > 0.5

---

## Quick Fixes

### Fix 1: Reload Data with BERT Embeddings

```powershell
# 1. Clear old data
dotnet run --project OfflineAI

# In program:
> exit

# 2. Delete database (start fresh)
# Run this SQL:
DROP TABLE IF EXISTS MemoryFragments

# 3. Add knowledge files
copy d:\tinyllama\trhunt_rules.txt d:\tinyllama\inbox\

# 4. Restart program (auto-processes with BERT)
dotnet run --project OfflineAI
```

---

### Fix 2: Lower Threshold Temporarily

In `Services/AiChatServicePooled.cs`, line ~78:

```csharp
// Change from 0.5 to 0.3
minRelevanceScore: 0.3
```

In `OfflineAI/Modes/RunVectorMemoryWithDatabaseMode.cs`, line ~144:

```csharp
// Change from 0.5 to 0.3
await vectorMemory.SearchRelevantMemoryAsync(query, topK: 5, minRelevanceScore: 0.3);
```

---

### Fix 3: Verify BERT Model is Used

Check startup output:
```
? REAL BERT embeddings initialized!
   Model: model.onnx
   Embedding dimension: 384
   This will provide TRUE semantic understanding!
```

**If missing:** Model not loaded, check path:
```
d:\tinyllama\models\all-MiniLM-L6-v2\model.onnx
```

---

## Run Diagnostics

```sh
dotnet run --project OfflineAI -- --diagnose-bert
```

**Expected output:**
```
Similarity: 'hello' vs 'Monster cards' = 0.084
Similarity: 'fight monster' vs 'Monster cards' = 0.635

? BERT embeddings working correctly!
```

**If diagnostics fail:** BERT model not working

---

## Most Likely Issue

Based on your symptoms (BERT working but no results), the most likely cause is:

**Database has fragments with OLD statistical embeddings, not BERT embeddings**

### Solution:
```sql
-- Clear old data
DELETE FROM MemoryFragments WHERE CollectionName = 'game-rules'
```

Then reload files:
```
1. Copy file to inbox: d:\tinyllama\inbox\trhunt_rules.txt
2. Restart program
3. Files will be processed with BERT embeddings
```

---

## Expected Behavior After Fix

```
> fight monster
[DEBUG] Searching 45 fragments for: 'fight monster'
[DEBUG] BERT output shape: [1, 128, 384]
[DEBUG] Top scores:
  0.635 - Treasure Hunt - Section 12: Monster Combat
  0.589 - Treasure Hunt - Section 13: Fighting Rules
  0.521 - Treasure Hunt - Section 8: Monster Spaces
[DEBUG] 3 results above threshold 0.5

Response: To fight a monster, roll a die and add your Power...
```

---

## Next Steps

1. Run program with new debug output
2. Check what debug messages appear
3. Report back what you see
4. We'll fix the specific issue

**Run this:**
```sh
dotnet run --project OfflineAI
```

Then try:
```
> /stats
> fight monster
```

Post the debug output here!
