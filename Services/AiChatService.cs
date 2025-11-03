using Factories;
using Factories.Extensions;
using System.Diagnostics;
using System.Text;
using MemoryLibrary.Models;

namespace Services;

public class AiChatService(
    ILlmMemory memory,
    ILlmMemory conversationMemory,
    string filePath,
    string modelPath,
    int timeoutMs = 30000)
{
    private ILlmMemory Memory { get; } = memory;
    private ILlmMemory ConversationHistory { get; } = conversationMemory;
    private bool _disposed;

    public async Task<string> SendMessageStreamAsync(string question)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ConversationHistory.ImportMemory(new MemoryFragment("User", question));

        var systemPrompt = BuildSystemPrompt();
        var process = LlmFactory.Create()
            .SetDefaultValues()
            .SetLlmCli(filePath)
            .SetModel(modelPath)
            .SetLlmContext(systemPrompt)
            .SetBoardGameSampling(maxTokens: 1000, temperature: 0.3f, topK: 20, topP: 0.8f)
            .SetPrompt(question)
            .Build();

        return await ExecuteProcessAsync(process);
    }

    private string BuildSystemPrompt()
    {
        const string basePrompt =
            "You are a helpful AI assistant. Answer questions accurately and concisely based on the provided context.";

        var prompt = new StringBuilder(basePrompt);

        prompt.AppendLine("\n\nContext:");
        prompt.AppendLine(Memory.ToString());

        // Include conversation history if it exists
        var conversationHistoryText = ConversationHistory.ToString();
        if (string.IsNullOrWhiteSpace(conversationHistoryText)) return prompt.ToString();

        prompt.AppendLine("\n\nRecent conversation:");
        prompt.AppendLine(conversationHistoryText);

        return prompt.ToString();
    }


    private async Task<string> ExecuteProcessAsync(Process process)
    {
        var output = new StringBuilder();
        var error = new StringBuilder();
        var lastOutputTime = DateTime.UtcNow;
        var assistantStartFound = false;
        var assistantTag = "<|assistant|>";
        var streamingOutput = new StringBuilder();

        var outputLock = new object();

        // Set up event handlers for capturing output
        process.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;

            lock (outputLock)
            {
                output.AppendLine(e.Data);
                lastOutputTime = DateTime.UtcNow;

                // Check if we've found the assistant tag and start streaming from there
                var fullText = output.ToString();
                if (!assistantStartFound)
                {
                    var assistantIndex = fullText.IndexOf(assistantTag);
                    if (assistantIndex < 0) return;

                    assistantStartFound = true;
                    var startIndex = assistantIndex + assistantTag.Length;
                    var textToStream = fullText.Substring(startIndex);
                    streamingOutput.Append(textToStream);
                    Console.Write($"\n{textToStream}");
                }
                else
                {
                    // Already found assistant tag, stream new content
                    Console.Write(e.Data + Environment.NewLine);
                    streamingOutput.AppendLine(e.Data);
                }
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                error.AppendLine(e.Data);
            }
        };

        Console.Write("Loading: ");
        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Monitor for 2 seconds of inactivity
            while (!process.HasExited)
            {
                await Task.Delay(1000); // Check every 100ms
                if (!assistantStartFound) Console.Write(".");

                TimeSpan timeSinceLastOutput;
                lock (outputLock)
                {
                    timeSinceLastOutput = DateTime.UtcNow - lastOutputTime;
                }

                if (assistantStartFound && timeSinceLastOutput.TotalSeconds >= 3)
                {
                    break;
                }

                // Also respect the overall timeout
                if (timeSinceLastOutput.TotalMilliseconds >= timeoutMs)
                {
                    break;
                }
            }

            if (!process.HasExited)
            {
                process.Kill(true);
            }

            // Extract clean answer from streaming output
            var cleanAnswer = streamingOutput.ToString();

            // Remove any trailing tags or prompt symbols
            var endTagIndex = cleanAnswer.IndexOf("<|");
            if (endTagIndex >= 0)
            {
                cleanAnswer = cleanAnswer.Substring(0, endTagIndex);
            }

            cleanAnswer = cleanAnswer.Trim();

            // Store only the clean answer in conversation history
            if (!string.IsNullOrWhiteSpace(cleanAnswer))
            {
                ConversationHistory.ImportMemory(new MemoryFragment("AI", cleanAnswer));
            }
        }
        catch (Exception ex)
        {
            return $"[EXCEPTION] {ex.Message}";
        }

        return "Conversation ended";
    }
}