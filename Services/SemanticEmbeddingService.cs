using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using BERTTokenizers;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;

namespace Services;

/// <summary>
/// REAL BERT-based embedding service using ONNX Runtime for actual semantic understanding.
/// This implementation runs the full BERT model to generate true semantic embeddings.
/// Memory-optimized for CPU execution (< 2GB RAM usage).
/// </summary>
public class SemanticEmbeddingService : ITextEmbeddingGenerationService
{
    private readonly BertUncasedLargeTokenizer _tokenizer;
    private readonly InferenceSession _session;
    private readonly int _maxSequenceLength = 256;  // Balanced: handles longer texts but less padding
    private readonly int _embeddingDimension;
    private readonly Stopwatch _totalTimer = new();
    private int _embeddingCount = 0;
    private readonly bool _isGpuEnabled;
    
    public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();

    /// <summary>
    /// Creates a REAL BERT-based semantic embedding service using ONNX Runtime.
    /// Downloads model from: https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2
    /// </summary>
    public SemanticEmbeddingService(string modelPath = @"d:\tinyllama\models\all-MiniLM-L6-v2\model.onnx", int embeddingDimension = 384)
    {
        _embeddingDimension = embeddingDimension;
        
        // Use the standard uncased tokenizer (works with smaller vocabulary)
        _tokenizer = new BertUncasedLargeTokenizer();
        
        // Check if model file exists
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException(
                $"BERT model not found at: {modelPath}\n\n" +
                $"Download from: https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/onnx/model.onnx\n" +
                $"Place in: d:\\tinyllama\\models\\all-MiniLM-L6-v2\\model.onnx\n\n" +
                $"Or run: .\\Scripts\\Download-BERT-Model.ps1");
        }
        
        var sessionOptions = new SessionOptions();
        
        // Try GPU first with improved DirectML handling
        bool gpuEnabled = false;
        try
        {
            // Try DirectML with explicit feature level configuration
            // DirectML requires Windows 10 version 1903+ with DirectX 12 support
            Console.WriteLine("?? Attempting to enable DirectML GPU acceleration...");
            
            // Use device ID 0 (default GPU)
            // DirectML will automatically select the best feature level available
            sessionOptions.AppendExecutionProvider_DML(0);
            gpuEnabled = true;
            Console.WriteLine("? DirectML GPU acceleration enabled!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"??  DirectML not available: {ex.Message}");
            
            try
            {
                // Fallback to CUDA if available
                Console.WriteLine("?? Attempting to enable CUDA GPU acceleration...");
                sessionOptions.AppendExecutionProvider_CUDA(0);
                gpuEnabled = true;
                Console.WriteLine("? CUDA GPU acceleration enabled!");
            }
            catch (Exception cudaEx)
            {
                Console.WriteLine($"??  CUDA not available: {cudaEx.Message}");
                Console.WriteLine("?? Falling back to memory-optimized CPU processing");
            }
        }
        
        // Configure session based on GPU availability
        if (gpuEnabled)
        {
            // GPU configuration - allow more memory usage
            sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
            sessionOptions.EnableCpuMemArena = true;
            sessionOptions.IntraOpNumThreads = Environment.ProcessorCount;
            sessionOptions.InterOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2);
            
            Console.WriteLine("?? GPU Configuration:");
            Console.WriteLine($"   Optimization: Full");
            Console.WriteLine($"   Memory Arena: Enabled");
        }
        else
        {
            // CPU-only: STRICT memory optimization for < 2GB usage
            sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_BASIC;
            
            // DISABLE memory arena to reduce RAM usage
            sessionOptions.EnableCpuMemArena = false;
            
            // Single-threaded execution to minimize memory overhead
            // (Multi-threading creates multiple copies of intermediate tensors)
            sessionOptions.IntraOpNumThreads = 1;
            sessionOptions.InterOpNumThreads = 1;
            
            // Limit execution mode to sequential
            sessionOptions.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
            
            Console.WriteLine("?? Memory-Optimized CPU Configuration:");
            Console.WriteLine($"   Target: < 2GB RAM usage");
            Console.WriteLine($"   Memory Arena: DISABLED (saves ~500MB)");
            Console.WriteLine($"   Threading: Single-threaded (saves ~200MB per thread)");
            Console.WriteLine($"   Execution: Sequential (minimal memory footprint)");
            Console.WriteLine($"   Optimization: Basic (reduced temporary allocations)");
            Console.WriteLine($"   ??  WARNING: This will be SLOW but memory-safe");
        }
        
