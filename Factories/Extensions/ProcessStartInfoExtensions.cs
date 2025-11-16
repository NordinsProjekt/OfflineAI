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

    public static ProcessStartInfo SetModel(this ProcessStartInfo processStartInfo, string modelPath)
    {
        processStartInfo.Arguments += $" -m \"{modelPath}\"";
        return processStartInfo;
    }
    
    public static Process Build(this ProcessStartInfo processStartInfo)
    {
        var process = new Process { StartInfo = processStartInfo };
        return process;
    }
}