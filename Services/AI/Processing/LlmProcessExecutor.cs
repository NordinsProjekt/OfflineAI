using System.Diagnostics;
using Entities;
using Services.Interfaces;

namespace Services.AI.Processing;

/// <summary>
/// Handles execution of LLM processes and output stream parsing.
/// Separates process management concerns from chat service logic.
/// </summary>
public class LlmProcessExecutor(int timeoutMs = 30000, ILlmMemory? conversationHistory = null)
{
    /// <summary>
    /// Executes an LLM process and captures its streaming output.
    /// </summary>
    /// <param name="process">The configured process to execute</param>
    /// <returns>Status message or exception details</returns>
    public async Task<string> ExecuteAsync(Process process)
    {
        var outputCapture = new ProcessOutputCapture();
        var streamMonitor = new StreamingOutputMonitor(timeoutMs);

        ConfigureProcessHandlers(process, outputCapture, streamMonitor);

        Console.Write("Loading: ");
        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await MonitorProcessAsync(process, streamMonitor);

            KillProcessIfRunning(process);

            var cleanAnswer = CleanOutput(outputCapture.StreamingOutput.ToString());

            StoreInConversationHistory(cleanAnswer);

            return "Conversation ended";
        }
        catch (Exception ex)
        {
            return $"[EXCEPTION] {ex.Message}";
        }
    }

    private void ConfigureProcessHandlers(
        Process process,
        ProcessOutputCapture outputCapture,
        StreamingOutputMonitor streamMonitor)
    {
        process.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;

            lock (streamMonitor.OutputLock)
            {
                outputCapture.Output.AppendLine(e.Data);
                streamMonitor.UpdateLastOutputTime();

                HandleStreamingOutput(outputCapture, streamMonitor, e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                outputCapture.Error.AppendLine(e.Data);
            }
        };
    }

    private void HandleStreamingOutput(
        ProcessOutputCapture outputCapture,
        StreamingOutputMonitor streamMonitor,
        string data)
    {
        var fullText = outputCapture.Output.ToString();

        if (!streamMonitor.AssistantStartFound)
        {
            var assistantIndex = fullText.IndexOf(streamMonitor.AssistantTag);
            if (assistantIndex < 0) return;

            streamMonitor.MarkAssistantFound();
            var startIndex = assistantIndex + streamMonitor.AssistantTag.Length;
            var textToStream = fullText.Substring(startIndex);
            outputCapture.StreamingOutput.Append(textToStream);
            Console.Write($"\n{textToStream}");
        }
        else
        {
            // Already found assistant tag, stream new content
            Console.Write(data + Environment.NewLine);
            outputCapture.StreamingOutput.AppendLine(data);
        }
    }

    private async Task MonitorProcessAsync(Process process, StreamingOutputMonitor streamMonitor)
    {
        while (!process.HasExited)
        {
            await Task.Delay(1000);
            
            if (!streamMonitor.AssistantStartFound)
            {
                Console.Write(".");
            }

            var timeSinceLastOutput = streamMonitor.GetTimeSinceLastOutput();

            // Break if inactivity timeout reached after assistant started
            if (streamMonitor.AssistantStartFound && timeSinceLastOutput.TotalSeconds >= 3)
            {
                break;
            }

            // Break if overall timeout reached
            if (timeSinceLastOutput.TotalMilliseconds >= timeoutMs)
            {
                break;
            }
        }
    }

    private static void KillProcessIfRunning(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(true);
        }
    }

    private static string CleanOutput(string output)
    {
        var cleanAnswer = output;

        // Remove any trailing tags or prompt symbols
        var endTagIndex = cleanAnswer.IndexOf("<|");
        if (endTagIndex >= 0)
        {
            cleanAnswer = cleanAnswer.Substring(0, endTagIndex);
        }

        return cleanAnswer.Trim();
    }

    private void StoreInConversationHistory(string cleanAnswer)
    {
        if (conversationHistory != null && !string.IsNullOrWhiteSpace(cleanAnswer))
        {
            conversationHistory.ImportMemory(new MemoryFragment("AI", cleanAnswer));
        }
    }
}