using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Order;
using BenchmarkSuite1.TestMemoryModels;
using MemoryLibrary.Models;
using MemoryLibraryBenchmarks.TestMemoryModels;
using Services;

namespace BenchmarkSuite1;

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class MemoryToStringBenchmarks
{
    private StringBuilderMemory stringBuilderMemory;
    private StringJoinMemory stringJoinMemory;
    private IMemoryFragment[] fragments;

    [GlobalSetup]
    public void Setup()
    {
        // Create a set of fragments so both memories have the same amount of text
        fragments = new IMemoryFragment[100];
        for (int i = 0; i < 100; i++)
        {
            var content = new StringBuilder();
            for (int j = 0; j < 200; j++) content.Append('x');
            fragments[i] = new MemoryFragment($"Title {i}", content.ToString());
        }

        stringBuilderMemory = new StringBuilderMemory();
        stringJoinMemory = new StringJoinMemory();

        foreach (var f in fragments)
        {
            stringBuilderMemory.ImportMemory(f);
            stringJoinMemory.ImportMemory(f);
        }
    }

    [Benchmark(Baseline = true, Description = "StringBuilder.AppendLine()")]
    public string ToString_StringBuilder()
    {
        return stringBuilderMemory.ToString();
    }

    [Benchmark(Description = "String.Join()")]
    public string ToString_StringJoin()
    {
        return stringJoinMemory.ToString();
    }
}