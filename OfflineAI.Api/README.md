# OfflineAI API Configuration

## Error: "No healthy instances available in pool"

This error occurs when the LLM model pool cannot be initialized. The most common cause is missing or incorrect configuration.

## Solution: Configure User Secrets

### Step 1: Open User Secrets
1. In Visual Studio, right-click the **OfflineAI.Api** project
2. Select **Manage User Secrets**
3. This will open the `secrets.json` file

### Step 2: Add Your Configuration
Replace the contents of `secrets.json` with:

```json
{
  "AppConfiguration": {
    "Llm": {
      "ExecutablePath": "C:\\path\\to\\your\\llama-cli.exe",
      "ModelPath": "C:\\path\\to\\your\\model.gguf",
      "ModelName": "mistral-7b-instruct-v0.2.q5_k_m",
      "ModelType": "Mistral",
      "UseGpu": false,
      "GpuLayers": 0,
      "ContextSize": 2048
    }
  }
}
```

### Step 3: Update the Paths
Replace the placeholder paths with your actual file paths:
- **ExecutablePath**: Path to your `llama-cli.exe` or similar LLM executable
- **ModelPath**: Path to your `.gguf` model file (e.g., Mistral, Llama, etc.)

### Step 4: Restart the API
1. Stop the API if it's running
2. Start it again
3. The console will show whether the configuration is valid

## Example Configuration

If you have the same setup as the AiDashboard project, you can copy the configuration from:
- The other User Secrets file you have open: `0b725f58-2de8-44d7-873c-73d5891fd43c\secrets.json`

## Verification

When the API starts correctly, you'll see:
```
? OfflineAI API is running
?? Swagger UI: https://localhost:7015/swagger
?? LLM Configured: True
```

When there are configuration errors, you'll see:
```
??  CONFIGURATION ERRORS DETECTED
? AppConfiguration:Llm:ExecutablePath is missing
? AppConfiguration:Llm:ModelPath is missing
```

## Testing

Once configured, test with:
```bash
curl -X 'POST' \
  'https://localhost:7015/api/Query' \
  -H 'accept: application/json' \
  -H 'Content-Type: application/json' \
  -d '{
  "question": "What is 10+9?",
  "enableRag": false,
  "maxTokens": 512,
  "temperature": 0.3
}'
```

## Quick Copy from AiDashboard

Since you have both User Secrets files open:
1. Copy the `AppConfiguration` section from the AiDashboard secrets
2. Paste it into the OfflineAI.Api secrets
3. Save and restart the API
