using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Caching;

/// <summary>
/// Eviction Policy: Less Used with Dynamic Aging
/// </summary>
/// <typeparam name="TKey"></typeparam>
/// <typeparam name="TValue"></typeparam>
public class LUDACache<TKey, TValue> : ICache<TKey, TValue>
{
    private readonly Dictionary<TKey, int> _perKeyMap = new();
    private readonly IndexBasedLinkedList<Entry> _entriesByHits = new();
    private readonly IndexBasedLinkedList<HitsCount> _hitsCount = new();

    public string Name { get; set; }
    
    public int MaximumEntriesCount { get; set; }
    
    public ICacheObserver Observer { get; set; }
    
    public TValue GetOrCreate(TKey key, Func<TKey, TValue> factory)
    {
        ref int entryIndex = ref CollectionsMarshal.GetValueRefOrAddDefault(_perKeyMap, key, out bool exists);
        Observer?.CountCacheCall();

        while (_perKeyMap.Count > MaximumEntriesCount)
        {
            RemoveFirst();
        }

        TValue value;

        if (!exists)
        {
            value = factory(key);
            Observer?.CountCacheMiss();
            
            Entry entry = new(key, value);

            if (_hitsCount.Count == 0)
            {
                entryIndex = _entriesByHits.AddFirst(entry);

                HitsCount firstHit = new();
                firstHit.hits = 1;
                firstHit.refCount = 1;
                firstHit.firstEntryWithHitsIndex = entryIndex;
                _hitsCount.AddFirst(firstHit);

                _entriesByHits[entryIndex].value.hitsCountIndex = _hitsCount.FirstIndex;
            }
            else
            {
                if (_hitsCount.Count == 1)
                {
                    entryIndex = _entriesByHits.AddFirst(entry);

                    ref var n = ref _hitsCount[_hitsCount.FirstIndex].value;
                    n.refCount++;
                    n.firstEntryWithHitsIndex = entryIndex;

                    _entriesByHits[entryIndex].value.hitsCountIndex = _hitsCount.FirstIndex;
                }
                else
                {
                    // Dynamic Aging is done by not inserting after first hits block instead of before

                    ref var n = ref _hitsCount[_hitsCount[_hitsCount.FirstIndex].after].value;

                    entryIndex = _entriesByHits.AddBefore(entry, n.firstEntryWithHitsIndex);

                    n.refCount++;
                    n.firstEntryWithHitsIndex = entryIndex;

                    _entriesByHits[entryIndex].value.hitsCountIndex = _hitsCount[_hitsCount.FirstIndex].after;
                }
            }
            
            return value;
        }
        else
        {
            ref var entryNode = ref _entriesByHits[entryIndex];
            ref var hitsCountNode = ref _hitsCount[entryNode.value.hitsCountIndex];

            value = entryNode.value.value;

            // If there is no next hitsCount node or next node is not hits + 1, we must create this node
            if (hitsCountNode.after == -1
            || _hitsCount[hitsCountNode.after].value.hits != hitsCountNode.value.hits + 1)
            {
                var oldEntryIndex = entryIndex;
                var nextEntryIndex = entryNode.after;

                // Copy (because otherwise entry is set to default after removal)
                var entry = entryNode.value;
                var oldHitsCountIndex = entry.hitsCountIndex;

                _entriesByHits.Remove(entryIndex);

                if (hitsCountNode.after == -1)
                {
                    // Top 1
                    entryIndex = _entriesByHits.AddLast(entry);
                }
                else
                {
                    Debug.Assert(_hitsCount[hitsCountNode.after].value.hits > hitsCountNode.value.hits + 1, "Ordering issue");
                    entryIndex = _entriesByHits.AddBefore(entry, _hitsCount[hitsCountNode.after].value.firstEntryWithHitsIndex);
                }

                // Add registration for hits + 1
                var hitsPlusOne = new HitsCount();
                hitsPlusOne.refCount = 1; // Only one ref since this is a new node
                hitsPlusOne.hits = hitsCountNode.value.hits + 1; // Next node is hits + 1
                hitsPlusOne.firstEntryWithHitsIndex = entryIndex;

                // Old hits registration get decrement of refcount
                hitsCountNode.value.refCount--;

                int hitsPlusOneIndex = _hitsCount.AddAfter(hitsPlusOne, entry.hitsCountIndex);
                _entriesByHits[entryIndex].value.hitsCountIndex = hitsPlusOneIndex;

                // If previous hits registration has no references anymore, remove it
                if (hitsCountNode.value.refCount <= 0)
                {
                    _hitsCount.Remove(entry.hitsCountIndex);
                }
                else
                {
                    if (_hitsCount[oldHitsCountIndex].value.firstEntryWithHitsIndex == oldEntryIndex)
                    {
                        _hitsCount[oldHitsCountIndex].value.firstEntryWithHitsIndex = nextEntryIndex;
                    }
                }
            }
            else
            {
                // Copy (because otherwise entry is set to default after removal)
                var entry = entryNode.value;
                var oldHitsCountIndex = entry.hitsCountIndex;
                entry.hitsCountIndex = hitsCountNode.after;

                var oldEntryIndex = entryIndex;
                var nextEntryIndex = entryNode.after;

                ref var hitsPlusOneNode = ref _hitsCount[hitsCountNode.after];

                _entriesByHits.Remove(entryIndex);
                entryIndex = _entriesByHits.AddAfter(entry, hitsPlusOneNode.value.firstEntryWithHitsIndex);

                hitsCountNode.value.refCount--;
                hitsPlusOneNode.value.refCount++;

                // If previous hits registration has no references anymore, remove it
                if (hitsCountNode.value.refCount <= 0)
                {
                    _hitsCount.Remove(oldHitsCountIndex);
                }
                else
                {
                    if (_hitsCount[oldHitsCountIndex].value.firstEntryWithHitsIndex == oldEntryIndex)
                    {
                        //Debug.Assert(hitsCountNode.after == _entriesByHits[nextEntryIndex].value.hitsCountIndex);
                        _hitsCount[oldHitsCountIndex].value.firstEntryWithHitsIndex = nextEntryIndex;
                    }
                }
            }

            return value;
        }
    }

    private void RemoveFirst()
    {
        var entry = _entriesByHits[_entriesByHits.FirstIndex];
        _perKeyMap.Remove(entry.value.key);
        ref var hitsCount = ref _hitsCount[entry.value.hitsCountIndex];
        hitsCount.value.refCount--;
        if (hitsCount.value.refCount == 0)
        {
            _hitsCount.Remove(entry.value.hitsCountIndex);
        }
        else
        {
            if (hitsCount.value.firstEntryWithHitsIndex == _entriesByHits.FirstIndex) // Normally this is always true
            {
                hitsCount.value.firstEntryWithHitsIndex = entry.after;
            }
        }
        _entriesByHits.Remove(_entriesByHits.FirstIndex);
    }

    public void Clear()
    {
        _hitsCount.Clear();
        _perKeyMap.Clear();
        _entriesByHits.Clear();
    }

    private struct Entry
    {
        public TKey key;
        public TValue value;
        public int hitsCountIndex;

        public Entry(TKey key, TValue value)
        {
            this.key = key;
            this.value = value;
            hitsCountIndex = -1;
        }
    }

    private record struct HitsCount
    {
        public int firstEntryWithHitsIndex;
        public int refCount;
        public int hits;
    }
}