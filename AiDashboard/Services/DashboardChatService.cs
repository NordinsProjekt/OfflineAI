using System;
using System.Threading.Tasks;
using Application.AI.Chat;
using Application.AI.Extensions;
using Application.AI.Pooling;
using Application.AI.Utilities;
using Services.Configuration;
using Services.Interfaces;
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
        private readonly ModelInstancePool _modelPool;
        private readonly GameDetector? _gameDetector;
        private bool _disposed;

        public DashboardChatService(
            ILlmMemory memory,
            ILlmMemory conversationMemory,
            ModelInstancePool modelPool,
            GameDetector? gameDetector = null)
        {
            _memory = memory ?? throw new ArgumentNullException(nameof(memory));
            _conversationMemory = conversationMemory ?? throw new ArgumentNullException(nameof(conversationMemory));
            _modelPool = modelPool ?? throw new ArgumentNullException(nameof(modelPool));
            _gameDetector = gameDetector;
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
        /// Send a message using the current dashboard settings.
        /// </summary>
        public async Task<string> SendMessageAsync(
            string message,
            bool ragMode,
            bool debugMode,
            bool showPerformanceMetrics,
            GenerationSettings generationSettings,
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
                // Create chat service with current settings
                var chatService = new AiChatServicePooled(
                    _memory,
                    _conversationMemory,
                    _modelPool,
                    generationSettings,
                    _gameDetector,
                    debugMode: debugMode,
                    enableRag: ragMode,
                    showPerformanceMetrics: showPerformanceMetrics);

                // Send message and return response
                var response = await chatService.SendMessageAsync(message);

                // Clean up model-specific artifacts using extension method
                response = response.CleanModelArtifacts();

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

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
