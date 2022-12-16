using System;

namespace Caching.Benchmarks;

public interface IGenerator<T>
{
    T Generate();
    void Reset();
}