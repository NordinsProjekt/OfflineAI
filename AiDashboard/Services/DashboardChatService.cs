using System;
using System.Threading.Tasks;
using Application.AI.Chat;
using Application.AI.Pooling;
using Services.Configuration;
using Services.Interfaces;

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
        private bool _disposed;

        public DashboardChatService(
            ILlmMemory memory,
            ILlmMemory conversationMemory,
            ModelInstancePool modelPool)
        {
            _memory = memory ?? throw new ArgumentNullException(nameof(memory));
            _conversationMemory = conversationMemory ?? throw new ArgumentNullException(nameof(conversationMemory));
            _modelPool = modelPool ?? throw new ArgumentNullException(nameof(modelPool));
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
                return $"[ERROR] Model pool has no available instances. Available: {_modelPool.AvailableCount}, Max: {_modelPool.MaxInstances}. " +
                       $"Check console for initialization errors.";
            }

            try
            {
                // Create chat service with current settings
                var chatService = new AiChatServicePooled(
                    _memory,
                    _conversationMemory,
                    _modelPool,
                    generationSettings,
                    debugMode: debugMode,
                    enableRag: ragMode,
                    showPerformanceMetrics: showPerformanceMetrics);

                // Send message and return response
                var response = await chatService.SendMessageAsync(message);

                // Clean up model-specific artifacts
                response = CleanModelArtifacts(response);

                // Append performance metrics to response if enabled
                if (showPerformanceMetrics && chatService.LastMetrics != null)
                {
                    var metrics = chatService.LastMetrics;
                    var tokensPerSec = metrics.CompletionTokens / (metrics.TotalTimeMs / 1000.0);
                    var totalTokens = metrics.PromptTokens + metrics.CompletionTokens;
                    
                    response += $"\n\n" +
                               $"============================\n" +
                               $"| **Performance Metrics**\n" +
                               $"============================\n" +
                               $"|  **Time:** {metrics.TotalTimeMs / 1000.0:F2}s\n" +
                               $"|  **Tokens:** {metrics.PromptTokens} prompt + {metrics.CompletionTokens} completion = {totalTokens} total\n" +
                               $"|  **Speed:** {tokensPerSec:F1} tokens/sec\n" +
                               $"============================";
                }

                return response;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("No healthy instances"))
            {
                return $"[ERROR] Model pool exhausted. This can happen if:\n" +
                       $"1. The LLM executable path is incorrect\n" +
                       $"2. The model file is corrupted or incompatible\n" +
                       $"3. Previous queries crashed the model processes\n" +
                       $"Try restarting the application.";
            }
            catch (Exception ex)
            {
                return $"[ERROR] {ex.GetType().Name}: {ex.Message}";
            }
        }

        /// <summary>
        /// Remove model-specific artifacts from responses.
        /// Handles Llama 3.2, TinyLlama, Mistral, Llama 2, Phi, ChatML, and other common model formats.
        /// </summary>
        private static string CleanModelArtifacts(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return response;

            // Remove special tokens from various model formats
            
            // Llama 3.2 tokens
            response = response.Replace("<|begin_of_text|>", "")
                              .Replace("<|end_of_text|>", "")
                              .Replace("<|eot_id|>", "")
                              .Replace("<|start_header_id|>", "")
                              .Replace("<|end_header_id|>", "");
            
            // TinyLlama / Phi tokens
            response = response.Replace("<|system|>", "")
                              .Replace("<|user|>", "")
                              .Replace("<|assistant|>", "")
                              .Replace("<|end|>", "")
                              .Replace("<|endoftext|>", "");

            // ChatML tokens
            response = response.Replace("<|im_start|>", "")
                              .Replace("<|im_end|>", "");

            // Mistral instruction tokens
            response = response.Replace("[INST]", "")
                              .Replace("[/INST]", "")
                              .Replace("<<SYS>>", "")
                              .Replace("<</SYS>>", "");

            // Llama 2 special tokens
            response = response.Replace("<s>", "")
                              .Replace("</s>", "");

            // Remove incomplete sentence markers
            if (response.EndsWith(">") && !response.EndsWith(">>"))
            {
                var lastCompleteStop = Math.Max(
                    response.LastIndexOf('.'),
                    Math.Max(response.LastIndexOf('!'), response.LastIndexOf('?'))
                );
                
                if (lastCompleteStop > 0 && lastCompleteStop < response.Length - 10)
                {
                    response = response.Substring(0, lastCompleteStop + 1);
                }
            }

            // Trim whitespace
            response = response.Trim();

            return response;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
