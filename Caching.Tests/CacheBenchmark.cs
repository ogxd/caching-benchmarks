using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Caching.Tests;

public class CacheBenchmark
{
    public static void Analyze<K, V>(string analysisName, Func<K, V> factory, IPlotter plotter, IGenerator<K> generator, IEnumerable<TestCase<K, V>> testCases)
    {
        var series = new List<Serie>();
        
        foreach (var testCase in testCases)
        {
            var serie = ComputeEfficiency(testCase, factory, generator);
            series.Add(serie);
        }

        plotter.Plot(analysisName, "Max cache size", "Efficiency %", series);
    }

    private static Serie ComputeEfficiency<K, V>(TestCase<K, V> testCase, Func<K, V> factory, IGenerator<K> generator) 
    {
        var measurements = new List<(int size, int calls, int misses)>();

        int iterations = 21;

        Stopwatch sw = Stopwatch.StartNew();

        for (int i = 1; i < iterations; i++)
        {
            int size = 1000 * i * i;

            testCase.Cache.Clear();
            testCase.Cache.MaxSize = size;

            generator.Reset();

            // Warmup
            for (int j = 0; j < 100_000; j++)
            {
                K key = generator.Generate();
                _ = testCase.Cache.GetOrCreate(key, factory);
            }

            testCase.Counter.Reset();

            for (int j = 0; j < 800_000; j++)
            {
                K key = generator.Generate();
                _ = testCase.Cache.GetOrCreate(key, factory);
            }

            measurements.Add((size, testCase.Counter.CountCalls, testCase.Counter.CountMisses));
        }

        sw.Stop();

        Console.WriteLine($"{testCase.Name} in {sw.Elapsed}");

        var points = measurements.Select(x => ((double)x.size, (x.calls == 0) ? 0 : 100d * (x.calls - x.misses) / x.calls)).ToArray();

        return new Serie(testCase.Name, points);
    }
}