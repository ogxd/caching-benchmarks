using System;
using System.Threading;
using Caching.Caches;
using NUnit.Framework;

namespace Caching.Tests;

public class ProbatoryLFUTests
{
    [Test]
    public void Entry_Start_As_Probatory()
    {
        CacheCounter counter = new();
        
        PLFUCache<int, int> cache = new();
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
    
    [Test]
    public void COfficial()
    {
        CacheCounter counter = new();
        
        //LUCache<int, int> cache = new();
        LUCacheOfficial<int, int> cache = new();
        cache.MaximumEntriesCount = 5;
        cache.Observer = counter;

        Random rnd = new(0);
        for (int i = 0; i < 20; i++)
        {
            cache.GetOrCreate(rnd.Next(0, 8), j => j);
        }
    }
    
    [Test]
    public void Custom()
    {
        CacheCounter counter = new();
        
        LUCache<int, int> cache = new();
        //LUCacheOfficial<int, int> cache = new();
        cache.MaximumEntriesCount = 5;
        cache.Observer = counter;

        Random rnd = new(0);
        for (int i = 0; i < 20; i++)
        {
            cache.GetOrCreate(rnd.Next(0, 8), j => j);
        }
    }
}