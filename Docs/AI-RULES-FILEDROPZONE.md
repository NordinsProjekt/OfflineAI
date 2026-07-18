# ?? AI Assistant Rules for FileDropZone Component

## MANDATORY RULES - MUST FOLLOW

### Rule 1: FileDropZone is the ONLY File Upload Component
**ALWAYS** use `<FileDropZone />` for file uploads.
**NEVER** create custom file upload implementations.

```razor
<!-- ? CORRECT -->
<FileDropZone OnFileSelected="HandleFile" />

<!-- ? WRONG - Don't create custom dropzones -->
<div class="custom-upload-area">
    <InputFile OnChange="..." />
</div>
```

---

### Rule 2: CSS Classes are LOCKED
**DO NOT** modify these CSS classes:
- `.file-dropzone`
- `.theme-dark` / `.theme-light`
- `.drag-over`
- `.dropzone-content`
- `.dropzone-icon`
- `.dropzone-title`
- `.dropzone-prompt`
- `.dropzone-input`
- `.dropzone-browse-btn`
- `.dropzone-hint`
- `.dropzone-error`

**Reason**: Unit tests enforce these classes. Changes will break tests.

---

### Rule 3: Run Tests Before Changes
Before modifying FileDropZone:
```bash
dotnet test --filter "FullyQualifiedName~FileDropZone"
```

All 38 tests MUST pass:
- 21 unit tests (FileDropZoneTests.cs)
- 17 bUnit tests (FileDropZoneBunitTests.cs)

---

### Rule 4: Use Standard Parameters
Prefer these standard configurations:

**Document Upload (Default)**:
```razor
<FileDropZone 
    Title="Upload Document"
    OnFileSelected="HandleFile" />
```

**Custom Document**:
```razor
<FileDropZone 
    Title="Upload Course Plan"
    Icon="[DOC]"
    AcceptedTypes=".txt,.docx"
    OnFileSelected="HandleFile"
    OnError="HandleError" />
```

**Image Upload**:
```razor
<FileDropZone 
    Title="Upload Image"
    Icon="[IMG]"
    AcceptedTypes=".jpg,.png,.gif"
    MaxFileSizeBytes="5242880"
    OnFileSelected="HandleImage" />
```

---

### Rule 5: Theme Must Match Page
- **Dark pages**: `DarkMode="true"` (default)
- **Light pages**: `DarkMode="false"`

```razor
<!-- Dark mode page -->
<FileDropZone DarkMode="true" />

<!-- Light mode page -->
<FileDropZone DarkMode="false" />
```

---

### Rule 6: Always Handle Errors
**ALWAYS** provide an `OnError` callback:

```razor
<FileDropZone 
    OnFileSelected="HandleFile"
    OnError="HandleError" />

@code {
    private string errorMessage = "";
    
    private void HandleError(string error)
    {
        errorMessage = error;
        // Show to user
    }
}
```

---

## QUICK REFERENCE

### Basic Implementation
```razor
@page "/upload"

<FileDropZone 
    Title="Upload Your File"
    OnFileSelected="HandleFileSelected"
    OnError="HandleError" />

@if (!string.IsNullOrEmpty(errorMessage))
{
    <div class="error">@errorMessage</div>
}

@code {
    private string errorMessage = "";
    
    private async Task HandleFileSelected(InputFileChangeEventArgs e)
    {
        errorMessage = "";
        var file = e.File;
        
        // Process file...
        try
        {
            // Your logic here
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
        }
    }
    
    private void HandleError(string error)
    {
        errorMessage = error;
    }
}
```

---

### Common Mistakes to Avoid

#### ? WRONG: Custom File Upload
```razor
<!-- DON'T DO THIS -->
<div class="my-custom-uploader">
    <InputFile OnChange="..." />
</div>
```

#### ? CORRECT: Use FileDropZone
```razor
<FileDropZone OnFileSelected="..." />
```

---

