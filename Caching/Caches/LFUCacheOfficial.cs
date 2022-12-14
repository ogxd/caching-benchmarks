using System;
using System.Collections.Generic;

namespace Caching.Caches;

public class LFUCacheOfficial<TKey, TValue> : ICache<TKey, TValue>
{
    private Dictionary<TKey, (LinkedListNode<TKey> node, TValue value, int count)> _cache;
    private Dictionary<int, LinkedList<TKey>> _countMap;

    private int _minCount;
    
    public string Name { get; set; }
    
    public int MaximumEntriesCount { get; set; }
    
    public ICacheObserver Observer { get; set; }
   
    public LFUCacheOfficial()
    {
        _countMap = new Dictionary<int, LinkedList<TKey>> { [1] = new() };
        _cache = new Dictionary<TKey, (LinkedListNode<TKey> node, TValue value, int count)>();
    }

    private void PromoteItem(TKey key, TValue value, int count, LinkedListNode<TKey> node)
    {
        var list = _countMap[count];
        list.Remove(node);

        if (_minCount == count && list.Count == 0)
            _minCount++;

        var newCount = count + 1;
        if (!_countMap.ContainsKey(newCount))
            _countMap[newCount] = new LinkedList<TKey>();

        _countMap[newCount].AddFirst(node);
        _cache[key] = (node, value, newCount);
    }

    public TValue GetOrCreate(TKey key, Func<TKey, TValue> factory)
    {
        Observer.CountCacheCall();

        if (_cache.ContainsKey(key))
        {
            var (node, v, count) = _cache[key];
            PromoteItem(key, v, count, node);
            return v;
        }

        TValue value = factory(key);
        Observer.CountCacheMiss();

        if (_cache.Count >= MaximumEntriesCount)
        {
            var minList = _countMap[_minCount];
            _cache.Remove(minList.Last!.Value);
            minList.RemoveLast();
        }

        _cache.Add(key, (_countMap[1].AddFirst(key), value, 1));
        _minCount = 1;

        return value;
    }

    public void Clear()
    {
        _countMap = new Dictionary<int, LinkedList<TKey>> { [1] = new() };
        _cache = new Dictionary<TKey, (LinkedListNode<TKey> node, TValue value, int count)>();
    }
}