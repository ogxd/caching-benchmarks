using System;

namespace Caching;

public class LFURACache<TKey, TValue> : LFUCache<TKey, TValue>
{
    public LFURACache(int maximumKeyCount, ICacheObserver cacheObserver) 
        : base(maximumKeyCount, cacheObserver)
    {

    }

    public override TValue GetOrCreate(TKey key, Func<TKey, TValue> factory)
    {
        if (_entriesByRecency.Count > 0)
        {
            // Retreive index on least recently refreshed item
            int leastRecentlyRefreshed = _entriesByRecency[_entriesByRecency.FirstIndex].value;
            
            Unpromote(ref leastRecentlyRefreshed);
        }

        return base.GetOrCreate(key, factory);
    }
}