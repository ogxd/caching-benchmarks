using System;
using System.Collections.Generic;

namespace Caching.Caches;

public class NaiveLFUCache<K, V> : ICache<K, V>
{
    private int _minCount;
    private int _capacity;
    private Dictionary<K, (LinkedListNode<K> node, V value, int count)> _cache;
    private Dictionary<int, LinkedList<K>> _countMap;
    private readonly ICacheObserver _cacheObserver;

    public int MaxSize { get => _capacity; set => _capacity = value; }

    public NaiveLFUCache(int capacity, ICacheObserver cacheObserver = null)
    {
        _capacity = capacity;
        _countMap = new Dictionary<int, LinkedList<K>> { [1] = new() };
        _cache = new Dictionary<K, (LinkedListNode<K> node, V value, int count)>(capacity);
        _cacheObserver = cacheObserver;
    }

    private void PromoteItem(K key, V value, int count, LinkedListNode<K> node)
    {
        var list = _countMap[count];
        list.Remove(node);

        if (_minCount == count && list.Count == 0)
            _minCount++;

        var newCount = count + 1;
        if (!_countMap.ContainsKey(newCount))
            _countMap[newCount] = new LinkedList<K>();

        _countMap[newCount].AddFirst(node);
        _cache[key] = (node, value, newCount);
    }

    public V GetOrCreate(K key, Func<K, V> factory)
    {
        _cacheObserver.CountCacheCall();

        if (_cache.ContainsKey(key))
        {
            var (node, v, count) = _cache[key];
            PromoteItem(key, v, count, node);
            return v;
        }
        else
        {
            _cacheObserver.CountCacheMiss();

            V value = factory(key);

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
    }

    public void Clear()
    {
        _countMap = new Dictionary<int, LinkedList<K>> { [1] = new() };
        _cache = new Dictionary<K, (LinkedListNode<K> node, V value, int count)>(_capacity);
    }
}