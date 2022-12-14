
using System;

namespace Caching;

/// <summary>
/// Metrics interface for Smart.Cache
/// </summary>
public interface ICacheObserver
{
    /// <summary>
    /// Monitors the cache latency when refreshing
    /// </summary>
    /// <param name="latencyOnRefresh">Latency of the factory function</param>
    void CountCacheLatency(TimeSpan latencyOnRefresh);

    /// <summary>
    /// Called for every cache call.
    /// </summary>
    void CountCacheCall();

    /// <summary>
    /// Count number of times a key with no value is requested, whether is was cached or not.
    /// </summary>
    void CountNoValue();

    /// <summary>
    /// Called for every factory method invocation, no matter if it returned a value or not.
    /// </summary>
    void CountCacheMiss();
}