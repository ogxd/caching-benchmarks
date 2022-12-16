using System;

namespace Caching;

public class CacheCounter : ICacheObserver
{
    public int CountCalls { get; private set; }

    public int CountMisses { get; private set; }

    public int CountNoValues { get; private set; }

    public double AverageLatency => _latencySumsMs / CountCalls;
    
    private double _latencySumsMs;

    public void CountCacheLatency(TimeSpan latency)
    {
        _latencySumsMs += latency.TotalMilliseconds;
    }

    public void CountCacheCall()
    {
        CountCalls++;
    }

    public void CountCacheMiss()
    {
        CountMisses++;
    }

    public void CountNoValue()
    {
        CountNoValues++;
    }

    public void Reset()
    {
        CountCalls = 0;
        CountMisses = 0;
        CountNoValues = 0;
        _latencySumsMs = 0;
    }
}