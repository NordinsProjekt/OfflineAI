using System;
using System.Diagnostics;
using System.Text;
using MemoryLibrary;
using MemoryLibrary.Models;
using OfflineAI;
using Services;

namespace BenchmarkSuite1
{
    public class Program
    {
        public static void Main(string[] args)
        {
            const int fragmentsCount = 100;
            const int fragmentSize = 200; // chars per fragment
            const int iterations = 1000;

            var fragments = new IMemoryFragment[fragmentsCount];
            for (int i = 0; i < fragmentsCount; i++)
            {
                var sb = new StringBuilder(fragmentSize);
                for (int j = 0; j < fragmentSize; j++) sb.Append('x');
                fragments[i] = new MemoryFragment($"Title {i}", sb.ToString());
            }

            var simple = new SimpleMemory();
            var gloom = new Gloomhaven();

            // Set Gloomhaven internal list via reflection
            var memField = typeof(Gloomhaven).GetField("Memory", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (memField != null)
            {
                var list = new System.Collections.Generic.List<IMemoryFragment>();
                foreach (var f in fragments) list.Add(f);
                memField.SetValue(gloom, list);
            }

            foreach (var f in fragments) simple.ImportMemory(f);

            // Warmup
            _ = simple.ToString();
            _ = gloom.ToString();

            Console.WriteLine("Manual benchmark: ToString() on SimpleMemory vs Gloomhaven");
            Console.WriteLine($"Fragments: {fragmentsCount}, Fragment size: {fragmentSize} chars, Iterations: {iterations}");

            long simpleTotal = 0;
            long gloomTotal = 0;

            var sw = new Stopwatch();

            // SimpleMemory timing
            GC.Collect(); GC.WaitForPendingFinalizers();
            sw.Restart();
            for (int i = 0; i < iterations; i++)
            {
                var s = simple.ToString();
                // use result to avoid optimization
                simpleTotal += s.Length;
            }
            sw.Stop();
            var simpleMs = sw.Elapsed.TotalMilliseconds;

            // Gloomhaven timing
            GC.Collect(); GC.WaitForPendingFinalizers();
            sw.Restart();
            for (int i = 0; i < iterations; i++)
            {
                var s = gloom.ToString();
                gloomTotal += s.Length;
            }
            sw.Stop();
            var gloomMs = sw.Elapsed.TotalMilliseconds;

            Console.WriteLine($"SimpleMemory: {simpleMs:F2} ms total ({simpleMs / iterations:F6} ms per call). Length checksum: {simpleTotal}");
            Console.WriteLine($"Gloomhaven: {gloomMs:F2} ms total ({gloomMs / iterations:F6} ms per call). Length checksum: {gloomTotal}");

            if (simpleMs < gloomMs)
                Console.WriteLine("Result: SimpleMemory is faster.");
            else if (gloomMs < simpleMs)
                Console.WriteLine("Result: Gloomhaven is faster.");
            else
                Console.WriteLine("Result: Equal.");
        }
    }
}
