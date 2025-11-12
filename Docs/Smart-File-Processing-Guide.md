# Smart File Processing Guide

## ?? Auto-Processing Knowledge Files

OfflineAI now automatically watches for new `.txt` files and vectorizes them with BERT embeddings!

---

## ?? How It Works

### 1. **Folder Structure**

```
d:\tinyllama\
??? inbox\              ? Place new .txt files here
??? archive\            ? Processed files moved here (with timestamp)
??? llama-cli.exe
??? tinyllama-1.1b-chat-v1.0.Q5_K_M.gguf
```

### 2. **Workflow**

```
1. Drop file ? inbox\treasure-hunt.txt
2. Program starts ? Auto-detects new files
3. Vectorizes with BERT ? Semantic embeddings
4. Saves to SQL ? VectorMemoryDB
5. Archives file ? archive\treasure-hunt_20241225-143022.txt
```

---

## ?? Usage

### Startup (Automatic)

When you run the program:

```bash
dotnet run --project OfflineAI
```

**Output:**
```
??????????????????????????????????????????????????????????
?           Smart File Auto-Processing                   ?
??????????????????????????????????????????????????????????

?? Found 2 new file(s) in inbox:
   • treasure-hunt.txt
   • munchkin-rules.txt

?? Processing and vectorizing new files...
  Loading treasure-hunt from d:\tinyllama\inbox\treasure-hunt.txt...
  Collected 45 sections from treasure-hunt
  Loading munchkin-rules from d:\tinyllama\inbox\munchkin-rules.txt...
  Collected 38 sections from munchkin-rules

Total fragments collected: 83

=== Saving to Database with Embeddings ===
? Archived: treasure-hunt.txt ? treasure-hunt_20241225-143022.txt
? Archived: munchkin-rules.txt ? munchkin-rules_20241225-143022.txt

? Successfully processed and archived 2 file(s)
```

### Manual Reload

While chatting, check for new files anytime:

```
> /reload
```

**Output:**
```
?? Checking for new files...
?? Found 1 new file(s)!
  Loading game-expansion from d:\tinyllama\inbox\game-expansion.txt...
  Collected 12 sections from game-expansion

? Reloaded collection with 95 fragments
```

---

## ?? File Format

### Example: `treasure-hunt.txt`

```txt
Game Setup
Players take turns placing treasure tokens on the board.
Each player starts with 3 gold pieces.

Movement Rules
Roll the dice to move your character.
You can move up to the number shown on the dice.

Monster Combat
When you encounter a monster, you must fight it.
Roll 2 dice and add your power value.
If the total exceeds the monster's strength, you win!
```

### Processing Result

```
? Splits into sections (separated by blank lines)
? Creates categories: "treasure-hunt - Section 1: Game Setup"
? Generates BERT embeddings for semantic search
? Saves to SQL: VectorMemoryDB.dbo.MemoryFragments
```

---

## ?? What Gets Vectorized

Each **section** (separated by blank lines) becomes a **vector**:

```csharp
new MemoryFragment(
    Category: "treasure-hunt - Section 3: Monster Combat",
    Content: "When you encounter a monster, you must fight it.
              Roll 2 dice and add your power value.
              If the total exceeds the monster's strength, you win!"
)
```

BERT embedding (384 dimensions):
```
[0.023, -0.145, 0.087, ..., 0.034]  // Semantic representation
```

---

## ?? Semantic Search Example

### Query: "How do I fight a monster?"

**Old (character frequency embeddings):**
```
[Relevance: 0.120] - Weak match ?
[Relevance: 0.117] - Poor similarity ?
```

**New (BERT embeddings):**
```
[Relevance: 0.847] - Excellent match! ?
[Relevance: 0.782] - Strong relevance ?
```

BERT understands:
- "fight" = "combat" = "battle"
- "monster" = "creature" = "enemy"
- Contextual meaning, not just letters!

---

## ?? Database Schema

