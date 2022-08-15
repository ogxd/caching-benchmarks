using System;

namespace Caching.Tests;

public class ObserverTest : ICacheObserver
{
    public string CacheName { get; } = "unit_tests";
    public string CacheType { get; } = "unit_tests";

    public int CacheCalls { get; private set; }

    public int CacheMisses { get; private set; }

    public int NoValues { get; private set; }

    public void CountCacheLatency(TimeSpan latencyOnRefresh)
    {
        //
    }

    public void CountCacheCall()
    {
        CacheCalls++;
    }

    public void CountCacheMiss()
    {
        CacheMisses++;
    }

    public void CountNoValue()
    {
        NoValues++;
    }

    public void Reset()
    {
        CacheCalls = 0;
        CacheMisses = 0;
        NoValues = 0;
    }
}