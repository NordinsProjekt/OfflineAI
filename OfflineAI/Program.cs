using Services;
using OfflineAI.Examples;

namespace OfflineAI
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            await RunOriginalMode();
        }

        static async Task RunOriginalMode()
        {
            Console.WriteLine("\n=== Original CLI Mode ===");
            Console.WriteLine("Type your boardgames question\n");

            var memory = new SimpleMemory();
            var service = new AiChatService(memory);

            Console.WriteLine("Type 'exit' to quit, or ask questions:");

            while (true)
            {
                Console.Write("> ");
                var input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input)) continue;
                if (input.ToLower() == "exit") break;

                Console.Write("Response: ");
                Console.Write(await service.SendMessageStreamAsync(input));
                Console.WriteLine("\n");
            }
        }
    }
}