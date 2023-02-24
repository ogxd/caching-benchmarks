using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Caching.Benchmarks;

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
    /// <param name="steps"></param>
    /// <param name="startEndSizes"></param>
    /// <param name="warmupIterations"></param>
    /// <param name="benchmarkIterations"></param>
    /// <param name="computeBaseline"></param>
    /// <typeparam name="K"></typeparam>
    /// <typeparam name="V"></typeparam>
    public static void PlotBenchmarkEfficiency<K, V>(
        IPlotter plotter,
        string plotName,
        IEnumerable<ICacheBuilder<K, V>> testCases,
        Func<K, V> factory,
        IGenerator<K> generator,
        int steps,
        Range startEndSizes,
        int warmupIterations,
        int benchmarkIterations,
        bool computeBaseline)
    {
        var series = new List<Serie>();

        if (computeBaseline)
        {
            var serie = ComputeBaseline(generator, steps, startEndSizes, warmupIterations, benchmarkIterations);
            series.Add(serie);
        }
        
        foreach (var testCase in testCases)
        {
            var serie = BenchmarkEfficiency(testCase, factory, generator, steps, startEndSizes, warmupIterations, benchmarkIterations);
            series.Add(serie);
        }

        plotter.Plot(plotName, "Max cache size", "Efficiency %", series);
    }

    private static Serie BenchmarkEfficiency<K, V>(
        ICacheBuilder<K, V> testCase,
        Func<K, V> factory,
        IGenerator<K> generator,
        int steps,
        Range startEndSizes,
        int warmupIterations,
        int benchmarkIterations) 
    {
        var measurements = new List<(int size, int calls, int misses)>();

        Stopwatch sw = Stopwatch.StartNew();

        string name = string.Empty;
        
        for (int i = 0; i < steps; i++)
        {
            int size = CalculateSizeForStep(steps, startEndSizes, i);

            var observer = new CacheCounter();
            
            testCase.WithMaximumEntries(size);
            testCase.WithObserver(observer);
            var cache = testCase.Build();

            name = cache.Name;
            
            generator.Reset();

            // Warmup
            for (int j = 0; j < warmupIterations; j++)
            {
                K key = generator.Generate();
                _ = cache.GetOrCreate(key, factory);
            }

            observer.Reset();

            for (int j = 0; j < benchmarkIterations; j++)
            {
                K key = generator.Generate();
                _ = cache.GetOrCreate(key, factory);
            }
            
            Debug.Assert(benchmarkIterations == observer.CountCalls);
            Debug.Assert(observer.CountMisses <= observer.CountCalls);

            measurements.Add((size, observer.CountCalls, observer.CountMisses));
        }

        sw.Stop();

        Console.WriteLine($"Benchmarked {name} in {sw.Elapsed}");

        var points = measurements.Select(x => ((double)x.size, (x.calls == 0) ? 0 : 100d * (x.calls - x.misses) / x.calls)).ToArray();

        return new Serie(name, points);
    }

    private static int CalculateSizeForStep(int steps, Range startEndSizes, int i)
    {
        return (int)Math.Ceiling(startEndSizes.Start.Value + 1d * (startEndSizes.End.Value - startEndSizes.Start.Value) * i / (steps - 1));
    }

    private static Serie ComputeBaseline<K>(
        IGenerator<K> generator,
        int steps,
        Range startEndSizes,
        int warmupIterations,
        int benchmarkIterations) 
    {
        // Let's say there is a cache that is able to have cache hits on every key that is accessed more than once
        // It would be theoritically speaking impossible to more efficient than this
        // We can simulate this to have a baseline on which we can judge the results of our implementations.
        // If a cache yields an efficiency that is close to the baseline, it means it can't probably be performing better than that.

        generator.Reset();

        HashSet<K> _uniqueKeys = new();

        // Warmup
        for (int j = 0; j < warmupIterations; j++)
        {
            K key = generator.Generate();
            _uniqueKeys.Add(key);
        }

        int cacheHits = 0;

        for (int j = 0; j < benchmarkIterations; j++)
        {
            K key = generator.Generate();
            if (!_uniqueKeys.Add(key))
            {
                cacheHits++;
            }
        }
        
        var points = Enumerable.Range(0, steps).Select(i => ((double)CalculateSizeForStep(steps, startEndSizes, i), 100d * cacheHits / benchmarkIterations)).ToArray();

        return new Serie("Baseline", points);
    }
}