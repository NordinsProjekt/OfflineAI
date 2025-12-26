# QuickAsk Model Dropdown Implementation

## Issue
The QuickAsk page at `/quick-ask` (shown in your screenshot) did not have a model dropdown selector.

## Solution
Added a model dropdown to the bottom info badges section of the QuickAsk page.

## Changes Made

### File: `AiDashboard/Components/Pages/QuickAsk.razor`

#### 1. Updated Info Badges Section (Line 116)

**Before:**
```razor
<div class="oa-info-badge">
    <span class="oa-badge-icon">CPU</span>
    <span>Model: @Dashboard.ModelService.CurrentModel</span>
</div>
```

**After:**
```razor
@if (Dashboard.ModelService.AvailableModels.Count > 0)
{
    <div class="oa-info-badge oa-model-selector">
        <span class="oa-badge-icon">CPU</span>
        <span class="model-label-text">Model:</span>
        <select class="oa-model-dropdown" @bind="SelectedModel" @bind:after="OnModelChanged">
            @foreach (var model in Dashboard.ModelService.AvailableModels)
            {
                <option value="@model">@model</option>
            }
        </select>
    </div>
}
else
{
    <div class="oa-info-badge">
        <span class="oa-badge-icon">CPU</span>
        <span>Model: @Dashboard.ModelService.CurrentModel</span>
    </div>
}
```

#### 2. Updated Code Section (Line 495)

**Fixed SelectedModel Property:**
```csharp
// Before:
private string SelectedModel => Dashboard.ModelService.CurrentModel;

// After:
private string SelectedModel
{
    get => Dashboard.ModelService.CurrentModel;
    set => Dashboard.ModelService.CurrentModel = value;
}
```

**Fixed OnModelChanged Method:**
```csharp
// Before:
private void OnModelChanged()
{
    Dashboard.ModelService.SetCurrentModel(SelectedModel);
}

// After:
private async Task OnModelChanged()
{
    // Trigger UI refresh
    await InvokeAsync(StateHasChanged);
}
```

#### 3. Added Styles (Already Present)

The styles for the model dropdown were already included in the file:
- `.oa-model-selector` - Container styling
- `.model-label-text` - Label styling
- `.oa-model-dropdown` - Dropdown styling with custom arrow

## Visual Result

The QuickAsk page now shows at the bottom:

```
[Lock] 100% Private - Runs on your machine
[Check] No setup required - Just ask!
[CPU] Model: [Dropdown with models]
```

When you click the dropdown, it will show all available `.gguf` models from your models directory.

## How It Works

1. **Conditional Rendering**: Shows dropdown if models are available, otherwise shows static text
2. **Two-Way Binding**: `@bind="SelectedModel"` updates the property when user selects a model
3. **After Change**: `@bind:after="OnModelChanged"` triggers UI refresh after selection
4. **Property Setter**: When user selects a model, the setter updates `Dashboard.ModelService.CurrentModel`

## Testing

### To Verify:
1. Navigate to `/quick-ask` (or click "Quick Ask" from home)
2. Scroll to the bottom of the card
3. Look for the third badge with "Model:"
4. You should see a dropdown with all available models

### If Dropdown Not Visible:
- Models list is empty
- Need to place `.gguf` files in the models directory
- Check console for model count

### Debug Check:
Add this temporarily to check if models are loaded:
```csharp
protected override void OnInitialized()
{
    Console.WriteLine($"QuickAsk: Available models: {Dashboard.ModelService.AvailableModels.Count}");
}
```

## Build Status

Build successful. All changes compile without errors.

## Pages with Model Dropdown

Now both pages have model dropdowns:
1. `/chat` - ChatTopBar component (top bar)
2. `/quick-ask` - QuickAsk page (bottom info badges)

Both use the same `Dashboard.ModelService.CurrentModel` property, so changing the model in one place updates it everywhere.

## Summary

The QuickAsk page now has a functional model dropdown that:
- Shows all available models
- Updates the current model selection
- Falls back to static text if no models available
- Matches the visual style of the page (white dropdown on gradient background)
- Uses the same state management as the chat page

Your screenshot should now show a dropdown in the bottom badges area where it currently says "Model: aya-23-8B-Q6_K.gguf".
