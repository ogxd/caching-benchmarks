using System;

namespace Caching;

public interface ICache<TItem, TValue>
{
    public TValue GetOrCreate(TItem item, Func<TItem, TValue> factory);

    public void Clear();

    public string Name { get; set; }
    
    public int MaximumEntriesCount { get; set; }
    
    public ICacheObserver Observer { get; set; }
}
