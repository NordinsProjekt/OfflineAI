using System;
using System.IO;
using System.Threading.Tasks;
using Application.AI.Chat;
using Application.AI.Extensions;
using Application.AI.Pooling;
using Application.AI.Utilities;
using Entities;
using Services.Configuration;
using Services.Interfaces;
using Services.Memory;
using Services.Repositories;
using Services.UI;

namespace AiDashboard.Services
{
    /// <summary>
    /// Wraps AiChatServicePooled for use in the Blazor dashboard.
    /// Manages chat state and applies dashboard settings to the AI chat service.
    /// </summary>
    public class DashboardChatService : IDisposable
    {
        private ILlmMemory _memory;
        private readonly ILlmMemory _conversationMemory;
        private readonly IModelInstancePool _modelPool;
        private readonly IDomainDetector? _domainDetector;
        private readonly IQuestionRepository? _questionRepository;
        private readonly ILlmRepository? _llmRepository;
        private Func<string?>? _getCurrentModelName;
        private bool _disposed;

        public DashboardChatService(
            ILlmMemory memory,
            ILlmMemory conversationMemory,
            IModelInstancePool modelPool,
            IDomainDetector? domainDetector = null,
            IQuestionRepository? questionRepository = null,
            ILlmRepository? llmRepository = null,
            Func<string?>? getCurrentModelName = null)
        {
            _memory = memory ?? throw new ArgumentNullException(nameof(memory));
            _conversationMemory = conversationMemory ?? throw new ArgumentNullException(nameof(conversationMemory));
            _modelPool = modelPool ?? throw new ArgumentNullException(nameof(modelPool));
            _domainDetector = domainDetector;
            _questionRepository = questionRepository;
            _llmRepository = llmRepository;
            _getCurrentModelName = getCurrentModelName;
        }

        /// <summary>
        /// Set the provider that returns the current model name.
        /// This allows the chat service to get the latest model name dynamically.
        /// </summary>
        public void SetCurrentModelNameProvider(Func<string?> getCurrentModelName)
        {
            _getCurrentModelName = getCurrentModelName ?? throw new ArgumentNullException(nameof(getCurrentModelName));
        }

        /// <summary>
        /// Initialize the model pool (lazy initialization on first use)
        /// </summary>
        private async Task EnsurePoolInitializedAsync()
        {
            // Check if pool has any instances - if it does, it's already initialized
            if (_modelPool.TotalInstances > 0)
                return;

            try
            {
                Console.WriteLine("[*] Initializing model pool...");
                await _modelPool.InitializeAsync((current, total) =>
                {
                    Console.WriteLine($"[*] Loading model instance {current}/{total}...");
                });
                Console.WriteLine($"[OK] Model pool initialized with {_modelPool.AvailableCount}/{_modelPool.MaxInstances} instances");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to initialize model pool: {ex.Message}");
                throw new InvalidOperationException($"Failed to initialize model pool: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Replace the current vector memory (for loading collections)
        /// </summary>
        public void SetVectorMemory(ILlmMemory newMemory)
        {
            _memory = newMemory ?? throw new ArgumentNullException(nameof(newMemory));
        }
        
        /// <summary>
        /// Update the collection name if using DatabaseVectorMemory.
        /// This allows switching between collections without creating a new memory instance.
        /// </summary>
        public void UpdateCollectionName(string collectionName)
        {
            if (_memory is DatabaseVectorMemory dbMemory)
            {
                dbMemory.SetCollectionName(collectionName);
            }
            else
            {
                Console.WriteLine($"[WARNING] Cannot update collection - memory is not DatabaseVectorMemory");
            }
        }
        
        /// <summary>
        /// Apply bot personality settings to generation settings.
        /// </summary>
        private void ApplyPersonalitySettings(BotPersonalityEntity? personality, GenerationSettings settings)
        {
            if (personality == null) return;
            
            // Apply personality-specific temperature if set
            if (personality.Temperature.HasValue)
            {
                settings.Temperature = personality.Temperature.Value;
            }
            
            // Can add more personality-specific settings here
        }

        /// <summary>
        /// Send a message using the current dashboard settings.
        /// </summary>
        public async Task<string> SendMessageAsync(
            string message,
            bool ragMode,
            bool debugMode,
            bool showPerformanceMetrics,
            GenerationSettings generationSettings,
            BotPersonalityEntity? personality = null,
            bool useGpu = false,
            int gpuLayers = 0,
            int timeoutSeconds = 30)
        {
            if (string.IsNullOrWhiteSpace(message))
                throw new ArgumentNullException(nameof(message));

            // Ensure pool is initialized before first use
            await EnsurePoolInitializedAsync();

            // Update timeout in the model pool
            _modelPool.TimeoutMs = timeoutSeconds * 1000;

            // Check pool health
            if (_modelPool.AvailableCount == 0)
            {
                return ExceptionMessageService.ModelPoolNoAvailableInstances(
                    _modelPool.AvailableCount, 
                    _modelPool.MaxInstances);
            }

            try
            {
                // Apply personality settings if provided
                ApplyPersonalitySettings(personality, generationSettings);
                
                // Override RAG mode if personality specifies it
                if (personality != null)
                {
                    ragMode = personality.EnableRag;
                }
                
                // Create LLM settings with GPU configuration
                var llmSettings = new LlmSettings
                {
                    UseGpu = useGpu,
                    GpuLayers = gpuLayers
                };

                // Create chat service with current settings
                var chatService = new AiChatServicePooled(
                    _memory,
                    _conversationMemory,
                    _modelPool,
                    generationSettings,
                    llmSettings,
                    debugMode: debugMode,
                    enableRag: ragMode,
                    showPerformanceMetrics: showPerformanceMetrics,
                    domainDetector: _domainDetector);

                // Send message and return response
                var response = await chatService.SendMessageAsync(message);

                // Clean up model-specific artifacts using extension method
                response = response.CleanModelArtifacts();

                // Save question and answer to database
                await SaveQuestionAnswerAsync(message, response);

                // Append performance metrics to response if enabled
                if (showPerformanceMetrics && chatService.LastMetrics != null)
                {
                    var metrics = chatService.LastMetrics;
                    response += DisplayService.FormatPerformanceMetrics(
                        metrics.TotalTimeMs,
                        metrics.PromptTokens,
                        metrics.CompletionTokens);
                }

                return response;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("No healthy instances"))
            {
                return ExceptionMessageService.ModelPoolExhausted();
            }
            catch (Exception ex)
            {
                return ExceptionMessageService.MessageProcessingError(
                    ex.GetType().Name, 
                    ex.Message);
            }
        }

        /// <summary>
        /// Save the question and answer to the database with the current LLM.
        /// </summary>
        private async Task SaveQuestionAnswerAsync(string question, string answer)
        {
            // Only save if repositories are available
            if (_questionRepository == null || _llmRepository == null)
            {
                return;
            }

            try
            {
                // Get current model name dynamically
                var currentModelFileName = _getCurrentModelName?.Invoke();
                
                if (string.IsNullOrWhiteSpace(currentModelFileName))
                {
                    Console.WriteLine("[WARNING] Cannot save question/answer: current model name is not available");
                    return;
                }

                // Get or create LLM entry
                var llmId = await _llmRepository.AddOrGetLlmAsync(currentModelFileName);

                // Save question and answer
                await _questionRepository.SaveQuestionAsync(question, answer, llmId);
            }
            catch (Exception ex)
            {
                // Log error but don't fail the chat operation
                Console.WriteLine($"[WARNING] Failed to save question/answer: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
