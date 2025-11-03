using Services;
using MemoryLibrary.Models;
using OfflineAI.Modes;

namespace OfflineAI
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            await RunOriginalModeTinyLlama.RunOriginalMode();
        }
    }
}