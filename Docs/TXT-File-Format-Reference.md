# TXT File Format Reference

## Overview

The OfflineAI system supports a structured TXT file format for knowledge ingestion. This format uses a simple, human-readable layout with automatic header detection and intelligent chunking.

---

## File Structure

### Basic Layout

```txt
##Domain Name

#Section Header 1
Content for section 1 goes here.
This can span multiple lines and paragraphs.

#Section Header 2
Content for section 2 goes here.

[Page: 5]

#Section Header 3
More content here with page reference.
```

---

## Syntax Rules

### 1. Domain Declaration (Required)

**Format:** `##Domain Name`

- Must be the **first line** of the file
- Starts with `##` (double hash)
- Identifies the knowledge domain/game/product
- Used as the base category for all fragments

**Examples:**
```txt
##Gloomhaven
##iPhone 15 Pro
##Project Alpha Documentation
##Mansion of Madness
```

---

### 2. Section Headers (Optional)

**Format:** `#Header Text`

- Starts with `#` (single hash)
- Creates a new section/topic
- Category becomes: `"Domain - Header Text"`
- Should be descriptive and concise

**Examples:**
```txt
#Setup Instructions
#Combat Rules
#Character Abilities
#Troubleshooting Guide
```

**Alternative: Automatic Header Detection**

If you don't use `#` markers, the system will auto-detect headers based on:
- ? Length < 60 characters
- ? Title Case (50%+ words start with capital)
- ? No sentence-ending punctuation (., !, ?, ,, ;)
- ? Not a bullet point (-, *, •)
- ? No colon followed by space

---

### 3. Page Numbers (Optional)

**Supported Formats:**
```txt
[Page: 5]
[Page 5]
--- Page 5 ---
Page 5
```

Page numbers are:
- Automatically detected
- Added to fragment metadata
- Preserved in the content for reference
- Not treated as section breaks

---

### 4. Content

- Plain text paragraphs
- Bullet lists
- Line breaks preserved
- Empty lines separate paragraphs

---

## Processing Behavior

### Chunking

- **Maximum chunk size:** 1500 characters
- **No minimum enforced** (allows small fragments)
- **Sentence-boundary splitting:** Avoids breaking mid-sentence
- **Multi-part naming:** `"Domain - Section (Part 2)"` for large sections

### Fragment Creation

Each section (or chunk) becomes a separate memory fragment:

| Domain Declaration | Section Header | Resulting Category |
|-------------------|----------------|-------------------|
| `##Gloomhaven` | `#Setup` | `Gloomhaven - Setup` |
| `##Gloomhaven` | `#Combat Rules` | `Gloomhaven - Combat Rules` |
| `##iPhone 15` | None (after domain) | `iPhone 15` |
| `##Project Alpha` | `#API Reference` | `Project Alpha - API Reference` |

---

## Complete Example

```txt
##Gloomhaven

Gloomhaven is a cooperative game of battling monsters and advancing a player's own individual goals. The game is meant for 1 to 4 players and takes approximately 30 minutes per player.

#Setup Instructions

[Page: 4]

Players will take on the role of a wandering mercenary with their own special set of skills and their own reasons for traveling to this remote corner of the world.

Each player chooses a character class and takes the corresponding character mat, miniature, and ability cards.

#Combat Rules

[Page: 12]

During combat, players use action cards to perform attacks and movements. Each card has a top action and a bottom action.

On your turn, you must play two cards:
- Choose the top action from one card
- Choose the bottom action from the other card
- Discard both cards after use

#Monster Behavior

Monsters activate at the end of each round. They follow these priorities:
1. Move toward the nearest enemy
2. Attack if in range
3. Use special abilities when available

[Page: 15]

Monster attack values are modified by the attack modifier deck. Draw one card per attack and apply the modifier shown.
```

### Generated Fragments

This file would create the following fragments:

1. **Category:** `Gloomhaven`  
   **Content:** Game introduction paragraph

2. **Category:** `Gloomhaven - Setup Instructions`  
   **Content:** Setup rules with `[Page: 4]` reference

3. **Category:** `Gloomhaven - Combat Rules`  
   **Content:** Combat mechanics with `[Page: 12]` reference

4. **Category:** `Gloomhaven - Monster Behavior`  
   **Content:** Monster rules with `[Page: 15]` reference

---

## Special Cases

### Comma-Separated Lists

If the domain line contains **2+ commas**, the entire file is treated as a single fragment:

```txt
##Gloomhaven, Jaws of the Lion, Forgotten Circles, Frosthaven

keywords: gloomhaven, gloom haven, gh
related: board game, dungeon crawler, tactical combat
```

This creates **one fragment** with the full content.

---

## Migration Guide

### Old Format (Automatic Detection)
```txt
Gloomhaven
Setup Instructions
Content here...
```

### New Format (Explicit Markers)
```txt
##Gloomhaven

#Setup Instructions
Content here...
```

**Note:** Old format files still work with automatic header detection, but the new format is **recommended** for:
- ? Clearer structure
- ? Guaranteed correct parsing
- ? Better readability
- ? Consistent behavior

---

## Best Practices

### ? DO

- Use `##Domain` on the first line
- Use `#Header` for clear section boundaries
- Keep headers under 60 characters
- Add page numbers for reference
- Use descriptive section names
- Separate paragraphs with blank lines

### ? DON'T

- Start domain names with `#` (use `##`)
- Use sentence punctuation in headers
- Make headers too long (>60 chars)
- Mix formats (stick to either `#` markers or auto-detection)
- Put page numbers in headers

---

## File Placement

Place TXT files in the configured inbox folder:

**Default:** `Knowledge/Inbox/`

Files are automatically:
1. Detected by the system
2. Processed into memory fragments
3. Archived with timestamp to `Knowledge/Archive/`

---

## Related Documentation

- **JSON Format:** See `JSON-File-Format-Reference.md` for structured data
- **PDF Format:** See `PDF-Processing-Guide.md` for PDF handling
- **Domain Management:** See `DomainsManagement.md` for domain configuration

---

## Implementation Reference

See: `Services/Memory/MultiFormatFileWatcher.cs`  
Method: `ProcessTextFileAsync()`  
Chunking: `SplitIntoChunks()`
