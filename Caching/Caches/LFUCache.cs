using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Caching;

public class LFUCache<TKey, TValue> : ICache<TKey, TValue>
{
    private readonly ICacheObserver _cacheObserver;

    private readonly Dictionary<TKey, int> _perKeyMap = new();
    private readonly IndexBasedLinkedList<Entry> _entriesByHits= new();
    private readonly IndexBasedLinkedList<FreqCount> _freqsLog10= new();
    
    // Only used for LFURA
    // Index of entry in entries by hits list, ordered by recency
    internal readonly IndexBasedLinkedList<int> _entriesByRecency = new();
    
    private int _maximumKeyCount;

    public LFUCache(
        int maximumKeyCount,
        ICacheObserver cacheObserver)
    {
        _cacheObserver = cacheObserver;
        _maximumKeyCount = maximumKeyCount;
    }

    public int MaximumEntriesCount { get => _maximumKeyCount; set => _maximumKeyCount = value; }

    public virtual TValue GetOrCreate(TKey key, Func<TKey, TValue> factory)
    {
        ref int entryIndex = ref CollectionsMarshal.GetValueRefOrAddDefault(_perKeyMap, key, out bool exists);
        _cacheObserver?.CountCacheCall();

        while (_perKeyMap.Count > _maximumKeyCount)
        {
            RemoveFirst();
        }

        TValue value;

        if (!exists)
        {
            value = factory(key);

            Entry entry = new(key, value);
            entryIndex = _entriesByHits.AddFirst(entry);

            var recencyIndex = _entriesByRecency.AddLast(entryIndex);
            _entriesByHits[_entriesByHits.FirstIndex].value.recency = recencyIndex;

            if (_freqsLog10.Count == 0)
            {
                FreqCount firstHit = new();
                firstHit.freqLog10 = 0; // Aproximation
                firstHit.refCount = 1;
                firstHit.firstEntryWithHitsIndex = entryIndex;
                _freqsLog10.AddFirst(firstHit);
            }
            else
            {
                _freqsLog10[_freqsLog10.FirstIndex].value.refCount++;
                _freqsLog10[_freqsLog10.FirstIndex].value.firstEntryWithHitsIndex = entryIndex;
            }

            _entriesByHits[_entriesByHits.FirstIndex].value.freqIndex = _freqsLog10.FirstIndex;

            _cacheObserver?.CountCacheMiss();

            return value;
        }
            
        return GetValue(ref entryIndex);
    }

    internal int GetFrequency(long lastUsedTimestamp)
    {
        long currentTimestamp = Stopwatch.GetTimestamp();
        double instantFreq = 1d * Stopwatch.Frequency / (currentTimestamp - lastUsedTimestamp);
        return (int)Math.Round(Math.Log10(instantFreq * 1d));
    }

    internal TValue GetValue(ref int entryIndex)
    {
        ref var entryNode = ref _entriesByHits[entryIndex];
        ref var freqNode = ref _freqsLog10[entryNode.value.freqIndex];
        
        int roundedFreq = GetFrequency(entryNode.value.lastUsed);
        
        // Refresh the "last used" timestamp
        entryNode.value.lastUsed = Stopwatch.GetTimestamp();
        
        // If frequency has changed, promote or unpromote the entry
        if (roundedFreq > freqNode.value.freqLog10)
        {
            Promote(ref entryIndex);
        }
        else if (roundedFreq < freqNode.value.freqLog10)
        {
            Unpromote(ref entryIndex);
        }

        return entryNode.value.value;
    }

