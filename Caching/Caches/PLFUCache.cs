﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace Caching;

/// <summary>
/// Eviction Policy: Probatory Less Frequently Used
/// </summary>
/// <typeparam name="TKey"></typeparam>
/// <typeparam name="TValue"></typeparam>
public class PLFUCache<TKey, TValue> : ICache<TKey, TValue>
{
    private readonly Dictionary<TKey, int> _perKeyMap = new();
    private readonly IndexBasedLinkedList<Entry> _hotEntries = new();
    private readonly IndexBasedLinkedList<Entry> _probatoryEntries = new();
    private readonly SortedDictionary<int, FrequencyGroup> _frequencyGroups = new();
    
    public string Name { get; set; }
    
    public int MaximumEntriesCount { get; set; }
    
    public ICacheObserver Observer { get; set; }
    
    public bool ProgressiveMove { get; set; }
    
    public bool PromoteToBottom { get; set; }

    public double ProbatoryScaleFactor { get; set; } = 1d;

    public virtual TValue GetOrCreate(TKey key, Func<TKey, TValue> factory)
    {
        while (_hotEntries.Count > MaximumEntriesCount)
        {
            RemoveFirst();
        }
        
        while (_probatoryEntries.Count > ProbatoryScaleFactor * MaximumEntriesCount)
        {
            Remove(_probatoryEntries[_probatoryEntries.FirstIndex].value.key);
        }
        
        ref int entryIndex = ref CollectionsMarshal.GetValueRefOrAddDefault(_perKeyMap, key, out bool exists);
        Observer?.CountCacheCall();

        TValue value;

        // Case 1: Entry did not exist -> It is added as a probatory entry (no value stored)
        if (!exists)
        {
            // Create new probatory entry
            Entry entry = new(key);
            entry.lastUsed = Stopwatch.GetTimestamp();
            
            // probatory segment is LRU style
            // We use negative index for probatory segments to keep all entrys under the same dictionary (avoid double lookups)
            entryIndex = -_probatoryEntries.AddLast(entry) - 1;
            
            Observer?.CountCacheMiss();
            
            // Compute value but not store it as it is likely to be a entry that will never be requested again
            return factory(key);
        }
        
        // Case 2: Entry exists but it was probatory -> Move it from probatory entries to hot entries
        if (entryIndex < 0)
        {
            // It was a probatory entry
            // We must not promote it to non probatory
            var probatoryIndex = -entryIndex - 1;

            // Copy
            Entry entry = _probatoryEntries[probatoryIndex].value;
            
            value = entry.value = factory(key);
            Observer?.CountCacheMiss();

            // Remove from probatory entries
            _probatoryEntries.Remove(probatoryIndex);

            long currentTimestamp = Stopwatch.GetTimestamp();
            int frequency = GetFrequency(entry.lastUsed, currentTimestamp);

            // If promoteToBottom, move probatory entry to bottom of hot entries, intead of placing it directly
            // in the bucket it would have belonged. This could prevent entries accessed twice in a row from
            // evicting "real" hot entries.
            if (PromoteToBottom && _frequencyGroups.Count > 0)
            {
                frequency = _frequencyGroups.First().Key;
            }
            
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
        return (int)Math.Ceiling(Math.Log10(instantFreq * 1d));
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

        if (ProgressiveMove)
        {
            // With this flag enabled entries move from a bucket to another without jumping directly to the target bucket
            // This should help make the cache behaviour smoother
            if (frequency > previousFrequency)
                frequency ++;
            else
                frequency --;
        }
        
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