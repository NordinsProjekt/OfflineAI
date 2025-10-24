using Services;

namespace OfflineAI.Examples;

public static class StreamingExample
{
    public static async Task RunStreamingExample()
    {
        using var service = new AiChatService();
        
        Console.WriteLine("=== Streaming Example ===");
      Console.WriteLine("This will show LLM output in real-time as it's generated.\n");
 
   var question = "What are the basic rules for movement in Gloomhaven?";
Console.WriteLine($"Question: {question}");
        Console.WriteLine("Response: ");
        
   // Stream the response with real-time updates
  await foreach (var update in service.SendMessageStreamAsync(question))
        {
          Console.Write(update);
            Console.Out.Flush(); // Ensure immediate output
        }
        
        Console.WriteLine("\n\n=== End of Response ===");
    }
    
    public static async Task RunComparisonExample()
    {
        using var service = new AiChatService();
        
        var question = "Explain the card play mechanics briefly.";
        
        Console.WriteLine("=== Comparison: Streaming vs Normal ===\n");
        
        // Normal mode (waits for complete response)
        Console.WriteLine("1. Normal Mode (waits for completion):");
    var startTime = DateTime.Now;
        var normalResponse = await service.SendMessageAsync(question);
        var normalDuration = DateTime.Now - startTime;
        Console.WriteLine($"Response: {normalResponse}");
    Console.WriteLine($"Total time: {normalDuration.TotalSeconds:F1} seconds\n");
 
        // Clear conversation for clean comparison
        service.ClearConversations();
        
  // Streaming mode (shows updates as they come)
        Console.WriteLine("2. Streaming Mode (real-time updates):");
        startTime = DateTime.Now;
        await foreach (var update in service.SendMessageStreamAsync(question))
        {
         Console.Write(update);
      Console.Out.Flush();
      }
        var streamDuration = DateTime.Now - startTime;
        Console.WriteLine($"\nTotal time: {streamDuration.TotalSeconds:F1} seconds");
    }
}