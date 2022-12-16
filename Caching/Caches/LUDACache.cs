namespace Caching;

/// <summary>
/// Eviction Policy: Less Used with Dynamic Aging
/// </summary>
/// <typeparam name="TKey"></typeparam>
/// <typeparam name="TValue"></typeparam>
public class LUDACache<TKey, TValue> : LUCache<TKey, TValue>
{
    // Only create one hit bucket if there are no bucket. If there are buckets, even if first bucket is for N hits, place
    // new cache entries there (dynamic aging)
    protected override bool ShouldCreateOneHitBucket => _hitsCount.Count == 0;
    
    protected override void AddInExistingBuckets(ref int entryIndex, Entry entry)
    {
        ref var hitsNode = ref _hitsCount[_hitsCount.FirstIndex];
        if (_hitsCount.Count > 1)
        {
            // Dynamic aging consists in placing new entries in the next bucket so that low buckets gets evicted as time goes
            entryIndex = _entriesByHits.AddBefore(entry, _hitsCount[hitsNode.after].value.firstEntryWithHitsIndex);

            _entriesByHits[entryIndex].value.hitsCountIndex = hitsNode.after;
            hitsNode = ref _hitsCount[hitsNode.after];
            hitsNode.value.firstEntryWithHitsIndex = entryIndex;
        }
        else
        {
            entryIndex = _entriesByHits.AddFirst(entry);
            hitsNode.value.firstEntryWithHitsIndex = entryIndex;

            _entriesByHits[entryIndex].value.hitsCountIndex = _hitsCount.FirstIndex;
        }

        hitsNode.value.refCount++;
    }
}