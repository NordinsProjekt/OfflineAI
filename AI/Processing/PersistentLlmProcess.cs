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
public class PersistentLlmProcess : IDisposable
{
    private readonly string _llmPath;
    private readonly string _modelPath;
    private readonly int _timeoutMs;
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private bool _disposed;

    public bool IsHealthy { get; private set; } = true;
    public DateTime LastUsed { get; private set; } = DateTime.UtcNow;

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
    public async Task<string> QueryAsync(string systemPrompt, string userQuestion)
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
            var processInfo = LlmFactory.CreateForLlama(_llmPath, _modelPath)
                .SetPrompt(fullPrompt);
            
            // Reduce hallucination with more conservative parameters
            processInfo.Arguments += " -n 200";           // Max 200 tokens
            processInfo.Arguments += " --temp 0.3";       // Lower temperature = more focused (was 0.4)
            processInfo.Arguments += " --top-p 0.85";     // More conservative sampling (was default)
            processInfo.Arguments += " --top-k 30";       // Limit vocabulary choices
            processInfo.Arguments += " --repeat-penalty 1.15";  // Penalize repetition
            processInfo.Arguments += " --presence-penalty 0.2"; // Reduce adding new concepts
            processInfo.Arguments += " --frequency-penalty 0.2"; // Discourage repeating patterns
            
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
        var assistantStarted = false;
        var lastOutputTime = DateTime.UtcNow;
        var processStartTime = DateTime.UtcNow;

        process.OutputDataReceived += (sender, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;

            lastOutputTime = DateTime.UtcNow;

            // Look for assistant tag
            if (!assistantStarted)
            {
                var fullText = output.ToString() + e.Data;
                var assistantIndex = fullText.IndexOf("Assistant:", StringComparison.OrdinalIgnoreCase);
                if (assistantIndex >= 0)
                {
                    assistantStarted = true;
                    var startIndex = assistantIndex + "Assistant:".Length;
                    output.Clear();
                    output.Append(fullText.Substring(startIndex));
                    Console.Write("\n"); // New line after loading indicator
                }
            }
            else
            {
                // Stream output to console as it arrives
                Console.Write(e.Data);
                output.AppendLine(e.Data);
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
            
            // If we've started getting assistant output and there's a pause, consider done
            if (assistantStarted && timeSinceOutput > 3000)
            {
                break;
            }

            // Overall timeout - use the configured timeout value
            if (totalTime > _timeoutMs)
            {
                Console.WriteLine($"\n[TIMEOUT after {totalTime/1000:F1}s]");
                break;
            }
        }

        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }

        process.Dispose();
        return output.ToString();
    }

    private static string CleanResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return string.Empty;

        var cleaned = response.Trim();

        // Remove trailing tags
        var endTagIndex = cleaned.IndexOf("<|");
        if (endTagIndex >= 0)
        {
            cleaned = cleaned.Substring(0, endTagIndex);
        }

        // Remove "User:" if it appears
        var userIndex = cleaned.IndexOf("User:", StringComparison.OrdinalIgnoreCase);
        if (userIndex >= 0)
        {
            cleaned = cleaned.Substring(0, userIndex);
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

