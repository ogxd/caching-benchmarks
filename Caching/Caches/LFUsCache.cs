using System;
using System.Collections.Generic;
using System.Threading;
using System.Runtime.InteropServices;

namespace Caching;

public class LFUsCache<TKey, TValue> : LFUsCache<TKey, TKey, TValue>
{
    public LFUsCache(
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

public class LFUsCache<TItem, TKey, TValue> : ICache<TItem, TValue>
{
    private readonly ICacheObserver _cacheObserver;

    private readonly Dictionary<TKey, int> _perKeyMap;
    private readonly IndexBasedLinkedList<Entry> _entriesByHits;

    private readonly Func<TItem, TKey> _keyFactory;

    private readonly int _maximumKeyCount;

    public LFUsCache(
        int maximumKeyCount,
        Func<TItem, TKey> keyFactory,
        IEqualityComparer<TKey> keyComparer = null,
        ICacheObserver cacheObserver = null,
        TimeSpan? expiration = null)
    {
        _keyFactory = keyFactory ?? throw new ArgumentNullException("keyFactory");
        _perKeyMap = new Dictionary<TKey, int>(keyComparer ?? EqualityComparer<TKey>.Default);
        _entriesByHits = new IndexBasedLinkedList<Entry>();
        _cacheObserver = cacheObserver;
        _maximumKeyCount = maximumKeyCount;
    }

    public TValue GetOrCreate(TItem item, Func<TItem, TValue> factory)
    {
        TKey key = _keyFactory(item);

        ref int entryIndex = ref CollectionsMarshal.GetValueRefOrAddDefault(_perKeyMap, key, out bool exists);
        _cacheObserver?.CountCacheCall();

        if (_perKeyMap.Count > 1.2d * _maximumKeyCount)
        {
            while (_perKeyMap.Count > _maximumKeyCount)
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

            _cacheObserver?.CountCacheMiss();

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
        public DateTime insertion;
        public DateTime lastUsed;
        public double frequency; // hits / s

        public Entry(TKey key, TValue value)
        {
            this.key = key;
            this.value = value;
            insertion = DateTime.UtcNow;
            lastUsed = DateTime.UtcNow;
            frequency = 0;
        }
    }
}