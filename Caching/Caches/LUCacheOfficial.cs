using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace Caching.Caches;

/// <summary>
/// Eviction Policy: Less Used
/// Implementation is taken from https://www.c-sharpcorner.com/article/fast-simplest-and-clean-o1-lfu-cache-algorithm-implementation-in-c-sharp/, but was optimized to remove unecessary lookups.
/// Most implementations online are based on using 2 dictionaries and LinkedLists as values
/// Example:
/// - https://leetcode.com/problems/lfu-cache/solutions/543658/c-solution/
/// - https://leetcode.com/problems/lfu-cache/solutions/94543/c-accept-solution-with-two-dictionary-and-linkedlist/
/// - https://leetcode.com/problems/lfu-cache/solutions/2815229/lfu-cache/ (leetcode "official" solution)
/// It turns out there is a much better solution using a single dictionary and two linkedlists (1D + 2L) (see <see cref="LFUCache{TKey,TValue}"/>) instead of 2 dictionaries with LinkedLists as counts values (2 * (D + D * L))
/// </summary>
/// <typeparam name="TKey"></typeparam>
/// <typeparam name="TValue"></typeparam>
public class LUCacheOfficial<TKey, TValue> : ICache<TKey, TValue>
{
    private Dictionary<TKey, (LinkedListNode<TKey> node, TValue value, int count)> _cache;
    private Dictionary<int, LinkedList<TKey>> _countMap;

    private int _minCount;
    
    public string Name { get; set; }
    
    public int MaximumEntriesCount { get; set; }
    
    public ICacheObserver Observer { get; set; }
   
    public LUCacheOfficial()
    {
        _countMap = new Dictionary<int, LinkedList<TKey>> { [1] = new() };
        _cache = new Dictionary<TKey, (LinkedListNode<TKey> node, TValue value, int count)>();
    }

    private void PromoteItem(TKey key, TValue value, int count, LinkedListNode<TKey> node)
    {
        if (_countMap.TryGetValue(count, out var list))
            list.Remove(node);

        if (_minCount == count && list.Count == 0)
            _minCount++;

        var newCount = count + 1;
        ref var newList = ref CollectionsMarshal.GetValueRefOrAddDefault(_countMap, newCount, out bool exists);
        if (!exists)
            newList = new LinkedList<TKey>();

        newList.AddFirst(node);
        _cache[key] = (node, value, newCount);
    }

    public TValue GetOrCreate(TKey key, Func<TKey, TValue> factory)
    {
        Console.WriteLine($"Request key:{key}");
        Console.WriteLine(string.Join(", ", _cache.OrderBy(x => x.Key).Select(x => $"{x.Key}/{x.Value.count}"))); 
        
        Observer.CountCacheCall();

        if (_cache.TryGetValue(key, out var tuple))
        {
            var (node, v, count) = tuple;
            PromoteItem(key, v, count, node);
            return v;
        }

        TValue value = factory(key);
        Observer.CountCacheMiss();

        if (_cache.Count >= MaximumEntriesCount)
        {
            var minList = _countMap[_minCount];
            Console.WriteLine($"Remove key:{minList.Last!.Value} (counts:{_minCount})");
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