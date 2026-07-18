# RAG Context Templates for OfflineAI API

## Overview

The OfflineAI API supports three ways to provide context for Retrieval-Augmented Generation (RAG):

1. **Manual Context** - You provide pre-formatted context directly
2. **Auto Vector Search** - API searches the vector database automatically  
3. **No RAG** - Direct LLM query without additional context

## Manual Context Templates

When using `enableRag: true` with a `context` field, follow these templates for best results.

### Game Rules Template

```json
{
  "question": "How do I win the game?",
  "context": "In [GAME NAME], the objective is [OBJECTIVE]. Players [BASIC MECHANICS]. [SPECIAL RULES]. The game ends when [END CONDITION].",
  "enableRag": true
}
```

**Example - Monopoly:**
```json
{
  "question": "How do I win in Monopoly?",
  "context": "In Monopoly, the objective is to bankrupt all other players. Players move around the board by rolling two dice. When landing on an unowned property, a player may buy it from the bank. If another player owns the property, rent must be paid. Players can build houses and hotels on their properties to increase rent. The game ends when all but one player has gone bankrupt.",
  "enableRag": true,
  "maxTokens": 512,
  "temperature": 0.3
}
```

**Example - Chess:**
```json
{
  "question": "How does the knight move?",
  "context": "The knight moves in an L-shape: two squares in one direction and then one square perpendicular, or vice versa. Knights are the only pieces that can jump over other pieces. A knight on d4 can move to c2, e2, f3, f5, e6, c6, b5, or b3.",
  "enableRag": true,
  "maxTokens": 256,
  "temperature": 0.2
}
```

### Product Documentation Template

```json
{
  "question": "How do I reset the device?",
  "context": "Product: [PRODUCT NAME]\nVersion: [VERSION]\n\nTo reset:\n1. [STEP 1]\n2. [STEP 2]\n3. [STEP 3]\n\nWarning: [WARNINGS]\nNote: [ADDITIONAL INFO]",
  "enableRag": true
}
```

**Example:**
```json
{
  "question": "How do I factory reset my router?",
  "context": "Product: HomeNet Router X500\nVersion: Firmware 2.1.4\n\nTo factory reset:\n1. Locate the reset button on the back of the router\n2. Press and hold the reset button for 10 seconds while the router is powered on\n3. Release when all lights flash simultaneously\n4. Wait 2-3 minutes for the router to restart\n\nWarning: This will erase all custom settings including Wi-Fi passwords.\nNote: Default admin credentials are: admin/admin",
  "enableRag": true,
  "maxTokens": 400,
  "temperature": 0.2
}
```

### Customer Support Template

```json
{
  "question": "How do I solve this issue?",
  "context": "Issue: [ISSUE DESCRIPTION]\n\nCommon Causes:\n- [CAUSE 1]\n- [CAUSE 2]\n\nSolution Steps:\n1. [STEP 1]\n2. [STEP 2]\n\nIf problem persists: [ESCALATION]",
  "enableRag": true
}
```

### Multi-Document Context Template

When combining multiple relevant passages:

```json
{
  "question": "What are the rules for both games?",
  "context": "[Document 1: Chess]\n[CHESS RULES]\n\n---\n\n[Document 2: Checkers]\n[CHECKERS RULES]",
  "enableRag": true
}
```

**Example:**
```json
{
  "question": "Can pieces move backwards in chess or checkers?",
  "context": "[Chess Rules]\nIn chess, most pieces can move in multiple directions. Pawns can only move forward, never backwards. Knights, bishops, rooks, queens, and kings can move backwards.\n\n---\n\n[Checkers Rules]\nIn checkers, regular pieces can only move forward diagonally. Once a piece reaches the opposite end and becomes a king, it can then move both forward and backward diagonally.",
  "enableRag": true,
  "maxTokens": 400,
  "temperature": 0.3
}
```

## Auto Vector Search

Instead of providing context manually, let the API search automatically:

### Basic Auto Search

```json
{
  "question": "How does castling work?",
  "enableRag": true,
  "topK": 3,
  "minRelevanceScore": 0.5
}
```

### Domain-Filtered Search

```json
{
  "question": "What happens when I land on Free Parking?",
  "enableRag": true,
  "domainFilter": ["monopoly"],
  "topK": 5,
  "minRelevanceScore": 0.6
}
```

### Multi-Domain Search

```json
{
  "question": "Can pieces jump over others?",
  "enableRag": true,
  "domainFilter": ["chess", "checkers", "chinese-checkers"],
  "topK": 4,
  "minRelevanceScore": 0.5
}
```

## Context Formatting Best Practices

### Length Guidelines

| Context Type | Recommended Length | Maximum |
|--------------|-------------------|---------|
| Single Rule | 200-500 chars | 1000 chars |
| Multiple Rules | 500-1000 chars | 2000 chars |
| Full Documentation | 1000-1500 chars | 3000 chars |

**Why limit context?**
- Faster inference
- More focused answers
- Lower token costs
- Better model performance

### Structure Guidelines

1. **Start with key information**
   ```
   "In Monopoly, the objective is to bankrupt opponents..."
   ```

2. **Use clear sections**
   ```
   "Objective: [GOAL]
    Basic Rules: [RULES]
    Special Cases: [EXCEPTIONS]"
   ```

3. **Separate distinct topics**
   ```
   "Chess knight movement: [INFO]
    
    ---
    
    Chess bishop movement: [INFO]"
   ```

4. **Include context markers**
   ```
   "[Game: Monopoly] [Topic: Property Purchase]
    When you land on..."
   ```

## Domain ID Reference

