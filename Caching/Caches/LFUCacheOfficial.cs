using System;
using System.Collections.Generic;

namespace Caching.Caches;

public class LFUCacheOfficial<TKey, TValue> : ICache<TKey, TValue>
{
    private readonly ICacheObserver _cacheObserver;
    
    private Dictionary<TKey, (LinkedListNode<TKey> node, TValue value, int count)> _cache;
    private Dictionary<int, LinkedList<TKey>> _countMap;

    private int _minCount;
    private int _capacity;
    
    public int MaximumEntriesCount { get => _capacity; set => _capacity = value; }

    public LFUCacheOfficial(int capacity, ICacheObserver cacheObserver = null)
    {
        _capacity = capacity;
        _countMap = new Dictionary<int, LinkedList<TKey>> { [1] = new() };
        _cache = new Dictionary<TKey, (LinkedListNode<TKey> node, TValue value, int count)>(capacity);
        _cacheObserver = cacheObserver;
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
        _cacheObserver.CountCacheCall();

        if (_cache.ContainsKey(key))
        {
            var (node, v, count) = _cache[key];
            PromoteItem(key, v, count, node);
            return v;
        }

        TValue value = factory(key);
        _cacheObserver.CountCacheMiss();

        if (_cache.Count >= _capacity)
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
        _cache = new Dictionary<TKey, (LinkedListNode<TKey> node, TValue value, int count)>(_capacity);
    }
}