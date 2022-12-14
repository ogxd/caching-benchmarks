using System;

namespace Caching;

public class CacheBuilder<TKey, TValue, TCache> : ICacheBuilder<TKey, TValue>
    where TCache : ICache<TKey, TValue>, new()
{
    private Action<TCache> _configurationActions;
    private ICacheObserver _observer;
    private int _maximumEntriesCount;
    private string _name;

    public ICacheBuilder<TKey, TValue> WithName(string name)
    {
        _name = name;
        return this;
    }

    public ICacheBuilder<TKey, TValue> WithMaximumEntries(int maximumEntriesCount)
    {
        _maximumEntriesCount = maximumEntriesCount;
        return this;
    }
    
    public ICacheBuilder<TKey, TValue> WithObserver(ICacheObserver observer)
    {
        _observer = observer;
        return this;
    }

    public CacheBuilder<TKey, TValue, TCache> WithConfiguration(Action<TCache> action)
    {
        _configurationActions += action;
        return this;
    }

    public ICache<TKey, TValue> Build()
    {
        var cache = new TCache();
        cache.Name = _name ?? nameof(TCache);
        cache.MaximumEntriesCount = _maximumEntriesCount;
        cache.Observer = _observer;
        _configurationActions?.Invoke(cache);
        return cache;
    }
}

public interface ICacheBuilder<TKey, TValue>
{
    public ICacheBuilder<TKey, TValue> WithName(string name);
    public ICacheBuilder<TKey, TValue> WithMaximumEntries(int maximumEntriesCount);
    public ICacheBuilder<TKey, TValue> WithObserver(ICacheObserver observer);
    public ICache<TKey, TValue> Build();
}