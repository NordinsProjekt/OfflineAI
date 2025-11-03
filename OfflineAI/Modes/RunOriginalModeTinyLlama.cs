using MemoryLibrary.Models;
using Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OfflineAI.Modes;

internal static class RunOriginalModeTinyLlama
{
    internal static async Task RunOriginalMode()
    {
        Console.WriteLine("\n=== Original CLI Mode ===");
        Console.WriteLine("=== Best AI BOT EVER ===");
        Console.WriteLine("Type your questions\n");

        var memory = new SimpleMemory();
        var conversationMemory = new SimpleMemory();

        var fileReader = new FileMemoryLoaderService();
        await fileReader.LoadFromFileAsync(@"d:\tinyllama\trhunt_rules.txt", memory);

        var service = new AiChatService(memory, 
            conversationMemory,
            @"d:\tinyllama\llama-cli.exe",
            @"d:\tinyllama\tinyllama-1.1b-chat-v1.0.Q5_K_M.gguf");

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