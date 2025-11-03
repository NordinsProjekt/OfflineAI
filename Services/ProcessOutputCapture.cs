using System.Text;

namespace Services;

/// <summary>
/// Captures output streams from the process.
/// </summary>
internal class ProcessOutputCapture
{
    public StringBuilder Output { get; } = new();
    public StringBuilder Error { get; } = new();
    public StringBuilder StreamingOutput { get; } = new();
}