using System;

namespace BenchmarkSuite1;

public class Program
{
    public static void Main(string[] args)
    {
      var benchmark = new MemoryToStringBenchmark(
            fragmentsCount: 100,
            fragmentSize: 2000,
            iterations: 1000
        );

        benchmark.RunToStringComparison();
    }
}