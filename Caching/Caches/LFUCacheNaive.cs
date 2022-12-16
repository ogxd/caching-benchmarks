using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Caching;

public class LFUCacheNaive<TKey, TValue> : ICache<TKey, TValue>
{
    private readonly Dictionary<TKey, int> _perKeyMap = new();
    private readonly IndexBasedLinkedList<Entry> _entriesByFrequency = new();

    public string Name { get; set; }
    
    public int MaximumEntriesCount { get; set; }
    
    public ICacheObserver Observer { get; set; }
    
    public TValue GetOrCreate(TKey key, Func<TKey, TValue> factory)
    {
        ref int entryIndex = ref CollectionsMarshal.GetValueRefOrAddDefault(_perKeyMap, key, out bool exists);
        Observer?.CountCacheCall();

        while (_perKeyMap.Count > MaximumEntriesCount)
        {
            RemoveFirst();
        }

        TValue value;

        if (!exists)
        {
            value = factory(key);
            Observer?.CountCacheMiss();
            
            Entry entry = new(key, value);
            entryIndex = _entriesByFrequency.AddFirst(entry);

            return value;
        }
        else
        {
            int after = _entriesByFrequency[entryIndex].After;
            ref Entry entry = ref _entriesByFrequency[entryIndex].value;
            double instantFreq = 1d / (DateTime.UtcNow - entry.lastUsed).TotalSeconds;
            // X% contrib
            entry.frequency = 0.5d * entry.frequency + 0.5d * instantFreq;
            entry.lastUsed = DateTime.UtcNow;

            int freqLog2 = (int)Math.Log2(entry.frequency);

            int current = -1;

            // Way too slow!
            while (after != -1 && freqLog2 >= _entriesByFrequency[after].value.frequency)
            {
                current = after;
                after = _entriesByFrequency[after].after;
            }

            if (current != -1)
            {
                var entryCopy = entry;
                _entriesByFrequency.Remove(entryIndex);
                _entriesByFrequency.AddAfter(entryCopy, current);
            }
            
            return entry.value;
        }
    }

    private void RemoveFirst()
    {
        var entry = _entriesByFrequency[_entriesByFrequency.FirstIndex];
        _perKeyMap.Remove(entry.value.key);
        _entriesByFrequency.Remove(_entriesByFrequency.FirstIndex);
    }

    public void Clear()
    {
        _perKeyMap.Clear();
        _entriesByFrequency.Clear();
    }

    internal struct Entry
    {
        public TKey key;
        public TValue value;
        public DateTime lastUsed;
        public double frequency; // hits / s

        public Entry(TKey key, TValue value)
        {
            this.key = key;
            this.value = value;
            lastUsed = DateTime.UtcNow;
            frequency = 0;
        }
    }
}