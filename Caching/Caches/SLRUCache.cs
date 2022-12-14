using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Caching;

public record struct Index
{
    public int index;
    public bool isProtected;
}

/// <summary>
/// Eviction Policy: Segmented Less Recently Used
/// </summary>
/// <typeparam name="TKey"></typeparam>
/// <typeparam name="TValue"></typeparam>
public class SLRUCache<TKey, TValue> : ICache<TKey, TValue>
{
    private readonly Dictionary<TKey, Index> _perKeyMap = new();
    private readonly IndexBasedLinkedList<Entry> _probationarySegment = new();
    private readonly IndexBasedLinkedList<Entry> _protectedSegment = new();

    public string Name { get; set; }
    
    public int MaximumEntriesCount { get; set; }

    public double MidPoint { get; set; }
    
    public ICacheObserver Observer { get; set; }

    public TValue GetOrCreate(TKey key, Func<TKey, TValue> factory)
    {
        ref Index index = ref CollectionsMarshal.GetValueRefOrAddDefault(_perKeyMap, key, out bool exists);
        Observer?.CountCacheCall();

        TValue value;

        if (!exists)
        {
            while (_probationarySegment.Count > MidPoint * MaximumEntriesCount)
            {
                RemoveFirst();
            }

            value = factory(key);

            Entry entry = new(key, value);
            index.index = _probationarySegment.AddLast(entry);
            index.isProtected = false;

            Observer?.CountCacheMiss();

            return value;
        }

        if (index.isProtected)
        {
            index.index = _protectedSegment.MoveToLast(index.index);

            return _protectedSegment[index.index].value.value;
        }

        
        Entry pentry = _probationarySegment[index.index].value;
        _probationarySegment.Remove(index.index);
        if (_protectedSegment.Count >= (1d - MidPoint) * MaximumEntriesCount)
        {
            Entry downgrade = _protectedSegment[_protectedSegment.FirstIndex].value;
            _protectedSegment.Remove(_protectedSegment.FirstIndex);
            int downgradeIndex = _probationarySegment.AddLast(downgrade);
            ref Index dow = ref CollectionsMarshal.GetValueRefOrNullRef(_perKeyMap, downgrade.key);
            dow.index = downgradeIndex;
            dow.isProtected = false;
        }
        index.index = _protectedSegment.AddLast(pentry);
        index.isProtected = true;

        return pentry.value;
    }

    private void RemoveFirst()
    {
        var entry = _probationarySegment[_probationarySegment.FirstIndex];
        _perKeyMap.Remove(entry.value.key);
        _probationarySegment.Remove(_probationarySegment.FirstIndex);
    }

    public void Clear()
    {
        _perKeyMap.Clear();
        _probationarySegment.Clear();
        _protectedSegment.Clear();
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