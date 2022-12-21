using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Caching;

/// <summary>
/// Eviction Policy: Less Used 
/// </summary>
/// <typeparam name="TKey"></typeparam>
/// <typeparam name="TValue"></typeparam>
public class PLUCache<TKey, TValue> : ICache<TKey, TValue>
{
    private readonly Dictionary<TKey, int> _perKeyMap = new();
    private protected readonly IndexBasedLinkedList<Entry> _hotEntries = new();
    private protected readonly IndexBasedLinkedList<Entry> _probatoryEntries = new();
    private protected readonly IndexBasedLinkedList<HitsCount> _hitsCount = new();

    public string Name { get; set; }
    
    public int MaximumEntriesCount { get; set; }
    
    public ICacheObserver Observer { get; set; }
    
    public bool InsertStartOfFrequencyBucket { get; set; }
    
    public double ProbatoryScaleFactor { get; set; } = 5d;
    
    public TValue GetOrCreate(TKey key, Func<TKey, TValue> factory)
    {
        ref int entryIndex = ref CollectionsMarshal.GetValueRefOrAddDefault(_perKeyMap, key, out bool exists);
        Observer?.CountCacheCall();

        TValue value;

        if (exists)
        {
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
                
                if (ShouldCreateOneHitBucket)
                {
                    entryIndex = _hotEntries.AddFirst(entry);

                    HitsCount firstHit = new();
                    firstHit.hits = 1;
                    firstHit.refCount = 1;
                    firstHit.firstEntryWithHitsIndex = entryIndex;
                    _hitsCount.AddFirst(firstHit);

                    _hotEntries[entryIndex].value.hitsCountIndex = _hitsCount.FirstIndex;
                }
                else
                {
                    AddInExistingBuckets(ref entryIndex, entry);
                }
                
                while (_hotEntries.Count >= MaximumEntriesCount)
                {
                    RemoveFirst();
                }
                
                return value;
            }

            return Promote(ref entryIndex);
        }
        
        while (_probatoryEntries.Count >= ProbatoryScaleFactor * MaximumEntriesCount)
        {
            RemoveFirstProbatory();
        }

        value = AddProbatory(ref entryIndex, key, factory);

        return value;
    }

    protected virtual TValue AddProbatory(ref int entryIndex, TKey key, Func<TKey, TValue> factory)
    {
        Entry entry = new(key, default);

        // probatory segment is LRU style
        // We use negative index for probatory segments to keep all entrys under the same dictionary (avoid double lookups)
        entryIndex = -_probatoryEntries.AddLast(entry) - 1;
            
        Observer?.CountCacheMiss();
            
        // Compute value but not store it as it is likely to be a entry that will never be requested again
        return factory(key);
    }

    protected virtual bool ShouldCreateOneHitBucket => _hitsCount.Count == 0 || _hitsCount[_hitsCount.FirstIndex].Value.hits > 1;

    protected virtual void AddInExistingBuckets(ref int entryIndex, Entry entry)
    {
        ref var hitsNode = ref _hitsCount[_hitsCount.FirstIndex];
        if (!InsertStartOfFrequencyBucket && _hitsCount.Count > 1)
        {
            entryIndex = _hotEntries.AddBefore(entry, _hitsCount[hitsNode.after].value.firstEntryWithHitsIndex);
        }
        else
        {
            entryIndex = _hotEntries.AddFirst(entry);
            hitsNode.value.firstEntryWithHitsIndex = entryIndex;
        }
        
        _hotEntries[entryIndex].value.hitsCountIndex = _hitsCount.FirstIndex;
        hitsNode.value.refCount++;
    }

    private TValue Promote(ref int entryIndex)
    {
        TValue value;
        ref var entryNode = ref _hotEntries[entryIndex];
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

            _hotEntries.Remove(entryIndex);

            if (hitsCountNode.after == -1)
            {
                // Top 1
                entryIndex = _hotEntries.AddLast(entry);
            }
            else
            {
                Debug.Assert(_hitsCount[hitsCountNode.after].value.hits > hitsCountNode.value.hits + 1, "Ordering issue");
                entryIndex = _hotEntries.AddBefore(entry, _hitsCount[hitsCountNode.after].value.firstEntryWithHitsIndex);
            }

            // Add registration for hits + 1
            var hitsPlusOne = new HitsCount();
            hitsPlusOne.refCount = 1; // Only one ref since this is a new node
            hitsPlusOne.hits = hitsCountNode.value.hits + 1; // Next node is hits + 1
            hitsPlusOne.firstEntryWithHitsIndex = entryIndex;

            // Old hits registration get decrement of refcount
            hitsCountNode.value.refCount--;

            int hitsPlusOneIndex = _hitsCount.AddAfter(hitsPlusOne, entry.hitsCountIndex);
            _hotEntries[entryIndex].value.hitsCountIndex = hitsPlusOneIndex;

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

            _hotEntries.Remove(entryIndex);
            entryIndex = _hotEntries.AddAfter(entry, hitsPlusOneNode.value.firstEntryWithHitsIndex);

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
                    //Debug.Assert(hitsCountNode.after == _hotEntries[nextEntryIndex].value.hitsCountIndex);
                    _hitsCount[oldHitsCountIndex].value.firstEntryWithHitsIndex = nextEntryIndex;
                }
            }
        }

        return value;
    }

    private void RemoveFirst()
    {
        var entry = _hotEntries[_hotEntries.FirstIndex];
        _perKeyMap.Remove(entry.value.key);
        ref var hitsCount = ref _hitsCount[entry.value.hitsCountIndex];
        
        hitsCount.value.refCount--;
        if (hitsCount.value.refCount == 0)
        {
            _hitsCount.Remove(entry.value.hitsCountIndex);
        }
        else
        {
            if (hitsCount.value.firstEntryWithHitsIndex == _hotEntries.FirstIndex) // Normally this is always true
            {
                hitsCount.value.firstEntryWithHitsIndex = entry.after;
            }
        }
        _hotEntries.Remove(_hotEntries.FirstIndex);
    }
    
    private void RemoveFirstProbatory()
    {
        var entry = _probatoryEntries[_probatoryEntries.FirstIndex];
        _perKeyMap.Remove(entry.value.key);
        _probatoryEntries.Remove(_probatoryEntries.FirstIndex);
    }

    public void Clear()
    {
        _hitsCount.Clear();
        _perKeyMap.Clear();
        _hotEntries.Clear();
    }

    protected struct Entry
    {
        public TKey key;
        public TValue value;
        public int hitsCountIndex;

        public Entry(TKey key, TValue value)
        {
            this.key = key;
            this.value = value;
            hitsCountIndex = -1;
        }
    }

    protected record struct HitsCount
    {
        public int firstEntryWithHitsIndex;
        public int refCount;
        public int hits;
    }
}