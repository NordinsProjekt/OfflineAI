using System.Diagnostics;
using System.Text;
using Factories;
using Factories.Extensions;

namespace Application.AI.Processing;

/// <summary>
/// Manages a persistent LLM process that stays loaded in memory and can handle multiple requests.
/// This avoids the overhead of loading/unloading the model for each conversation.
/// 
/// NOTE: This is a simplified implementation. For production use with llama-cli interactive mode,
/// you may need to use llama.cpp server mode instead (--server flag) with HTTP API.
/// </summary>
public class PersistentLlmProcess : IPersistentLlmProcess
{
    private readonly string _llmPath;
    private readonly string _modelPath;
    private int _timeoutMs;
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private bool _disposed;

    public bool IsHealthy { get; private set; } = true;
    public DateTime LastUsed { get; private set; } = DateTime.UtcNow;
    
    public int TimeoutMs 
    { 
        get => _timeoutMs;
        set
        {
            if (value < 1000 || value > 300000)
                throw new ArgumentOutOfRangeException(nameof(value), "Timeout must be between 1 and 300 seconds (1000-300000ms)");
            _timeoutMs = value;
        }
    }

    private PersistentLlmProcess(string llmPath, string modelPath, int timeoutMs)
    {
        _llmPath = llmPath;
        _modelPath = modelPath;
        _timeoutMs = timeoutMs;
    }

    /// <summary>
    /// Creates a persistent LLM process manager.
    /// Each query will spawn a new process but the manager stays alive.
    /// For true persistent processes, consider using llama.cpp server mode.
    /// </summary>
    public static async Task<PersistentLlmProcess> CreateAsync(
        string llmPath, 
        string modelPath, 
        int timeoutMs = 30000)
    {
        // Validate paths exist
        if (!File.Exists(llmPath))
            throw new FileNotFoundException($"LLM executable not found: {llmPath}");
        
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"Model file not found: {modelPath}");

