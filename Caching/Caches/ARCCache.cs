using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace Caching;

public class ARCCache<TKey, TValue> : ICache<TKey, TValue>
{
    private readonly Dictionary<TKey, Index> _perKeyMap = new();

    private readonly IndexBasedLinkedList<Entry>[] _lrus = { new(), new(),  new(), new() };

    private IndexBasedLinkedList<Entry> B1 => _lrus[0];
    private IndexBasedLinkedList<Entry> T1 => _lrus[1];
    private IndexBasedLinkedList<Entry> B2 => _lrus[2];
    private IndexBasedLinkedList<Entry> T2 => _lrus[3];

    private int _p;

    public string Name { get; set; }
    
    public int MaximumEntriesCount { get; set; }
    
    public ICacheObserver Observer { get; set; }

    public TValue GetOrCreate(TKey key, Func<TKey, TValue> factory)
    {
        ref Index index = ref CollectionsMarshal.GetValueRefOrAddDefault(_perKeyMap, key, out bool exists);
        Observer?.CountCacheCall();

        Entry entry;
            
        if (!exists)
        {
            entry = new(key, factory(key));
            Observer?.CountCacheMiss();
            
            // Add new entry to end of T1
            index.index = T1.AddLast(entry);
            index.lru = 1;
        }
        else
        {
            entry = _lrus[index.lru][index.index].value;
            
            switch (index.lru)
            {
                case 0: // Promote from B1 to T1
                    index.lru = 1;
                    // Entry in B1 had no value, we should compute it
                    entry.value = factory(key);
                    Observer?.CountCacheMiss();
                    B1.Remove(index.index);
                    index.index = T1.AddLast(entry);
                    _p =  Math.Min(MaximumEntriesCount, _p + Math.Max(B2.Count / B1.Count, 1));
                    break;
                case 1: // Promote from T1 to T2
                    index.lru = 3;
                    T1.Remove(index.index);
                    index.index = T2.AddLast(entry);
                    break;
                case 2: // Promote from B2 to T2
                    index.lru = 3;
                    // Entry in B2 had no value, we should compute it
                    entry.value = factory(key);
                    Observer?.CountCacheMiss();
                    B2.Remove(index.index);
                    index.index = T2.AddLast(entry);
                    break;
                case 3: // Refresh position in T2
                    T2.Remove(index.index);
                    index.index = T2.AddLast(entry);
                    break;
            }
        }

        if (T2.Count + T1.Count > MaximumEntriesCount)
        {
            if (T1.Count > 0 && index.lru == 1)
            {
                
            }
            else
            {
                
            }
            
            int lruToEvictIndex = index.lru ^ 2;
            if (_lrus[lruToEvictIndex].Count == 0 || (lruToEvictIndex == 1 && _lrus[lruToEvictIndex].Count >= p)
                lruToEvictIndex ^= 2;
            
            Debug.Assert(lruToEvictIndex == 1 || lruToEvictIndex == 3);
            
            // Evict T1 if new entry was added to T2 and the way around
            var lruToEvict = _lrus[lruToEvictIndex];
            var lruGhost = _lrus[lruToEvictIndex - 1];
            var entryToEvict = lruToEvict[lruToEvict.FirstIndex].value;
            entryToEvict.value = default;
            ref Index indexToEvict = ref CollectionsMarshal.GetValueRefOrAddDefault(_perKeyMap, entryToEvict.key, out _);
            lruToEvict.Remove(lruToEvict.FirstIndex);
            indexToEvict.index = lruGhost.AddLast(entryToEvict);
            indexToEvict.lru = lruToEvictIndex - 1;
        }

        Trim(B1);
        Trim(B2);

        return entry.value;
    }

    private void Trim(IndexBasedLinkedList<Entry> lru)
    {
        if (lru.Count > 5 * MaximumEntriesCount) // Threshold to be defined
        {
            // Evict T1 if new entry was added to T2 and the way around
            var keyToRemove = lru[lru.FirstIndex].value.key;
            lru.Remove(lru.FirstIndex);
            _perKeyMap.Remove(keyToRemove);
        }
    }

    public void Clear()
    {
        _perKeyMap.Clear();
        B1.Clear();
        T1.Clear();
        T2.Clear();
        B2.Clear();
    }

    private struct Index
    {
        public int lru;
        public int index;

        public Index(int lru, int index)
        {
            this.lru = lru;
            this.index = index;
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