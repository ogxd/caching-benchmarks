using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Caching;

/// <summary>
/// Eviction Policy: Multi-Segmented Less Recently Used
/// </summary>
/// <typeparam name="TKey"></typeparam>
/// <typeparam name="TValue"></typeparam>
// Multi Segment Least Recently Used
public class MSLRUCache<TKey, TValue> : ICache<TKey, TValue>
{
    private readonly ICacheObserver _cacheObserver;

    private readonly Dictionary<TKey, (int segment, int index)> _perKeyMap = new();
    private readonly IndexBasedLinkedList<Entry>[] _entries;

    private int _maximumKeyCount;
    private readonly int _segmentsCount;

    public MSLRUCache(
        int maximumKeyCount,
        int segmentsCount,
        ICacheObserver cacheObserver)
    {
        _cacheObserver = cacheObserver;
        _segmentsCount = segmentsCount;
        MaximumEntriesCount = maximumKeyCount;

        _entries = new IndexBasedLinkedList<Entry>[segmentsCount];
        for (int i = 0; i < segmentsCount; i++)
        {
            _entries[i] = new IndexBasedLinkedList<Entry>(_maximumKeyCount / _segmentsCount);
        }
    }

    // Rounding to have even segments
    public int MaximumEntriesCount { get => _maximumKeyCount; set => _maximumKeyCount = _segmentsCount * value / _segmentsCount; }

    public TValue GetOrCreate(TKey key, Func<TKey, TValue> factory)
    {
        ref var entryIndex = ref CollectionsMarshal.GetValueRefOrAddDefault(_perKeyMap, key, out bool exists);
        _cacheObserver?.CountCacheCall();

        TValue value;

        if (!exists)
        {
            value = factory(key);

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

                    if (!Unsafe.IsNullRef(ref e))
                    {
                        e.segment = currentSegment;
                        e.index = _entries[currentSegment].AddLast(removedEntry);
                    }
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

    private struct Entry
    {
        public TKey key;
        public TValue value;

        public Entry(TKey key, TValue value)
        {
            this.key = key;
            this.value = value;
        }
    }
}