using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.ML.OnnxRuntime;
using BERTTokenizers;
using Services.UI;
using Application.AI.Utilities;
using Application.AI.Extensions;

namespace Application.AI.Embeddings;

/// <summary>
/// REAL BERT-based embedding service using ONNX Runtime for actual semantic understanding.
/// This implementation runs the full BERT model to generate true semantic embeddings.
/// Memory-optimized for CPU execution (< 2GB RAM usage).
/// </summary>
public class SemanticEmbeddingService : ITextEmbeddingGenerationService
{
    private readonly BertUncasedLargeTokenizer _tokenizer;
    private readonly InferenceSession _session;
    private readonly int _maxSequenceLength = 256;  // all-MiniLM-L6-v2 uses 256 tokens, not 512
    private readonly int _embeddingDimension;
    private readonly bool _isGpuEnabled;
    
    public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();

    /// <summary>
    /// Creates a REAL BERT-based semantic embedding service using ONNX Runtime.
    /// Downloads model from: https://huggingface.com/sentence-transformers/all-MiniLM-L6-v2
    /// </summary>
    public SemanticEmbeddingService(string modelPath = @"d:\tinyllama\models\all-MiniLM-L6-v2\model.onnx", int embeddingDimension = 384)
    {
        _embeddingDimension = embeddingDimension;
        
        // Use the standard uncased tokenizer (works with smaller vocabulary)
        _tokenizer = new BertUncasedLargeTokenizer();
        
        // Check if model file exists
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException(ExceptionMessageService.BertModelNotFound(modelPath));
        }
        
        var sessionOptions = new SessionOptions();
        
        // Try DirectML for GPU acceleration (Windows only)
        bool gpuEnabled = false;
        try
        {
            // Try DirectML with explicit feature level configuration
            // DirectML requires Windows 10 version 1903+ with DirectX 12 support
            DisplayService.ShowAttemptingGpuAcceleration("DirectML");
            
            // Use device ID 0 (default GPU)
            // DirectML will automatically select the best feature level available
            sessionOptions.AppendExecutionProvider_DML(0);
            gpuEnabled = true;
            DisplayService.ShowGpuAccelerationEnabled("DirectML");
        }
        catch (Exception ex)
        {
            DisplayService.ShowGpuAccelerationNotAvailable("DirectML", ex.Message);
            DisplayService.ShowFallingBackToCpu();
        }
        
        // Configure session based on GPU availability
        if (gpuEnabled)
        {
            // GPU configuration - allow more memory usage
            sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
            sessionOptions.EnableCpuMemArena = true;
            sessionOptions.IntraOpNumThreads = Environment.ProcessorCount;
            sessionOptions.InterOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2);
            
            DisplayService.ShowGpuConfiguration();
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
            
            DisplayService.ShowCpuConfiguration();
        }
        
        _session = new InferenceSession(modelPath, sessionOptions);
        _isGpuEnabled = gpuEnabled;
        
        DisplayService.ShowEmbeddingServiceInitialized(
            Path.GetFileName(modelPath), 
            _embeddingDimension, 
            gpuEnabled);
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
            // Step 1: Normalize text to handle special characters
            text = TextNormalizer.NormalizeWithLimits(text, maxLength: 5000, fallbackText: "[empty text]");
            
            // Step 2: Tokenize text
            var tokenization = BertTokenizationHelper.TokenizeWithFallback(
                _tokenizer, 
                text, 
                _maxSequenceLength);
            
            // Step 3: Create input tensors for ONNX
            var tensors = BertTokenizationHelper.CreateInputTensors(tokenization);
            
            // Step 4: Prepare ONNX inputs
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", tensors["input_ids"]),
                NamedOnnxValue.CreateFromTensor("attention_mask", tensors["attention_mask"]),
                NamedOnnxValue.CreateFromTensor("token_type_ids", tensors["token_type_ids"])
            };
            
            // Step 5: Run BERT inference
            using var results = _session.Run(inputs);
            var outputTensor = results.First().AsEnumerable<float>().ToArray();
            
            // Step 6: Apply attention-masked mean pooling and L2 normalization
            var embedding = EmbeddingPooling.PoolAndNormalize(
                outputTensor, 
                tokenization.AttentionMask, 
                _embeddingDimension);
            
            // Step 7: Clean up temporary arrays for CPU memory optimization
            if (!_isGpuEnabled)
            {
                BertTokenizationHelper.CleanupArrays(tokenization);
                Array.Clear(outputTensor, 0, outputTensor.Length);
            }
            
            return embedding.AsReadOnlyMemory();
        }
        catch (Exception ex)
        {
            var errorMessage = ExceptionMessageService.EmbeddingGenerationFailed(ex.GetType().Name, ex.Message);
            DisplayService.ShowEmbeddingError(errorMessage);
            throw;
        }
    }
}

