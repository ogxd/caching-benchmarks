using System;
using System.Collections.Generic;
using NUnit.Framework;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caching.Caches;

namespace Caching.Benchmarks;

public class Benchmarks
{
    [Test]
    [NonParallelizable]
    public async Task P1_LRU_Scan_Resistance_Problem()
    {
        var plotter = new LiveCharts2Plotter();
        var simulations = new [] {
            new SwitchableGenerator<long>(100_000, false, new SparseLongGenerator(50_000), new SparseLongGenerator(UInt32.MaxValue))
        };
        
        var caches = new [] {
            new CacheBuilder<long, long, LRUCache<long, long>>().WithName("LRU"),
            new CacheBuilder<long, long, SLRUCache<long, long>>().WithConfiguration(c => c.MidPoint = 0.1).WithName("SLRU 0.1"),
            new CacheBuilder<long, long, SLRUCache<long, long>>().WithConfiguration(c => c.MidPoint = 0.2).WithName("SLRU 0.2"),
            new CacheBuilder<long, long, MSLRUCache<long, long>>().WithConfiguration(c => c.SegmentsCount = 3).WithName("MSLRU 3"),
            new CacheBuilder<long, long, MSLRUCache<long, long>>().WithConfiguration(c => c.SegmentsCount = 5).WithName("MSLRU 5"),
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
        }));

        await Task.WhenAll(tasks);
        
        plotter.Save("../../../../Results");
    }
    
    [Test]
    [NonParallelizable]
    public async Task P1_LFU_And_Real_LFU()
    {
        var plotter = new LiveCharts2Plotter();
        var simulations = new [] {
            // Generator keys stuck
            new SwitchableGenerator<long>(100_000, false, new SparseLongGenerator(50_000), new SparseLongGenerator(UInt32.MaxValue))
        };
        
        var caches = new [] {
            new CacheBuilder<long, long, LRUCache<long, long>>().WithName("LRU"),
            new CacheBuilder<long, long, ARCCache<long, long>>().WithName("ARC"),
            new CacheBuilder<long, long, SLRUCache<long, long>>().WithConfiguration(c => c.MidPoint = 0.1).WithName("SLRU 0.1"),
            new CacheBuilder<long, long, SLRUCache<long, long>>().WithConfiguration(c => c.MidPoint = 0.2).WithName("SLRU 0.2"),
            new CacheBuilder<long, long, MSLRUCache<long, long>>().WithConfiguration(c => c.SegmentsCount = 3).WithName("MSLRU 3"),
            new CacheBuilder<long, long, MSLRUCache<long, long>>().WithConfiguration(c => c.SegmentsCount = 5).WithName("MSLRU 5"),
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
        }));

        await Task.WhenAll(tasks);
        
        plotter.Save("../../../../Results");
    }
    
    [Test]
    [NonParallelizable]
    public async Task P1_All_Caches_All_Simutations()
    {
        var plotter = new LiveCharts2Plotter();
        
        var simulations = new List<(string name, IGenerator<long> generator)>();
        
        simulations.Add(("Sparse 500K", new SparseLongGenerator(500_000)));
        simulations.Add(("Gaussian σ = 200K", new GaussianLongGenerator(0, 200_000)));
        simulations.Add(("Gaussian σ = 100K", new GaussianLongGenerator(0, 100_000)));
        simulations.Add(("Gaussian σ = 50K", new GaussianLongGenerator(0, 50_000)));
        simulations.Add(("Gaussian Bi-Modal", new MultimodalGenerator<long>(new GaussianLongGenerator(0, 50_000), new GaussianLongGenerator(100_000, 50_000))));
        simulations.Add(("Gaussian Switch Near", new SwitchableGenerator<long>(10_000, true, new GaussianLongGenerator(0, 50_000), new GaussianLongGenerator(100_000, 50_000))));
        simulations.Add(("Gaussian Switch Far", new SwitchableGenerator<long>(1000_000, true, new GaussianLongGenerator(0, 10_000), new GaussianLongGenerator(10_000_000, 5_000))));
        simulations.Add(("Dataset CL", new DataBasedGenerator("Datasets/case_cl.dat")));
        simulations.Add(("Dataset VCC", new DataBasedGenerator("Datasets/case_vcc.dat")));
        simulations.Add(("Dataset VDC", new DataBasedGenerator("Datasets/case_vdc.dat")));
        simulations.Add(("Dataset Shared CL+VCC+VDC", new MultimodalGenerator<long>(new DataBasedGenerator("Datasets/case_cl.dat"), new DataBasedGenerator("Datasets/case_vcc.dat"), new DataBasedGenerator("Datasets/case_vdc.dat"))));
        
        var caches = new [] {
            // new CacheBuilder<long, long, LRUCache<long, long>>().WithName("LRU"),
            // new CacheBuilder<long, long, SLRUCache<long, long>>().WithConfiguration(c => c.MidPoint = 0.2).WithName("SLRU 0.2"),
            // new CacheBuilder<long, long, MSLRUCache<long, long>>().WithConfiguration(c => c.SegmentsCount = 5).WithName("MSLRU 5"),
            new CacheBuilder<long, long, LUCache<long, long>>().WithName("LU"),
            new CacheBuilder<long, long, LUCacheOfficial<long, long>>().WithName("LU Official"),
            // new CacheBuilder<long, long, LUDACache<long, long>>().WithName("LUDA"),
            // new CacheBuilder<long, long, LFUCacheNaive<long, long>>().WithName("LFU Naive"),
            new CacheBuilder<long, long, LFUCache<long, long>>().WithName("LFU"),
            new CacheBuilder<long, long, ARCCache<long, long>>().WithName("ARC"),
            new CacheBuilder<long, long, LIRSCacheAI<long, long>>().WithName("LIRS AI"),
            new CacheBuilder<long, long, LIRSCache<long, long>>().WithName("LIRS"),
            // new CacheBuilder<long, long, LFURACache<long, long>>().WithName("LFURA"),
            // new CacheBuilder<long, long, PLFUCache<long, long>>().WithConfiguration(c => c.ProgressiveMove = true).WithConfiguration(c => c.PromoteToBottom = true).WithConfiguration(c => c.ProbatoryScaleFactor = 10d).WithName("PLFU Progressive"),
            // new CacheBuilder<long, long, PLFUCache<long, long>>().WithConfiguration(c => c.ProbatoryScaleFactor = 10d).WithName("PLFU"),
            // new CacheBuilder<long, long, PLFUCache<long, long>>().WithConfiguration(c => c.ProbatoryScaleFactor = 100d).WithName("PLFU Large"),
            // new CacheBuilder<long, long, LIRSCache<long, long>>().WithName("LIRS"),
        };

        // Run benchmarks in parallel
        var tasks = simulations.Select(simulation => Task.Run(() =>
        {
            CacheBenchmarkUtilities.PlotBenchmarkEfficiency(
                plotter, // Plotter to handle output series
                simulation.name, // Name of the simulation
                caches, // Actual implementations to test. Each will lead to a serie.
                x => x + 1, // Cache factory
                simulation.generator // Generator for input data
            );
        }));

        await Task.WhenAll(tasks);
        
        plotter.Save("../../../../Results");
    }

    [Test]
    [NonParallelizable]
    public async Task P1_Best_Cache_For_TFS()
    {
        
    }
    
    // private static IEnumerable<TestCase<long, long>> Caches()
    // {
    //     CacheCounter counter;
    //     yield return new ("LRU", new LRUCache<long, long>(CACHE_SIZE, cacheObserver: counter = new CacheCounter()), counter);
    //     // yield return new ("SLRU", new SLRUCache<long, long>(maximumKeyCount: CACHE_SIZE, 1.0d, cacheObserver: counter = new CacheCounter()), counter);
    //     // yield return new ("MSLRU", new MSLRUCache<long, long>(maximumKeyCount: CACHE_SIZE, 5, cacheObserver: counter = new CacheCounter()), counter);
    //     // yield return new ("Naive LFU", new NaiveLFUCache<long, long>(CACHE_SIZE, cacheObserver: counter = new CacheCounter()), counter);
    //     //yield return new ("LU", new LUCache<long, long>(maximumKeyCount: CACHE_SIZE, cacheObserver: counter = new CacheCounter()), counter);
    //     //yield return new ("LUDA", new LUDACache<long, long>(maximumKeyCount: CACHE_SIZE, cacheObserver: counter = new CacheCounter()), counter);
    //     yield return new ("LFU", new LFUCache<long, long>(CACHE_SIZE, cacheObserver: counter = new CacheCounter()), counter);
    //     yield return new ("Probatory LFU", new PLFUCache<long, long>(CACHE_SIZE, 10, true, true, cacheObserver: counter = new CacheCounter()), counter);
    //     //yield return new ("LFURA", new LFURACache<long, long>(maximumKeyCount: CACHE_SIZE, cacheObserver: counter = new CacheCounter()), counter);
    //     //yield return new ("LIRS", new LIRSCache<long, long>(maximumKeyCount: CACHE_SIZE, 1.0d, cacheObserver: counter = new CacheCounter()), counter);
    // }
    
    // [Test]
    // public void Caching_Works_As_Expected([ValueSource(nameof(Caches))] TestCase<long, long> testCase)
    // {
    //     var value = testCase.Cache.GetOrCreate(1, x => x + 42);
    //
    //     Assert.AreEqual(43, value);
    //     Assert.AreEqual(1, testCase.Counter.CountCalls);
    //     Assert.AreEqual(1, testCase.Counter.CountMisses);
    //
    //     value = testCase.Cache.GetOrCreate(1, x => x + 42);
    //
    //     Assert.AreEqual(43, value);
    //     Assert.AreEqual(2, testCase.Counter.CountCalls);
    //     Assert.AreEqual(1, testCase.Counter.CountMisses);
    // }

    // [Test]
    // [NonParallelizable]
    // public async Task Plot_Efficiency()
    // {
    //     //var plotter = new ScotPlottPlotter(); // Windows only
    //     //var plotter = new ScotPlott5Plotter(); // Very alpha lib
    //     var plotter = new LiveCharts2Plotter(); // Cross-platform
    //     var simulations = new List<(string name, IGenerator<long> generator)>();
    //
    //     simulations.Add(("2Sparse 500K", new SparseLongGenerator(500_000)));
    //     simulations.Add(("2Gaussian σ = 200K", new GaussianLongGenerator(0, 200_000)));
    //     simulations.Add(("2Gaussian σ = 100K", new GaussianLongGenerator(0, 100_000)));
    //     simulations.Add(("2Gaussian σ = 50K", new GaussianLongGenerator(0, 50_000)));
    //     simulations.Add(("2Gaussian Bi-Modal", new MultimodalGenerator<long>(new GaussianLongGenerator(0, 50_000), new GaussianLongGenerator(100_000, 50_000))));
    //     simulations.Add(("2Gaussian Switch Near", new SwitchableGenerator<long>(10_000, true, new GaussianLongGenerator(0, 50_000), new GaussianLongGenerator(100_000, 50_000))));
    //     simulations.Add(("2Gaussian Switch Far", new SwitchableGenerator<long>(1000_000, true, new GaussianLongGenerator(0, 10_000), new GaussianLongGenerator(10_000_000, 5_000))));
    //     simulations.Add(("2Dataset CL", new DataBasedGenerator("Datasets/case_cl.dat")));
    //     simulations.Add(("2Dataset VCC", new DataBasedGenerator("Datasets/case_vcc.dat")));
    //     simulations.Add(("2Dataset VDC", new DataBasedGenerator("Datasets/case_vdc.dat")));
    //     simulations.Add(("2Dataset Shared CL+VCC+VDC", new MultimodalGenerator<long>(new DataBasedGenerator("Datasets/case_cl.dat"), new DataBasedGenerator("Datasets/case_vcc.dat"), new DataBasedGenerator("Datasets/case_vdc.dat"))));
    //
    //     // Run benchmarks in parallel
    //     var tasks = simulations.Select(simulation => Task.Run(() =>
    //     {
    //         CacheBenchmarkUtilities.PlotBenchmarkEfficiency(
    //             plotter, // Plotter to handle output series
    //             simulation.name, // Name of the simulation
    //             Caches(), // Actual implementations to test. Each will lead to a serie.
    //             x => x + 1, // Cache factory
    //             simulation.generator // Generator for input data
    //         );
    //     })).ToArray();
    //
    //     await Task.WhenAll(tasks);
    //     
    //     plotter.Save("../../../../Results");
    // }
}