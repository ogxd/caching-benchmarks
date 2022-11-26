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
        yield return new ("SLRU", new SLRUCache<long, long>(maximumKeyCount: CACHE_SIZE, 1.0d, cacheObserver: counter = new CacheCounter()), counter);
        yield return new ("MSLRU", new MSLRUCache<long, long>(maximumKeyCount: CACHE_SIZE, 5, cacheObserver: counter = new CacheCounter()), counter);
        //yield return new ("Real LFU", new RealLFUCache<long, long>(CACHE_SIZE, cacheObserver: counter = new CacheCounter()), counter);
        yield return new ("LU", new LUCache<long, long>(maximumKeyCount: CACHE_SIZE, cacheObserver: counter = new CacheCounter()), counter);
        yield return new ("LUDA", new LUDACache<long, long>(maximumKeyCount: CACHE_SIZE, cacheObserver: counter = new CacheCounter()), counter);
        yield return new ("LFU", new LFUCache<long, long>(maximumKeyCount: CACHE_SIZE, cacheObserver: counter = new CacheCounter()), counter);
        yield return new ("LFURA", new LFURACache<long, long>(maximumKeyCount: CACHE_SIZE, cacheObserver: counter = new CacheCounter()), counter);
        //yield return new TestCase("", new LIRSCache<long, long>(maximumKeyCount: CACHE_SIZE, 1.0d, cacheObserver: counter = new CacheCounter()), counter);
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
        //var plotter = new ScotPlott5Plotter();
        var plotter = new LiveCharts2Plotter();
        var simulations = new List<(string name, IGenerator<long> generator)>();

        simulations.Add(("Sparse 500K", new SparseLongGenerator(500_000)));
        simulations.Add(("Gaussian σ = 200K", new GaussianLongGenerator(0, 200_000)));
        simulations.Add(("Gaussian σ = 100K", new GaussianLongGenerator(0, 100_000)));
        simulations.Add(("Gaussian σ = 50K", new GaussianLongGenerator(0, 50_000)));
        simulations.Add(("Gaussian Bi-Modal", new MultimodalGenerator<long>(new GaussianLongGenerator(0, 50_000), new GaussianLongGenerator(100_000, 50_000))));
        simulations.Add(("Gaussian Switch Near", new SwitchableGenerator<long>(10_000, new GaussianLongGenerator(0, 50_000), new GaussianLongGenerator(100_000, 50_000))));
        simulations.Add(("Gaussian Switch Far", new SwitchableGenerator<long>(10_000, new GaussianLongGenerator(0, 50_000), new GaussianLongGenerator(10_000_000, 50_000))));
        simulations.Add(("Dataset CL", new DataBasedGenerator("case_cl.dat")));
        simulations.Add(("Dataset VCC", new DataBasedGenerator("case_vcc.dat")));
        simulations.Add(("Dataset VDC", new DataBasedGenerator("case_vdc.dat")));
        simulations.Add(("Dataset Shared CL+VCC+VDC", new MultimodalGenerator<long>(new DataBasedGenerator("case_cl.dat"), new DataBasedGenerator("case_vcc.dat"), new DataBasedGenerator("case_vdc.dat"))));

        // Run simulations in parallel
        var tasks = simulations.Select(simulation => Task.Run(() =>
        {
            CacheBenchmark.Analyze(
                simulation.name, // Name of the simulation
                x => x + 1, // Cache factory
                plotter, // Plotter to handle output series
                simulation.generator, // Generator for input data
                Caches() // Actual implementations to test. Each will lead to a serie.
            );
        })).ToArray();

        await Task.WhenAll(tasks);
        
        plotter.Save("graphs");
    }
}