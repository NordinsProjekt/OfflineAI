using Services;
using OfflineAI.Examples;

namespace OfflineAI
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            using var service = new AiChatService();

            Console.WriteLine("LLM Chat Interface");
            Console.WriteLine("Commands:");
            Console.WriteLine("  'stream' - Switch to streaming mode");
            Console.WriteLine("  'normal' - Switch to normal mode");
            Console.WriteLine("  'example' - Run streaming example");
            Console.WriteLine("  'compare' - Compare streaming vs normal");
            Console.WriteLine("  'exit' - Exit application");
            Console.WriteLine("  Or just type your question\n");

            var streamingMode = true;
            Console.WriteLine("Current mode: streaming\n");

            while (true)
            {
                Console.Write("> ");
                var input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input)) continue;

                switch (input.ToLower())
                {
                    case "exit":
                        return;

                    case "stream":
                        streamingMode = true;
                        Console.WriteLine("Switched to streaming mode\n");
                        continue;

                    case "normal":
                        streamingMode = false;
                        Console.WriteLine("Switched to normal mode\n");
                        continue;

                    case "example":
                        await StreamingExample.RunStreamingExample();
                        Console.WriteLine();
                        continue;

                    case "compare":
                        await StreamingExample.RunComparisonExample();
                        Console.WriteLine();
                        continue;
                }

                // Process as question
                if (streamingMode)
                {
                    Console.Write("Response: ");
                    await foreach (var update in service.SendMessageStreamAsync(input))
                    {
                        Console.Write(update);
                        Console.Out.Flush(); // Ensure immediate display
                    }

                    Console.WriteLine(); // New line after complete response
                }
                else
                {
                    var response = await service.SendMessageAsync(input);
                    Console.WriteLine($"Response: {response}");
                }

                Console.WriteLine(); // Extra spacing
            }
        }
    }
}