﻿using System;
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

public class LFURACache<TItem, TKey, TValue> : ICache<TItem, TValue>
{
    private readonly ICacheObserver _cacheObserver;

    private readonly Dictionary<TKey, int> _perKeyMap;
    private readonly IndexBasedLinkedList<Entry> _entriesByHits;
    private readonly IndexBasedLinkedList<HitsCount> _hitsCount;
    private readonly IndexBasedLinkedList<int> _entriesByRecency;

    private readonly Func<TItem, TKey> _keyFactory;

    private readonly int _maximumKeyCount;

    public LFURACache(
        int maximumKeyCount,
        Func<TItem, TKey> keyFactory,
        IEqualityComparer<TKey> keyComparer = null,
        ICacheObserver cacheObserver = null,
        TimeSpan? expiration = null)
    {
        _keyFactory = keyFactory ?? throw new ArgumentNullException("keyFactory");
        _perKeyMap = new Dictionary<TKey, int>(keyComparer ?? EqualityComparer<TKey>.Default);
        _entriesByHits = new IndexBasedLinkedList<Entry>();
        _hitsCount = new IndexBasedLinkedList<HitsCount>();
        _entriesByRecency = new();
        _cacheObserver = cacheObserver;
        _maximumKeyCount = maximumKeyCount;
    }

    public TValue GetOrCreate(TItem item, Func<TItem, TValue> factory)
    {
        if (_entriesByRecency.Count > 0)
        {
            var x = _entriesByRecency[_entriesByRecency.FirstIndex].value;
            // Refreshes order of least recently used item
            
            ref int eI = ref CollectionsMarshal.GetValueRefOrAddDefault(_perKeyMap, _entriesByHits[x].value.key, out bool t);
            Debug.Assert(t);
            Debug.Assert(x == eI);

            GetValue(ref eI, false);

            //_perKeyMap[_entriesByHits[x].value.key] = x;

            Debug.Assert(_entriesByHits[_entriesByRecency[_entriesByRecency.FirstIndex].value].used);
            Debug.Assert(_entriesByRecency.Count == _entriesByHits.Count);
        }


        TKey key = _keyFactory(item);

        ref int entryIndex = ref CollectionsMarshal.GetValueRefOrAddDefault(_perKeyMap, key, out bool exists);
        _cacheObserver?.CountCacheCall();

        if (_perKeyMap.Count > 1.2d * _maximumKeyCount)
        {
            while (_perKeyMap.Count > 1.0d * _maximumKeyCount)
            {
                RemoveFirst();
            }
        }

        TValue value;

        if (!exists)
        {
            value = factory(item);

            Entry entry = new(key, value);
            entryIndex = _entriesByHits.AddFirst(entry);

            var recencyIndex = _entriesByRecency.AddLast(entryIndex);
            _entriesByHits[_entriesByHits.FirstIndex].value.recency = recencyIndex;

            if (_hitsCount.Count == 0
             || _hitsCount[_hitsCount.FirstIndex].Value.hits > 1)
            {
                HitsCount firstHit = new();
                firstHit.hits = 0;
                firstHit.refCount = 1;
                firstHit.firstEntryWithHitsIndex = entryIndex;
                _hitsCount.AddFirst(firstHit);
            }
            else
            {
                _hitsCount[_hitsCount.FirstIndex].value.refCount++;
                _hitsCount[_hitsCount.FirstIndex].value.firstEntryWithHitsIndex = entryIndex;
            }

            _entriesByHits[_entriesByHits.FirstIndex].value.hitsCountIndex = _hitsCount.FirstIndex;

            _cacheObserver?.CountCacheMiss();

            return value;
        }
        else
        {
            value = GetValue(ref entryIndex);

            return value;
        }
    }