        await Task.CompletedTask;
        return new PersistentLlmProcess(llmPath, modelPath, timeoutMs);
    }

    /// <summary>
    /// Sends a query using the configured LLM.
    /// Thread-safe: only one request can be processed at a time per instance.
    /// </summary>
    public async Task<string> QueryAsync(
        string systemPrompt, 
        string userQuestion,
        int maxTokens = 200,
        float temperature = 0.3f,
        int topK = 30,
        float topP = 0.85f,
        float repeatPenalty = 1.15f,
        float presencePenalty = 0.2f,
        float frequencyPenalty = 0.2f,
        bool useGpu = false,
        int gpuLayers = 0)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PersistentLlmProcess));

        if (!IsHealthy)
            throw new InvalidOperationException("Process manager is not healthy");

        await _requestLock.WaitAsync();
        try
        {
            LastUsed = DateTime.UtcNow;

            // Build the full prompt
            var fullPrompt = $"{systemPrompt}\n\nUser: {userQuestion}\nAssistant:";

            // Create process for this query
            var processInfo = LlmFactory.CreateForLlama(_llmPath, _modelPath);
            
            // Add the prompt directly to arguments
            processInfo.Arguments += $" -p \"{fullPrompt}\"";
            
            // Set context size to prevent memory issues (2048 tokens = ~1500 chars of context)
            processInfo.Arguments += $" -c 2048";
            
            // Configure GPU offloading
            // -ngl 0 = CPU only (no GPU layers)
            // -ngl > 0 = offload that many layers to GPU
            if (useGpu && gpuLayers > 0)
            {
                processInfo.Arguments += $" -ngl {gpuLayers}";
            }
            else
            {
                // Force CPU-only execution
                processInfo.Arguments += $" -ngl 0";
            }
            
            // Apply generation parameters
            processInfo.Arguments += $" -n {maxTokens}";
            processInfo.Arguments += $" --temp {temperature:F2}";
            processInfo.Arguments += $" --top-p {topP:F2}";
            processInfo.Arguments += $" --top-k {topK}";
            processInfo.Arguments += $" --repeat-penalty {repeatPenalty:F2}";
            processInfo.Arguments += $" --presence-penalty {presencePenalty:F2}";
            processInfo.Arguments += $" --frequency-penalty {frequencyPenalty:F2}";
            
            var process = processInfo.Build();

            // Execute and capture output
            var response = await ExecuteProcessAsync(process);
            return CleanResponse(response);
        }
        catch (Exception ex)
        {
            IsHealthy = false;
            throw new InvalidOperationException($"Failed to query LLM: {ex.Message}", ex);
        }
        finally
        {
            _requestLock.Release();
        }
    }

    private async Task<string> ExecuteProcessAsync(Process process)
    {
        var output = new StringBuilder();
        var error = new StringBuilder();
        var assistantStarted = false;
        var endMarkerDetected = false; // Flag to signal end of generation
        var lastOutputTime = DateTime.UtcNow;
        var processStartTime = DateTime.UtcNow;
        var outputLock = new object();
        var fullOutput = new StringBuilder(); // Keep track of all output for debugging

        // Use fixed 10-second pause timeout
        // This detects when the LLM has stopped generating (paused for more than 10 seconds)
        const int pauseTimeoutMs = 10000;  // 10 seconds

        process.OutputDataReceived += (sender, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;

            lock (outputLock)
            {
                lastOutputTime = DateTime.UtcNow;
                fullOutput.AppendLine(e.Data); // Keep full output for analysis

                // Look for assistant tag - support multiple formats
                if (!assistantStarted)
                {
                    var fullText = output.ToString() + e.Data;
                    
                    // Try different assistant tag formats used by different models
                    // Order matters - check more specific patterns first!
                    foreach (var (pattern, marker) in LlmOutputPatterns.AssistantPatterns)
                    {
                        var assistantIndex = fullText.IndexOf(pattern, StringComparison.Ordinal); // Use Ordinal for exact match
                        if (assistantIndex >= 0)
                        {
                            assistantStarted = true;
                            var startIndex = assistantIndex + marker.Length;
                            output.Clear();
                            
                            // Extract text after the assistant marker
                            var textAfterMarker = fullText.Substring(startIndex).TrimStart('\r', '\n', ' ');
                            output.Append(textAfterMarker);
                            
                            Console.WriteLine($"\n[Detected format: {pattern}]"); // Debug info
                            Console.Write(textAfterMarker); // Write the first chunk
                            break;
                        }
                    }
                }
                else
                {
                    // Check if this chunk contains an end marker
                    foreach (var endMarker in LlmOutputPatterns.EndMarkers)
                    {
                        if (e.Data.Contains(endMarker))
                        {
                            // Extract text before the end marker
                            var endIndex = e.Data.IndexOf(endMarker, StringComparison.Ordinal);
                            if (endIndex > 0)
                            {
                                var finalText = e.Data.Substring(0, endIndex);
                                Console.Write(finalText);
                                output.Append(finalText);
                            }
                            Console.WriteLine($"\n[End marker detected: {endMarker}]");
                            endMarkerDetected = true;
                            break;
                        }
                    }
                    
                    // Only append if no end marker detected
                    if (!endMarkerDetected)
                    {
                        // Stream output to console as it arrives
                        Console.Write(e.Data);
                        output.Append(e.Data); // Use Append instead of AppendLine to preserve formatting
                    }
                }
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                lock (outputLock)
                {
                    error.AppendLine(e.Data);
                }
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        Console.Write("Loading");

        // Wait for completion with timeout
        while (!process.HasExited)
        {
            await Task.Delay(1000);
            if (!assistantStarted) Console.Write(".");
            
            var timeSinceOutput = (DateTime.UtcNow - lastOutputTime).TotalMilliseconds;
            var totalTime = (DateTime.UtcNow - processStartTime).TotalMilliseconds;
            
            // Check if end marker was detected
            if (endMarkerDetected)
            {
                Console.WriteLine($"\n[Generation complete - end marker found]");
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                    }
                }
                catch { /* Process may have already exited */ }
                break;
            }
            
            // If we've started getting assistant output and there's a pause, consider done
            if (assistantStarted && timeSinceOutput > pauseTimeoutMs)
            {
                Console.WriteLine($"\n[Generation complete - {pauseTimeoutMs/1000}s pause detected]");
                break;
            }

            // Overall timeout - use the configured timeout value
            if (totalTime > _timeoutMs)
            {
                Console.WriteLine($"\n[TIMEOUT after {totalTime/1000:F1}s]");
                break;
            }
        }

        // Give a small delay to ensure all output is captured
        if (assistantStarted)
        {
            await Task.Delay(500);
        }

        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }

        // Wait for output/error streams to finish
        process.WaitForExit();

        var result = output.ToString();
        var errorText = error.ToString();

        // Debug: If result is empty but we have full output, log for analysis
        if (string.IsNullOrWhiteSpace(result) && fullOutput.Length > 0)
        {
            Console.WriteLine($"\n[WARNING] No assistant marker detected. Full output:");
            Console.WriteLine(fullOutput.ToString().Substring(0, Math.Min(500, fullOutput.Length)));
        }

        if (!string.IsNullOrWhiteSpace(errorText) && !errorText.Contains("ggml_cuda_init") && !errorText.Contains("load_backend"))
        {
            // Only log errors that aren't informational backend messages
            Console.WriteLine($"\n[LLM Error]: {errorText}");
        }

        process.Dispose();
        return result;
    }

    private static string CleanResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return string.Empty;

        var cleaned = response.Trim();

        // Remove trailing special tokens used by different models
        foreach (var marker in LlmOutputPatterns.EndMarkers)
        {
            var endTagIndex = cleaned.IndexOf(marker, StringComparison.Ordinal);
            if (endTagIndex >= 0)
            {
                cleaned = cleaned.Substring(0, endTagIndex);
            }
        }

        // Remove incomplete sentence at the end if it ends with '>'
        // This happens when generation is cut off mid-token
        if (cleaned.EndsWith(">") && !cleaned.EndsWith(">>"))
        {
            var lastCompleteStop = Math.Max(
                cleaned.LastIndexOf('.'),
                Math.Max(cleaned.LastIndexOf('!'), cleaned.LastIndexOf('?'))
            );
            
            if (lastCompleteStop > 0)
            {
                cleaned = cleaned.Substring(0, lastCompleteStop + 1);
            }
        }

        return cleaned.Trim();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _requestLock?.Dispose();
    }
}

