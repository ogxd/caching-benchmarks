using NUnit.Framework;

namespace Caching.Tests;

public class LinkedDictionaryTests
{
    [Test]
    public void Test()
    {
        LinkedDictionary<int, int> dictionary = new();

        dictionary.TryAddFirst(1, 1);
        dictionary.TryAddFirst(2, 2);
        dictionary.TryAddFirst(3, 3);
        
        Assert.True(dictionary.TryGetFirst(out int firstKey, out int firstValue));
        Assert.AreEqual(3, firstKey);
        Assert.AreEqual(3, firstValue);
        
        Assert.True(dictionary.TryGetAfter(3, out int keyAfter, out int valueAfter));
        Assert.AreEqual(2, keyAfter);
        Assert.AreEqual(2, valueAfter);

        Assert.True(dictionary.Remove(2));
        
        Assert.True(dictionary.TryGetAfter(3, out keyAfter, out valueAfter));
        Assert.AreEqual(1, keyAfter);
        Assert.AreEqual(1, valueAfter);
        
        Assert.True(dictionary.ContainsKey(1));
        Assert.False(dictionary.ContainsKey(2));
        Assert.True(dictionary.ContainsKey(3));
    }
}