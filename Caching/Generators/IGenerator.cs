using System;

namespace Caching;

public interface IGenerator<T>
{
    T Generate();
    void Reset();
}