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

    public static ProcessStartInfo SendQuestion(this ProcessStartInfo processStartInfo, string question)
    {
        processStartInfo.Arguments += $" -p \"{question}\"";
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

    public static ProcessStartInfo SetSystemPrompt(this ProcessStartInfo processStartInfo, string systemPrompt)
    {
        processStartInfo.Arguments += $" --system \"{systemPrompt}\"";
        return processStartInfo;
    }

    // Advanced methods (for newer versions)
    public static ProcessStartInfo ConfigureOptimalLlm(this ProcessStartInfo processStartInfo, string fileName)
    {
        processStartInfo.FileName = fileName;
        processStartInfo.Arguments = "";
        return processStartInfo;
    }

    public static ProcessStartInfo SetPerformanceOptions(this ProcessStartInfo processStartInfo,
        int threads = -1,
        int contextSize = 4096,
        int batchSize = 512,
        int gpuLayers = -1)
    {
        if (threads > 0)
            processStartInfo.Arguments += $" -t {threads}";

        processStartInfo.Arguments += $" -c {contextSize}";
        processStartInfo.Arguments += $" -b {batchSize}";

        if (gpuLayers > 0)
            processStartInfo.Arguments += $" -ngl {gpuLayers}";

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

    public static ProcessStartInfo SetCreativeSampling(this ProcessStartInfo processStartInfo,
        int maxTokens = 256,
        float temperature = 0.7f,
        int topK = 40,
        float topP = 0.9f)
    {
        processStartInfo.Arguments += $" -n {maxTokens}";
        processStartInfo.Arguments += $" --temp {temperature:F1}";
        processStartInfo.Arguments += $" --top-k {topK}";
        processStartInfo.Arguments += $" --top-p {topP:F1}";

        return processStartInfo;
    }

    public static ProcessStartInfo BuildBoardGameAssistant(this ProcessStartInfo processStartInfo,
        string llamaPath,
        string modelPath,
        string systemPrompt,
        string userPrompt,
        int maxTokens = 150,
        bool useGpu = true,
        string? cacheFile = null)
    {
        return processStartInfo
            .ConfigureOptimalLlm(llamaPath)
            .SetModel(modelPath)
            .SetPerformanceOptions(threads: Environment.ProcessorCount, gpuLayers: useGpu ? -1 : 0)
            .SetBoardGameSampling(maxTokens: maxTokens)
            .SetSystemPrompt(systemPrompt)
            .SetPrompt(userPrompt);
    }
    
    public static Process Build(this ProcessStartInfo processStartInfo)
    {
        var process = new Process { StartInfo = processStartInfo };
        return process;
    }
}