    private TValue GetValue(ref int entryIndex, bool updateUsed = true)
    {
        TValue value;

        ref var entryNode = ref _entriesByHits[entryIndex];
        ref var hitsCountNode = ref _hitsCount[entryNode.value.hitsCountIndex];

        long tt = Stopwatch.GetTimestamp();
        double instantFreq = 1d * Stopwatch.Frequency / (tt - entryNode.value.lastUsed);
        int roundedFreq = (int)Math.Round(Math.Log10(instantFreq * 1d));

        if (updateUsed)
        {
            entryNode.value.lastUsed = tt;
        }

        value = entryNode.value.value;

        if (roundedFreq > hitsCountNode.value.hits)
        {
            // If there is no next hitsCount node or next node is not hits + 1, we must create this node
            if (hitsCountNode.after == -1
            || _hitsCount[hitsCountNode.after].value.hits != hitsCountNode.value.hits + 1)
            {
                var oldEntryIndex = entryIndex;
                var nextEntryIndex = entryNode.after;

                // Copy (because otherwise entry is set to default after removal)
                var entry = entryNode.value;
                var oldHitsCountIndex = entry.hitsCountIndex;

                _entriesByRecency.Remove(entry.recency);

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

                var recencyIndex = _entriesByRecency.AddLast(entryIndex);
                _entriesByHits[entryIndex].value.recency = recencyIndex;

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

                _entriesByRecency.Remove(entry.recency);

                _entriesByHits.Remove(entryIndex);
                entryIndex = _entriesByHits.AddAfter(entry, hitsPlusOneNode.value.firstEntryWithHitsIndex);

                var recencyIndex = _entriesByRecency.AddLast(entryIndex);
                _entriesByHits[entryIndex].value.recency = recencyIndex;

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
                        _hitsCount[oldHitsCountIndex].value.firstEntryWithHitsIndex = nextEntryIndex;
                    }
                }
            }
        }
        else if (roundedFreq < hitsCountNode.value.hits)
        {
            // If there is no next hitsCount node or next node is not hits + 1, we must create this node
            if (hitsCountNode.before == -1
            || _hitsCount[hitsCountNode.before].value.hits != hitsCountNode.value.hits - 1)
            {
                var oldEntryIndex = entryIndex;
                var nextEntryIndex = entryNode.after;

                // Copy (because otherwise entry is set to default after removal)
                var entry = entryNode.value;
                var oldHitsCountIndex = entry.hitsCountIndex;

                _entriesByRecency.Remove(entry.recency);

                _entriesByHits.Remove(entryIndex);

                if (hitsCountNode.before == -1)
                {
                    entryIndex = _entriesByHits.AddFirst(entry);
                }
                else
                {
                    Debug.Assert(_hitsCount[hitsCountNode.before].value.hits < hitsCountNode.value.hits + 1, "Ordering issue");
                    entryIndex = _entriesByHits.AddBefore(entry, _hitsCount[hitsCountNode.before].value.firstEntryWithHitsIndex);
                }

                var recencyIndex = _entriesByRecency.AddLast(entryIndex);
                _entriesByHits[entryIndex].value.recency = recencyIndex;

                // Add registration for hits + 1
                var hitsPlusOne = new HitsCount();
                hitsPlusOne.refCount = 1; // Only one ref since this is a new node
                hitsPlusOne.hits = hitsCountNode.value.hits - 1; // Next node is hits + 1
                hitsPlusOne.firstEntryWithHitsIndex = entryIndex;

                // Old hits registration get decrement of refcount
                hitsCountNode.value.refCount--;

                int hitsPlusOneIndex = _hitsCount.AddBefore(hitsPlusOne, entry.hitsCountIndex);
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
                entry.hitsCountIndex = hitsCountNode.before;

                var oldEntryIndex = entryIndex;
                var nextEntryIndex = entryNode.after;

                ref var hitsMinusOneNode = ref _hitsCount[hitsCountNode.before];

                _entriesByRecency.Remove(entry.recency);

                _entriesByHits.Remove(entryIndex);
                entryIndex = _entriesByHits.AddAfter(entry, hitsMinusOneNode.value.firstEntryWithHitsIndex);

                var recencyIndex = _entriesByRecency.AddLast(entryIndex);
                _entriesByHits[entryIndex].value.recency = recencyIndex;

                hitsCountNode.value.refCount--;
                hitsMinusOneNode.value.refCount++;

                // If previous hits registration has no references anymore, remove it
                if (hitsCountNode.value.refCount <= 0)
                {
                    _hitsCount.Remove(oldHitsCountIndex);
                }
                else
                {
                    if (_hitsCount[oldHitsCountIndex].value.firstEntryWithHitsIndex == oldEntryIndex)
                    {
                        _hitsCount[oldHitsCountIndex].value.firstEntryWithHitsIndex = nextEntryIndex;
                    }
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

        _entriesByRecency.Remove(entry.value.recency);
        _entriesByHits.Remove(_entriesByHits.FirstIndex);
    }

    public void Clear()
    {
        _hitsCount.Clear();
        _perKeyMap.Clear();
        _entriesByHits.Clear();
        _entriesByRecency.Clear();
    }

    private bool CheckHitsCount()
    {
        var hitsCount = _hitsCount.ToArray();
        for (int i = 0; i < hitsCount.Length - 1; i++)
        {
            // Check order
            if (hitsCount[i].hits >= hitsCount[i + 1].hits)
                return false;

            // Check pointer
            if (hitsCount[i].firstEntryWithHitsIndex == hitsCount[i + 1].firstEntryWithHitsIndex)
                return false;
        }

        return true;
    }

    private bool CheckThatFirstEntryIsFirst()
    {
        var hitsCount = _hitsCount.ToArray();
        for (int i = 0; i < hitsCount.Length; i++)
        {
            int firstEntryWithHitsIndex = hitsCount[i].firstEntryWithHitsIndex;

            var entry = _entriesByHits[firstEntryWithHitsIndex];
            int before = entry.before;

            if (before != -1)
            {
                var entryBefore = _entriesByHits[before];

                if (entry.value.hitsCountIndex == entryBefore.value.hitsCountIndex)
                    return false;
            }
        }

        return true;
    }


    private bool CheckOrder()
    {
        var entries = _entriesByHits.ToArray();
        var hitsCount = _hitsCount.ToArray();
        for (int i = 0; i < entries.Length - 1; i++)
        {
            if (_hitsCount[entries[i].hitsCountIndex].value.hits > _hitsCount[entries[i + 1].hitsCountIndex].value.hits)
                return false;

            if (_hitsCount[entries[i].hitsCountIndex].value.refCount <= 0)
                return false;
        }

        return true;
    }


    private void CheckPromote()
    {

    }

#pragma warning disable S125

    //   A  B  C D E F G H I J K L M N O P Q
    // 123 42 12 7 5 5 3 3 2 2 1 1 1 1 1 1 1

    // Get O
    //                        v--------|
    //   A  B  C D E F G H I J K L M N O P Q
    // 123 42 12 7 5 5 3 3 2 2 1 1 1 1 1 1 1

    // Get R
    //                                       v
    //   A  B  C D E F G H I J O K L M N P Q R
    // 123 42 12 7 5 5 3 3 2 2 2 1 1 1 1 1 1 1

    // Get R
    //                                       v
    //   A  B  C D E F G H I J O K L M N P Q R
    // 123 42 12 7 5 5 3 3 2 2 2 1 1 1 1 1 1 1
    //   |  |  | |   |   |     |             |
    // 123 42 12 7   5   3     2             1  
    //   1  1  1 1   2   2     3             7

    // Get N
    //                                 |
    //   A  B  C D E F G H I J O K L M N P Q R
    // 123 42 12 7 5 5 3 3 2 2 2 1 1 1 1 1 1 1
    //   |  |  | |   |   |     |             |
    // 123 42 12 7   5   3     2             1  
    //   1  1  1 1   2   2     3             7


    internal struct Entry
    {
        public TKey key;
        public TValue value;
        public DateTime insertion;
        public long lastUsed;
        public int hitsCountIndex;
        public int recency;

        public Entry(TKey key, TValue value)
        {
            this.key = key;
            this.value = value;
            insertion = DateTime.UtcNow;
            lastUsed = Stopwatch.GetTimestamp();
            hitsCountIndex = -1;
            recency = 0;
        }
    }

    internal record struct HitsCount
    {
        public int firstEntryWithHitsIndex;
        public int refCount;
        public int hits;
    }
}