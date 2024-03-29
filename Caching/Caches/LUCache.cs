﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Caching;

/// <summary>
/// Eviction Policy: Less Used 
/// </summary>
/// <typeparam name="TKey"></typeparam>
/// <typeparam name="TValue"></typeparam>
public class LUCache<TKey, TValue> : ICache<TKey, TValue>
{
    private readonly Dictionary<TKey, int> _perKeyMap = new();
    private protected readonly IndexBasedLinkedList<Entry> _entriesByHits = new();
    private protected readonly IndexBasedLinkedList<HitsCount> _hitsCount = new();

    public string Name { get; set; }
    
    public int MaximumEntriesCount { get; set; }
    
    public ICacheObserver Observer { get; set; }
    
    public bool InsertStartOfFrequencyBucket { get; set; }
    
    public TValue GetOrCreate(TKey key, Func<TKey, TValue> factory)
    {
        ref int entryIndex = ref CollectionsMarshal.GetValueRefOrAddDefault(_perKeyMap, key, out bool exists);
        Observer?.CountCacheCall();

        TValue value;

        if (exists)
        {
            return Promote(ref entryIndex);
        }
        
        while (_entriesByHits.Count >= MaximumEntriesCount)
        {
            RemoveFirst();
        }

        value = Add(ref entryIndex, key, factory);

        return value;
    }

    protected virtual TValue Add(ref int entryIndex, TKey key, Func<TKey, TValue> factory)
    {
        TValue value;
        value = factory(key);
        Observer?.CountCacheMiss();

        Entry entry = new(key, value);

        if (ShouldCreateOneHitBucket)
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
            AddInExistingBuckets(ref entryIndex, entry);
        }

        return value;
    }

    protected virtual bool ShouldCreateOneHitBucket => _hitsCount.Count == 0 || _hitsCount[_hitsCount.FirstIndex].Value.hits > 1;

    protected virtual void AddInExistingBuckets(ref int entryIndex, Entry entry)
    {
        ref var hitsNode = ref _hitsCount[_hitsCount.FirstIndex];
        if (!InsertStartOfFrequencyBucket && _hitsCount.Count > 1)
        {
            entryIndex = _entriesByHits.AddBefore(entry, _hitsCount[hitsNode.after].value.firstEntryWithHitsIndex);
        }
        else
        {
            entryIndex = _entriesByHits.AddFirst(entry);
            hitsNode.value.firstEntryWithHitsIndex = entryIndex;
        }
        
        _entriesByHits[entryIndex].value.hitsCountIndex = _hitsCount.FirstIndex;
        hitsNode.value.refCount++;
    }

    private TValue Promote(ref int entryIndex)
    {
        TValue value;
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
                    //Debug.Assert(hitsCountNode.after == _hotEntries[nextEntryIndex].value.hitsCountIndex);
                    _hitsCount[oldHitsCountIndex].value.firstEntryWithHitsIndex = nextEntryIndex;
                }
            }
        }

        return value;
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

    protected struct Entry
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

    protected record struct HitsCount
    {
        public int firstEntryWithHitsIndex;
        public int refCount;
        public int hits;
    }
}