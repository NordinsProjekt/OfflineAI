# Text File Template for RAG Knowledge Base

## ?? Complete Template with Page Numbers

```
[Game Name]

[Page: 1]

[Section Header]
Content for this section...
Can span multiple paragraphs.

More content on same page...

[Page: 2]

Content continues from previous section...
Page numbers can appear mid-section.

[Another Section Header]
New section content here...

[Page: 3]

More content...
```

---

## ?? Real Example - Mansion of Madness

```
Mansion of Madness

[Page: 12]

Insane Condition
If an investigator has suffered Horror—whether faceup or facedown—equal to or exceeding his sanity, that investigator becomes Insane.

When an investigator becomes Insane, he gains an Insane Condition and discards all of his facedown Horror.

[Page: 13]

If an effect causes an investigator to suffer Horror in excess of his sanity, he suffers all Horror from that effect before becoming Insane and discarding his facedown Horror.

Each Insane Condition has a required number of players which is indicated on the bottom-right corner on the back of the card.

Insane Investigators
An investigator cannot reveal the back of his Insane Condition (the side without art) to the other investigators unless an effect specifically allows him to do so.

[Page: 14]

An investigator's Insane Condition can alter how that investigator wins or loses the game. In such a case, the investigator may wish to perform one or more of the rarely used actions.

When an Insane investigator has suffered Horror equal to or exceeding his sanity, that investigator is eliminated.
```

**Result:** 2 fragments
- Fragment 1: "Mansion of Madness - Insane Condition" (pages 12-13)
- Fragment 2: "Mansion of Madness - Insane Investigators" (pages 13-14)

**Each fragment includes:** `[Page: N]` at the start of content

---

## ?? Supported Page Number Formats

The processor recognizes these formats:

### Format 1: Bracketed (Recommended)
```
[Page: 5]       ? Clean, explicit
[Page 5]        ? Also works
```

### Format 2: Dashed
```
--- Page 5 ---  ? PDF-like markers
```

### Format 3: Simple
```
Page 5          ? Must be on its own line
```

---

## ??? Template Structure

```
Line 1:     [Game Name / Domain Name]     ? REQUIRED: Identifies the game
Line 2-N:   [Page: X]                     ? OPTIONAL: Page marker
            [Section Header]               ? Section title (2-4 words)
            Content...                     ? Content paragraphs
            
            [Page: X+1]                   ? New page marker
            More content...
            
            [Next Section Header]
            Content...
```

---

## ? Complete Template

```
Mansion of Madness

[Page: 1]

Game Overview
This is a cooperative game where investigators explore a mansion and solve mysteries.

Setup Phase
Each player chooses an investigator and receives their character card.

[Page: 2]

Place the map tiles according to the scenario guide.

Items and Equipment
An investigator can carry any number of common items. However, an investigator can have only two possessions.

[Page: 3]

Combat Rules
To attack a monster, roll dice equal to your strength value.

Victory Conditions
Investigators win by completing all objectives before the timer runs out.
```

---

## ?? Header Rules (Reminder)

### ? Good Headers
```
Insane Condition        ? Descriptive, Title Case
Combat Rules            ? 2 words, clear
Items and Equipment     ? Multiple words OK
Setup Phase             ? Short and clear
Victory Conditions      ? Descriptive
```

### ? Bad Headers
```
When an investigator becomes Insane          ? Sentence starter "When"
If an investigator has suffered Horror       ? Sentence starter "If"  
The investigator gains a condition.          ? Starts with "The" + period
Items: What You Can Carry                    ? Contains colon
This is a very long header that is too long  ? > 60 characters
```

---

## ?? How Page Numbers Work

### Page Number Tracking
```
[Page: 5]
Section A content...     ? Fragment includes "Page: 5"
More content...

[Page: 6]  
Continues...             ? Still in Section A, now "Page: 6"

Section B
New section starts...    ? New fragment, includes "Page: 6"
```

### Result
- **Fragment 1** (Section A): 
  ```
  [Page: 5]
  
  Section A content...
  More content...
  ```

- **Fragment 2** (Section B):
  ```
  [Page: 6]
  
  New section starts...
  ```

---

## ?? Page Number Best Practices

### 1. Place at Start of Physical Page
```
[Page: 12]

Insane Condition
Content from page 12...
```

### 2. Mid-Section Page Breaks
```
Insane Condition
Content on page 12...

[Page: 13]

Continues on page 13...
```

### 3. One Page Number Per Logical Page
```
[Page: 5]     ? Good

[Page: 5]     ? Don't repeat
[Page: 5]     ? unnecessarily
```

---

## ?? Verification

After processing, your fragments will contain:

```
Category: "Mansion of Madness - Insane Condition"
Content:
[Page: 12]

If an investigator has suffered Horror—whether faceup or facedown—
equal to or exceeding his sanity, that investigator becomes Insane.
...
```

**Console output:**
```
Loading Mansion of Madness from insane.txt...
Collected 2 sections from Mansion of Madness
```

---

## ?? Quick Template (Copy/Paste)

```
[Your Game Name]

[Page: 1]

[Section 1 Header]
Content for section 1...

[Page: 2]

More content...

[Section 2 Header]
Content for section 2...

[Page: 3]

More content...
```

---

## ?? Usage

1. **Copy template above**
2. **Fill in:**
   - Game name (line 1)
   - Page numbers where content appears
   - Section headers (2-4 words, Title Case)
   - Content (paragraphs)
3. **Save as** `[gamename].txt`
4. **Place in** inbox folder (e.g., `d:\tinyllama\inbox\`)
5. **Verify** console shows "Collected N sections"

---

## ?? Expected Results

**Good:**
```
Loading Mansion of Madness from mansion-insane.txt...
Collected 2 sections from Mansion of Madness

Fragments:
1. "Mansion of Madness - Insane Condition" (page 12-13)
2. "Mansion of Madness - Insane Investigators" (page 13-14)
```

**Bad (needs fixing):**
```
Loading Mansion of Madness from mansion-insane.txt...
Collected 8 sections from Mansion of Madness  ? Too many! Fix headers.
```

---

## ?? See Also

- `QUICK_START_TEXT_FORMAT.md` - Quick reference
- `TEXT_FILE_FORMAT_GUIDE.md` - Complete guide
- `SAMPLE_mansion_insane.txt` - Working example
- `TEXT_PROCESSING_FIX.md` - Troubleshooting
