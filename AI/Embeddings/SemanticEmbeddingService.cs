using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using Services.UI;
using Application.AI.Utilities;
using Application.AI.Extensions;

namespace Application.AI.Embeddings;

/// <summary>
/// REAL BERT-based embedding service using ONNX Runtime for actual semantic understanding.
/// This implementation runs the full BERT model to generate true semantic embeddings.
/// Memory-optimized for CPU execution (< 2GB RAM usage).
/// Supports both BERT-style (3 inputs) and MPNet-style (2 inputs) models.
/// NOTE: Uses Microsoft.ML.Tokenizers with proper BERT vocabulary for accurate semantic search.
/// </summary>
public class SemanticEmbeddingService : ITextEmbeddingGenerationService
{
    private readonly Tokenizer _tokenizer;
    private readonly InferenceSession _session;
    private readonly int _maxSequenceLength = 256;  // MPNet and MiniLM both use 256 token context window
    private readonly int _embeddingDimension;
    private readonly bool _isGpuEnabled;
    private readonly bool _requiresTokenTypeIds;
    private readonly bool _debugMode;
    
    public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();

    /// <summary>
    /// Creates a REAL BERT-based semantic embedding service using ONNX Runtime.
    /// Supports models from: https://huggingface.com/sentence-transformers/
    /// - all-MiniLM-L6-v2: 384 dims, fast (fallback)
    /// - all-mpnet-base-v2: 768 dims, best quality (recommended)
    /// </summary>
    /// <param name="modelPath">Required. Path to the ONNX model file. Must be provided via configuration.</param>
    /// <param name="vocabPath">Required. Path to the vocabulary file. Must be provided via configuration.</param>
    /// <param name="embeddingDimension">Optional. The dimension of the embedding vectors. Default is 768.</param>
    /// <param name="debugMode">Optional. Enable debug logging. Default is false.</param>
    /// <remarks>
    /// BREAKING CHANGE: modelPath and vocabPath are now required parameters (previously had default values).
    /// Callers must explicitly provide these paths, typically from AppConfiguration.
    /// </remarks>
    public SemanticEmbeddingService(
        string modelPath,
        string vocabPath,
        int embeddingDimension = 768,
        bool debugMode = false)
    {
        // Validate required parameters
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            throw new ArgumentException("Model path must be provided via configuration (AppConfiguration:Embedding:ModelPath)", nameof(modelPath));
        }
        
        if (string.IsNullOrWhiteSpace(vocabPath))
        {
            throw new ArgumentException("Vocab path must be provided via configuration (AppConfiguration:Embedding:VocabPath)", nameof(vocabPath));
        }
        
        _embeddingDimension = embeddingDimension;
        _debugMode = debugMode;
        
        // Create PROPER tokenizer with real BERT vocabulary
        // This replaces the problematic BERTTokenizers library
        try
        {
            if (!File.Exists(vocabPath))
            {
                throw new FileNotFoundException($"BERT vocabulary file not found: {vocabPath}");
            }
            
            // Load BERT tokenizer from vocab file
            _tokenizer = BertTokenizer.Create(vocabPath);
            
            DisplayService.WriteLine($"? Loaded BERT tokenizer with vocabulary from: {Path.GetFileName(vocabPath)}");
            DisplayService.WriteLine($"   Tokenizer: Microsoft.ML.Tokenizers (BERT)");
        }
        catch (Exception ex)
        {
            DisplayService.WriteLine($"? Failed to load tokenizer: {ex.Message}");
            DisplayService.WriteLine($"??  Semantic search quality will be impacted!");
            throw;
        }
        
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
            if (_debugMode)
            {
                // Only show acceleration attempt in debug mode
                DisplayService.ShowAttemptingGpuAcceleration("DirectML");
            }
            
