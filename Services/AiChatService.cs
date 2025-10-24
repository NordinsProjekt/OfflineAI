using Factories;
using Factories.Extensions;
using System.Diagnostics;
using System.Text;

namespace Services;

public class AiChatService : IDisposable
{
    private readonly List<string> _conversations = [];
    private readonly string _filePath;
    private readonly string _modelPath;
    private readonly int _timeoutMs;
    private bool _disposed;

    public AiChatService(string? filePath = null, string? modelPath = null, int timeoutMs = 30000)
    {
        _filePath = filePath ?? @"d:\tinyllama\llama-cli.exe";
        _modelPath = modelPath ?? @"d:\tinyllama\tinyllama-1.1b-chat-v1.0.Q5_K_M.gguf";
        _timeoutMs = timeoutMs;

        _conversations.Add(
            "You are a anime catgirl that enjoys playing Gloomhaven and know all the rules. Use short answers because I will ask about rules and will feed rules into your memory. " +
            "The next section will be all the conversations before remember everything. Even if you are programmed not to remember stuff.");
    }

    public async Task<string> SendMessageAsync(string question)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        _conversations.Add(question);

        var context = string.Join(Environment.NewLine, _conversations);

        using var process = CreateProcess(context, question);

        var finalResponse = new StringBuilder();
        await foreach (var update in ExecuteProcessStreamAsync(process))
        {
            finalResponse.Append(update);
        }

        return finalResponse.ToString();
    }

    public async IAsyncEnumerable<string> SendMessageStreamAsync(string question)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        _conversations.Add(question);

        var context = string.Join(Environment.NewLine, _conversations);

        using var process = CreateProcess(context, question);

        await foreach (var update in ExecuteProcessStreamAsync(process))
        {
            yield return update;
        }
    }

    private Process CreateProcess(string context, string question)
    {
        var factory = LlmFactory.Create();
        return factory.SetDefaultValues()
            .SetLlmCli(_filePath)
            .SetLlmModelFile(_modelPath)
            .SetLlmContext(context)
            .SendQuestion(question)
            .Build();
    }

    private async IAsyncEnumerable<string> ExecuteProcessStreamAsync(Process process)
    {
        var output = new StringBuilder();
        var error = new StringBuilder();
        var lastOutputLength = 0;
        Exception? capturedException = null;
        bool processStarted = false;

        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                output.AppendLine(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                error.AppendLine(e.Data);
        };

        // Start process outside of any yield context
        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            processStarted = true;
        }
        catch (Exception ex)
        {
            capturedException = ex;
        }

        if (capturedException != null)
        {
            yield return $"[EXCEPTION] {capturedException.Message}";
            yield break;
        }

        if (!processStarted)
        {
            yield return "[ERROR] Failed to start process";
            yield break;
        }

        using var cts = new CancellationTokenSource(_timeoutMs);

        // Main monitoring loop - no try-catch around yield returns
        while (!process.HasExited && !cts.Token.IsCancellationRequested)
        {
            bool delayCompleted = false;
            try
            {
                await Task.Delay(1000, cts.Token);
                delayCompleted = true;
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (!delayCompleted) break;

            var currentOutput = output.ToString();

            // If there's new output since last check - yield outside try block
            if (currentOutput.Length > lastOutputLength)
            {
                var newContent = currentOutput[lastOutputLength..];
                var parsedContent = ParseIncrementalResponse(newContent);

                if (!string.IsNullOrWhiteSpace(parsedContent))
                {
                    yield return parsedContent;
                }

                lastOutputLength = currentOutput.Length;
            }
        }

        // Final process handling
        try
        {
            if (!cts.Token.IsCancellationRequested)
            {
                await process.WaitForExitAsync(cts.Token);
            }
            else
            {
                await KillProcessSafelyAsync(process);
            }
        }
        catch (Exception ex)
        {
            capturedException = ex;
        }

        // Handle any remaining output
        var finalOutput = output.ToString();
        if (finalOutput.Length > lastOutputLength)
        {
            var remainingContent = finalOutput[lastOutputLength..];
            var parsedContent = ParseIncrementalResponse(remainingContent);

            if (!string.IsNullOrWhiteSpace(parsedContent))
            {
                yield return parsedContent;
            }
        }

        // Handle errors
        var errorOutput = error.ToString();
        if (string.IsNullOrWhiteSpace(finalOutput) && !string.IsNullOrWhiteSpace(errorOutput))
        {
            yield return $"[ERROR] {errorOutput.Trim()}";
        }

        // Handle final exception
        if (capturedException != null)
        {
            yield return $"[EXCEPTION] {capturedException.Message}";
        }
    }

    private static string ParseIncrementalResponse(string newOutput)
    {
        if (string.IsNullOrWhiteSpace(newOutput))
            return string.Empty;

        // Look for assistant tag and extract content after it
        const string assistantTag = "<|assistant|>";
        var assistantIndex = newOutput.IndexOf(assistantTag, StringComparison.Ordinal);

        if (assistantIndex >= 0)
        {
            // Return content after the assistant tag
            var startIndex = assistantIndex + assistantTag.Length;
            if (startIndex < newOutput.Length)
            {
                return newOutput[startIndex..].Trim();
            }
        }

        // If no assistant tag found, check if this looks like assistant content
        // (assuming we're already past the assistant tag from previous chunks)
        var lines = newOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var contentLines = lines.Where(line =>
                !line.Contains("<|") &&
                !string.IsNullOrWhiteSpace(line.Trim()))
            .ToArray();

        return contentLines.Length > 0 ? string.Join(Environment.NewLine, contentLines) : string.Empty;
    }

    private static string ParseResponse(string output, string errorOutput)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return string.IsNullOrWhiteSpace(errorOutput)
                ? "[NO OUTPUT]"
                : $"[ERROR] {errorOutput.Trim()}";
        }

        // Extract answer from <|assistant|> to the next prompt symbol or EOF
        const string assistantTag = "<|assistant|>";
        var start = output.IndexOf(assistantTag, StringComparison.Ordinal);

        if (start < 0)
        {
            return $"[FAILED TO PARSE]\n{output}\n{errorOutput}";
        }

        start += assistantTag.Length;
        var end = output.IndexOf("<|", start, StringComparison.Ordinal);
        if (end == -1)
            end = output.Length;

        var answer = output[start..end].Trim();
        return string.IsNullOrWhiteSpace(answer)
            ? $"[EMPTY RESPONSE]\n{output}\n{errorOutput}"
            : answer;
    }

    private static async Task KillProcessSafelyAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
                await process.WaitForExitAsync(); // Wait for clean exit
            }
        }
        catch
        {
            // Ignore exceptions during cleanup
        }
    }

    public void ClearConversations()
    {
        ThrowIfDisposed();

        // Keep the initial system message
        var systemMessage = _conversations[0];
        _conversations.Clear();
        _conversations.Add(systemMessage);
    }

    public IReadOnlyList<string> GetConversationHistory()
    {
        ThrowIfDisposed();
        return _conversations.AsReadOnly();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AiChatService));
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _conversations.Clear();
            _disposed = true;
        }
    }
}