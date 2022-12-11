#define PROGRESSIVE

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace Caching;

public class ProbatoryLFUCache<TKey, TValue> : ProbatoryLFUCache<TKey, TKey, TValue>
{
    public ProbatoryLFUCache(
        int maximumKeyCount,
        double probatoryScaleFactor = 10,
        IEqualityComparer<TKey> keyComparer = null,
        ICacheObserver cacheObserver = null) : base(
            maximumKeyCount,
            static item => item,
            probatoryScaleFactor,
            keyComparer,
            cacheObserver)
    { }
}

public class ProbatoryLFUCache<TItem, TKey, TValue> : ICache<TItem, TValue>
{
    private readonly ICacheObserver _cacheObserver;

    private readonly Dictionary<TKey, int> _perKeyMap;
    private readonly IndexBasedLinkedList<Entry> _hotEntries;
    private readonly IndexBasedLinkedList<Entry> _probatoryEntries;
    private readonly SortedDictionary<int, FrequencyGroup> _frequencyGroups = new();
    
    private readonly Func<TItem, TKey> _keyFactory;

    private int _maximumEntriesCount;
    private double _probatoryScaleFactor;

    public ProbatoryLFUCache(
        int maximumEntriesCount,
        Func<TItem, TKey> keyFactory,
        double probatoryScaleFactor,
        IEqualityComparer<TKey> keyComparer,
        ICacheObserver cacheObserver)
    {
        _keyFactory = keyFactory ?? throw new ArgumentNullException("keyFactory");
        _perKeyMap = new Dictionary<TKey, int>(keyComparer ?? EqualityComparer<TKey>.Default);
        _hotEntries = new IndexBasedLinkedList<Entry>();
        _probatoryEntries = new IndexBasedLinkedList<Entry>();
        _cacheObserver = cacheObserver;
        _maximumEntriesCount = maximumEntriesCount;
        _probatoryScaleFactor = probatoryScaleFactor;
    }

    public int MaxSize { get => _maximumEntriesCount; set => _maximumEntriesCount = value; }

    public virtual TValue GetOrCreate(TItem item, Func<TItem, TValue> factory)
    {
        TKey key = _keyFactory(item);

        while (_hotEntries.Count > _maximumEntriesCount)
        {
            RemoveFirst();
        }
        
        while (_probatoryEntries.Count > _probatoryScaleFactor * _maximumEntriesCount)
        {
            Remove(_probatoryEntries[_probatoryEntries.FirstIndex].value.key);
        }
        
        ref int entryIndex = ref CollectionsMarshal.GetValueRefOrAddDefault(_perKeyMap, key, out bool exists);
        _cacheObserver?.CountCacheCall();

        TValue value;

        // Case 1: Entry did not exist -> It is added as a probatory entry (no value stored)
        if (!exists)
        {
            _cacheObserver?.CountCacheMiss();
            
            // Create new probatory entry
            Entry entry = new(key);
            entry.lastUsed = Stopwatch.GetTimestamp();
            
            // probatory segment is LRU style
            // We use negative index for probatory segments to keep all entrys under the same dictionary (avoid double lookups)
            entryIndex = -_probatoryEntries.AddLast(entry) - 1;
            
            // Compute value but not store it as it is likely to be a entry that will never be requested again
            return factory(item);
        }
        
        // Case 2: Entry exists but it was probatory -> Move it from probatory entries to hot entries
        if (entryIndex < 0)
        {
            _cacheObserver?.CountCacheMiss();
            
            // It was a probatory entry
            // We must not promote it to non probatory
            var probatoryIndex = -entryIndex - 1;

            // Copy
            Entry entry = _probatoryEntries[probatoryIndex].value;
            
            value = entry.value = factory(item);

            // Remove from probatory entries
            _probatoryEntries.Remove(probatoryIndex);

            long currentTimestamp = Stopwatch.GetTimestamp();
            int frequency = GetFrequency(entry.lastUsed, currentTimestamp);

            entry.frequency = frequency;
            entry.lastUsed = currentTimestamp;

            // Case 2.1: There was already entries with that frequency
            if (_frequencyGroups.TryGetValue(frequency, out FrequencyGroup frequencyGroup))
            {
                entryIndex = _hotEntries.AddBefore(entry, frequencyGroup.firstEntryWithHitsIndex);
            }
            // Case 2.2: It's the first entry with that frequency
            else
            {
                entryIndex = _hotEntries.AddFirst(entry);
                frequencyGroup = new();
                _frequencyGroups.Add(frequency, frequencyGroup);
            }
            
            frequencyGroup.refCount++;
            frequencyGroup.firstEntryWithHitsIndex = entryIndex;

            return value;
        }

        // It was a hot entry
        return GetValue(ref entryIndex);
    }

