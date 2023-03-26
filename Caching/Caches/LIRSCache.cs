using System;
using System.Collections.Generic;

namespace Caching;

public class LIRSCache<TKey, TValue> : ICache<TKey, TValue>
{
    // Could be great optimized with OptimizedLinkedList
    private readonly LinkedList<TKey> S;
    private readonly LinkedList<TKey> Q;
    private readonly Dictionary<TKey, CacheItem> cache;

    public LIRSCache()
    {
        S = new LinkedList<TKey>();
        Q = new LinkedList<TKey>();
        cache = new Dictionary<TKey, CacheItem>(MaximumEntriesCount);
    }
    
    public string Name { get; set; }
    public int MaximumEntriesCount { get; set; }
    public ICacheObserver Observer { get; set; }

    public TValue GetOrCreate(TKey key, Func<TKey, TValue> factory)
    {
        Observer.CountCacheCall();

        if (cache.TryGetValue(key, out CacheItem item))
        {
            if (item.IsResident)
            {
                S.Remove(item.Node);
                item.Node = Q.AddLast(key);
                item.IsResident = false;
            }
            else
            {
                Q.Remove(item.Node);
                item.Node = Q.AddLast(key);
            }

            return item.Value;
        }

        var value = factory(key);
        Observer.CountCacheMiss();
        Add(key, value);
        return value;
    }
    
    public void Add(TKey key, TValue value)
    {
        if (cache.Count >= MaximumEntriesCount)
        {
            RemoveLeastRecentlyUsed();
        }

        var newItem = new CacheItem(value);
        cache.Add(key, newItem);
        newItem.Node = S.AddLast(key);
    }

    private void RemoveLeastRecentlyUsed()
    {
        if (S.Count > 0)
        {
            TKey lruKey = S.First.Value;
            S.RemoveFirst();
            cache.Remove(lruKey);
        }
        else if (Q.Count > 0)
        {
            TKey lruKey = Q.First.Value;
            Q.RemoveFirst();
            cache.Remove(lruKey);
        }
    }

    public void Clear()
    {
        cache.Clear();
        Q.Clear();
        S.Clear();
    }
    
    private class CacheItem
    {
        public LinkedListNode<TKey> Node { get; set; }
        public TValue Value { get; }
        public bool IsResident { get; set; }

        public CacheItem(TValue value)
        {
            Value = value;
            IsResident = true;
        }
    }
}