        _session = new InferenceSession(modelPath, sessionOptions);
        _isGpuEnabled = gpuEnabled;
        
        Console.WriteLine("?? REAL BERT embeddings initialized!");
        Console.WriteLine($"   Model: {Path.GetFileName(modelPath)}");
        Console.WriteLine($"   Embedding dimension: {_embeddingDimension}");
        Console.WriteLine($"   Execution: {(gpuEnabled ? "GPU ?" : "CPU ?? (memory-optimized)")}");
        Console.WriteLine($"   Processing: Sequential (one embedding at a time)");
    }
    
    public async Task<IList<ReadOnlyMemory<float>>> GenerateEmbeddingsAsync(
        IList<string> data,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ReadOnlyMemory<float>>();
        
        for (int i = 0; i < data.Count; i++)
        {
            var embedding = await GenerateEmbeddingAsync(data[i], kernel, cancellationToken);
            results.Add(embedding);
            
            // Aggressive garbage collection on CPU to keep memory < 2GB
            if (!_isGpuEnabled && i % 3 == 0)
            {
                GC.Collect(0, GCCollectionMode.Forced, blocking: true, compacting: true);
            }
        }
        
        return results;
    }

    public Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(
        string data,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        // Generate REAL BERT embedding
        var embedding = GenerateBertEmbedding(data);
        
        _embeddingCount++;
        
        // VERY aggressive memory cleanup on CPU every single embedding
        if (!_isGpuEnabled)
        {
            // Full Gen 2 collection to reclaim all memory
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
        
        return Task.FromResult(embedding);
    }

    /// <summary>
    /// Generates a real BERT embedding using the ONNX model.
    /// Memory-optimized for CPU execution.
    /// </summary>
    private ReadOnlyMemory<float> GenerateBertEmbedding(string text)
    {
        try
        {
            // Normalize text to handle special characters that might cause tokenizer issues
            text = NormalizeText(text);
            
            // Safety check: ensure we have valid text after normalization
            if (string.IsNullOrWhiteSpace(text))
            {
                text = "[empty text]";
            }
            
            // Ensure text is not too long
            if (text.Length > 5000)
            {
                text = text.Substring(0, 5000);
            }
            
            // Tokenize and encode input directly
            var encoded = _tokenizer.Encode(_maxSequenceLength, new[] { text });
            
            // Safety check: ensure tokenization produced valid results
            if (encoded == null || encoded.Count == 0)
            {
                text = "fallback text for empty tokenization";
                encoded = _tokenizer.Encode(_maxSequenceLength, new[] { text });
            }
            
            // Extract the token IDs from the encoded tuple
            var inputIds = new long[encoded.Count];
            var attentionMask = new long[encoded.Count];
            var tokenTypeIds = new long[encoded.Count];
            
            for (int i = 0; i < encoded.Count; i++)
            {
                inputIds[i] = encoded[i].InputIds;
                attentionMask[i] = encoded[i].AttentionMask;
                tokenTypeIds[i] = encoded[i].TokenTypeIds;
            }
            
            // Create input tensors with minimal memory footprint
            var inputIdsTensor = new DenseTensor<long>(inputIds, new[] { 1, inputIds.Length });
            var attentionMaskTensor = new DenseTensor<long>(attentionMask, new[] { 1, attentionMask.Length });
            var tokenTypeIdsTensor = new DenseTensor<long>(tokenTypeIds, new[] { 1, tokenTypeIds.Length });
            
            // Prepare inputs
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
                NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor),
                NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIdsTensor)
            };
            
            // Run inference
            using var results = _session.Run(inputs);
            
            // Extract embeddings
            var outputTensor = results.First().AsEnumerable<float>().ToArray();
            
            // Attention-masked mean pooling (best approach for this model)
            // Only average over non-padding tokens
            var sequenceLength = outputTensor.Length / _embeddingDimension;
            var embedding = new float[_embeddingDimension];
            
            // Count actual tokens (non-padding)
            int actualTokenCount = 0;
            for (int i = 0; i < sequenceLength; i++)
            {
                if (attentionMask[i] == 1)
                {
                    actualTokenCount++;
                }
            }
            
            // Mean pooling with attention mask
            for (int i = 0; i < _embeddingDimension; i++)
            {
                float sum = 0;
                for (int j = 0; j < sequenceLength; j++)
                {
                    // Only include tokens where attention_mask is 1 (non-padding)
                    if (attentionMask[j] == 1)
                    {
                        sum += outputTensor[j * _embeddingDimension + i];
                    }
                }
                // Divide by actual token count, not total sequence length
                embedding[i] = actualTokenCount > 0 ? sum / actualTokenCount : 0;
            }
            
            // Normalize to unit length
            var magnitude = Math.Sqrt(embedding.Sum(x => x * x));
            if (magnitude > 0)
            {
                for (int i = 0; i < embedding.Length; i++)
                {
                    embedding[i] /= (float)magnitude;
                }
            }
            
            // Clean up temporary arrays explicitly
            if (!_isGpuEnabled)
            {
                Array.Clear(inputIds, 0, inputIds.Length);
                Array.Clear(attentionMask, 0, attentionMask.Length);
                Array.Clear(tokenTypeIds, 0, tokenTypeIds.Length);
                Array.Clear(outputTensor, 0, outputTensor.Length);
            }
            
            return new ReadOnlyMemory<float>(embedding);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] BERT embedding failed: {ex.Message}");
            throw;
        }
    }
    
    /// <summary>
    /// Normalizes text to handle special Unicode characters that might cause tokenizer issues.
    /// Converts smart quotes, curly apostrophes, and other problematic characters to ASCII equivalents.
    /// </summary>
    private static string NormalizeText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        
        // Replace smart/curly quotes with straight quotes
        text = text.Replace('\u201C', '"'); // " (left double quotation mark)
        text = text.Replace('\u201D', '"'); // " (right double quotation mark)
        text = text.Replace('\u2018', '\''); // ' (left single quotation mark)
        text = text.Replace('\u2019', '\''); // ' (right single quotation mark)
        
        // Replace em dash and en dash with regular dash
        text = text.Replace('\u2013', '-'); // – (en dash)
        text = text.Replace('\u2014', '-'); // — (em dash)
        
        // Replace ellipsis (multi-char replacement needs string version)
        text = text.Replace("\u2026", "..."); // … (horizontal ellipsis)
        
        // Remove or replace other problematic Unicode characters
        text = text.Replace('\u00A0', ' '); // Non-breaking space
        text = text.Replace("\u200B", ""); // Zero-width space
        
        // Remove control characters except common whitespace
        var normalized = new System.Text.StringBuilder(text.Length);
        foreach (char c in text)
        {
            // Keep: letters, digits, punctuation, and common whitespace
            if (char.IsLetterOrDigit(c) || 
                char.IsPunctuation(c) || 
                c == ' ' || c == '\t' || c == '\r' || c == '\n')
            {
                normalized.Append(c);
            }
            // Skip other control characters and rare Unicode
        }
        
        return normalized.ToString();
    }
}
