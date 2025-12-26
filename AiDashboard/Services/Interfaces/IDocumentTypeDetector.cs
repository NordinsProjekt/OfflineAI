namespace AiDashboard.Services;

public interface IDocumentTypeDetector
{
    DocumentTypeResult DetectType(string content);
    List<AnalysisAction> GetRecommendedActions(DocumentTypeDetector.DocumentType type);
    string GetTypeName(DocumentTypeDetector.DocumentType type);
    string GetTypeIcon(DocumentTypeDetector.DocumentType type);
}
