using System;
using System.Diagnostics;
using System.Text;
using BenchmarkSuite1.TestMemoryModels;
using MemoryLibrary.Models;
using MemoryLibraryBenchmarks.TestMemoryModels;
using Services;

namespace BenchmarkSuite1;

public class MemoryToStringBenchmark
{
    private readonly int _fragmentsCount;
    private readonly int _fragmentSize;
    private readonly int _iterations;

    public MemoryToStringBenchmark(int fragmentsCount = 100, int fragmentSize = 20000, int iterations = 1000)
    {
        _fragmentsCount = fragmentsCount;
        _fragmentSize = fragmentSize;
        _iterations = iterations;
    }

    public void RunToStringComparison()
    {
        var fragments = CreateFragments();
        var (stringBuilder, stringJoin) = SetupMemoryInstances(fragments);

        // Warmup
        _ = stringBuilder.ToString();
        _ = stringJoin.ToString();

        PrintHeader();

        var stringBuilderResults = BenchmarkStringBuilderMemory(stringBuilder);
        var stringJoinResults = BenchmarkStringJoinMemory(stringJoin);

        DisplayResults(stringBuilderResults, stringJoinResults);
        DisplayComparison(stringBuilderResults, stringJoinResults);
    }

    private IMemoryFragment[] CreateFragments()
    {
        var fragments = new IMemoryFragment[_fragmentsCount];
        for (int i = 0; i < _fragmentsCount; i++)
        {
            var sb = new StringBuilder(_fragmentSize);
            for (int j = 0; j < _fragmentSize; j++) sb.Append('x');
            fragments[i] = new MemoryFragment($"Title {i}", sb.ToString());
        }

        return fragments;
    }

    private (StringBuilderMemory stringBuilder, StringJoinMemory stringJoin) SetupMemoryInstances(IMemoryFragment[] fragments)
    {
        var stringBuilder = new StringBuilderMemory();
        var stringJoin = new StringJoinMemory();

        foreach (var f in fragments)
        {
            stringBuilder.ImportMemory(f);
            stringJoin.ImportMemory(f);
        }

        return (stringBuilder, stringJoin);
    }

    private void PrintHeader()
    {
        Console.WriteLine("Benchmark: ToString() Performance - StringBuilder vs String.Join");
        Console.WriteLine(
            $"Configuration: {_fragmentsCount} fragments × {_fragmentSize} chars, {_iterations} iterations");
        Console.WriteLine(new string('=', 80));
    }

    private BenchmarkResults BenchmarkStringBuilderMemory(StringBuilderMemory memory)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var gen0Before = GC.CollectionCount(0);
        var gen1Before = GC.CollectionCount(1);
        var gen2Before = GC.CollectionCount(2);
        var memoryBefore = GC.GetTotalMemory(false);

        var sw = Stopwatch.StartNew();
        long total = 0;
        for (int i = 0; i < _iterations; i++)
        {
            var s = memory.ToString();
            total += s.Length;
        }

        sw.Stop();

        var memoryAfter = GC.GetTotalMemory(false);
        var gen0After = GC.CollectionCount(0);
        var gen1After = GC.CollectionCount(1);
        var gen2After = GC.CollectionCount(2);

        return new BenchmarkResults(
            "StringBuilder",
            sw.Elapsed.TotalMilliseconds,
            memoryAfter - memoryBefore,
            gen0After - gen0Before,
            gen1After - gen1Before,
            gen2After - gen2Before,
            total,
            _iterations
        );
    }

    private BenchmarkResults BenchmarkStringJoinMemory(StringJoinMemory memory)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var gen0Before = GC.CollectionCount(0);
        var gen1Before = GC.CollectionCount(1);
        var gen2Before = GC.CollectionCount(2);
        var memoryBefore = GC.GetTotalMemory(false);

        var sw = Stopwatch.StartNew();
        long total = 0;
        for (int i = 0; i < _iterations; i++)
        {
            var s = memory.ToString();
            total += s.Length;
        }

        sw.Stop();

        var memoryAfter = GC.GetTotalMemory(false);
        var gen0After = GC.CollectionCount(0);
        var gen1After = GC.CollectionCount(1);
        var gen2After = GC.CollectionCount(2);

        return new BenchmarkResults(
            "String.Join",
            sw.Elapsed.TotalMilliseconds,
            memoryAfter - memoryBefore,
            gen0After - gen0Before,
            gen1After - gen1Before,
            gen2After - gen2Before,
            total,
            _iterations
        );
    }

    private void DisplayResults(BenchmarkResults stringBuilderResults, BenchmarkResults stringJoinResults)
    {
        Console.WriteLine("\n--- StringBuilder Implementation ---");
        stringBuilderResults.Print();

        Console.WriteLine("\n--- String.Join Implementation ---");
        stringJoinResults.Print();

        Console.WriteLine("\n" + new string('=', 80));
    }

    private void DisplayComparison(BenchmarkResults stringBuilderResults, BenchmarkResults stringJoinResults)
    {
        // Performance comparison
        if (stringBuilderResults.TotalMilliseconds < stringJoinResults.TotalMilliseconds)
        {
            var speedup = stringJoinResults.TotalMilliseconds / stringBuilderResults.TotalMilliseconds;
            Console.WriteLine($"⚡ Result: StringBuilder is {speedup:F2}x FASTER");
        }
        else if (stringJoinResults.TotalMilliseconds < stringBuilderResults.TotalMilliseconds)
        {
            var speedup = stringBuilderResults.TotalMilliseconds / stringJoinResults.TotalMilliseconds;
            Console.WriteLine($"⚡ Result: String.Join is {speedup:F2}x FASTER");
        }
        else
        {
            Console.WriteLine("Result: Equal performance.");
        }

        // Memory comparison
        if (stringBuilderResults.MemoryDelta < stringJoinResults.MemoryDelta)
        {
            var ratio = (double)stringJoinResults.MemoryDelta / stringBuilderResults.MemoryDelta;
            Console.WriteLine($"💾 Memory: StringBuilder allocates {ratio:F2}x LESS memory");
        }
        else if (stringJoinResults.MemoryDelta < stringBuilderResults.MemoryDelta)
        {
            var ratio = (double)stringBuilderResults.MemoryDelta / stringJoinResults.MemoryDelta;
            Console.WriteLine($"💾 Memory: String.Join allocates {ratio:F2}x LESS memory");
        }
        else
        {
            Console.WriteLine("Memory: Equal allocation.");
        }
    }
}

public record BenchmarkResults(
    string ImplementationName,
    double TotalMilliseconds,
    long MemoryDelta,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    long LengthChecksum,
    int Iterations)
{
    public void Print()
    {
        Console.WriteLine($"  Total Time:{TotalMilliseconds:F2} ms");
        Console.WriteLine($"  Per Call:  {TotalMilliseconds / Iterations:F6} ms");
        Console.WriteLine($"  Memory Delta:      {MemoryDelta / 1024.0:F2} KB");
        Console.WriteLine($"  Avg per Call:      {MemoryDelta / (double)Iterations:F2} bytes");
        Console.WriteLine($"  Gen 0 Collections: {Gen0Collections}");
        Console.WriteLine($"  Gen 1 Collections: {Gen1Collections}");
        Console.WriteLine($"  Gen 2 Collections: {Gen2Collections}");
        Console.WriteLine($"  Length Checksum:   {LengthChecksum}");
    }
}