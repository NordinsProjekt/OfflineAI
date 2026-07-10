# Domain Filter Discovery - Quick Reference

## New Endpoints Added

### 1. Get All Domains
```http
GET /api/Domains
```

**Returns:**
- List of all available domain filters
- Categories
- Usage examples

**Example Response:**
```json
{
  "domains": [
    {
      "domainId": "chess",
      "displayName": "Chess",
      "category": "board-games",
      "createdAt": "2024-01-15T10:30:00Z"
    }
  ],
  "count": 1,
  "categories": ["board-games"],
  "usage": {
    "message": "Use domainId values in the domainFilter array",
    "example": {
      "domainFilter": ["chess"]
    }
  }
}
```

### 2. Get Domains by Category
```http
GET /api/Domains/category/{category}
```

**Example:**
```http
GET /api/Domains/category/board-games
```

**Returns:**
```json
{
  "category": "board-games",
  "domains": [
    { "domainId": "chess", "displayName": "Chess", "category": "board-games" },
    { "domainId": "monopoly", "displayName": "Monopoly", "category": "board-games" }
  ],
  "count": 2
}
```

### 3. Get All Categories
```http
GET /api/Domains/categories
```

**Returns:**
```json
{
  "categories": ["board-games", "card-games", "video-games"],
  "count": 3
}
```

## Workflow

### Recommended Usage Pattern

```bash
# 1. Discover available domains
GET /api/Domains

# 2. Choose relevant domains from response
# Look at the "domainId" field

# 3. Use in RAG query
POST /api/Query
{
  "question": "How do I castle in chess?",
  "enableRag": true,
  "domainFilter": ["chess"],  # Use domainId here
  "topK": 3,
  "minRelevanceScore": 0.6
}
```

### Browse by Category

```bash
# 1. Get all categories
GET /api/Domains/categories

# 2. Get domains for a category
GET /api/Domains/category/board-games

# 3. Use multiple domains from same category
POST /api/Query
{
  "question": "What happens when I roll doubles?",
  "enableRag": true,
  "domainFilter": ["monopoly", "backgammon"],
  "topK": 4
}
```

## When Domains Are Not Available

If you call `/api/Domains` and get:

```json
{
  "error": "Domain repository not configured",
  "message": "Auto vector search RAG is not enabled...",
  "availableModes": [
    "Direct Query (enableRag: false)",
    "Manual Context RAG (provide context field)"
  ]
}
```

**You can still use:**
1. **Direct queries** - No RAG, just LLM knowledge
2. **Manual context RAG** - Provide your own `context` field

**To enable domain filtering:**
- Set up embedding service
- Configure vector database
- Register domain repository in DI container

## Testing

Use the updated `.http` file which now includes:

```http
### Get All Available Domain Filters
GET {{OfflineAI.Api_HostAddress}}/api/Domains

### Get Domains by Category
GET {{OfflineAI.Api_HostAddress}}/api/Domains/category/board-games

### Get All Categories
GET {{OfflineAI.Api_HostAddress}}/api/Domains/categories
```

## Common Scenarios

### Scenario 1: "What domains can I use?"
```bash
GET /api/Domains
# Use the "domainId" values in your queries
```

### Scenario 2: "What board game rules do you have?"
```bash
GET /api/Domains/category/board-games
# Returns all board game domains
```

### Scenario 3: "Show me all categories"
```bash
GET /api/Domains/categories
# Returns list of all category names
```

### Scenario 4: "Search across multiple related domains"
```bash
# Get domains in a category
GET /api/Domains/category/card-games

# Use multiple domains in query
POST /api/Query
{
  "question": "What's the best poker hand?",
  "enableRag": true,
  "domainFilter": ["poker", "texas-holdem", "omaha"],
  "topK": 5
}
```

## Quick Tips

? **Always check domains first** - Run `GET /api/Domains` before making RAG queries with domain filters

? **Use categories to group** - Get related domains via `GET /api/Domains/category/{category}`

? **Cache the results** - Domain lists don't change frequently, cache them in your client

? **Start broad, then narrow** - Begin without domain filter, add filters if results aren't focused enough

? **Multiple domains = broader search** - Use array with multiple domainIds: `["chess", "checkers"]`

? **Don't guess domain names** - Use the discovery endpoint to get exact `domainId` values

? **Don't use displayName** - Always use `domainId`, not `displayName` in filters

## Response Status Codes

| Code | Meaning | Action |
|------|---------|--------|
| 200 | Success | Domains retrieved successfully |
| 503 | Service Unavailable | Domain repository not configured - use manual RAG |
| 500 | Server Error | Check logs, verify database connection |

## Integration Example

```javascript
// JavaScript/TypeScript example
class OfflineAIClient {
  async getAvailableDomains() {
    const response = await fetch('https://localhost:7015/api/Domains');
    const data = await response.json();
    
    if (response.status === 503) {
      console.log('Domain filtering not available - using manual RAG');
      return null;
    }
    
    return data.domains.map(d => d.domainId);
  }
  
  async queryWithDomains(question, domainIds) {
    const response = await fetch('https://localhost:7015/api/Query', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        question,
        enableRag: true,
        domainFilter: domainIds,
        topK: 3,
        minRelevanceScore: 0.6
      })
    });
    
    return await response.json();
  }
}

// Usage
const client = new OfflineAIClient();
const domains = await client.getAvailableDomains();

if (domains && domains.includes('chess')) {
  const answer = await client.queryWithDomains(
    'How does the knight move?',
    ['chess']
  );
  console.log(answer.answer);
}
```

## Summary

**New capabilities:**
- ? Discover all available domain filters
- ? Browse domains by category
- ? Get list of all categories
- ? Dynamic domain selection based on availability

**Benefits:**
- No guessing domain names
- Easy domain discovery
- Category-based filtering
- Better query targeting
- Improved RAG relevance
