using NUnit.Framework;
using Caching.Caches;

namespace Caching.Benchmarks;

// Questions to answer:
// Why LFU over LU ?
// Why LFURA over LFU ?
// 

public class Benchmarks
{
    [Test]
    [NonParallelizable]
    public async Task Scan_Resistance()
    {
        var simulations = new List<(string name, IGenerator<long> generator)>();
        var caches = new List<ICacheBuilder<long, long>>();

        // Alternate between two mode. First mode are frequently accessed keys, second mode is keys likely to be accessed only once
        // First simulation flips between the two modes every 100k iterations, while second mode flips on every call.
        // In both cases, theoritical maximum cache efficiency is 50% (hits on the first mode)
        simulations.Add(("Scan 1", new SwitchableGenerator<long>(100_000, false, new SparseLongGenerator(50_000), new SparseLongGenerator(UInt32.MaxValue))));
        simulations.Add(("Scan 2", new MultimodalGenerator<long>(new SparseLongGenerator(50_000), new SparseLongGenerator(UInt32.MaxValue))));

        caches.Add(new CacheBuilder<long, long, LRUCache<long, long>>().WithName("LRU"));
        caches.Add(new CacheBuilder<long, long, SLRUCache<long, long>>().WithConfiguration(c => c.MidPoint = 0.1).WithName("SLRU 0.1"));
        caches.Add(new CacheBuilder<long, long, SLRUCache<long, long>>().WithConfiguration(c => c.MidPoint = 0.2).WithName("SLRU 0.2"));
        caches.Add(new CacheBuilder<long, long, MSLRUCache<long, long>>().WithConfiguration(c => c.SegmentsCount = 3).WithName("MSLRU 3"));
        caches.Add(new CacheBuilder<long, long, MSLRUCache<long, long>>().WithConfiguration(c => c.SegmentsCount = 5).WithName("MSLRU 5"));
        caches.Add(new CacheBuilder<long, long, ARCCache<long, long>>().WithName("ARC"));

        await RunAsync(simulations, caches);
    }
    
    [Test]
    [NonParallelizable]
    public async Task Stuck_Resistance()
    {      
        var simulations = new List<(string name, IGenerator<long> generator)>();
        var caches = new List<ICacheBuilder<long, long>>();

        // For almost the full warmup duration, use a simple gaussian mode with a low std dev (easy hits), and then switch to the another mode with completely different hits (but as easy to hit, same std dev)
        // The switch occurs a few thousands iteration before the warmup ends to give a change for the cache to adapt to new mode during its warmup if it can.
        simulations.Add(("Stuck", new SwitchableGenerator<long>(800_000, true, new GaussianLongGenerator(0, 50_000), new GaussianLongGenerator(1_000_000, 50_000))));

        caches.Add(new CacheBuilder<long, long, LUCacheOfficial<long, long>>().WithName("LU Official"));
        caches.Add(new CacheBuilder<long, long, LUCache<long, long>>().WithName("LU Custom"));
        caches.Add(new CacheBuilder<long, long, LUDACache<long, long>>().WithName("LUDA"));
        caches.Add(new CacheBuilder<long, long, LUCache<long, long>>().WithConfiguration(c => c.InsertStartOfFrequencyBucket = true).WithName("LU Insert Bottom"));
        caches.Add(new CacheBuilder<long, long, LFUCache<long, long>>().WithName("LFU"));
        caches.Add(new CacheBuilder<long, long, LFURACache<long, long>>().WithName("LFURA"));

        // Warmup for a long duration, to accentuate potential "stuck keys" effect when mode switches
        await RunAsync(simulations, caches, warmupIterations: 1_000_000, 200_000);
    }
    
    [Test]
    [NonParallelizable]
    public async Task P1_All_Caches_All_Simutations()
    {
        var simulations = new List<(string name, IGenerator<long> generator)>();
        var caches = new List<ICacheBuilder<long, long>>();
        
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
        
        caches.Add(new CacheBuilder<long, long, LRUCache<long, long>>().WithName("LRU"));
        caches.Add(new CacheBuilder<long, long, SLRUCache<long, long>>().WithConfiguration(c => c.MidPoint = 0.2).WithName("SLRU"));
        caches.Add(new CacheBuilder<long, long, MSLRUCache<long, long>>().WithConfiguration(c => c.SegmentsCount = 5).WithName("MSLRU"));
        caches.Add(new CacheBuilder<long, long, LUCache<long, long>>().WithName("LU"));
        caches.Add(new CacheBuilder<long, long, LUDACache<long, long>>().WithName("LUDA"));
        caches.Add(new CacheBuilder<long, long, LFUCache<long, long>>().WithName("LFU"));
        caches.Add(new CacheBuilder<long, long, LFURACache<long, long>>().WithName("LFURA"));
        caches.Add(new CacheBuilder<long, long, LIRSCache<long, long>>().WithName("LFURA"));
        caches.Add(new CacheBuilder<long, long, ARCCache<long, long>>().WithName("LFURA"));

        await RunAsync(simulations, caches);
    }

    private static async Task RunAsync(
        IReadOnlyList<(string name, IGenerator<long> generator)> simulations,
        IReadOnlyList<ICacheBuilder<long, long>> caches,
        int warmupIterations = 100_000,
        int benchmarkIterations = 800_000)
    {
        var plotter = new LiveCharts2Plotter();
        
        // Run benchmarks in parallel
        var tasks = simulations.Select(simulation => Task.Run(() =>
        {
            CacheBenchmarkUtilities.PlotBenchmarkEfficiency(
                plotter, // Plotter to handle output series
                simulation.name, // Name of the simulation
                caches, // Actual implementations to test. Each will lead to a serie.
                x => x + 1, // Cache factory
                simulation.generator, // Generator for input data
                warmupIterations,
                benchmarkIterations
            );
        }));

        await Task.WhenAll(tasks);
        
        plotter.Save("../../../../Results");
    }
}