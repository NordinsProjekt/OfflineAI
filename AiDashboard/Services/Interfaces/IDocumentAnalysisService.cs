using Microsoft.AspNetCore.Components.Forms;

namespace AiDashboard.Services;

public interface IDocumentAnalysisService
{
    Task<(bool Success, string Text, string Error)> ExtractTextAsync(IBrowserFile file, long maxFileSize = 10485760);
    List<DocumentChunk> SplitIntoChunks(string text, int maxChunkSize = 4000, int overlap = 200);
    List<DocumentChunk> SearchMultipleTerms(List<DocumentChunk> chunks, string[] searchTerms, bool caseSensitive = false);
    string CreateAnalysisPrompt(List<DocumentChunk> matchedChunks, string searchTerm, string outputFormat);
}
