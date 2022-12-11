using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Caching;

public class ProbatoryLFUCache<TKey, TValue> : ProbatoryLFUCache<TKey, TKey, TValue>
{
    public ProbatoryLFUCache(
        int maximumKeyCount,
        IEqualityComparer<TKey> keyComparer = null,
        ICacheObserver cacheObserver = null) : base(
            maximumKeyCount,
            static item => item,
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
    private readonly LinkedDictionary<int, FreqCount> _freqsLog10 = new();
    
    private readonly Func<TItem, TKey> _keyFactory;

    private int _maximumKeyCount;

    public ProbatoryLFUCache(
        int maximumKeyCount,
        Func<TItem, TKey> keyFactory,
        IEqualityComparer<TKey> keyComparer = null,
        ICacheObserver cacheObserver = null)
    {
        _keyFactory = keyFactory ?? throw new ArgumentNullException("keyFactory");
        _perKeyMap = new Dictionary<TKey, int>(keyComparer ?? EqualityComparer<TKey>.Default);
        _hotEntries = new IndexBasedLinkedList<Entry>();
        _probatoryEntries = new IndexBasedLinkedList<Entry>();
        _cacheObserver = cacheObserver;
        _maximumKeyCount = maximumKeyCount;
    }

    public int MaxSize { get => _maximumKeyCount; set => _maximumKeyCount = value; }

    public virtual TValue GetOrCreate(TItem item, Func<TItem, TValue> factory)
    {
        TKey key = _keyFactory(item);

        ref int entryIndex = ref CollectionsMarshal.GetValueRefOrAddDefault(_perKeyMap, key, out bool exists);
        _cacheObserver?.CountCacheCall();

        while (_hotEntries.Count > _maximumKeyCount)
        {
            RemoveFirst();
        }

        TValue value;

        // Case 1: Entry did not exist -> It is added as a probatory entry (no value stored)
        if (!exists)
        {
            _cacheObserver?.CountCacheMiss();
            
            // Create new probatory entry
            Entry entry = new(key);
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

            Entry entry = _probatoryEntries[probatoryIndex].value;
            value = entry.value = factory(item);

            // Remove from probatory entries
            _probatoryEntries.Remove(probatoryIndex);

            var timestamp = Stopwatch.GetTimestamp();
            int frequency = GetFrequency(entry.lastUsed);

            entry.frequency = frequency;
            entry.lastUsed = timestamp;
            
            // Add to hot segment
            ref FreqCount freqCount = ref _freqsLog10.GetValueRefOrNullRef(frequency);
            
            // Case 2.1: It's the first entry with that frequency
            if (Unsafe.IsNullRef(ref freqCount))
            {
                int nextFreq = frequency + 1;
                // Problem: we can't look indefinitely, it's not sorted so there is no end... that sucks :) 
                while (_freqsLog10.ContainsKey(nextFreq))
                {
                    
                }
                entryIndex = _hotEntries.AddFirst(entry);
            }
            // Case 2.2: There was already entries with that frequency
            else
            {
                entryIndex = _hotEntries.AddBefore(entry, freqCount.firstEntryWithHitsIndex);
                freqCount.firstEntryWithHitsIndex = entryIndex;
                freqCount.refCount++;
            }
            
            freqCount.refCount++;
            freqCount.firstEntryWithHitsIndex = entryIndex;

            return value;
        }

        // It was a hot entry
        return GetValue(ref entryIndex);
    }

    internal int GetFrequency(long lastUsedTimestamp)
    {
        long currentTimestamp = Stopwatch.GetTimestamp();
        double instantFreq = 1d * Stopwatch.Frequency / (currentTimestamp - lastUsedTimestamp);
        return (int)Math.Round(Math.Log10(instantFreq * 1d));
    }

    internal TValue GetValue(ref int entryIndex)
    {
        ref var entryNode = ref _hotEntries[entryIndex];
        ref var freqNode = ref CollectionsMarshal.GetValueRefOrNullRef(_freqsLog10, entryNode.value.frequency);
        
        Debug.Assert(!Unsafe.IsNullRef(ref freqNode));
        
        int roundedFreq = GetFrequency(entryNode.value.lastUsed);
        
        // Refresh the "last used" timestamp
        entryNode.value.lastUsed = Stopwatch.GetTimestamp();

        // Refresh frequency if it changed
        if (roundedFreq != entryNode.value.frequency)
        {
            // Remove from previous freq bucket
            ref FreqCount currentFreq = ref CollectionsMarshal.GetValueRefOrAddDefault(_freqsLog10, entryNode.value.frequency, out _);
            currentFreq.refCount--;
            if (currentFreq.refCount == 0)
            {
                // Empty freq bucket
                _freqsLog10.Remove(entryNode.value.frequency);
            }
            else if (currentFreq.firstEntryWithHitsIndex == entryIndex)
            {
                currentFreq.firstEntryWithHitsIndex = entryNode.after;
            }
            
            // Add to new bucket
            ref FreqCount freqCount = ref CollectionsMarshal.GetValueRefOrAddDefault(_freqsLog10, roundedFreq, out bool fexists);
            freqCount.refCount++;
            freqCount.firstEntryWithHitsIndex = entryIndex;
            
            // TODO: Move entry to before firstEntryWithHitsIndex
            
            // Update entry with new freq
            entryNode.value.frequency = roundedFreq;
        }

        return entryNode.value.value;
    }

    private void RemoveFirst()
    {
        var entry = _hotEntries[_hotEntries.FirstIndex];

        Remove(entry.value.key);
    }

    private void Remove(TKey key)
    {
        var entryIndex = _perKeyMap[key];

        var entry = _hotEntries[entryIndex];
        _perKeyMap.Remove(entry.value.key);

        if (entryIndex >= 0)
        {
            ref FreqCount freqCount = ref CollectionsMarshal.GetValueRefOrNullRef(_freqsLog10, entry.value.frequency);
            
            Debug.Assert(!Unsafe.IsNullRef(ref freqCount));
        
            freqCount.refCount--;
            if (freqCount.refCount == 0)
            {
                // Empty freq bucket
                _freqsLog10.Remove(entry.value.frequency);
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

            _probatoryEntries.Remove(probatoryIndex);
        }
    }

    public void Clear()
    {
        _freqsLog10.Clear();
        _perKeyMap.Clear();
        _hotEntries.Clear();
        _probatoryEntries.Clear();
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

    internal record struct FreqCount
    {
        public int firstEntryWithHitsIndex;
        public int refCount;
        public int freqLog10;
    }
}