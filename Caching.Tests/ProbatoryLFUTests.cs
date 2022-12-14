using System.Threading;
using NUnit.Framework;

namespace Caching.Tests;

public class ProbatoryLFUTests
{
    [Test]
    public void Entry_Start_As_Probatory()
    {
        CacheCounter counter = new();
        
        ProbatoryLFUCache<int, int> cache = new(100, 1, true, true, cacheObserver: counter);

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