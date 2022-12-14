using System;

namespace Caching;

public interface ICache<TItem, TValue>
{
    public TValue GetOrCreate(TItem item, Func<TItem, TValue> factory);

    public void Clear();

    public int MaximumEntriesCount { get; internal set; }
    
    public ICacheObserver Observer { get; internal set; }
}
