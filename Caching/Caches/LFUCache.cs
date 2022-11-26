using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Caching;

public class LFUCache<TKey, TValue> : LFUCache<TKey, TKey, TValue>
{
    public LFUCache(
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

public class LFUCache<TItem, TKey, TValue> : ICache<TItem, TValue>
{
    private readonly ICacheObserver _cacheObserver;

    private readonly Dictionary<TKey, int> _perKeyMap;
    private readonly IndexBasedLinkedList<Entry> _entriesByHits;
    private readonly IndexBasedLinkedList<FreqCount> _freqsLog10;
    
    // Only use for LFURA
    internal readonly IndexBasedLinkedList<int> _entriesByRecency;
    
    private readonly Func<TItem, TKey> _keyFactory;

    private int _maximumKeyCount;

    public LFUCache(
        int maximumKeyCount,
        Func<TItem, TKey> keyFactory,
        IEqualityComparer<TKey> keyComparer = null,
        ICacheObserver cacheObserver = null,
        TimeSpan? expiration = null)
    {
        _keyFactory = keyFactory ?? throw new ArgumentNullException("keyFactory");
        _perKeyMap = new Dictionary<TKey, int>(keyComparer ?? EqualityComparer<TKey>.Default);
        _entriesByHits = new IndexBasedLinkedList<Entry>();
        _freqsLog10 = new IndexBasedLinkedList<FreqCount>();
        _entriesByRecency = new IndexBasedLinkedList<int>();
        _cacheObserver = cacheObserver;
        _maximumKeyCount = maximumKeyCount;
    }

    public int MaxSize { get => _maximumKeyCount; set => _maximumKeyCount = value; }

    public virtual TValue GetOrCreate(TItem item, Func<TItem, TValue> factory)
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
            entryIndex = _entriesByHits.AddFirst(entry);

            var recencyIndex = _entriesByRecency.AddLast(entryIndex);
            _entriesByHits[_entriesByHits.FirstIndex].value.recency = recencyIndex;

            if (_freqsLog10.Count == 0)
            {
                FreqCount firstHit = new();
                firstHit.freqLog10 = 0; // Aproximation
                firstHit.refCount = 1;
                firstHit.firstEntryWithHitsIndex = entryIndex;
                _freqsLog10.AddFirst(firstHit);
            }
            else
            {
                _freqsLog10[_freqsLog10.FirstIndex].value.refCount++;
                _freqsLog10[_freqsLog10.FirstIndex].value.firstEntryWithHitsIndex = entryIndex;
            }

            _entriesByHits[_entriesByHits.FirstIndex].value.freqIndex = _freqsLog10.FirstIndex;

            _cacheObserver?.CountCacheMiss();

            return value;
        }
        else
        {
            value = GetValue(ref entryIndex);

            return value;
        }
    }

