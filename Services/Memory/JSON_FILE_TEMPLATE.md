# JSON File Template for RAG Knowledge Base

## ?? Simplified JSON Template

```json
{
  "domainName": "Your Game Name",
  "sourceFile": "Rules Reference.pdf",
  "sections": [
    {
      "heading": "Section Heading",
      "pageNumber": 1,
      "content": "Content for this section..."
    },
    {
      "heading": "Another Section",
      "pageNumber": 2,
      "content": "More content here..."
    }
  ]
}
```

---

## ??? Structure Overview

```
Top Level:
?? domainName    (required) - Game/Domain name
?? sourceFile    (required) - Source document name
?? sections      (required) - Array of sections
   ?? heading    (required) - Section heading
   ?? pageNumber (optional) - Page number
   ?? content    (required) - Section content
```

---

## ?? Real Example - Mansion of Madness

```json
{
  "domainName": "Mansion of Madness",
  "sourceFile": "Rules Reference.pdf",
  "sections": [
    {
      "heading": "Insane Condition",
      "pageNumber": 12,
      "content": "If an investigator has suffered Horror—whether faceup or facedown—equal to or exceeding his sanity, that investigator becomes Insane.\n\nWhen an investigator becomes Insane, he gains an Insane Condition and discards all of his facedown Horror."
    },
    {
      "heading": "Insane Investigators",
      "pageNumber": 13,
      "content": "An investigator cannot reveal the back of his Insane Condition to other investigators.\n\nWhen an Insane investigator has suffered Horror equal to or exceeding his sanity, that investigator is eliminated."
    }
  ]
}
```

**Result:** 2 fragments
- Fragment 1: "Mansion of Madness - Insane Condition"
- Fragment 2: "Mansion of Madness - Insane Investigators"

**Each fragment includes:**
```
[Source: Rules Reference.pdf]
[Page: 12]

Content...
```

---

## ?? Required Fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `domainName` | string | ? Yes | Game/Domain name (e.g., "Mansion of Madness") |
| `sourceFile` | string | ? Yes | Source document (e.g., "Rules Reference.pdf") |
| `sections` | array | ? Yes | Array of section objects |
| `sections[].heading` | string | ? Yes | Section heading |
| `sections[].pageNumber` | number | ? Optional | Page number |
| `sections[].content` | string | ? Yes | Section content |

---

## ? Minimal Valid JSON

```json
{
  "domainName": "My Game",
  "sourceFile": "Rulebook.pdf",
  "sections": [
    {
      "heading": "Setup",
      "content": "Place the game board in the center of the table."
    }
  ]
}
```

---

## ?? Complete Example

```json
{
  "domainName": "Mansion of Madness",
  "sourceFile": "Core Rulebook.pdf",
  "sections": [
    {
      "heading": "Game Overview",
      "pageNumber": 1,
      "content": "This is a cooperative game where investigators explore a mansion and solve mysteries."
    },
    {
      "heading": "Setup Phase",
      "pageNumber": 2,
      "content": "Each player chooses an investigator and receives their character card.\n\nPlace the map tiles according to the scenario guide."
    },
    {
      "heading": "Items and Equipment",
      "pageNumber": 3,
      "content": "An investigator can carry any number of common items. However, an investigator can have only two possessions."
    },
    {
      "heading": "Combat Rules",
      "pageNumber": 5,
      "content": "To attack a monster, roll dice equal to your strength value.\n\nEach damage result deals one damage to the monster."
    },
    {
      "heading": "Victory Conditions",
      "pageNumber": 8,
      "content": "Investigators win by completing all objectives before the timer runs out."
    }
  ]
}
```

**Result:** 5 fragments with source tracking

---

## ?? Key Features

### 1. Source Tracking
```json
"sourceFile": "Rules Reference.pdf"
```
**Appears in fragment:**
```
[Source: Rules Reference.pdf]
[Page: 12]

Content...
```

### 2. Domain Organization
```json
"domainName": "Mansion of Madness"
```
**Creates category:**
```
"Mansion of Madness - Insane Condition"
```

### 3. Page Numbers
```json
"pageNumber": 12
```
**Prepended to content:**
```
[Page: 12]

Content...
```

---

## ?? Quick Template (Copy/Paste)

```json
{
  "domainName": "Your Game Name",
  "sourceFile": "Rulebook.pdf",
  "sections": [
    {
      "heading": "Section 1",
      "pageNumber": 1,
      "content": "Content for section 1..."
    },
    {
      "heading": "Section 2",
      "pageNumber": 2,
      "content": "Content for section 2..."
    }
  ]
}
```

---

## ?? Multi-line Content

Use `\n` for line breaks:

```json
{
  "heading": "Combat",
  "content": "Paragraph 1\n\nParagraph 2 with line break\n\nParagraph 3"
}
```

---

## ? Expected Results

### Console Output
```
Loading Mansion of Madness (Rules Reference.pdf) from mansion-rules.json...
Collected 2 sections from Mansion of Madness
```

### Fragment Output
```
Category: "Mansion of Madness - Insane Condition"
Content:
[Source: Rules Reference.pdf]
[Page: 12]

If an investigator has suffered Horror—whether faceup or facedown—
equal to or exceeding his sanity, that investigator becomes Insane.
```

---

## ?? Validation & Error Handling

### Valid JSON ?
```json
{
  "domainName": "My Game",
  "sourceFile": "Rulebook.pdf",
  "sections": [
    {
      "heading": "Setup",
      "content": "Setup instructions..."
    }
  ]
}
```

### Missing Required Field ?
```json
{
  "domainName": "My Game",
  "sections": [...]
}
```
**Error:** "JSON file must contain 'sourceFile' property"

### Empty Domain Name ?
```json
{
  "domainName": "",
  "sourceFile": "Rulebook.pdf",
  "sections": [...]
}
```
**Error:** "'domainName' property cannot be empty"

---

## ?? Usage

1. **Copy template** above
2. **Edit JSON:**
   - Set `domainName` (game name)
   - Set `sourceFile` (document name)
   - Add sections with `heading`, `content`, optional `pageNumber`
   - Use `\n` for line breaks
3. **Validate JSON** (jsonlint.com or VS Code)
4. **Save as** `[gamename].json`
5. **Place in** inbox folder (e.g., `d:\tinyllama\inbox\`)
6. **Process** automatically
7. **Verify** console output

---

## ??? Tools

### Validation
- **Online:** https://jsonlint.com/
- **VS Code:** Built-in JSON validation
- **Schema:** Use `game-knowledge-schema.json`

### Auto-completion (VS Code)
Add schema reference:
```json
{
  "$schema": "./game-knowledge-schema.json",
  "domainName": "...",
  ...
}
```

---

## ?? See Also

- `JSON_QUICK_START.md` - Quick reference
- `game-knowledge-schema.json` - Validation schema
- `SAMPLE_mansion_insane.json` - Simple example
- `SAMPLE_mansion_complete.json` - Complex example
- `JSON_VS_TEXT_COMPARISON.md` - Format comparison
