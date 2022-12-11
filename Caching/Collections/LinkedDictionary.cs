using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Caching;

// Downside : store keys twice
public class LinkedDictionary<K, V> // : IDictionary<K, V>, IReadOnlyDictionary<K, V>
{
    private readonly Dictionary<K, int> _dictionary = new();
    private readonly IndexBasedLinkedList<Entry> _list = new();

    public bool TryAddFirst(K key, V value)
    {
        ref int listIndex = ref CollectionsMarshal.GetValueRefOrAddDefault(_dictionary, key, out bool exists);

        if (exists)
            return false;

        listIndex = _list.AddFirst(new Entry(key, value));
        return true;
    }
    
    public bool TryAddLast(K key, V value)
    {
        ref int listIndex = ref CollectionsMarshal.GetValueRefOrAddDefault(_dictionary, key, out bool exists);

        if (exists)
            return false;

        listIndex = _list.AddLast(new Entry(key, value));
        return true;
    }

    public bool TryAddAfter(K key, V value, int afterIndex)
    {
        ref int listIndex = ref CollectionsMarshal.GetValueRefOrAddDefault(_dictionary, key, out bool exists);

        if (exists)
            return false;

        listIndex = _list.AddAfter(new Entry(key, value), afterIndex);
        return true;
    }
    
    public bool TryAddBefore(K key, V value, int beforeIndex)
    {
        ref int listIndex = ref CollectionsMarshal.GetValueRefOrAddDefault(_dictionary, key, out bool exists);

        if (exists)
            return false;

        listIndex = _list.AddBefore(new Entry(key, value), beforeIndex);
        return true;
    }
    
    public bool TryGetValue(K key, out V value)
    {
        value = default;
        
        if (!_dictionary.TryGetValue(key, out int listIndex))
            return false;

        value = _list[listIndex].value._value;
        return true;
    }
    
    public bool TryGetAfter(K key, out K keyAfter, out V valueAfter)
    {
        keyAfter = default;
        valueAfter = default;
        
        if (!TryGetAfter(key, out Entry entry))
            return false;

        keyAfter = entry._key;
        valueAfter = entry._value;
        return true;
    }
    
    internal bool TryGetAfter(K key, out Entry entryAfter)
    {
        entryAfter = default;
        
        if (!_dictionary.TryGetValue(key, out int listIndex))
            return false;

        int afterIndex = _list[listIndex].after;
        if (afterIndex == -1)
            return false;
        
        entryAfter = _list[afterIndex].value;
        return true;
    }
    
    public bool TryGetBefore(K key, out K keyBefore, out V valueBefore)
    {
        keyBefore = default;
        valueBefore = default;
        
        if (!TryGetBefore(key, out Entry entry))
            return false;

        keyBefore = entry._key;
        valueBefore = entry._value;
        return true;
    }
    
    internal bool TryGetBefore(K key, out Entry entryBefore)
    {
        entryBefore = default;
        
        if (!_dictionary.TryGetValue(key, out int listIndex))
            return false;

        int beforeIndex = _list[listIndex].before;
        if (beforeIndex == -1)
            return false;
        
        entryBefore = _list[beforeIndex].value;
        return true;
    }

    public bool ContainsKey(K key)
    {
        return _dictionary.ContainsKey(key);
    }
    
    public bool TryGetFirst(out K key, out V value)
    {
        key = default;
        value = default;
        
        if (_list.Count == 0)
            return false;

        var node = _list[_list.FirstIndex].value;
        key = node._key;
        value = node._value;

        return true;
    }
    
    public bool TryGetLast(out K key, out V value)
    {
        key = default;
        value = default;
        
        if (_list.Count == 0)
            return false;

        var node = _list[_list.LastIndex].value;
        key = node._key;
        value = node._value;

        return true;
    }

    public bool Remove(K key)
    {
        if (!_dictionary.Remove(key, out int entryIndex))
            return false;

        bool removed = _list.Remove(entryIndex);
        Debug.Assert(removed);

        return true;
    }

    public ref V GetValueRefOrNullRef(K key)
    { 
        if (!_dictionary.TryGetValue(key, out int listIndex))
            return ref Unsafe.NullRef<V>();

        return ref _list[listIndex].value._value;
    }

    public int Count => _dictionary.Count;

    internal struct Entry
    {
        internal K _key;
        internal V _value;

        public Entry(K key, V value)
        {
            _key = key;
            _value = value;
        }
    }
}