    internal void Promote(ref int entryIndex)
    {
        ref var entryNode = ref _entriesByHits[entryIndex];
        ref var freqNode = ref _freqsLog10[entryNode.value.freqIndex];
        
        // If there is no next hitsCount node or next node is not hits + 1, we must create this node
        if (freqNode.after == -1)
        {
            var oldEntryIndex = entryIndex;
            var nextEntryIndex = entryNode.after;

            // Copy (because otherwise entry is set to default after removal)
            var entry = entryNode.value;
            var oldHitsCountIndex = entry.freqIndex;

            _entriesByHits.Remove(entryIndex);

            if (freqNode.after == -1)
            {
                // Top 1
                entryIndex = _entriesByHits.AddLast(entry);
            }
            else
            {
                Debug.Assert(_freqsLog10[freqNode.after].value.freqLog10 > freqNode.value.freqLog10 + 1, "Ordering issue");
                entryIndex = _entriesByHits.AddBefore(entry, _freqsLog10[freqNode.after].value.firstEntryWithHitsIndex);
            }

            _entriesByRecency.Remove(entry.recency);
            var recencyIndex = _entriesByRecency.AddLast(entryIndex);
            _entriesByHits[entryIndex].value.recency = recencyIndex;

            // Add registration for hits + 1
            var nextFreq = new FreqCount();
            nextFreq.refCount = 1; // Only one ref since this is a new node
            nextFreq.freqLog10 = freqNode.value.freqLog10 + 1; // Next node is hits + 1
            nextFreq.firstEntryWithHitsIndex = entryIndex;

            // Old hits registration get decrement of refcount
            freqNode.value.refCount--;

            int hitsPlusOneIndex = _freqsLog10.AddAfter(nextFreq, entry.freqIndex);
            _entriesByHits[entryIndex].value.freqIndex = hitsPlusOneIndex;

            // If previous hits registration has no references anymore, remove it
            if (freqNode.value.refCount <= 0)
            {
                _freqsLog10.Remove(entry.freqIndex);
            }
            else
            {
                if (_freqsLog10[oldHitsCountIndex].value.firstEntryWithHitsIndex == oldEntryIndex)
                {
                    _freqsLog10[oldHitsCountIndex].value.firstEntryWithHitsIndex = nextEntryIndex;
                }
            }
        }
        else
        {
            // Copy (because otherwise entry is set to default after removal)
            var entry = entryNode.value;
            var oldHitsCountIndex = entry.freqIndex;
            entry.freqIndex = freqNode.after;

            var oldEntryIndex = entryIndex;
            var nextEntryIndex = entryNode.after;

            ref var hitsPlusOneNode = ref _freqsLog10[freqNode.after];

            _entriesByHits.Remove(entryIndex);
            entryIndex = _entriesByHits.AddAfter(entry, hitsPlusOneNode.value.firstEntryWithHitsIndex);

            _entriesByRecency.Remove(entry.recency);
            var recencyIndex = _entriesByRecency.AddLast(entryIndex);
            _entriesByHits[entryIndex].value.recency = recencyIndex;

            freqNode.value.refCount--;
            hitsPlusOneNode.value.refCount++;

            // If previous hits registration has no references anymore, remove it
            if (freqNode.value.refCount <= 0)
            {
                _freqsLog10.Remove(oldHitsCountIndex);
            }
            else
            {
                if (_freqsLog10[oldHitsCountIndex].value.firstEntryWithHitsIndex == oldEntryIndex)
                {
                    _freqsLog10[oldHitsCountIndex].value.firstEntryWithHitsIndex = nextEntryIndex;
                }
            }
        }
    }