Common domain IDs for filtering vector searches:

### Board Games
- `monopoly` - Monopoly board game
- `chess` - Chess
- `checkers` - Checkers/Draughts
- `scrabble` - Scrabble word game
- `risk` - Risk strategy game
- `clue` - Clue/Cluedo mystery game

### Card Games
- `poker` - Poker variants
- `uno` - UNO card game
- `bridge` - Contract Bridge
- `hearts` - Hearts card game
- `spades` - Spades card game

### Other Categories
- `support-docs` - Customer support documentation
- `product-manuals` - Product user manuals
- `faq` - Frequently asked questions
- `troubleshooting` - Technical troubleshooting guides

## Parameter Tuning

### Temperature Settings

| Use Case | Temperature | Reasoning |
|----------|-------------|-----------|
| Factual Q&A | 0.1 - 0.3 | Precise, deterministic |
| Game Rules | 0.2 - 0.4 | Clear, consistent |
| Creative Writing | 0.7 - 1.0 | Varied, creative |
| Code Generation | 0.2 - 0.5 | Syntactically correct |

### TopK Settings

| Knowledge Base Size | TopK | Reasoning |
|---------------------|------|-----------|
| Small (< 100 docs) | 2-3 | Limited options |
| Medium (100-1000 docs) | 3-5 | Balanced coverage |
| Large (> 1000 docs) | 5-10 | Ensure relevance |

### MinRelevanceScore Settings

| Precision Need | Score | What You Get |
|----------------|-------|--------------|
| High Recall | 0.3-0.4 | More results, some irrelevant |
| Balanced | 0.5-0.6 | **Recommended** default |
| High Precision | 0.7-0.8 | Fewer, highly relevant |
| Exact Match | 0.9+ | Very strict matching |

## Complete Examples

### Customer Support - Password Reset

```json
{
  "question": "I forgot my password, how do I reset it?",
  "context": "Password Reset Process:\n\n1. Click 'Forgot Password' on the login page\n2. Enter your registered email address\n3. Check your email for a reset link (may take 2-5 minutes)\n4. Click the link and create a new password\n5. Password must be 8+ characters with at least one number\n\nIf you don't receive the email:\n- Check spam/junk folder\n- Verify you're using the correct email address\n- Contact support at support@example.com\n\nSecurity Note: Reset links expire after 1 hour.",
  "enableRag": true,
  "maxTokens": 400,
  "temperature": 0.2
}
```

### Game Rules - Multiple Scenarios

```json
{
  "question": "What happens when I roll doubles in Monopoly?",
  "context": "Monopoly - Rolling Doubles:\n\n1. If you roll doubles (both dice show the same number), take your turn as normal\n2. After completing your turn, roll again\n3. You can roll doubles up to 2 times in a row\n4. If you roll doubles a third time, go directly to Jail\n5. Do not collect $200 if sent to Jail on doubles\n\nExiting Jail:\n- If you're in Jail and roll doubles, you get out immediately and move\n- This is the only time rolling doubles in Jail is beneficial\n\nStrategy Tip: Rolling doubles is advantageous as it gives you extra turns to buy properties or collect rent.",
  "enableRag": true,
  "maxTokens": 512,
  "temperature": 0.3
}
```

### Technical Documentation - Multi-Step Process

```json
{
  "question": "How do I deploy the application?",
  "context": "Deployment Guide - Production Environment\n\nPrerequisites:\n- Node.js 18+ installed\n- Access to production server\n- Environment variables configured\n\nDeployment Steps:\n\n1. Build Production Bundle:\n   npm run build\n   \n2. Run Tests:\n   npm test\n   \n3. Deploy to Server:\n   scp -r dist/ user@server:/var/www/app\n   \n4. Restart Application:\n   ssh user@server 'pm2 restart app'\n   \n5. Verify Deployment:\n   curl https://yourapp.com/health\n\nRollback Procedure:\nIf deployment fails:\n1. ssh user@server\n2. cd /var/www/app\n3. git checkout previous-tag\n4. pm2 restart app\n\nMonitoring: Check logs at /var/log/app/error.log",
  "enableRag": true,
  "maxTokens": 600,
  "temperature": 0.2
}
```

## Testing Your Context

### Validation Checklist

Before sending a query, verify:

- [ ] Context is relevant to the question
- [ ] Context length is reasonable (< 2000 chars)
- [ ] Context is clearly formatted
- [ ] Temperature matches use case
- [ ] TopK and minRelevanceScore are appropriate
- [ ] Domain filters are correct (if using auto search)

### Quick Test

```json
{
  "question": "Summarize this information",
  "context": "[YOUR CONTEXT HERE]",
  "enableRag": true,
  "maxTokens": 200,
  "temperature": 0.3
}
```

If the summary captures the key points, your context is well-formatted!

## Troubleshooting

### "Answer doesn't use the context"

**Causes:**
- Context not relevant enough
- Temperature too high (model being creative)
- Context too long (model loses focus)

**Solutions:**
- Make context more focused
- Lower temperature to 0.2-0.3
- Reduce context length
- Rephrase question to reference context

### "Context not found" (auto search)

**Causes:**
- minRelevanceScore too high
- Domain filter too restrictive
- Documents not in database

**Solutions:**
- Lower minRelevanceScore to 0.4-0.5
- Remove or broaden domainFilter
- Increase topK
- Verify database has documents

### "Slow responses"

**Causes:**
- Context too long
- maxTokens too high
- Cold start (first query)

**Solutions:**
- Reduce context to <1000 chars
- Lower maxTokens to 256-512
- Wait for model warm-up (first query is slow)
