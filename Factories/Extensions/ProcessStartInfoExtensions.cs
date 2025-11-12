using System.Diagnostics;
using System.Text.Json;

namespace Factories.Extensions;

public static class ProcessStartInfoExtensions
{
    public static ProcessStartInfo SetDefaultValues(this ProcessStartInfo processStartInfo)
    {
        processStartInfo.UseShellExecute = false;
        processStartInfo.RedirectStandardOutput = true;
        processStartInfo.RedirectStandardError = true;
        processStartInfo.CreateNoWindow = true;

        return processStartInfo;
    }

// Original methods for backwards compatibility
    public static ProcessStartInfo SetLlmCli(this ProcessStartInfo processStartInfo, string fileName)
    {
        processStartInfo.FileName = fileName;
        return processStartInfo;
    }

    public static ProcessStartInfo SetLlmContext(this ProcessStartInfo processStartInfo, string context)
    {
        // For very old versions, embed context in the prompt instead
        processStartInfo.Arguments += $" --system-prompt \"{context}\"";
        return processStartInfo;
    }

    public static ProcessStartInfo SetModel(this ProcessStartInfo processStartInfo, string modelPath)
    {
        processStartInfo.Arguments += $" -m \"{modelPath}\"";
        return processStartInfo;
    }

    public static ProcessStartInfo SetPrompt(this ProcessStartInfo processStartInfo, string prompt)
    {
        processStartInfo.Arguments += $" -p \"{prompt}\"";
        return processStartInfo;
    }

    public static ProcessStartInfo SetBoardGameSampling(this ProcessStartInfo processStartInfo,
        int maxTokens = 150,
        float temperature = 0.3f,
        int topK = 20,
        float topP = 0.8f)
    {
        processStartInfo.Arguments += $" -n {maxTokens}";
        processStartInfo.Arguments += $" --temp {temperature:F1}";
        processStartInfo.Arguments += $" --top-k {topK}";
        processStartInfo.Arguments += $" --top-p {topP:F1}";
        processStartInfo.Arguments += " --repeat-penalty 1.1";
        processStartInfo.Arguments += " --repeat-last-n 32";

        return processStartInfo;
    }
    
    public static Process Build(this ProcessStartInfo processStartInfo)
    {
        var process = new Process { StartInfo = processStartInfo };
        return process;
    }
}