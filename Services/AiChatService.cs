using Factories;
using Factories.Extensions;
using System.Diagnostics;
using System.Text;

namespace Services;

public class AiChatService(ILlmMemory memory, string? filePath = null, string? modelPath = null, int timeoutMs = 15000)
{
    private ILlmMemory Memory { get; } = memory;

    private readonly string _filePath = filePath ?? @"d:\tinyllama\llama-cli.exe";
    private readonly int _timeoutMs = timeoutMs;

    //private readonly string _modelPath = modelPath ?? @"d:\tinyllama\tinyllama-tinyQuest.gguf";
    //private readonly string _modelPath = modelPath ?? @"d:\tinyllama\tinyllama-boardgames-v2-f16.gguf";
    private readonly string _modelPath = modelPath ?? @"d:\tinyllama\tinyllama-boardgames-v2.gguf";
    private bool _disposed;

    public async Task<string> SendMessageStreamAsync(string question)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        var systemPrompt = BuildSystemPrompt();
        var process = LlmFactory.Create()
            .SetDefaultValues()
            .SetLlmCli(_filePath)
            .SetModel(_modelPath)
            .SetLlmContext(systemPrompt)
            .SendQuestion(question)
            .Build();

        return await ExecuteProcessAsync(process);
    }

    private string BuildSystemPrompt()
    {
        var memoryContext = Memory.ExportMemory();
        var basePrompt = "You are a boardgame expert, you have access to a few boardgames. " +
                         "Use short, concise answers because users will ask about rules and game mechanics. " +
                         "Be helpful but keep responses focused and under 150 tokens.";

        if (!string.IsNullOrWhiteSpace(memoryContext))
        {
            return $"{basePrompt}\n\nGame Knowledge:\n{memoryContext.Trim()}";
        }

        return basePrompt;
    }


    private async Task<string> ExecuteProcessAsync(Process process)
    {
        var output = new StringBuilder();
        var error = new StringBuilder();

        // Set up event handlers for capturing output
        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                output.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                error.AppendLine(e.Data);
            }
        };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) output.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) error.AppendLine(e.Data);
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Wait 5 seconds for output then kill
            Thread.Sleep(15000);

            if (!process.HasExited)
            {
                process.Kill(true);
            }

            var fullOutput = output.ToString();

            // Extract answer from <|assistant|> to the next prompt symbol or EOF
            var assistantTag = "<|assistant|>";
            var start = fullOutput.LastIndexOf(assistantTag);
            if (start >= 0)
            {
                start += assistantTag.Length;
                var end = fullOutput.IndexOf("<|", start); // optionally stop before next tag
                if (end == -1) end = fullOutput.Length;

                var answer = fullOutput.Substring(start, end - start).Trim();
                return answer;
            }

            return $"[FAILED TO PARSE]\n{fullOutput}\n{error}";
        }
        catch (Exception ex)
        {
            return $"[EXCEPTION] {ex.Message}";
        }
    }
}