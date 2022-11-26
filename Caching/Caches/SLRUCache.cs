using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Caching;

public class SLRUCache<TKey, TValue> : SLRUCache<TKey, TKey, TValue>
{
    public SLRUCache(
        int maximumKeyCount,
        double oversize,
        IEqualityComparer<TKey> keyComparer = null,
        ICacheObserver cacheObserver = null,
        TimeSpan? expiration = null) : base(
            maximumKeyCount,
            oversize,
            static item => item,
            keyComparer,
            cacheObserver,
            expiration)
    { }
}

public record struct Index
{
    public int index;
    public bool isProtected;
}

public class SLRUCache<TItem, TKey, TValue> : ICache<TItem, TValue>
{
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);

    private readonly ICacheObserver _cacheObserver;

    private readonly Dictionary<TKey, Index> _perKeyMap;
    private readonly IndexBasedLinkedList<Entry> _probationarySegment;
    private readonly IndexBasedLinkedList<Entry> _protectedSegment;

    private readonly Func<TItem, TKey> _keyFactory;

    private readonly double _oversize;

    private int _maximumKeyCount;

    public SLRUCache(
        int maximumKeyCount,
        double oversize,
        Func<TItem, TKey> keyFactory,
        IEqualityComparer<TKey> keyComparer = null,
        ICacheObserver cacheObserver = null,
        TimeSpan? expiration = null)
    {
        _keyFactory = keyFactory ?? throw new ArgumentNullException("keyFactory");
        _perKeyMap = new Dictionary<TKey, Index>(keyComparer ?? EqualityComparer<TKey>.Default);
        _protectedSegment = new IndexBasedLinkedList<Entry>();
        _probationarySegment = new IndexBasedLinkedList<Entry>();
        _cacheObserver = cacheObserver;
        _maximumKeyCount = maximumKeyCount;
        _oversize = oversize;
    }

    public int MaxSize { get => _maximumKeyCount; set => _maximumKeyCount = value; }

    public TValue GetOrCreate(TItem item, Func<TItem, TValue> factory)
    {
        TKey key = _keyFactory(item);

        ref Index index = ref CollectionsMarshal.GetValueRefOrAddDefault(_perKeyMap, key, out bool exists);
        _cacheObserver?.CountCacheCall();

        TValue value;

        if (!exists)
        {
            while (_probationarySegment.Count > 0.2 * _maximumKeyCount)
            {
                RemoveFirst();
            }

            value = factory(item);

            Entry entry = new(key, value);
            index.index = _probationarySegment.AddLast(entry);
            index.isProtected = false;

            _cacheObserver?.CountCacheMiss();

            return value;
        }
        else
        {
            if (index.isProtected)
            {
                index.index = _protectedSegment.MoveToLast(index.index);

                return _protectedSegment[index.index].value.value;
            }
            else
            {
                Entry entry = _probationarySegment[index.index].value;
                _probationarySegment.Remove(index.index);
                if (_protectedSegment.Count >= 0.8 * _maximumKeyCount)
                {
                    Entry downgrade = _protectedSegment[_protectedSegment.FirstIndex].value;
                    _protectedSegment.Remove(_protectedSegment.FirstIndex);
                    int downgradeIndex = _probationarySegment.AddLast(downgrade);
                    ref Index dow = ref CollectionsMarshal.GetValueRefOrNullRef(_perKeyMap, downgrade.key);
                    dow.index = downgradeIndex;
                    dow.isProtected = false;
                }
                index.index = _protectedSegment.AddLast(entry);
                index.isProtected = true;

                return entry.value;
            }
        }
    }

    private void RemoveFirst()
    {
        var entry = _probationarySegment[_probationarySegment.FirstIndex];
        _perKeyMap.Remove(entry.value.key);
        _probationarySegment.Remove(_probationarySegment.FirstIndex);
    }

    public void Clear()
    {
        _perKeyMap.Clear();
        _probationarySegment.Clear();
        _protectedSegment.Clear();
    }

    internal struct Entry
    {
        public TKey key;
        public TValue value;
        public DateTime insertion;
        public DateTime lastUsed;

        public Entry(TKey key, TValue value)
        {
            this.key = key;
            this.value = value;
            insertion = DateTime.UtcNow;
            lastUsed = DateTime.UtcNow;
        }
    }
}