    internal void Unpromote(ref int entryIndex)
    {
        ref var entryNode = ref _entriesByHits[entryIndex];
        ref var freqNode = ref _freqsLog10[entryNode.value.freqIndex];
        
        // If there is no next hitsCount node or next node is not hits + 1, we must create this node
        if (freqNode.before == -1)
        {
            var oldEntryIndex = entryIndex;
            var nextEntryIndex = entryNode.after;

            // Copy (because otherwise entry is set to default after removal)
            var entry = entryNode.value;
            var oldHitsCountIndex = entry.freqIndex;

            _entriesByHits.Remove(entryIndex);

            // if (freqNode.after == -1)
            if (freqNode.before == -1)
            {
                // entryIndex = _entriesByHits.AddLast(entry);
                entryIndex = _entriesByHits.AddFirst(entry);
            }
            else
            {
                Debug.Assert(_freqsLog10[freqNode.before].value.freqLog10 < freqNode.value.freqLog10 + 1, "Ordering issue");
                // entryIndex = _entriesByHits.AddBefore(entry, _freqsLog10[freqNode.after].value.firstEntryWithHitsIndex);
                entryIndex = _entriesByHits.AddBefore(entry, _freqsLog10[freqNode.before].value.firstEntryWithHitsIndex);
            }

            _entriesByRecency.Remove(entry.recency);
            var recencyIndex = _entriesByRecency.AddLast(entryIndex);
            _entriesByHits[entryIndex].value.recency = recencyIndex;

            // Add registration for freq - 1
            var prevFreq = new FreqCount();
            prevFreq.refCount = 1; // Only one ref since this is a new node
            // nextFreq.freqLog10 = freqNode.value.freqLog10 + 1;
            prevFreq.freqLog10 = freqNode.value.freqLog10 - 1; // Next node is hits + 1
            prevFreq.firstEntryWithHitsIndex = entryIndex;

            // Old hits registration get decrement of refcount
            freqNode.value.refCount--;

            // int hitsPlusOneIndex = _freqsLog10.AddAfter(nextFreq, entry.frequency);
            int hitsPlusOneIndex = _freqsLog10.AddBefore(prevFreq, entry.freqIndex);
            _entriesByHits[entryIndex].value.freqIndex = hitsPlusOneIndex;

            // If previous hits registration has no references anymore, remove it
            if (freqNode.value.refCount <= 0)
            {
                _freqsLog10.Remove(entry.freqIndex);
            }
            else
            {
                if (_freqsLog10[oldHitsCountIndex].value.firstEntryWithHitsIndex == oldEntryIndex)
                {
                    _freqsLog10[oldHitsCountIndex].value.firstEntryWithHitsIndex = nextEntryIndex;
                }
            }
        }
        else
        {
            // Copy (because otherwise entry is set to default after removal)
            var entry = entryNode.value;
            var oldHitsCountIndex = entry.freqIndex;
            entry.freqIndex = freqNode.before;

            var oldEntryIndex = entryIndex;
            var nextEntryIndex = entryNode.after;

            ref var hitsMinusOneNode = ref _freqsLog10[freqNode.before];

            _entriesByHits.Remove(entryIndex);
            entryIndex = _entriesByHits.AddAfter(entry, hitsMinusOneNode.value.firstEntryWithHitsIndex);

            _entriesByRecency.Remove(entry.recency);
            var recencyIndex = _entriesByRecency.AddLast(entryIndex);
            _entriesByHits[entryIndex].value.recency = recencyIndex;

            freqNode.value.refCount--;
            hitsMinusOneNode.value.refCount++;

            // If previous hits registration has no references anymore, remove it
            if (freqNode.value.refCount <= 0)
            {
                _freqsLog10.Remove(oldHitsCountIndex);
            }
            else
            {
                if (_freqsLog10[oldHitsCountIndex].value.firstEntryWithHitsIndex == oldEntryIndex)
                {
                    _freqsLog10[oldHitsCountIndex].value.firstEntryWithHitsIndex = nextEntryIndex;
                }
            }
        }
    }

    private void RemoveFirst()
    {
        var entry = _entriesByHits[_entriesByHits.FirstIndex];

        Remove(entry.value.key);
    }

    private void Remove(TKey key)
    {
        var entryIndex = _perKeyMap[key];

        var entry = _entriesByHits[entryIndex];
        _perKeyMap.Remove(entry.value.key);
        ref var hitsCount = ref _freqsLog10[entry.value.freqIndex];
        hitsCount.value.refCount--;
        if (hitsCount.value.refCount == 0)
        {
            _freqsLog10.Remove(entry.value.freqIndex);
        }
        else
        {
            if (hitsCount.value.firstEntryWithHitsIndex == entryIndex)
            {
                hitsCount.value.firstEntryWithHitsIndex = entry.after;
            }
        }

        _entriesByRecency.Remove(entry.value.recency);
        _entriesByHits.Remove(entryIndex);
    }

    public void Clear()
    {
        _freqsLog10.Clear();
        _perKeyMap.Clear();
        _entriesByHits.Clear();
        _entriesByRecency.Clear();
    }

    private struct Entry
    {
        public TKey key;
        public TValue value;
        public long lastUsed;
        public int freqIndex;
        public int recency;

        public Entry(TKey key, TValue value)
        {
            this.key = key;
            this.value = value;
            lastUsed = Stopwatch.GetTimestamp();
            freqIndex = -1;
            recency = 0;
        }
    }

    private record struct FreqCount
    {
        public int firstEntryWithHitsIndex;
        public int refCount;
        public int freqLog10;
    }
}