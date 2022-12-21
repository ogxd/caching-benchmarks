using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Caching;

/// <summary>
/// Eviction Policy: Probatory Less Frequently Used with Recency Aging
/// </summary>
/// <typeparam name="TKey"></typeparam>
/// <typeparam name="TValue"></typeparam>
public class PLFURACache<TKey, TValue> : PLFUCache<TKey, TValue>
{
    public override TValue GetOrCreate(TKey key, Func<TKey, TValue> factory)
    {
        if (_entriesByRecency.Count > 0)
        {
            // Retreive index on least recently refreshed item
            int lruIndex = _entriesByRecency[_entriesByRecency.FirstIndex].value;
            Entry lruEntry = _hotEntries[lruIndex].value;
            ref int entryIndex = ref CollectionsMarshal.GetValueRefOrNullRef(_perKeyMap, lruEntry.key);
            
            // lruIndex == entryIndex but we need a ref to the value in the map to update it
            Debug.Assert(lruIndex == entryIndex);

            // Unpromote that item frequency
            Touch(ref entryIndex, lruEntry.frequency, lruEntry.frequency - 1);
        }

        return base.GetOrCreate(key, factory);
    }
}