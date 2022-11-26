using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Caching;

public class LRUCache<TKey, TValue> : LRUCache<TKey, TKey, TValue>
{
    public LRUCache(
        int maximumKeyCount,
        double oversize,
        IEqualityComparer<TKey> keyComparer = null,
        ICacheObserver cacheObserver = null,
        TimeSpan? expiration = null) : base(
            maximumKeyCount,
            oversize,
            static item => item,
            keyComparer,
            cacheObserver,
            expiration)
    { }
}

public class LRUCache<TItem, TKey, TValue> : ICache<TItem, TValue>
{
    private readonly ICacheObserver _cacheObserver;

    private readonly Dictionary<TKey, int> _perKeyMap;
    private readonly IndexBasedLinkedList<Entry> _entriesByHits;

    private readonly Func<TItem, TKey> _keyFactory;

    private readonly double _oversize;

    private int _maximumKeyCount;

    public LRUCache(
        int maximumKeyCount,
        double oversize,
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
        _oversize = oversize;
    }

    public int MaxSize { get => _maximumKeyCount; set => _maximumKeyCount = value; }

    public TValue GetOrCreate(TItem item, Func<TItem, TValue> factory)
    {
        TKey key = _keyFactory(item);

        ref int entryIndex = ref CollectionsMarshal.GetValueRefOrAddDefault(_perKeyMap, key, out bool exists);
        _cacheObserver?.CountCacheCall();

        while (_perKeyMap.Count > _maximumKeyCount)
        {
            RemoveFirst();
        }

        TValue value;

        if (!exists)
        {
            value = factory(item);

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

    internal struct Entry
    {
        public TKey key;
        public TValue value;
        public DateTime insertion;
        public DateTime lastUsed;

        public Entry(TKey key, TValue value)
        {
            this.key = key;
            this.value = value;
            insertion = DateTime.UtcNow;
            lastUsed = DateTime.UtcNow;
        }
    }
}