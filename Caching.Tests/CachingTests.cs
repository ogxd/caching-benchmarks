using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Caching.Tests;

public class CachingTests
{
    private const int WARMUP_ITERATIONS = 1_000_000;
    private const int TEST_ITERATIONS = 1_000_000;
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
        //yield return new LRUCache<long, long>(maximumKeyCount: CACHE_SIZE, 1.2d, cacheObserver: _cacheMonitoring);
        yield return new LUCache<long, long>(maximumKeyCount: CACHE_SIZE, cacheObserver: _cacheMonitoring);
        yield return new LFUsCache<long, long>(maximumKeyCount: CACHE_SIZE, cacheObserver: _cacheMonitoring);
        yield return new LFUCache<long, long>(maximumKeyCount: CACHE_SIZE, cacheObserver: _cacheMonitoring);
        yield return new LFURACache<long, long>(maximumKeyCount: CACHE_SIZE, cacheObserver: _cacheMonitoring);
    }

    private static IEnumerable<IGenerator<long>> Generators()
    {
        yield return new SparseLongGenerator(2 * CACHE_SIZE);
        // Specific case were out of CACHE_SIZE we can get up to a maximum of 38.29% hits (best case)
        yield return new GaussianLongGenerator(0, 4 * CACHE_SIZE);
        // Specific case were out of CACHE_SIZE we can get up to a maximum of 68.27% hits (best case)
        yield return new GaussianLongGenerator(0, 0.5 * CACHE_SIZE);
        // Specific case were out of CACHE_SIZE we can get up to a maximum of 95.45% hits (best case)
        yield return new GaussianLongGenerator(0, 0.25 * CACHE_SIZE);
        yield return new DataBasedGenerator("hashes.dat");
        // Two gaussian modes
        //yield return new MultimodalGenerator<long>(new GaussianLongGenerator(0, 5000), new GaussianLongGenerator(100_000, 2500));
        yield return new MultimodalGenerator<long>(new GaussianLongGenerator(0, 5000), new SparseLongGenerator(10_000_000));
        // Slight switch
        yield return new SwitchableGenerator<long>(WARMUP_ITERATIONS, new GaussianLongGenerator(0, 5000), new GaussianLongGenerator(10_000, 5000));
        // Complete switch
        yield return new SwitchableGenerator<long>(WARMUP_ITERATIONS, new GaussianLongGenerator(0, 5000), new GaussianLongGenerator(10_000_000, 5000));
    }

    [Test]
    public void TheoriticalMaxDataScore()
    {
        var generator = new DataBasedGenerator("hashes.dat");

        Dictionary<long, int> hashByOccurrences = new Dictionary<long, int>();

        for (int i = 0; i < 0; i++)
        {
            generator.Generate();
        }

        for (int i = 0; i < 1_000_000; i++)
        {
            ref var count = ref CollectionsMarshal.GetValueRefOrAddDefault(hashByOccurrences, generator.Generate(), out _);
            count++;
        }

        int sum = 0;
        int j = 0;
        foreach (var entry in hashByOccurrences.OrderByDescending(x => x.Value))
        {
            j++;
            sum += entry.Value;

            if (j > 1.0d * CACHE_SIZE)
            {
                break;
            }
        }

        TestContext.Out.WriteLine("Best theoritical hit rate: " + 100d * sum / 1_000_000 + " %");
    }

    [Test]
    public void BasicTest([ValueSource(nameof(Caches))] ICache<long, long> cache)
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
    public void ComplextTest([ValueSource(nameof(Caches))] ICache<long, long> cache)
    {
        for (int i = 0; i < 2; i++)
        {
            cache.GetOrCreate(1, x => x + 42);
        }

        for (int i = 0; i < 2; i++)
        {
            cache.GetOrCreate(2, x => x + 42);
        }

        for (int i = 0; i < 2; i++)
        {
            cache.GetOrCreate(1, x => x + 42);
        }
    }

    [Test]
    [NonParallelizable]
    public void Benchmark(
        [ValueSource(nameof(Caches))] ICache<long, long> cache,
        [ValueSource(nameof(Generators))] IGenerator<long> generator)
    {
        generator.Reset();
        cache.Clear();
        _cacheMonitoring.Reset();

        // Warmup cache
        for (int i = 0; i < WARMUP_ITERATIONS; i++)
        {
            cache.GetOrCreate(generator.Generate(), static x => x + 1);

            Thread.SpinWait(10);
        }

        _cacheMonitoring.Reset();

        GC.Collect();
        GC.TryStartNoGCRegion(10_000_000);
        var bytesBefore = GC.GetTotalAllocatedBytes(true);

        for (int i = 0; i < TEST_ITERATIONS; i++)
        {
            cache.GetOrCreate(generator.Generate(), static x => x + 1);
        }

        var bytesAfter = GC.GetTotalAllocatedBytes(true);
        GC.EndNoGCRegion();

        Console.WriteLine("Cache: " + cache.GetType());
        Console.WriteLine("Generator: " + generator.GetType());
        Console.WriteLine(Math.Round(100d - 100d * _cacheMonitoring.CacheMisses / _cacheMonitoring.CacheCalls, 2) + " % hits");
        Console.WriteLine(Math.Round(0.001d * (bytesAfter - bytesBefore), 2) + " kb");
    }
}