#### ? WRONG: Modifying CSS
```css
/* DON'T DO THIS */
.dropzone-title {
    font-size: 3rem; /* Breaking change! */
}
```

#### ? CORRECT: Use Parameters
```razor
<!-- Customization via parameters, not CSS -->
<FileDropZone Title="My Custom Title" />
```

---

#### ? WRONG: Ignoring Errors
```razor
<!-- DON'T DO THIS -->
<FileDropZone OnFileSelected="HandleFile" />
<!-- No error handling! -->
```

#### ? CORRECT: Handle Errors
```razor
<FileDropZone 
    OnFileSelected="HandleFile"
    OnError="HandleError" />
```

---

## DECISION TREE

```
Need file upload?
?? YES ? Use FileDropZone
?   ?? Single file? ? AllowMultiple="false" (default)
?   ?? Multiple files? ? AllowMultiple="true"
?   ?? Specific types? ? Set AcceptedTypes=".ext"
?   ?? Size limit? ? Set MaxFileSizeBytes
?   ?? Dark theme? ? DarkMode="true" (default)
?
?? NO ? Use different component
```

---

## TESTING CHECKLIST

Before committing changes involving FileDropZone:

- [ ] Component imports correctly
- [ ] OnFileSelected callback is set
- [ ] OnError callback is set
- [ ] Theme matches page (DarkMode parameter)
- [ ] Drag & drop works
- [ ] Click to browse works
- [ ] File validation works
- [ ] Error messages display
- [ ] All 38 tests pass
- [ ] No CSS modifications
- [ ] Documentation updated (if new use case)

---

## PARAMETERS QUICK GUIDE

| When to Use | Parameter | Example Value |
|-------------|-----------|---------------|
| Custom title | `Title` | "Upload Course Plan" |
| Custom prompt | `PromptText` | "Drag your file here" |
| Custom icon | `Icon` | "[DOC]" or "[IMG]" |
| Help text | `HintText` | "Max 10MB, PDF only" |
| File types | `AcceptedTypes` | ".pdf,.docx" |
| Size limit | `MaxFileSizeBytes` | 10485760 (10MB) |
| Multiple files | `AllowMultiple` | true |
| Light theme | `DarkMode` | false |
| File handler | `OnFileSelected` | HandleFile method |
| Error handler | `OnError` | HandleError method |

---

## CODE GENERATION TEMPLATE

When AI needs to create file upload functionality:

```razor
@page "/[page-name]"
@rendermode InteractiveServer

<PageTitle>[Page Title]</PageTitle>

<div class="page-container">
    <h1>[Page Heading]</h1>
    
    <FileDropZone 
        Title="[Upload Title]"
        Icon="[Icon]"
        HintText="[Help Text]"
        AcceptedTypes="[.ext1,.ext2]"
        MaxFileSizeBytes="[bytes]"
        DarkMode="[true|false]"
        OnFileSelected="HandleFileSelected"
        OnError="HandleError" />
    
    @if (!string.IsNullOrEmpty(errorMessage))
    {
        <div class="error-message">@errorMessage</div>
    }
    
    @if (fileUploaded)
    {
        <!-- Show file info or processing state -->
    }
</div>

@code {
    private string errorMessage = "";
    private bool fileUploaded = false;
    
    private async Task HandleFileSelected(InputFileChangeEventArgs e)
    {
        errorMessage = "";
        var file = e.File;
        
        try
        {
            // TODO: Process file
            fileUploaded = true;
        }
        catch (Exception ex)
        {
            errorMessage = $"Error processing file: {ex.Message}";
        }
    }
    
    private void HandleError(string error)
    {
        errorMessage = error;
    }
}
```

---

## REMEMBER

1. **FileDropZone is the standard** - Use it everywhere
2. **Tests must pass** - 38/38 required
3. **CSS is locked** - Don't modify classes
4. **Errors matter** - Always handle them
5. **Theme consistency** - Match the page

---

**These rules apply to ALL file upload scenarios in the OfflineAI project.**
