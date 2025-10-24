using System.Diagnostics;

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

    public static ProcessStartInfo SetLlmCli(this ProcessStartInfo processStartInfo, string fileName)
    {
        processStartInfo.FileName = fileName;

        return processStartInfo;
    }

    public static ProcessStartInfo SetLlmModelFile(this ProcessStartInfo processStartInfo, string arg)
    {
        processStartInfo.Arguments += @"--model "+arg;

        return processStartInfo;
    }

    public static ProcessStartInfo SetLlmContext(this ProcessStartInfo processStartInfo, string? context)
    {
        processStartInfo.Arguments += $" -sys \"{context}\"";

        return processStartInfo;
    }

    public static ProcessStartInfo SendQuestion(this ProcessStartInfo processStartInfo, string question)
    {
        processStartInfo.Arguments += $" --prompt \"{question}\" ";
        processStartInfo.Arguments += "--n-predict -2";

        return processStartInfo;
    }

    public static Process Build(this ProcessStartInfo processStartInfo)
    {
        var process = new Process { StartInfo = processStartInfo };

        return process;
    }
}