    internal int GetFrequency(long lastUsedTimestamp, long newTimestamp)
    {
        double instantFreq = 1d * Stopwatch.Frequency / (newTimestamp - lastUsedTimestamp);
        return (int)Math.Round(Math.Log10(instantFreq * 1d));
    }

    internal TValue GetValue(ref int entryIndex)
    {
        ref var entryNode = ref _hotEntries[entryIndex];

        int previousFrequency = entryNode.value.frequency;
        
        long currentTimestamp = Stopwatch.GetTimestamp();
        int frequency = GetFrequency(entryNode.value.lastUsed, currentTimestamp);
        
        entryNode.value.lastUsed = currentTimestamp;

        // Do nothing if frequency is unchanged
        if (frequency == previousFrequency)
        {
            return entryNode.value.value;
        }
        
        // Remove from previous freq bucket
        var frequencyGroup = _frequencyGroups[previousFrequency];
        frequencyGroup.refCount--;
        if (frequencyGroup.refCount == 0)
        {
            // Remove empty frequency group
            _frequencyGroups.Remove(previousFrequency);
        }
        else if (frequencyGroup.firstEntryWithHitsIndex == entryIndex)
        {
            frequencyGroup.firstEntryWithHitsIndex = entryNode.after;
        }

#if PROGRESSIVE
        // Progressive move (optional)
        if (frequency > previousFrequency)
            frequency += 1;
        else
            frequency -= 1;
#endif
        
        entryNode.value.frequency = frequency;
        
        var entry = entryNode.value;

        // Move entry
        _hotEntries.Remove(entryIndex);

        // Case A: There was already entries with that frequency
        if (_frequencyGroups.TryGetValue(frequency, out frequencyGroup))
        {
            entryIndex = _hotEntries.AddBefore(entry, frequencyGroup.firstEntryWithHitsIndex);
        }
        // Case B: It's the first entry with that frequency
        else
        {
            entryIndex = _hotEntries.AddFirst(entry);
            frequencyGroup = new();
            _frequencyGroups.Add(frequency, frequencyGroup);
        }
        
        frequencyGroup.refCount++;
        frequencyGroup.firstEntryWithHitsIndex = entryIndex;

        return entry.value;
    }

    private void Check()
    {
        var array = _hotEntries.Select(x => x.frequency).ToArray();
        
        Console.WriteLine(array);
    }

    private void RemoveFirst()
    {
        var entry = _hotEntries[_frequencyGroups.First().Value.firstEntryWithHitsIndex];
        
        //Debug.Assert(_hotEntries.FirstIndex == _frequencyGroups.Last().Value.firstEntryWithHitsIndex);

        //Check();
        
        Remove(entry.value.key);
    }

    private void Remove(TKey key)
    {
        var entryIndex = _perKeyMap[key];

        if (entryIndex >= 0)
        {
            var entry = _hotEntries[entryIndex];
            _perKeyMap.Remove(entry.value.key);
            
            FrequencyGroup freqCount = _frequencyGroups[entry.value.frequency];
            
            freqCount.refCount--;
            if (freqCount.refCount == 0)
            {
                // Empty freq bucket
                _frequencyGroups.Remove(entry.value.frequency);
            }
            else if (freqCount.firstEntryWithHitsIndex == entryIndex)
            {
                freqCount.firstEntryWithHitsIndex = entry.after;
            }

            _hotEntries.Remove(entryIndex);
        }
        else
        {
            var probatoryIndex = -entryIndex - 1;
            
            var entry = _probatoryEntries[probatoryIndex];
            _perKeyMap.Remove(entry.value.key);

            _probatoryEntries.Remove(probatoryIndex);
        }
    }

    public void Clear()
    {
        _frequencyGroups.Clear();
        _perKeyMap.Clear();
        _hotEntries.Clear();
        _probatoryEntries.Clear();
    }

    internal struct Entry
    {
        public TKey key;
        public TValue? value;
        public long lastUsed;
        public int frequency;

        public Entry(TKey key)
        {
            this.key = key;
            lastUsed = Stopwatch.GetTimestamp();
            frequency = -1;
            value = default;
        }
    }

    internal class FrequencyGroup
    {
        public int firstEntryWithHitsIndex;
        public int refCount;
    }
}