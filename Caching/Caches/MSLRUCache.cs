using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Caching;

public class MSLRUCache<TKey, TValue> : MSLRUCache<TKey, TKey, TValue>
{
    public MSLRUCache(
        int maximumKeyCount,
        int segments,
        IEqualityComparer<TKey> keyComparer = null,
        ICacheObserver cacheObserver = null,
        TimeSpan? expiration = null) : base(
            maximumKeyCount,
            segments,
            static item => item,
            keyComparer,
            cacheObserver,
            expiration)
    { }
}

// Multi Segment Least Recently Used
public class MSLRUCache<TItem, TKey, TValue> : ICache<TItem, TValue>
{
    private readonly ICacheObserver _cacheObserver;

    private readonly Dictionary<TKey, (int segment, int index)> _perKeyMap;
    private readonly IndexBasedLinkedList<Entry>[] _entries;

    private readonly Func<TItem, TKey> _keyFactory;

    private int _maximumKeyCount;
    private int _segmentsCount;

    public MSLRUCache(
        int maximumKeyCount,
        int segmentsCount,
        Func<TItem, TKey> keyFactory,
        IEqualityComparer<TKey> keyComparer = null,
        ICacheObserver cacheObserver = null,
        TimeSpan? expiration = null)
    {
        _keyFactory = keyFactory ?? throw new ArgumentNullException("keyFactory");
        _perKeyMap = new Dictionary<TKey, (int segment, int index)>(keyComparer ?? EqualityComparer<TKey>.Default);
        _cacheObserver = cacheObserver;
        _segmentsCount = segmentsCount;
        MaxSize = maximumKeyCount;

        _entries = new IndexBasedLinkedList<Entry>[segmentsCount];
        for (int i = 0; i < segmentsCount; i++)
        {
            _entries[i] = new IndexBasedLinkedList<Entry>(_maximumKeyCount / _segmentsCount);
        }
    }

    // Rounding to have even segments
    public int MaxSize { get => _maximumKeyCount; set => _maximumKeyCount = _segmentsCount * value / _segmentsCount; }

    public TValue GetOrCreate(TItem item, Func<TItem, TValue> factory)
    {
        TKey key = _keyFactory(item);

        ref var entryIndex = ref CollectionsMarshal.GetValueRefOrAddDefault(_perKeyMap, key, out bool exists);
        _cacheObserver?.CountCacheCall();

        TValue value;

        if (!exists)
        {
            value = factory(item);

            TryRemoveSegmentLRU(0, out var e);
            _perKeyMap.Remove(e.key);

            Entry entry = new(key, value);
            entryIndex.index = _entries[0].AddLast(entry);
            entryIndex.segment = 0;

            _cacheObserver?.CountCacheMiss();

            return value;
        }
        else
        {
            var segment = _entries[entryIndex.segment];
            var entryNode = segment[entryIndex.index];

            value = entryNode.value.value;

            segment.Remove(entryIndex.index);

            // If last segment, update MRU within this segment
            if (entryIndex.segment == _segmentsCount - 1)
            {
                entryIndex.index = segment.AddLast(entryNode.value);
            }
            else // If not last segment, promote to MRU in next segment
            {
                entryIndex.segment++;
                entryIndex.index = _entries[entryIndex.segment].AddLast(entryNode.value);
            }

            // Cascade demote
            int currentSegment = entryIndex.segment;
            while (TryRemoveSegmentLRU(entryIndex.segment, out Entry removedEntry))
            {
                if (currentSegment == 0)
                {
                    _perKeyMap.Remove(removedEntry.key);
                }
                else
                {
                    ref var e = ref CollectionsMarshal.GetValueRefOrNullRef(_perKeyMap, removedEntry.key);

                    e.segment = currentSegment;
                    e.index = _entries[currentSegment].AddLast(removedEntry);
                }

                currentSegment--;
                if (currentSegment == -1)
                    break;
            }

            Debug.Assert(_perKeyMap.Count <= _maximumKeyCount, $"{_perKeyMap.Count} <= {_maximumKeyCount}");

            foreach (var entrySegment in _entries)
            {
                Debug.Assert(entrySegment.Count <= _maximumKeyCount / _segmentsCount);
            }

            return value;
        }
    }

    private bool TryRemoveSegmentLRU(int segmentIndex, out Entry removedEntry)
    {
        if (_entries[segmentIndex].Count >= _maximumKeyCount / _segmentsCount)
        {
            var segment = _entries[segmentIndex];
            removedEntry = segment[segment.FirstIndex].value;

            segment.Remove(segment.FirstIndex);

            return true;
        }

        removedEntry = default;
        return false;
    }

    public void Clear()
    {
        _perKeyMap.Clear();
        foreach (var segment in _entries)
        {
            segment.Clear();
        }
    }

    internal struct Entry
    {
        public TKey key;
        public TValue value;
        public DateTime insertion;
        public long lastUsed;

        public Entry(TKey key, TValue value)
        {
            this.key = key;
            this.value = value;
            insertion = DateTime.UtcNow;
            lastUsed = Stopwatch.GetTimestamp();
        }
    }
}