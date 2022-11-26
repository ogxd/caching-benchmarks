using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Caching;

public class LFURACache<TKey, TValue> : LFURACache<TKey, TKey, TValue>
{
    public LFURACache(
        int maximumKeyCount,
        IEqualityComparer<TKey> keyComparer = null,
        ICacheObserver cacheObserver = null,
        TimeSpan? expiration = null) : base(
        maximumKeyCount,
        static item => item,
        keyComparer,
        cacheObserver,
        expiration)
    { }
}

public class LFURACache<TItem, TKey, TValue> : LFUCache<TItem, TKey, TValue>
{
    public LFURACache(
        int maximumKeyCount,
        Func<TItem, TKey> keyFactory,
        IEqualityComparer<TKey> keyComparer = null,
        ICacheObserver cacheObserver = null,
        TimeSpan? expiration = null) : base(maximumKeyCount, keyFactory, keyComparer, cacheObserver, expiration)
    {

    }

    public override TValue GetOrCreate(TItem item, Func<TItem, TValue> factory)
    {
        if (_entriesByRecency.Count > 0)
        {
            // Retreive index on least recently refreshed item
            int leastRecentlyRefreshed = _entriesByRecency[_entriesByRecency.FirstIndex].value;
            
            GetValue(ref leastRecentlyRefreshed, false);
        }

        return base.GetOrCreate(item, factory);
    }
}