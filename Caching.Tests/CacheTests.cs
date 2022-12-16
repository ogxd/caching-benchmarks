using System.Threading;
using Caching.Caches;
using NUnit.Framework;

namespace Caching.Tests;

public class CacheTests
{
    [TestCase<LRUCache<long, long>>]
    [TestCase<SLRUCache<long, long>>]
    [TestCase<MSLRUCache<long, long>>]
    [TestCase<LUCache<long, long>>]
    [TestCase<LUCacheOfficial<long, long>>]
    [TestCase<LUDACache<long, long>>]
    [TestCase<LFUCacheNaive<long, long>>]
    [TestCase<LFUCache<long, long>>]
    [TestCase<LFURACache<long, long>>]
    [TestCase<ARCCache<long, long>>] // Has probatory segment but won't kick-in until capacity is reached
    public void Second_Access_Is_Cache_Hit(ICache<long, long> cache)
    {
        CacheCounter counter = new();
        
        cache.MaximumEntriesCount = 100;
        cache.Observer = counter;

        Assert.AreEqual(0, cache.GetOrCreate(0, x => x));
        
        Assert.AreEqual(1, counter.CountMisses);
        Assert.AreEqual(1, counter.CountCalls);

        Assert.AreEqual(0, cache.GetOrCreate(0, x => x));
        
        Assert.AreEqual(1, counter.CountMisses);
        Assert.AreEqual(2, counter.CountCalls);

        Assert.AreEqual(1, cache.GetOrCreate(1, x => x));
        
        Assert.AreEqual(2, counter.CountMisses);
        Assert.AreEqual(3, counter.CountCalls);
        
        Assert.AreEqual(0, cache.GetOrCreate(0, x => x));
        
        Assert.AreEqual(2, counter.CountMisses);
        Assert.AreEqual(4, counter.CountCalls);
    }
    
    [TestCase<PLFUCache<long, long>>]
    public void Entry_Is_Probatory_If_Accessed_Once(ICache<long, long> cache)
    {
        CacheCounter counter = new();
        
        cache.MaximumEntriesCount = 100;
        cache.Observer = counter;

        Assert.AreEqual(0, cache.GetOrCreate(0, x => x));
        
        Assert.AreEqual(1, counter.CountMisses);
        Assert.AreEqual(1, counter.CountCalls);

        Thread.Sleep(100);
        
        Assert.AreEqual(0, cache.GetOrCreate(0, x => x));
        
        // Shall count as a miss again because item was in a probatory segment
        Assert.AreEqual(2, counter.CountMisses);
        Assert.AreEqual(2, counter.CountCalls);

        Assert.AreEqual(0, cache.GetOrCreate(0, x => x));
        
        // This time it shouldn't leat to a cache miss
        Assert.AreEqual(2, counter.CountMisses);
        Assert.AreEqual(3, counter.CountCalls);
    }
}