using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Caching.Tests;

public class CachingTests
{
    private const int CACHE_SIZE = 10_000;

    private static ObserverTest _cacheMonitoring = new ObserverTest();

    [SetUp]
    public void Setup()
    {
        _cacheMonitoring.Reset();
    }

    private static IEnumerable<ICache<long, long>> Caches()
    {
        yield return new LRUCache<long, long>(maximumKeyCount: (int)(1.2d * CACHE_SIZE), 1.0d, cacheObserver: _cacheMonitoring);
        yield return new SLRUCache<long, long>(maximumKeyCount: CACHE_SIZE, 1.0d, cacheObserver: _cacheMonitoring);
        yield return new MSLRUCache<long, long>(maximumKeyCount: (int)(1.2d * CACHE_SIZE), 5, cacheObserver: _cacheMonitoring);
        //yield return new LRUCache<long, long>(maximumKeyCount: CACHE_SIZE, 1.2d, cacheObserver: _cacheMonitoring);
        yield return new LUCache<long, long>(maximumKeyCount: CACHE_SIZE, cacheObserver: _cacheMonitoring);
        yield return new LFUCache<long, long>(maximumKeyCount: CACHE_SIZE, cacheObserver: _cacheMonitoring);
        yield return new LFURACache<long, long>(maximumKeyCount: CACHE_SIZE, cacheObserver: _cacheMonitoring);
        yield return new LIRSCache<long, long>(maximumKeyCount: CACHE_SIZE, 1.0d, cacheObserver: _cacheMonitoring);
    }

    [Test]
    public void Caching_Works_As_Expected([ValueSource(nameof(Caches))] ICache<long, long> cache)
    {
        var value = cache.GetOrCreate(1, x => x + 42);

        Assert.AreEqual(43, value);
        Assert.AreEqual(1, _cacheMonitoring.CacheCalls);
        Assert.AreEqual(1, _cacheMonitoring.CacheMisses);

        value = cache.GetOrCreate(1, x => x + 42);

        Assert.AreEqual(43, value);
        Assert.AreEqual(2, _cacheMonitoring.CacheCalls);
        Assert.AreEqual(1, _cacheMonitoring.CacheMisses);
    }

    [Test]
    [NonParallelizable]
    public void Plot_Efficiency()
    {
        var generators = new List<(string, IGenerator<long>)>();

        generators.Add(("Sparse 50K", new SparseLongGenerator(50_000)));
        generators.Add(("Gaussian σ = 20K", new GaussianLongGenerator(0, 20_000)));
        generators.Add(("Gaussian σ = 10K", new GaussianLongGenerator(0, 10_000)));
        generators.Add(("Gaussian σ = 5K", new GaussianLongGenerator(0, 5000)));
        generators.Add(("Gaussian Bi-Modal", new MultimodalGenerator<long>(new GaussianLongGenerator(0, 5000), new GaussianLongGenerator(10_000, 5_000))));
        generators.Add(("Gaussian Switch Near", new SwitchableGenerator<long>(10_000, new GaussianLongGenerator(0, 5000), new GaussianLongGenerator(10_000, 5000))));
        generators.Add(("Gaussian Switch Far", new SwitchableGenerator<long>(10_000, new GaussianLongGenerator(0, 5000), new GaussianLongGenerator(1_000_000, 5000))));
        generators.Add(("Dataset CL", new DataBasedGenerator("case_cl.dat")));
        generators.Add(("Dataset VCC", new DataBasedGenerator("case_vcc.dat")));
        generators.Add(("Dataset VDC", new DataBasedGenerator("case_vdc.dat")));
        generators.Add(("Dataset Shared CL+VCC+VDC", new MultimodalGenerator<long>(new DataBasedGenerator("case_cl.dat"), new DataBasedGenerator("case_vcc.dat"), new DataBasedGenerator("case_vdc.dat"))));

        foreach (var generator in generators)
        {
            CacheBenchmark.Analyze(generator.Item1, _cacheMonitoring, (long x) => x + 1, generator.Item2, Caches().Select(x => (x.GetType().Name, x)).ToArray());
        }
    }
}