    internal TValue GetValue(ref int entryIndex, bool updateUsed = true)
    {
        TValue value;

        ref var entryNode = ref _entriesByHits[entryIndex];
        ref var freqNode = ref _freqsLog10[entryNode.value.freqIndex];

        long tt = Stopwatch.GetTimestamp();
        double instantFreq = 1d * Stopwatch.Frequency / (tt - entryNode.value.lastUsed);
        int roundedFreq = (int)Math.Round(Math.Log10(instantFreq * 1d));

        if (updateUsed)
        {
            entryNode.value.lastUsed = tt;
        }
        else
        {
            roundedFreq -= 1;
            //roundedFreq = int.MinValue;
        }

        value = entryNode.value.value;

        if (roundedFreq > freqNode.value.freqLog10)
        {
            // If there is no next hitsCount node or next node is not hits + 1, we must create this node
            if (freqNode.after == -1)
            {
                var oldEntryIndex = entryIndex;
                var nextEntryIndex = entryNode.after;

                // Copy (because otherwise entry is set to default after removal)
                var entry = entryNode.value;
                var oldHitsCountIndex = entry.freqIndex;

                _entriesByHits.Remove(entryIndex);

                if (freqNode.after == -1)
                {
                    // Top 1
                    entryIndex = _entriesByHits.AddLast(entry);
                }
                else
                {
                    Debug.Assert(_freqsLog10[freqNode.after].value.freqLog10 > freqNode.value.freqLog10 + 1, "Ordering issue");
                    entryIndex = _entriesByHits.AddBefore(entry, _freqsLog10[freqNode.after].value.firstEntryWithHitsIndex);
                }

                _entriesByRecency.Remove(entry.recency);
                var recencyIndex = _entriesByRecency.AddLast(entryIndex);
                _entriesByHits[entryIndex].value.recency = recencyIndex;

                // Add registration for hits + 1
                var nextFreq = new FreqCount();
                nextFreq.refCount = 1; // Only one ref since this is a new node
                nextFreq.freqLog10 = freqNode.value.freqLog10 + 1; // Next node is hits + 1
                nextFreq.firstEntryWithHitsIndex = entryIndex;

                // Old hits registration get decrement of refcount
                freqNode.value.refCount--;

                int hitsPlusOneIndex = _freqsLog10.AddAfter(nextFreq, entry.freqIndex);
                _entriesByHits[entryIndex].value.freqIndex = hitsPlusOneIndex;

                // If previous hits registration has no references anymore, remove it
                if (freqNode.value.refCount <= 0)
                {
                    _freqsLog10.Remove(entry.freqIndex);
                }
                else
                {
                    if (_freqsLog10[oldHitsCountIndex].value.firstEntryWithHitsIndex == oldEntryIndex)
                    {
                        _freqsLog10[oldHitsCountIndex].value.firstEntryWithHitsIndex = nextEntryIndex;
                    }
                }
            }
            else
            {
                // Copy (because otherwise entry is set to default after removal)
                var entry = entryNode.value;
                var oldHitsCountIndex = entry.freqIndex;
                entry.freqIndex = freqNode.after;

                var oldEntryIndex = entryIndex;
                var nextEntryIndex = entryNode.after;

                ref var hitsPlusOneNode = ref _freqsLog10[freqNode.after];

                _entriesByHits.Remove(entryIndex);
                entryIndex = _entriesByHits.AddAfter(entry, hitsPlusOneNode.value.firstEntryWithHitsIndex);

                _entriesByRecency.Remove(entry.recency);
                var recencyIndex = _entriesByRecency.AddLast(entryIndex);
                _entriesByHits[entryIndex].value.recency = recencyIndex;

                freqNode.value.refCount--;
                hitsPlusOneNode.value.refCount++;

                // If previous hits registration has no references anymore, remove it
                if (freqNode.value.refCount <= 0)
                {
                    _freqsLog10.Remove(oldHitsCountIndex);
                }
                else
                {
                    if (_freqsLog10[oldHitsCountIndex].value.firstEntryWithHitsIndex == oldEntryIndex)
                    {
                        _freqsLog10[oldHitsCountIndex].value.firstEntryWithHitsIndex = nextEntryIndex;
                    }
                }
            }
        }
        else if (roundedFreq < freqNode.value.freqLog10)
        {
            // If there is no next hitsCount node or next node is not hits + 1, we must create this node
            if (freqNode.before == -1)
            {
                var oldEntryIndex = entryIndex;
                var nextEntryIndex = entryNode.after;

                // Copy (because otherwise entry is set to default after removal)
                var entry = entryNode.value;
                var oldHitsCountIndex = entry.freqIndex;     

                _entriesByHits.Remove(entryIndex);

                if (freqNode.before == -1)
                {
                    entryIndex = _entriesByHits.AddFirst(entry);
                }
                else
                {
                    Debug.Assert(_freqsLog10[freqNode.before].value.freqLog10 < freqNode.value.freqLog10 + 1, "Ordering issue");
                    entryIndex = _entriesByHits.AddBefore(entry, _freqsLog10[freqNode.before].value.firstEntryWithHitsIndex);
                }

                _entriesByRecency.Remove(entry.recency);
                var recencyIndex = _entriesByRecency.AddLast(entryIndex);
                _entriesByHits[entryIndex].value.recency = recencyIndex;

                // Add registration for freq - 1
                var prevFreq = new FreqCount();
                prevFreq.refCount = 1; // Only one ref since this is a new node
                prevFreq.freqLog10 = freqNode.value.freqLog10 - 1; // Next node is hits + 1
                prevFreq.firstEntryWithHitsIndex = entryIndex;

                // Old hits registration get decrement of refcount
                freqNode.value.refCount--;

                int hitsPlusOneIndex = _freqsLog10.AddBefore(prevFreq, entry.freqIndex);
                _entriesByHits[entryIndex].value.freqIndex = hitsPlusOneIndex;

                // If previous hits registration has no references anymore, remove it
                if (freqNode.value.refCount <= 0)
                {
                    _freqsLog10.Remove(entry.freqIndex);
                }
                else
                {
                    if (_freqsLog10[oldHitsCountIndex].value.firstEntryWithHitsIndex == oldEntryIndex)
                    {
                        _freqsLog10[oldHitsCountIndex].value.firstEntryWithHitsIndex = nextEntryIndex;
                    }
                }
            }
            else
            {
                // Copy (because otherwise entry is set to default after removal)
                var entry = entryNode.value;
                var oldHitsCountIndex = entry.freqIndex;
                entry.freqIndex = freqNode.before;

                var oldEntryIndex = entryIndex;
                var nextEntryIndex = entryNode.after;

                ref var hitsMinusOneNode = ref _freqsLog10[freqNode.before];

                _entriesByHits.Remove(entryIndex);
                entryIndex = _entriesByHits.AddAfter(entry, hitsMinusOneNode.value.firstEntryWithHitsIndex);
                
                _entriesByRecency.Remove(entry.recency);
                var recencyIndex = _entriesByRecency.AddLast(entryIndex);
                _entriesByHits[entryIndex].value.recency = recencyIndex;

                freqNode.value.refCount--;
                hitsMinusOneNode.value.refCount++;

                // If previous hits registration has no references anymore, remove it
                if (freqNode.value.refCount <= 0)
                {
                    _freqsLog10.Remove(oldHitsCountIndex);
                }
                else
                {
                    if (_freqsLog10[oldHitsCountIndex].value.firstEntryWithHitsIndex == oldEntryIndex)
                    {
                        _freqsLog10[oldHitsCountIndex].value.firstEntryWithHitsIndex = nextEntryIndex;
                    }
                }
            }
        }

        return value;
    }

