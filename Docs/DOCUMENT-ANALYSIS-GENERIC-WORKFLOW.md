# Document Analysis Page - Generic Workflow Implementation

## Problem
The DocumentAnalysis page is currently hardcoded for Swedish YH course plans only. It needs to support generic document analysis with conditional Swedish-specific options.

## Required Changes

### 1. Change Subtitle (Line ~20)
```razor
<!-- BEFORE -->
<p class="oa-workflow-subtitle">Upload a Swedish YH course plan and extract structured objectives</p>

<!-- AFTER -->
<p class="oa-workflow-subtitle">Upload any document and choose how you want to analyze it</p>
```

### 2. Update File Accept Types (Line ~37)
```razor
<!-- BEFORE -->
<InputFile @ref="fileInput" id="file-input" OnChange="HandleFileSelected" class="oa-file-input" accept=".txt,.doc,.docx" />
<p class="oa-upload-hint">Supports: TXT, DOCX (Max 10MB)</p>

<!-- AFTER -->
<InputFile @ref="fileInput" id="file-input" OnChange="HandleFileSelected" class="oa-file-input" accept=".txt,.doc,.docx,.pdf" />
<p class="oa-upload-hint">Supports: TXT, DOCX, PDF (Max 10MB)</p>
```

### 3. Replace Analysis Options Section (Lines ~79-98)
Replace the entire section that shows `recommendedActions` with:

```razor
@if (uploadedFile != null && !isAnalyzing && string.IsNullOrEmpty(errorMessage))
{
    <div class="oa-analysis-options">
        <h3>What would you like to do with this document?</h3>
        <p class="oa-options-subtitle">Choose an analysis type below</p>
        
        <div class="oa-option-grid">
            @* Generic Options - Always Shown *@
            <button class="oa-option-button" @onclick='@(() => AnalyzeDocument("summarize"))'>
                <span class="oa-option-icon">[SUM]</span>
                <span class="oa-option-text">Summarize</span>
                <span class="oa-option-description">Get a concise summary</span>
            </button>

            <button class="oa-option-button" @onclick='@(() => AnalyzeDocument("qa"))'>
                <span class="oa-option-icon">[Q&A]</span>
                <span class="oa-option-text">Ask Questions</span>
                <span class="oa-option-description">Interactive Q&A</span>
            </button>

            @* Swedish Kursplan Options - Conditional *@
            @if (detectedType?.Type == DocumentTypeDetector.DocumentType.SwedishYhKursplan || 
                 detectedType?.Type == DocumentTypeDetector.DocumentType.SwedishKursplan)
            {
                <button class="oa-option-button oa-featured" @onclick='@(() => AnalyzeDocument("extract-kursmal"))'>
                    <span class="oa-option-icon">[K]</span>
                    <span class="oa-option-text">Extrahera Kursmål</span>
                    <span class="oa-option-description">Extract learning objectives</span>
                    <span class="oa-option-badge">Recommended</span>
                </button>
            }
        </div>
    </div>
}
```

### 4. Update AnalyzeDocument Method (Line ~439)
Change from hardcoded `AnalyzeKursmal()` to switch statement:

```csharp
private async Task AnalyzeDocument(string analysisType)
{
    // ... existing setup code ...
    
    try
    {
        switch (analysisType)
        {
            case "summarize":
                analysisResult = "SUMMARY\n\nFeature coming soon!";
                break;
            case "qa":
                analysisResult = "Q&A MODE\n\nFeature coming soon!";
                break;
            case "extract-kursmal":
                await AnalyzeKursmal();
                break;
            default:
                analysisResult = "Unknown analysis type.";
                break;
        }
    }
    // ... existing catch/finally blocks ...
}
```

## Implementation Notes
- DO NOT modify FileDropZone component
- Keep all existing methods (AnalyzeKursmal, CopyResults, etc.)
- Only change the UI flow to be generic-first with conditional Swedish options
- PDF support already added via UglyToad.PdfPig
