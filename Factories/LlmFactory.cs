using System.Diagnostics;

namespace Factories;

public static class LlmFactory
{
    public static ProcessStartInfo Create()
    {
        return new ProcessStartInfo();
    }
}