    private void RemoveFirst()
    {
        var entry = _entriesByHits[_entriesByHits.FirstIndex];

        Remove(entry.value.key);
    }

    private void Remove(TKey key)
    {
        var entryIndex = _perKeyMap[key];

        var entry = _entriesByHits[entryIndex];
        _perKeyMap.Remove(entry.value.key);
        ref var hitsCount = ref _freqsLog10[entry.value.freqIndex];
        hitsCount.value.refCount--;
        if (hitsCount.value.refCount == 0)
        {
            _freqsLog10.Remove(entry.value.freqIndex);
        }
        else
        {
            hitsCount.value.firstEntryWithHitsIndex = entry.after;
        }

        _entriesByRecency.Remove(entry.value.recency);
        _entriesByHits.Remove(entryIndex);
    }

    public void Clear()
    {
        _freqsLog10.Clear();
        _perKeyMap.Clear();
        _entriesByHits.Clear();
        _entriesByRecency.Clear();
    }

    private bool CheckHitsCount()
    {
        var hitsCount = _freqsLog10.ToArray();
        for (int i = 0; i < hitsCount.Length - 1; i++)
        {
            // Check order
            if (hitsCount[i].freqLog10 >= hitsCount[i + 1].freqLog10)
                return false;

            // Check pointer
            if (hitsCount[i].firstEntryWithHitsIndex == hitsCount[i + 1].firstEntryWithHitsIndex)
                return false;
        }

        return true;
    }

    private bool CheckThatFirstEntryIsFirst()
    {
        var hitsCount = _freqsLog10.ToArray();
        for (int i = 0; i < hitsCount.Length; i++)
        {
            int firstEntryWithHitsIndex = hitsCount[i].firstEntryWithHitsIndex;

            var entry = _entriesByHits[firstEntryWithHitsIndex];
            int before = entry.before;

            if (before != -1)
            {
                var entryBefore = _entriesByHits[before];

                if (entry.value.freqIndex == entryBefore.value.freqIndex)
                    return false;
            }
        }

        return true;
    }


    private bool CheckOrder()
    {
        var entries = _entriesByHits.ToArray();

        for (int i = 0; i < entries.Length - 1; i++)
        {
            if (_freqsLog10[entries[i].freqIndex].value.freqLog10 > _freqsLog10[entries[i + 1].freqIndex].value.freqLog10)
                return false;

            if (_freqsLog10[entries[i].freqIndex].value.refCount <= 0)
                return false;
        }

        return true;
    }

    private bool CheatRemove()
    {
        var entries = _entriesByHits.ToArray();
        var hitsCount = _freqsLog10.ToArray();

        entries = entries.OrderBy(x => _freqsLog10[x.freqIndex].value.freqLog10).ToArray();

        int i = 0;

        while (_perKeyMap.Count > _maximumKeyCount)
        {
            Remove(entries[i].key);
            i++;
        }

        return true;
    }


    private void CheckPromote()
    {

    }

#pragma warning disable S125

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
        public long lastUsed;
        public int freqIndex;
        public int recency;

        public Entry(TKey key, TValue value)
        {
            this.key = key;
            this.value = value;
            insertion = DateTime.UtcNow;
            lastUsed = Stopwatch.GetTimestamp();
            freqIndex = -1;
            recency = 0;
        }
    }

    internal record struct FreqCount
    {
        public int firstEntryWithHitsIndex;
        public int refCount;
        public int freqLog10;
    }
}