namespace OfflineAI.Api.Models;

/// <summary>A file present in the active workspace.</summary>
public class WorkspaceFileInfo
{
    public required string Name { get; set; }
    public long SizeBytes { get; set; }
    public DateTime LastModifiedUtc { get; set; }
}

/// <summary>Result of uploading a file into the active workspace.</summary>
public class UploadFileResponse
{
    public required string Filename { get; set; }
    public required string Message { get; set; }
}

/// <summary>Extracted text content of a workspace file (plain text or PDF).</summary>
public class FileTextResponse
{
    public required string Filename { get; set; }
    public required string Text { get; set; }
}

/// <summary>Request body for ingesting a workspace PDF into the RAG knowledge base.</summary>
public class IngestPdfRequest
{
    /// <summary>Collection/domain name the extracted fragments are stored under. Defaults to the file name.</summary>
    public string? CollectionName { get; set; }

    /// <summary>If true, replaces any existing fragments already stored under the same collection.</summary>
    public bool ReplaceExisting { get; set; } = false;
}

/// <summary>Result of ingesting a PDF into the RAG knowledge base.</summary>
public class IngestPdfResponse
{
    public required string Filename { get; set; }
    public required string CollectionName { get; set; }
    public int FragmentsCreated { get; set; }
}

/// <summary>Request body for asking a question about an image already stored in the active workspace.</summary>
public class WorkspaceImageQuestionRequest
{
    public required string Question { get; set; }
}
