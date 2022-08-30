using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.VisualBasic;

namespace Caching;

public class LIRSCache<TKey, TValue> : LIRSCache<TKey, TKey, TValue>
{
    public LIRSCache(
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

public record struct StackIndex
{
    public int indexS;
    public int indexQ;
}

public class LIRSCache<TItem, TKey, TValue> : ICache<TItem, TValue>
{
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);

    private readonly ICacheObserver _cacheObserver;

    private readonly Dictionary<TKey, StackIndex> _perKeyMap;
    private readonly IndexBasedLinkedList<Entry> _S;
    private readonly IndexBasedLinkedList<Entry> _Q;

    private readonly Func<TItem, TKey> _keyFactory;

    private readonly double _oversize;

    private int _maximumKeyCount;

    public LIRSCache(
        int maximumKeyCount,
        double oversize,
        Func<TItem, TKey> keyFactory,
        IEqualityComparer<TKey> keyComparer = null,
        ICacheObserver cacheObserver = null,
        TimeSpan? expiration = null)
    {
        _keyFactory = keyFactory ?? throw new ArgumentNullException("keyFactory");
        _perKeyMap = new Dictionary<TKey, StackIndex>(keyComparer ?? EqualityComparer<TKey>.Default);
        _S = new IndexBasedLinkedList<Entry>();
        _Q = new IndexBasedLinkedList<Entry>();
        _cacheObserver = cacheObserver;
        _maximumKeyCount = maximumKeyCount;
        _oversize = oversize;
    }

    private int targetL;
    private int targetHr;

    private int L;
    private int Hr;
    private int Hn;
    private int Hd;

    private int C => 0;

    public int MaxSize { get => _maximumKeyCount; set => _maximumKeyCount = value; }

    public TValue GetOrCreate(TItem item, Func<TItem, TValue> factory)
    {
        TKey key = _keyFactory(item);

        ref StackIndex entryIndex = ref CollectionsMarshal.GetValueRefOrAddDefault(_perKeyMap, key, out bool exists);
        _cacheObserver?.CountCacheCall();

        TValue value;

        if (exists)
        {
            if (entryIndex.indexS != -1)
            {
                bool wasLIR = _S[entryIndex.indexS].value.isLIR;

                _S[entryIndex.indexS].value.isLIR = true;
                if (!_S[entryIndex.indexS].value.isResident)
                {
                    _S[entryIndex.indexS].value.value = factory(item);
                    _S[entryIndex.indexS].value.isResident = true;

                    _cacheObserver?.CountCacheMiss();
                }
                entryIndex.indexS = _S.MoveToLast(entryIndex.indexS);

                if (!wasLIR && _S.Count > 1)
                {
                    var entry = _S[_S.FirstIndex].value;
                    ref StackIndex x = ref CollectionsMarshal.GetValueRefOrNullRef(_perKeyMap, entry.key);
                    x.indexS = -1;
                    _S.Remove(_S.FirstIndex);

                    if (x.indexQ == -1)
                    {
                        x.indexQ = _Q.AddLast(entry);
                    }
                    else
                    {
                        x.indexQ = _Q.MoveToLast(x.indexQ);
                    }
                }
            }
            else
            {
                Debug.Assert(entryIndex.indexQ != -1);

                entryIndex.indexS = _S.AddLast(_Q[entryIndex.indexQ].value);
            }

            if (entryIndex.indexQ != -1)
            {
                _Q[entryIndex.indexQ].value.isLIR = true;
                entryIndex.indexQ = _Q.MoveToLast(entryIndex.indexQ);
            }

            value = _S[entryIndex.indexS].value.value;

            EnsureSize();
        }
        else
        {
            EnsureSize();

            Entry entry = new Entry(key, value = factory(item));
            entry.isResident = true;
            entry.isLIR = false; // Pas si vite papillon

            entryIndex.indexS = _S.AddLast(entry);
            entryIndex.indexQ = _Q.AddLast(entry);

            _cacheObserver?.CountCacheMiss();
        }

        return value;
    }

    public void EnsureSize()
    {
        // All HIR at the bottom of the LIRS stack gets evicted
        while (_S.Count > 0 && !_S[_S.FirstIndex].value.isLIR)
        {
            // Evict bottom of Q
            ref StackIndex x = ref CollectionsMarshal.GetValueRefOrNullRef(_perKeyMap, _S[_S.FirstIndex].value.key);
            Evict(ref x);
        }

        while (_Q.Count > 0.1 * _maximumKeyCount)
        {
            // Evict bottom of Q
            ref StackIndex x = ref CollectionsMarshal.GetValueRefOrNullRef(_perKeyMap, _Q[_Q.FirstIndex].value.key);
            Evict(ref x);
        }
    }

    public void Evict(ref StackIndex stackIndex)
    {
        TKey key = default(TKey);

        if (stackIndex.indexS != -1)
        {
            key = _S[stackIndex.indexS].value.key;

            if (_S[stackIndex.indexS].value.isResident)
            {
                _S[stackIndex.indexS].value.isResident = false;
                _S[stackIndex.indexS].value.value = default(TValue);
            }
            else
            {
                _S.Remove(stackIndex.indexS);
                stackIndex.indexS = -1;
            }
        }

        if (stackIndex.indexQ != -1)
        {
            key = _Q[stackIndex.indexQ].value.key;

            _Q.Remove(stackIndex.indexQ);
            stackIndex.indexQ = -1;
        }

        if (stackIndex.indexS == -1 && stackIndex.indexQ == -1)
        {
            _perKeyMap.Remove(key);
        }
    }

    public void BringCurrentBackToTarget()
    {
        while (L > targetL)
        {
            // demote LRU LIR block to HIR status, move it from S to the MRU position of Q, prune LRU HIR blocks in S, and update L, Hr , Hn, and Hd
            // todo
        }

        while (Hr > targetHr)
        {
            // eject the data of LRU resident HIR block in Q, and update Hr as well as Hd if applicable
            // todo
        }
    }

    public void Clear()
    {
        _perKeyMap.Clear();
        _Q.Clear();
        _S.Clear();
    }

    internal struct Entry
    {
        public TKey key;
        public TValue value;
        public DateTime insertion;
        public DateTime lastUsed;
        public bool isResident;
        public bool isLIR;

        public Entry(TKey key, TValue value)
        {
            this.key = key;
            this.value = value;
            insertion = DateTime.UtcNow;
            lastUsed = DateTime.UtcNow;
            isResident = true;
            isLIR = false;
        }

        public Entry(TKey key)
        {
            this.key = key;
            insertion = DateTime.UtcNow;
            lastUsed = DateTime.UtcNow;
            value = default(TValue);
            isResident = false;
            isLIR = false;
        }
    }
}