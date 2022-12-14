using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Caching;

public class LFUCacheNaive<TKey, TValue> : ICache<TKey, TValue>
{
    private readonly ICacheObserver _cacheObserver;

    private readonly Dictionary<TKey, int> _perKeyMap = new();
    private readonly IndexBasedLinkedList<Entry> _entriesByHits = new();

    private int _maximumKeyCount;

    public LFUCacheNaive(int maximumKeyCount, ICacheObserver cacheObserver)
    {
        _cacheObserver = cacheObserver;
        _maximumKeyCount = maximumKeyCount;
    }

    public int MaximumEntriesCount { get => _maximumKeyCount; set => _maximumKeyCount = value; }

    public TValue GetOrCreate(TKey key, Func<TKey, TValue> factory)
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
            _cacheObserver?.CountCacheMiss();
            
            Entry entry = new(key, value);
            entryIndex = _entriesByHits.AddFirst(entry);

            return value;
        }
        else
        {
            int after = _entriesByHits[entryIndex].After;
            ref Entry entry = ref _entriesByHits[entryIndex].value;
            double instantFreq = 1d / (DateTime.UtcNow - entry.lastUsed).TotalSeconds;
            // X% contrib
            entry.frequency = 0.99d * entry.frequency + 0.01d * instantFreq;
            entry.lastUsed = DateTime.UtcNow;

            int current = -1;

            // Way too slow!
            while (after != -1 && entry.frequency > _entriesByHits[after].value.frequency)
            {
                current = after;
                after = _entriesByHits[after].After;
            }

            if (current != -1)
            {
                var entryCopy = entry;
                _entriesByHits.Remove(entryIndex);
                _entriesByHits.AddAfter(entryCopy, current);
            }
            
            return entry.value;
        }
    }

    private void RemoveFirst()
    {
        var entry = _entriesByHits[_entriesByHits.FirstIndex];
        _perKeyMap.Remove(entry.value.key);
        _entriesByHits.Remove(_entriesByHits.FirstIndex);
    }

    public void Clear()
    {
        _perKeyMap.Clear();
        _entriesByHits.Clear();
    }

    internal struct Entry
    {
        public TKey key;
        public TValue value;
        public DateTime lastUsed;
        public double frequency; // hits / s

        public Entry(TKey key, TValue value)
        {
            this.key = key;
            this.value = value;
            lastUsed = DateTime.UtcNow;
            frequency = 0;
        }
    }
}