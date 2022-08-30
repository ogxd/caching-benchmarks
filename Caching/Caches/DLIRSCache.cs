/*
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

public class DLIRSCache<TKey, TValue> : DLIRSCache<TKey, TKey, TValue>
{
    public DLIRSCache(
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

public class DLIRSCache<TItem, TKey, TValue> : ICache<TItem, TValue>
{
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);

    private readonly ICacheObserver _cacheObserver;

    private readonly Dictionary<TKey, int> _perKeyMap;
    private readonly IndexBasedLinkedList<Entry> _LIRSStack;

    private readonly Func<TItem, TKey> _keyFactory;

    private readonly double _oversize;

    private readonly int _maximumKeyCount;

    public DLIRSCache(
        int maximumKeyCount,
        double oversize,
        Func<TItem, TKey> keyFactory,
        IEqualityComparer<TKey> keyComparer = null,
        ICacheObserver cacheObserver = null,
        TimeSpan? expiration = null)
    {
        _keyFactory = keyFactory ?? throw new ArgumentNullException("keyFactory");
        _perKeyMap = new Dictionary<TKey, int>(keyComparer ?? EqualityComparer<TKey>.Default);
        _LIRSStack = new IndexBasedLinkedList<Entry>();
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

    public TValue GetOrCreate(TItem item, Func<TItem, TValue> factory)
    {
        TKey key = _keyFactory(item);

        ref int entryIndex = ref CollectionsMarshal.GetValueRefOrAddDefault(_perKeyMap, key, out bool exists);
        _cacheObserver?.CountCacheCall();

        TValue value;

        // S or LIRSStack contains all entires (LIR, HIR resident and non resident)
        // Q contains 

        if (exists)
        {
            if (_LIRSStack[entryIndex].value.isLIR)
            {
                // CASE 1

            }
            else if (non-resident HIR)
            {
                // CASE 2
                targetHr = Math.Max(C - 1, targetHr + Math.Min());

                BringCurrentBackToTarget();
                // Promote x’s status to LIR, fetch the data, and move it to the MRU position of S
                _LIRSStack[entryIndex].value.isLIR = true;
                _LIRSStack[entryIndex].value.value = factory(item);
                entryIndex = _LIRSStack.MoveToLast(entryIndex);
                L++;
            }
            else if (resident HIR in Q)
            {
                if (resident HIR in S)
                {
                    // CASE 3
                }
                else
                {
                    // CASE 4
                }
            }
        }
        else
        {
            // CASE 5
            if (Hr == 0 && L < targetL)
            {
                // L ← L + 1 and set x to LIR status
                // todo
            }
            else
            {
                // otherwise set x to HIR status, Hr ← Hr + 1, put x in the MRU position of Q, and call BringCurrentBackToTarget
                // todo
            }

            // Fetch the data and put x in the MRU position of S
            // todo
            value = factory(item);

            if (L + Hr + Hn > 2 * C)
            {
                // remove L +Hr + Hn − 2c non-resident HIR blocks close to the LRU position of S and update Hn
                // todo
            }
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
        _LIRSStack.Clear();
    }

    internal struct Entry
    {
        public TKey key;
        public TValue value;
        public DateTime insertion;
        public DateTime lastUsed;
        public bool isLIR;

        public Entry(TKey key, TValue value)
        {
            this.key = key;
            this.value = value;
            insertion = DateTime.UtcNow;
            lastUsed = DateTime.UtcNow;
            isLIR = false;
        }
    }
}
*/