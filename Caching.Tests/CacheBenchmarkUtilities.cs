using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Caching.Tests;

public class CacheBenchmarkUtilities
{
    /// <summary>
    /// Run a benchmark on the cache efficiency (cache hits / cache calls %) and plot it
    /// </summary>
    /// <param name="plotter">Object to build the plot with</param>
    /// <param name="plotName">Name of the plot</param>
    /// <param name="testCases">Test cases. One test case = one serie in the plot</param>
    /// <param name="factory">Cache factory to use on cache misses</param>
    /// <param name="generator">Generator to generate input data for the benchmark</param>
    /// <typeparam name="K"></typeparam>
    /// <typeparam name="V"></typeparam>
    public static void PlotBenchmarkEfficiency<K, V>(IPlotter plotter, string plotName, IEnumerable<ICacheBuilder<K, V>> testCases, Func<K, V> factory, IGenerator<K> generator)
    {
        var series = new List<Serie>();
        
        foreach (var testCase in testCases)
        {
            var serie = BenchmarkEfficiency(testCase, factory, generator);
            series.Add(serie);
        }

        plotter.Plot(plotName, "Max cache size", "Efficiency %", series);
    }

    private static Serie BenchmarkEfficiency<K, V>(ICacheBuilder<K, V> testCase, Func<K, V> factory, IGenerator<K> generator) 
    {
        var measurements = new List<(int size, int calls, int misses)>();

        int iterations = 10;

        Stopwatch sw = Stopwatch.StartNew();

        string name = string.Empty;
        
        for (int i = 1; i <= iterations; i++)
        {
            int size = 1000 * i * i;

            var observer = new CacheCounter();
            
            testCase.WithMaximumEntries(size);
            testCase.WithObserver(observer);
            var cache = testCase.Build();

            name = cache.Name;
            
            generator.Reset();

            // Warmup
            for (int j = 0; j < 100_000; j++)
            {
                K key = generator.Generate();
                _ = cache.GetOrCreate(key, factory);
            }

            observer.Reset();

            for (int j = 0; j < 800_000; j++)
            {
                K key = generator.Generate();
                _ = cache.GetOrCreate(key, factory);
            }

            measurements.Add((size, observer.CountCalls, observer.CountMisses));
        }

        sw.Stop();

        Console.WriteLine($"Benchmarked {name} in {sw.Elapsed}");

        var points = measurements.Select(x => ((double)x.size, (x.calls == 0) ? 0 : 100d * (x.calls - x.misses) / x.calls)).ToArray();

        return new Serie(name, points);
    }
}