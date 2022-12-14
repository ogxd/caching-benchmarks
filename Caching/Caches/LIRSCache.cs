using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Caching;

public record struct StackIndex
{
    public int indexS;
    public int indexQ;
}

public class LIRSCache<TKey, TValue> : ICache<TKey, TValue>
{
    private readonly Dictionary<TKey, StackIndex> _perKeyMap = new();
    private readonly IndexBasedLinkedList<Entry> _S = new();
    private readonly IndexBasedLinkedList<Entry> _Q = new();

    private readonly Func<TKey, TKey> _keyFactory;

    private int targetL;
    private int targetHr;

    private int L;
    private int Hr;
    private int Hn;
    private int Hd;
    
    public string Name { get; set; }

    public int MaximumEntriesCount { get; set; }
    
    public ICacheObserver Observer { get; set; }

    public TValue GetOrCreate(TKey key, Func<TKey, TValue> factory)
    {
        ref StackIndex entryIndex = ref CollectionsMarshal.GetValueRefOrAddDefault(_perKeyMap, key, out bool exists);
        Observer?.CountCacheCall();

        TValue value;

        if (exists)
        {
            if (entryIndex.indexS != -1)
            {
                bool wasLIR = _S[entryIndex.indexS].value.isLIR;

                _S[entryIndex.indexS].value.isLIR = true;
                if (!_S[entryIndex.indexS].value.isResident)
                {
                    _S[entryIndex.indexS].value.value = factory(key);
                    _S[entryIndex.indexS].value.isResident = true;

                    Observer?.CountCacheMiss();
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

            Entry entry = new Entry(key, value = factory(key));
            entry.isResident = true;
            entry.isLIR = false; // Pas si vite papillon

            entryIndex.indexS = _S.AddLast(entry);
            entryIndex.indexQ = _Q.AddLast(entry);

            Observer?.CountCacheMiss();
        }

        return value;
    }

    private void EnsureSize()
    {
        // All HIR at the bottom of the LIRS stack gets evicted
        while (_S.Count > 0 && !_S[_S.FirstIndex].value.isLIR)
        {
            // Evict bottom of Q
            ref StackIndex x = ref CollectionsMarshal.GetValueRefOrNullRef(_perKeyMap, _S[_S.FirstIndex].value.key);
            Evict(ref x);
        }

        while (_Q.Count > 1d * MaximumEntriesCount) // Shall it be 0.1 or 1?
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
        public bool isResident;
        public bool isLIR;

        public Entry(TKey key, TValue value)
        {
            this.key = key;
            this.value = value;
            isResident = true;
            isLIR = false;
        }
    }
}