using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Caching;

public class LRUCache<TKey, TValue> : ICache<TKey, TValue>
{
    private readonly ICacheObserver _cacheObserver;

    private readonly Dictionary<TKey, int> _perKeyMap = new();
    private readonly IndexBasedLinkedList<Entry> _entriesByHits = new();

    private int _maximumKeyCount;

    public LRUCache(int maximumKeyCount, ICacheObserver cacheObserver)
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

            Entry entry = new(key, value);
            entryIndex = _entriesByHits.AddLast(entry);

            _cacheObserver?.CountCacheMiss();

            return value;
        }
        else
        {
            int after = _entriesByHits[entryIndex].After;
            Entry entry = _entriesByHits[entryIndex].Value;

            if (after != -1)
            {
                _entriesByHits.Remove(entryIndex);
                _entriesByHits.AddLast(entry);
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

    private struct Entry
    {
        public TKey key;
        public TValue value;

        public Entry(TKey key, TValue value)
        {
            this.key = key;
            this.value = value;
        }
    }
}