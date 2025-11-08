using Services;
using System;
using System.Threading.Tasks;
using Services.AI.Embeddings;

namespace OfflineAI.Diagnostics;

public class BertDiagnostics
{
    public static async Task RunDiagnosticsAsync()
    {
        Console.WriteLine("=== BERT Model Diagnostics ===\n");
        
        try
        {
            var embeddingService = new SemanticEmbeddingService();
            
            Console.WriteLine("Testing simple text...");
            var embedding1 = await embeddingService.GenerateEmbeddingAsync("hello");
            Console.WriteLine($"? Generated embedding with {embedding1.Length} dimensions\n");
            
            Console.WriteLine("Testing game query...");
            var embedding2 = await embeddingService.GenerateEmbeddingAsync("How do I fight a monster?");
            Console.WriteLine($"? Generated embedding with {embedding2.Length} dimensions\n");
            
            Console.WriteLine("Testing board game text...");
            var embedding3 = await embeddingService.GenerateEmbeddingAsync("Monster cards are used when you fight");
            Console.WriteLine($"? Generated embedding with {embedding3.Length} dimensions\n");
            
            // Calculate similarities
            var sim1 = CosineSimilarity(embedding1, embedding3);
            var sim2 = CosineSimilarity(embedding2, embedding3);
            
            Console.WriteLine($"Similarity: 'hello' vs 'Monster cards' = {sim1:F3}");
            Console.WriteLine($"Similarity: 'fight monster' vs 'Monster cards' = {sim2:F3}\n");
            
            if (sim1 < 0.3 && sim2 > 0.5)
            {
                Console.WriteLine("? BERT embeddings working correctly!");
            }
            else
            {
                Console.WriteLine($"??  Unexpected similarity scores!");
                Console.WriteLine($"   Expected: 'hello' < 0.3, got {sim1:F3}");
                Console.WriteLine($"   Expected: 'fight monster' > 0.5, got {sim2:F3}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"? Error: {ex.Message}");
            Console.WriteLine($"   {ex.StackTrace}");
        }
    }
    
    private static double CosineSimilarity(ReadOnlyMemory<float> v1, ReadOnlyMemory<float> v2)
    {
        var span1 = v1.Span;
        var span2 = v2.Span;
        
        double dotProduct = 0;
        double mag1 = 0;
        double mag2 = 0;
        
        for (int i = 0; i < Math.Min(span1.Length, span2.Length); i++)
        {
            dotProduct += span1[i] * span2[i];
            mag1 += span1[i] * span1[i];
            mag2 += span2[i] * span2[i];
        }
        
        mag1 = Math.Sqrt(mag1);
        mag2 = Math.Sqrt(mag2);
        
        if (mag1 == 0 || mag2 == 0) return 0;
        
        return dotProduct / (mag1 * mag2);
    }
}