            // Use device ID 0 (default GPU)
            sessionOptions.AppendExecutionProvider_DML(0);
            gpuEnabled = true;
            if (_debugMode)
            {
                DisplayService.ShowGpuAccelerationEnabled("DirectML");
            }
        }
        catch (Exception ex)
        {
            if (_debugMode)
            {
                DisplayService.ShowGpuAccelerationNotAvailable("DirectML", ex.Message);
                DisplayService.ShowFallingBackToCpu();
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
            
            if (_debugMode)
            {
                DisplayService.ShowGpuConfiguration();
            }
        }
        else
        {
            // CPU-only: STRICT memory optimization for < 2GB usage
            sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_BASIC;
            
            // DISABLE memory arena to reduce RAM usage
            sessionOptions.EnableCpuMemArena = false;
            
            // Single-threaded execution to minimize memory overhead
            sessionOptions.IntraOpNumThreads = 1;
            sessionOptions.InterOpNumThreads = 1;
            
            // Limit execution mode to sequential
            sessionOptions.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
            
            if (_debugMode)
            {
                DisplayService.ShowCpuConfiguration();
            }
        }
        
        _session = new InferenceSession(modelPath, sessionOptions);
        _isGpuEnabled = gpuEnabled;
        
        // Detect model input requirements
        // BERT-based models (MiniLM): need input_ids, attention_mask, token_type_ids
        // MPNet-based models: need input_ids, attention_mask only
        var inputMetadata = _session.InputMetadata;
        _requiresTokenTypeIds = inputMetadata.ContainsKey("token_type_ids");
        
        var modelType = _requiresTokenTypeIds ? "BERT-style (3 inputs)" : "MPNet-style (2 inputs)";
        DisplayService.ShowEmbeddingServiceInitialized(
            Path.GetFileName(modelPath), 
            _embeddingDimension, 
            gpuEnabled);
        DisplayService.WriteLine($"Model type: {modelType}");
        DisplayService.WriteLine($"Tokenizer: Microsoft.ML.Tokenizers (BERT vocabulary)");
    }
    
    public async Task<IList<ReadOnlyMemory<float>>> GenerateEmbeddingsAsync(
        IList<string> data,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ReadOnlyMemory<float>>();
        
        for (int i = 0; i < data.Count; i++)
        {
            if (_debugMode)
            {
                Console.WriteLine($"[DEBUG] Generating embedding {i + 1}/{data.Count}...");
            }
            
            try
            {
                var embedding = await GenerateEmbeddingAsync(data[i], kernel, cancellationToken);
                results.Add(embedding);
                
                if (_debugMode)
                {
                    Console.WriteLine($"[DEBUG] Successfully generated embedding {i + 1}/{data.Count}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to generate embedding {i + 1}/{data.Count}: {ex.Message}");
                throw;
            }
            
            // Aggressive garbage collection on CPU to keep memory < 2GB
            if (!_isGpuEnabled && i % 3 == 0)
            {
                if (_debugMode)
                {
                    Console.WriteLine($"[DEBUG] Running GC after embedding {i + 1}...");
                }
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
    /// Supports both BERT-style (3 inputs) and MPNet-style (2 inputs) models.
    /// OPTIMIZED: Uses proper BERT tokenization with real vocabulary for accurate semantic search.
    /// </summary>
    private ReadOnlyMemory<float> GenerateBertEmbedding(string text)
    {
        try
        {
            if (_debugMode)
            {
                Console.WriteLine($"[DEBUG] Step 1: Normalizing text (length: {text.Length})...");
            }
            
            // OPTIMIZATION: For very large texts, truncate more aggressively before normalization
            // This prevents tokenization issues with massive documents
            int maxTextLength = 5000; // Default from TextNormalizer
            if (text.Length > maxTextLength * 2)
            {
                if (_debugMode)
                {
                    Console.WriteLine($"[WARN] Text is very large ({text.Length} chars), truncating to {maxTextLength} chars to prevent memory issues");
                }
                text = text.Substring(0, maxTextLength);
            }
            
            // Step 1: Normalize text to handle special characters
            text = TextNormalizer.NormalizeWithLimits(text, maxLength: maxTextLength, fallbackText: "[empty text]");
            
            if (_debugMode)
            {
                Console.WriteLine($"[DEBUG] Step 1 complete. Normalized length: {text.Length}");
                Console.WriteLine($"[DEBUG] Step 2: Tokenizing with BERT vocabulary...");
            }
            
            // Step 2: Tokenize text using Microsoft.ML.Tokenizers with real BERT vocabulary
            var result = _tokenizer.EncodeToTokens(text, out string? normalizedText);
            
            // Extract token IDs and truncate to max sequence length (minus 2 for [CLS] and [SEP])
            var tokens = result
                .Take(_maxSequenceLength - 2)
                .Select(t => t.Id)
                .ToList();
            
            // Build input arrays with [CLS], tokens, [SEP], and padding
            var inputIds = new List<int> { 101 }; // [CLS] token
            inputIds.AddRange(tokens);
            inputIds.Add(102); // [SEP] token
            
            // Create attention mask (1 for real tokens, 0 for padding)
            var attentionMask = new List<int>();
            for (int i = 0; i < inputIds.Count; i++)
            {
                attentionMask.Add(1);
            }
            
            // Pad to maxSequenceLength
            while (inputIds.Count < _maxSequenceLength)
            {
                inputIds.Add(0); // [PAD]
                attentionMask.Add(0);
            }
            
            // Create token type IDs (all 0s for single sentence)
            var tokenTypeIds = Enumerable.Repeat(0, _maxSequenceLength).ToList();
            
            if (_debugMode)
            {
                Console.WriteLine($"[DEBUG] Step 2 complete. Token count: {tokens.Count + 2} (including [CLS] and [SEP])");
                Console.WriteLine($"[DEBUG] Step 3: Creating input tensors...");
            }
            
            // Step 3: Create input tensors for ONNX
            var inputIdsTensor = new DenseTensor<long>(
                inputIds.Select(x => (long)x).ToArray(),
                new[] { 1, _maxSequenceLength });
            var attentionMaskTensor = new DenseTensor<long>(
                attentionMask.Select(x => (long)x).ToArray(),
                new[] { 1, _maxSequenceLength });
            var tokenTypeIdsTensor = new DenseTensor<long>(
                tokenTypeIds.Select(x => (long)x).ToArray(),
                new[] { 1, _maxSequenceLength });
            
            if (_debugMode)
            {
                Console.WriteLine($"[DEBUG] Step 3 complete. Tensors created.");
                Console.WriteLine($"[DEBUG] Step 4: Preparing ONNX inputs...");
            }
            
            // Step 4: Prepare ONNX inputs based on model requirements
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
                NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor)
            };
            
            // Only add token_type_ids if the model requires it (BERT-style models)
            // MPNet-based models don't use this input
            if (_requiresTokenTypeIds)
            {
                inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIdsTensor));
            }
            
            if (_debugMode)
            {
                Console.WriteLine($"[DEBUG] Step 4 complete. Input count: {inputs.Count}");
                Console.WriteLine($"[DEBUG] Step 5: Running BERT inference (this may take a while)...");
            }
            
            // Step 5: Run BERT inference
            using var results = _session.Run(inputs);
            
            if (_debugMode)
            {
                Console.WriteLine($"[DEBUG] Step 5 complete. Got results.");
                Console.WriteLine($"[DEBUG] Step 6: Extracting output tensor...");
            }
            
            var outputTensor = results.First().AsEnumerable<float>().ToArray();
            
            if (_debugMode)
            {
                Console.WriteLine($"[DEBUG] Step 6 complete. Tensor size: {outputTensor.Length}");
                Console.WriteLine($"[DEBUG] Step 7: Pooling and normalizing...");
            }
            
            // Step 7: Apply attention-masked mean pooling and L2 normalization
            var embedding = EmbeddingPooling.PoolAndNormalize(
                outputTensor,
                attentionMask.Select(x => (long)x).ToArray(),
                _embeddingDimension);
            
            if (_debugMode)
            {
                Console.WriteLine($"[DEBUG] Step 7 complete. Embedding dimension: {embedding.Length}");
                Console.WriteLine($"[DEBUG] Step 8: Cleaning up...");
            }
            
            // Step 8: Clean up temporary arrays for CPU memory optimization
            if (!_isGpuEnabled)
            {
                inputIds.Clear();
                attentionMask.Clear();
                tokenTypeIds.Clear();
                Array.Clear(outputTensor, 0, outputTensor.Length);
            }
            
            if (_debugMode)
            {
                Console.WriteLine($"[DEBUG] Step 8 complete. Returning embedding.");
            }
            
            return embedding.AsReadOnlyMemory();
        }
        catch (Exception ex)
        {
            if (_debugMode)
            {
                Console.WriteLine($"[DEBUG] EXCEPTION in GenerateBertEmbedding: {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine($"[DEBUG] Stack trace: {ex.StackTrace}");
            }
            var errorMessage = ExceptionMessageService.EmbeddingGenerationFailed(ex.GetType().Name, ex.Message);
            DisplayService.ShowEmbeddingError(errorMessage);
            throw;
        }
    }
}

