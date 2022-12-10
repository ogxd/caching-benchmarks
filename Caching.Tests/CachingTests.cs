using System;
using Caching.Caches;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Caching.Tests;

public record TestCase<K, V>(string Name, ICache<K, V> Cache, CacheCounter Counter);

public class CachingTests
{
    private const int CACHE_SIZE = 10_000;

    private static IEnumerable<TestCase<long, long>> Caches()
    {
        CacheCounter counter;
        yield return new ("LRU", new LRUCache<long, long>(maximumKeyCount: CACHE_SIZE, 1.0d, cacheObserver: counter = new CacheCounter()), counter);
        // yield return new ("SLRU", new SLRUCache<long, long>(maximumKeyCount: CACHE_SIZE, 1.0d, cacheObserver: counter = new CacheCounter()), counter);
        // yield return new ("MSLRU", new MSLRUCache<long, long>(maximumKeyCount: CACHE_SIZE, 5, cacheObserver: counter = new CacheCounter()), counter);
        // yield return new ("Naive LFU", new NaiveLFUCache<long, long>(CACHE_SIZE, cacheObserver: counter = new CacheCounter()), counter);
        yield return new ("LU", new LUCache<long, long>(maximumKeyCount: CACHE_SIZE, cacheObserver: counter = new CacheCounter()), counter);
        yield return new ("LUDA", new LUDACache<long, long>(maximumKeyCount: CACHE_SIZE, cacheObserver: counter = new CacheCounter()), counter);
        yield return new ("LFU", new LFUCache<long, long>(maximumKeyCount: CACHE_SIZE, cacheObserver: counter = new CacheCounter()), counter);
        yield return new ("Probatory LFU", new ProbatoryLFUCache<long, long>(maximumKeyCount: CACHE_SIZE, cacheObserver: counter = new CacheCounter()), counter);
        yield return new ("LFURA", new LFURACache<long, long>(maximumKeyCount: CACHE_SIZE, cacheObserver: counter = new CacheCounter()), counter);
        //yield return new ("LIRS", new LIRSCache<long, long>(maximumKeyCount: CACHE_SIZE, 1.0d, cacheObserver: counter = new CacheCounter()), counter);
    }
    
    [Test]
    public void Caching_Works_As_Expected([ValueSource(nameof(Caches))] TestCase<long, long> testCase)
    {
        var value = testCase.Cache.GetOrCreate(1, x => x + 42);

        Assert.AreEqual(43, value);
        Assert.AreEqual(1, testCase.Counter.CountCalls);
        Assert.AreEqual(1, testCase.Counter.CountMisses);

        value = testCase.Cache.GetOrCreate(1, x => x + 42);

        Assert.AreEqual(43, value);
        Assert.AreEqual(2, testCase.Counter.CountCalls);
        Assert.AreEqual(1, testCase.Counter.CountMisses);
    }

    [Test]
    [NonParallelizable]
    public async Task Plot_Efficiency()
    {
        //var plotter = new ScotPlottPlotter(); // Windows only
        //var plotter = new ScotPlott5Plotter(); // Very alpha lib
        var plotter = new LiveCharts2Plotter(); // Cross-platform
        var simulations = new List<(string name, IGenerator<long> generator)>();

        simulations.Add(("2Sparse 500K", new SparseLongGenerator(500_000)));
        simulations.Add(("2Gaussian σ = 200K", new GaussianLongGenerator(0, 200_000)));
        simulations.Add(("2Gaussian σ = 100K", new GaussianLongGenerator(0, 100_000)));
        simulations.Add(("2Gaussian σ = 50K", new GaussianLongGenerator(0, 50_000)));
        simulations.Add(("2Gaussian Bi-Modal", new MultimodalGenerator<long>(new GaussianLongGenerator(0, 50_000), new GaussianLongGenerator(100_000, 50_000))));
        simulations.Add(("2Gaussian Switch Near", new SwitchableGenerator<long>(10_000, true, new GaussianLongGenerator(0, 50_000), new GaussianLongGenerator(100_000, 50_000))));
        simulations.Add(("2Gaussian Switch Far", new SwitchableGenerator<long>(1000_000, true, new GaussianLongGenerator(0, 10_000), new GaussianLongGenerator(10_000_000, 5_000))));
        simulations.Add(("2Dataset CL", new DataBasedGenerator("Datasets/case_cl.dat")));
        simulations.Add(("2Dataset VCC", new DataBasedGenerator("Datasets/case_vcc.dat")));
        simulations.Add(("2Dataset VDC", new DataBasedGenerator("Datasets/case_vdc.dat")));
        simulations.Add(("2Dataset Shared CL+VCC+VDC", new MultimodalGenerator<long>(new DataBasedGenerator("Datasets/case_cl.dat"), new DataBasedGenerator("Datasets/case_vcc.dat"), new DataBasedGenerator("Datasets/case_vdc.dat"))));

        // Run benchmarks in parallel
        var tasks = simulations.Select(simulation => Task.Run(() =>
        {
            CacheBenchmarkUtilities.PlotBenchmarkEfficiency(
                plotter, // Plotter to handle output series
                simulation.name, // Name of the simulation
                Caches(), // Actual implementations to test. Each will lead to a serie.
                x => x + 1, // Cache factory
                simulation.generator // Generator for input data
            );
        })).ToArray();

        await Task.WhenAll(tasks);
        
        plotter.Save("../../../../Results");
    }
    
    [Test]
    [NonParallelizable]
    public async Task P1_LRU_VS_LU()
    {
        
        var plotter = new LiveCharts2Plotter();
        var simulations = new [] {
            new SwitchableGenerator<long>(100_000, false, new SparseLongGenerator(50_000), new SparseLongGenerator(UInt32.MaxValue))
        };
        
        var counter = new CacheCounter();
        
        var caches = new [] {
            new TestCase<long, long>("LRU", new LRUCache<long, long>(maximumKeyCount: CACHE_SIZE, 1.0d, cacheObserver: counter), counter),
            new TestCase<long, long>("SLRU 0.1", new SLRUCache<long, long>(maximumKeyCount: CACHE_SIZE, 0.1, cacheObserver: counter), counter),
            new TestCase<long, long>("SLRU 0.2", new SLRUCache<long, long>(maximumKeyCount: CACHE_SIZE, 0.2, cacheObserver: counter), counter),
            new TestCase<long, long>("MSLRU 3", new MSLRUCache<long, long>(maximumKeyCount: CACHE_SIZE, 3, cacheObserver: counter), counter),
            new TestCase<long, long>("MSLRU 5", new MSLRUCache<long, long>(maximumKeyCount: CACHE_SIZE, 5, cacheObserver: counter), counter),
        };

        // Run benchmarks in parallel
        var tasks = simulations.Select(simulation => Task.Run(() =>
        {
            CacheBenchmarkUtilities.PlotBenchmarkEfficiency(
                plotter, // Plotter to handle output series
                "Scan", // Name of the simulation
                caches, // Actual implementations to test. Each will lead to a serie.
                x => x + 1, // Cache factory
                simulation // Generator for input data
            );
        })).ToArray();

        await Task.WhenAll(tasks);
        
        plotter.Save("../../../../Results");
    }
}