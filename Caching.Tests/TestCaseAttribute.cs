using NUnit.Framework;

namespace Caching.Tests;

public class TestCaseAttribute<T>
    : TestCaseAttribute where T : new()
{
    public TestCaseAttribute()
        : base(new T()) { }
}