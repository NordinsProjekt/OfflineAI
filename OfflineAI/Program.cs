using Services.UI;
using OfflineAI.Modes;
using OfflineAI.Diagnostics;

namespace OfflineAI
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            // Check if diagnostics flag is passed
            if (args.Length > 0 && args[0] == "--diagnose-bert")
            {
                await BertDiagnostics.RunDiagnosticsAsync();
                return;
            }
            
            // Check for embedding investigation
            if (args.Length > 0 && args[0] == "--diagnose-embeddings")
            {
                await EmbeddingDiagnostic.RunAsync();
                return;
            }
            
            DisplayService.ShowVectorMemoryDatabaseHeader();
            DisplayService.WriteLine("\n🚀 Starting OfflineAI with BERT Embeddings + SQL Database...\n");
            
            // Only one mode: Database persistence with BERT embeddings
            await RunVectorMemoryWithDatabaseMode.RunAsync();
        }
    }
}