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
        
        // CRITICAL: llama.cpp outputs UTF-8, but Windows defaults to Windows-1252
        // This causes Swedish characters (å, ä, ö) to display as garbage (├Ñ, ├ñ, etc.)
        // Force UTF-8 encoding for both output and error streams
        processStartInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;
        processStartInfo.StandardErrorEncoding = System.Text.Encoding.UTF8;

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