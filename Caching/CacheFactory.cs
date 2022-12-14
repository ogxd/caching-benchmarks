using System;

namespace Caching;

public class CacheFactory<TKey, TValue, TCache>
    where TCache : ICache<TKey, TValue>, new()
{
    private Action<TCache> _configurationActions;
    private Func<ICacheObserver> _observerFactory;
    private int _maximumEntriesCount;
    
    public void WithMaximumEntries(int maximumEntriesCount)
    {
        _maximumEntriesCount = maximumEntriesCount;
    }
    
    public void WithObserver(Func<ICacheObserver> observerFactory)
    {
        _observerFactory = observerFactory;
    }

    public void WithConfiguration(Action<TCache> action)
    {
        _configurationActions += action;
    }

    public ICache<TKey, TValue> Build()
    {
        var cache = new TCache();
        cache.MaximumEntriesCount = _maximumEntriesCount;
        cache.Observer = _observerFactory?.Invoke();
        _configurationActions?.Invoke(cache);
        return cache;
    }
}