```sql
CREATE TABLE MemoryFragments (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    Category NVARCHAR(MAX),
    Content NVARCHAR(MAX),
    EmbeddingJson NVARCHAR(MAX),  -- BERT vector [384 floats]
    CollectionName NVARCHAR(255),
    SourceFile NVARCHAR(500),
    CreatedAt DATETIME2
)
```

---

## ?? Available Commands

### During Chat

| Command | Description |
|---------|-------------|
| `/debug <query>` | Show relevant memory fragments with scores |
| `/stats` | Show collection statistics |
| `/collections` | List all collections in database |
| `/pool` | Show model instance pool status |
| `/reload` | Check inbox for new files and process them |
| `exit` | Quit the program |

---

## ?? Configuration

### Change Folder Paths

Edit `OfflineAI/Modes/RunVectorMemoryWithDatabaseMode.cs`:

```csharp
// Smart file processing folders
var inboxFolder = @"d:\tinyllama\inbox";     // ? Change this
var archiveFolder = @"d:\tinyllama\archive"; // ? Change this
```

### Change Collection Name

```csharp
var collectionName = "game-rules"; // ? Change to "board-games" or "rpg-rules"
```

### Change Database

Edit `dbConfig.ConnectionString`:

```csharp
ConnectionString = @"Server=(localdb)\mssqllocaldb;Database=VectorMemoryDB;..."
```

---

## ?? Troubleshooting

### "No new files found"

? Check `inbox` folder exists: `d:\tinyllama\inbox\`
? Files must be `.txt` extension
? Files are at root of inbox (not in subfolders)

### "Database connection failed"

See: `Docs/LocalDB-Setup.md`

```bash
# Fix LocalDB
sqllocaldb stop mssqllocaldb
sqllocaldb delete mssqllocaldb
sqllocaldb create mssqllocaldb
sqllocaldb start mssqllocaldb
```

### "Low relevance scores"

? Using BERT embeddings? (See console output on startup)
? Check query: "How do I fight?" (good) vs "fight" (too vague)
? Check fragments: Are they descriptive enough?

---

## ?? Performance

### BERT Embedding Speed

- **Small file (50 KB):** ~2-3 seconds
- **Medium file (200 KB):** ~8-10 seconds
- **Large file (1 MB):** ~30-40 seconds

### Memory Usage

- **Idle:** ~150 MB
- **Processing file:** +50-100 MB (temporary)
- **Model pool (3 instances):** +3-5 GB

### Database Size

- **100 fragments:** ~2 MB
- **1,000 fragments:** ~15 MB
- **10,000 fragments:** ~150 MB

---

## ?? Related Docs

- `LocalDB-Setup.md` - SQL Server LocalDB configuration
- `VectorMemoryDatabaseGuide.md` - Full database guide
- `SemanticKernelGuide.md` - Semantic Kernel overview

---

## ? Benefits

### 1. **Zero Manual Work**
- Drop files in `inbox\`
- System auto-processes
- Files archived automatically

### 2. **True Semantic Search**
- BERT embeddings understand meaning
- Synonym recognition (win = victory)
- Context-aware relevance

### 3. **Persistent Knowledge**
- Embeddings saved to SQL
- No re-processing on restart
- Fast startup time

### 4. **Multi-Game Support**
- Each file becomes a knowledge source
- Separate categories maintained
- Single database, multiple domains

---

## ?? Next Steps

1. **Test with your files:**
   ```bash
   # Copy a .txt file
   copy rulebook.txt d:\tinyllama\inbox\
   
   # Run program
   dotnet run --project OfflineAI
   ```

2. **Ask semantic questions:**
   ```
   > How do I win the game?
   > What happens when I fight a monster alone?
   > Can I use a helper to defeat enemies?
   ```

3. **Check relevance scores:**
   ```
   > /debug How do I fight a monster?
   ```
   
   Look for scores **> 0.7** (BERT should achieve this!)

---

**Enjoy your automated knowledge base! ??**
