using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Caching;

public class LUDACache<TKey, TValue> : LUDACache<TKey, TKey, TValue>
{
    public LUDACache(
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

public class LUDACache<TItem, TKey, TValue> : ICache<TItem, TValue>
{
    private readonly ICacheObserver _cacheObserver;

    private readonly Dictionary<TKey, int> _perKeyMap;
    private readonly IndexBasedLinkedList<Entry> _entriesByHits;
    private readonly IndexBasedLinkedList<HitsCount> _hitsCount;

    private readonly Func<TItem, TKey> _keyFactory;

    private int _maximumKeyCount;

    public LUDACache(
        int maximumKeyCount,
        Func<TItem, TKey> keyFactory,
        IEqualityComparer<TKey> keyComparer = null,
        ICacheObserver cacheObserver = null,
        TimeSpan? expiration = null)
    {
        _keyFactory = keyFactory ?? throw new ArgumentNullException("keyFactory");
        _perKeyMap = new Dictionary<TKey, int>(keyComparer ?? EqualityComparer<TKey>.Default);
        _entriesByHits = new IndexBasedLinkedList<Entry>();
        _hitsCount = new IndexBasedLinkedList<HitsCount>();
        _cacheObserver = cacheObserver;
        _maximumKeyCount = maximumKeyCount;
    }

    public int MaxSize { get => _maximumKeyCount; set => _maximumKeyCount = value; }

    public TValue GetOrCreate(TItem item, Func<TItem, TValue> factory)
    {
        TKey key = _keyFactory(item);

        ref int entryIndex = ref CollectionsMarshal.GetValueRefOrAddDefault(_perKeyMap, key, out bool exists);
        _cacheObserver?.CountCacheCall();

        //if (_perKeyMap.Count > 1.2d * _maximumKeyCount)
        //{
        //    while (_perKeyMap.Count > _maximumKeyCount)
        //    {
        //        RemoveFirst();
        //    }
        //}

        while (_perKeyMap.Count > 1.2d * _maximumKeyCount)
        {
            RemoveFirst();
        }

        TValue value;

        if (!exists)
        {
            value = factory(item);

            Entry entry = new(key, value);

            if (_hitsCount.Count == 0)
            {
                entryIndex = _entriesByHits.AddFirst(entry);

                HitsCount firstHit = new();
                firstHit.hits = 1;
                firstHit.refCount = 1;
                firstHit.firstEntryWithHitsIndex = entryIndex;
                _hitsCount.AddFirst(firstHit);

                _entriesByHits[entryIndex].value.hitsCountIndex = _hitsCount.FirstIndex;
            }
            else
            {
                if (_hitsCount.Count == 1)
                {
                    entryIndex = _entriesByHits.AddFirst(entry);

                    ref var n = ref _hitsCount[_hitsCount.FirstIndex].value;
                    n.refCount++;
                    n.firstEntryWithHitsIndex = entryIndex;

                    _entriesByHits[entryIndex].value.hitsCountIndex = _hitsCount.FirstIndex;
                }
                else
                {
                    // Dynamic Aging is done by not inserting after first hits block instead of before

                    ref var n = ref _hitsCount[_hitsCount[_hitsCount.FirstIndex].after].value;

                    entryIndex = _entriesByHits.AddBefore(entry, n.firstEntryWithHitsIndex);

                    n.refCount++;
                    n.firstEntryWithHitsIndex = entryIndex;

                    _entriesByHits[entryIndex].value.hitsCountIndex = _hitsCount[_hitsCount.FirstIndex].after;
                }
            }

            _cacheObserver?.CountCacheMiss();

            return value;
        }
        else
        {
            ref var entryNode = ref _entriesByHits[entryIndex];
            ref var hitsCountNode = ref _hitsCount[entryNode.value.hitsCountIndex];

            value = entryNode.value.value;

            // If there is no next hitsCount node or next node is not hits + 1, we must create this node
            if (hitsCountNode.after == -1
            || _hitsCount[hitsCountNode.after].value.hits != hitsCountNode.value.hits + 1)
            {
                var oldEntryIndex = entryIndex;
                var nextEntryIndex = entryNode.after;

                // Copy (because otherwise entry is set to default after removal)
                var entry = entryNode.value;
                var oldHitsCountIndex = entry.hitsCountIndex;

                _entriesByHits.Remove(entryIndex);

                if (hitsCountNode.after == -1)
                {
                    // Top 1
                    entryIndex = _entriesByHits.AddLast(entry);
                }
                else
                {
                    Debug.Assert(_hitsCount[hitsCountNode.after].value.hits > hitsCountNode.value.hits + 1, "Ordering issue");
                    entryIndex = _entriesByHits.AddBefore(entry, _hitsCount[hitsCountNode.after].value.firstEntryWithHitsIndex);
                }

                // Add registration for hits + 1
                var hitsPlusOne = new HitsCount();
                hitsPlusOne.refCount = 1; // Only one ref since this is a new node
                hitsPlusOne.hits = hitsCountNode.value.hits + 1; // Next node is hits + 1
                hitsPlusOne.firstEntryWithHitsIndex = entryIndex;

                // Old hits registration get decrement of refcount
                hitsCountNode.value.refCount--;

                int hitsPlusOneIndex = _hitsCount.AddAfter(hitsPlusOne, entry.hitsCountIndex);
                _entriesByHits[entryIndex].value.hitsCountIndex = hitsPlusOneIndex;

                // If previous hits registration has no references anymore, remove it
                if (hitsCountNode.value.refCount <= 0)
                {
                    _hitsCount.Remove(entry.hitsCountIndex);
                }
                else
                {
                    if (_hitsCount[oldHitsCountIndex].value.firstEntryWithHitsIndex == oldEntryIndex)
                    {
                        _hitsCount[oldHitsCountIndex].value.firstEntryWithHitsIndex = nextEntryIndex;
                    }
                }
            }
            else
            {
                // Copy (because otherwise entry is set to default after removal)
                var entry = entryNode.value;
                var oldHitsCountIndex = entry.hitsCountIndex;
                entry.hitsCountIndex = hitsCountNode.after;

                var oldEntryIndex = entryIndex;
                var nextEntryIndex = entryNode.after;

                ref var hitsPlusOneNode = ref _hitsCount[hitsCountNode.after];

                _entriesByHits.Remove(entryIndex);
                entryIndex = _entriesByHits.AddAfter(entry, hitsPlusOneNode.value.firstEntryWithHitsIndex);

                hitsCountNode.value.refCount--;
                hitsPlusOneNode.value.refCount++;

                // If previous hits registration has no references anymore, remove it
                if (hitsCountNode.value.refCount <= 0)
                {
                    _hitsCount.Remove(oldHitsCountIndex);
                }
                else
                {
                    if (_hitsCount[oldHitsCountIndex].value.firstEntryWithHitsIndex == oldEntryIndex)
                    {
                        //Debug.Assert(hitsCountNode.after == _entriesByHits[nextEntryIndex].value.hitsCountIndex);
                        _hitsCount[oldHitsCountIndex].value.firstEntryWithHitsIndex = nextEntryIndex;
                    }
                }
            }

            //Debug.Assert(CheckThatFirstEntryIsFirst());
            //Debug.Assert(CheckHitsCount());
            //Debug.Assert(CheckOrder());

            return value;
        }
    }

    private void RemoveFirst()
    {
        var entry = _entriesByHits[_entriesByHits.FirstIndex];
        _perKeyMap.Remove(entry.value.key);
        ref var hitsCount = ref _hitsCount[entry.value.hitsCountIndex];
        hitsCount.value.refCount--;
        if (hitsCount.value.refCount == 0)
        {
            _hitsCount.Remove(entry.value.hitsCountIndex);
        }
        else
        {
            if (hitsCount.value.firstEntryWithHitsIndex == _entriesByHits.FirstIndex) // Normally this is always true
            {
                hitsCount.value.firstEntryWithHitsIndex = entry.after;
            }
        }
        _entriesByHits.Remove(_entriesByHits.FirstIndex);
    }

    public void Clear()
    {
        _hitsCount.Clear();
        _perKeyMap.Clear();
        _entriesByHits.Clear();
    }

    private bool CheckHitsCount()
    {
        var hitsCount = _hitsCount.ToArray();
        for (int i = 0; i < hitsCount.Length - 1; i++)
        {
            // Check order
            if (hitsCount[i].hits >= hitsCount[i + 1].hits)
                return false;

            // Check pointer
            if (hitsCount[i].firstEntryWithHitsIndex == hitsCount[i + 1].firstEntryWithHitsIndex)
                return false;
        }

        return true;
    }

    private bool CheckThatFirstEntryIsFirst()
    {
        var hitsCount = _hitsCount.ToArray();
        for (int i = 0; i < hitsCount.Length; i++)
        {
            int firstEntryWithHitsIndex = hitsCount[i].firstEntryWithHitsIndex;

            var entry = _entriesByHits[firstEntryWithHitsIndex];
            int before = entry.before;

            if (before != -1)
            {
                var entryBefore = _entriesByHits[before];

                if (entry.value.hitsCountIndex == entryBefore.value.hitsCountIndex)
                    return false;
            }
        }

        return true;
    }


    private bool CheckOrder()
    {
        var entries = _entriesByHits.ToArray();
        var hitsCount = _hitsCount.ToArray();
        for (int i = 0; i < entries.Length - 1; i++)
        {
            if (_hitsCount[entries[i].hitsCountIndex].value.hits > _hitsCount[entries[i + 1].hitsCountIndex].value.hits)
                return false;

            if (_hitsCount[entries[i].hitsCountIndex].value.refCount <= 0)
                return false;
        }

        return true;
    }

    //   A  B  C D E F G H I J K L M N O P Q
    // 123 42 12 7 5 5 3 3 2 2 1 1 1 1 1 1 1

    // Get O
    //                        v--------|
    //   A  B  C D E F G H I J K L M N O P Q
    // 123 42 12 7 5 5 3 3 2 2 1 1 1 1 1 1 1

    // Get R
    //                                       v
    //   A  B  C D E F G H I J O K L M N P Q R
    // 123 42 12 7 5 5 3 3 2 2 2 1 1 1 1 1 1 1

    // Get R
    //                                       v
    //   A  B  C D E F G H I J O K L M N P Q R
    // 123 42 12 7 5 5 3 3 2 2 2 1 1 1 1 1 1 1
    //   |  |  | |   |   |     |             |
    // 123 42 12 7   5   3     2             1  
    //   1  1  1 1   2   2     3             7

    // Get N
    //                                 |
    //   A  B  C D E F G H I J O K L M N P Q R
    // 123 42 12 7 5 5 3 3 2 2 2 1 1 1 1 1 1 1
    //   |  |  | |   |   |     |             |
    // 123 42 12 7   5   3     2             1  
    //   1  1  1 1   2   2     3             7


    internal struct Entry
    {
        public TKey key;
        public TValue value;
        public DateTime insertion;
        public int hitsCountIndex;
        //public int hits;

        public Entry(TKey key, TValue value)
        {
            this.key = key;
            this.value = value;
            insertion = DateTime.UtcNow;
            //hits = 1;
            hitsCountIndex = -1;
        }
    }

    internal record struct HitsCount
    {
        public int firstEntryWithHitsIndex;
        public int refCount;
        public int hits;
    }
}