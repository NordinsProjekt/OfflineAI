using System;
using System.Linq;
using Services.AI.Embeddings;

namespace OfflineAI.Diagnostics;

/// <summary>
/// Direct investigation program to see what's wrong with embeddings.
/// Run this instead of the full test suite to see console output.
/// </summary>
public class EmbeddingDiagnostic
{
    public static async Task RunAsync()
    {
        Console.WriteLine("?????????????????????????????????????????????????????????????????");
        Console.WriteLine("?  BERT Embedding Diagnostic Tool");
        Console.WriteLine("?????????????????????????????????????????????????????????????????\n");
        
        var modelPath = @"d:\tinyllama\models\all-MiniLM-L6-v2\model.onnx";
        
        if (!File.Exists(modelPath))
        {
            Console.WriteLine($"? Model not found at: {modelPath}");
            return;
        }
        
        Console.WriteLine("Initializing BERT model...");
        var embeddingService = new SemanticEmbeddingService(modelPath, embeddingDimension: 384);
        Console.WriteLine("? Model loaded\n");
        
        // Test 1: Completely different texts
        Console.WriteLine("??? Test 1: Unrelated Texts ???");
        await TestSimilarity(embeddingService, 
            "hello", 
            "Players collect treasure cards by moving through dungeon spaces");
        
        // Test 2: Similar texts
        Console.WriteLine("\n??? Test 2: Similar Texts ???");
        await TestSimilarity(embeddingService, 
            "collect treasure cards", 
            "gathering treasure items");
        
        // Test 3: Very different topics
        Console.WriteLine("\n??? Test 3: Different Topics ???");
        await TestSimilarity(embeddingService, 
            "dungeon monster battle", 
            "sunny weather forecast");
        
        // Test 4: Embedding distribution analysis
        Console.WriteLine("\n??? Test 4: Embedding Analysis ???");
        await AnalyzeEmbedding(embeddingService, "hello");
        await AnalyzeEmbedding(embeddingService, "treasure cards");
        await AnalyzeEmbedding(embeddingService, "the quick brown fox jumps over the lazy dog");
        
        Console.WriteLine("\n??? Test 5: All Pairwise Similarities ???");
        var testWords = new[] { "hello", "treasure", "monster", "weather", "game" };
        Console.WriteLine($"\nTesting {testWords.Length} words:");
        
        var embeddings = new System.Collections.Generic.List<(string text, ReadOnlyMemory<float> embedding)>();
        foreach (var word in testWords)
        {
            var emb = await embeddingService.GenerateEmbeddingAsync(word);
            embeddings.Add((word, emb));
        }
        
        Console.WriteLine("\nSimilarity Matrix:");
        Console.Write($"{"",12}");
        foreach (var word in testWords)
        {
            Console.Write($"{word,12}");
        }
        Console.WriteLine();
        
        for (int i = 0; i < embeddings.Count; i++)
        {
            Console.Write($"{embeddings[i].text,12}");
            for (int j = 0; j < embeddings.Count; j++)
            {
                if (i == j)
                {
                    Console.Write($"{"1.000",12}");
                }
                else
                {
                    var sim = CosineSimilarity(embeddings[i].embedding, embeddings[j].embedding);
                    Console.Write($"{sim,12:F3}");
                }
            }
            Console.WriteLine();
        }
        
        Console.WriteLine("\n?????????????????????????????????????????????????????????????????");
        Console.WriteLine("?  Diagnostic Complete");
        Console.WriteLine("?????????????????????????????????????????????????????????????????");
        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }
    
    private static async Task TestSimilarity(SemanticEmbeddingService service, string text1, string text2)
    {
        Console.WriteLine($"Text 1: \"{text1}\"");
        Console.WriteLine($"Text 2: \"{text2}\"");
        
        var emb1 = await service.GenerateEmbeddingAsync(text1);
        var emb2 = await service.GenerateEmbeddingAsync(text2);
        
        var similarity = CosineSimilarity(emb1, emb2);
        
        var verdict = similarity switch
        {
            >= 0.8 => "? VERY SIMILAR (expected for similar texts)",
            >= 0.6 => "??  SIMILAR (unexpected for unrelated texts)",
            >= 0.4 => "??  SOMEWHAT SIMILAR (borderline)",
            _ => "? DIFFERENT (expected for unrelated texts)"
        };
        
        Console.WriteLine($"Similarity: {similarity:F3} - {verdict}");
    }
    
    private static async Task AnalyzeEmbedding(SemanticEmbeddingService service, string text)
    {
        Console.WriteLine($"\nText: \"{text}\"");
        
        var embedding = await service.GenerateEmbeddingAsync(text);
        var array = embedding.ToArray();
        
        var mean = array.Average();
        var variance = array.Sum(v => Math.Pow(v - mean, 2)) / array.Length;
        var stdDev = Math.Sqrt(variance);
        var magnitude = Math.Sqrt(array.Sum(v => v * v));
        var zerosCount = array.Count(v => Math.Abs(v) < 0.0001);
        var minValue = array.Min();
        var maxValue = array.Max();
        
        Console.WriteLine($"  Dimension: {array.Length}");
        Console.WriteLine($"  Magnitude: {magnitude:F6} (should be ?1.0 for normalized)");
        Console.WriteLine($"  Mean: {mean:F6}");
        Console.WriteLine($"  Std Dev: {stdDev:F6}");
        Console.WriteLine($"  Min: {minValue:F6}, Max: {maxValue:F6}");
        Console.WriteLine($"  Near-zero values: {zerosCount}/{array.Length} ({(zerosCount * 100.0 / array.Length):F1}%)");
        Console.WriteLine($"  First 10: [{string.Join(", ", array.Take(10).Select(v => $"{v:F4}"))}]");
        
        if (Math.Abs(magnitude - 1.0) > 0.01)
        {
            Console.WriteLine("  ??  WARNING: Vector not properly normalized!");
        }
        
        if (stdDev < 0.01)
        {
            Console.WriteLine("  ??  WARNING: Very low variance - embeddings may be too uniform!");
        }
    }
    
    private static double CosineSimilarity(ReadOnlyMemory<float> vector1, ReadOnlyMemory<float> vector2)
    {
        var v1 = vector1.Span;
        var v2 = vector2.Span;

        double dotProduct = 0;
        double magnitude1 = 0;
        double magnitude2 = 0;

        for (int i = 0; i < v1.Length; i++)
        {
            dotProduct += v1[i] * v2[i];
            magnitude1 += v1[i] * v1[i];
            magnitude2 += v2[i] * v2[i];
        }

        magnitude1 = Math.Sqrt(magnitude1);
        magnitude2 = Math.Sqrt(magnitude2);

        if (magnitude1 == 0 || magnitude2 == 0)
        {
            return 0;
        }

        return dotProduct / (magnitude1 * magnitude2);
    }
}
