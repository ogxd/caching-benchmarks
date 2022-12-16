using System;

namespace Caching.Benchmarks;

public class SparseLongGenerator : IGenerator<long>
{
    private Random _random;
    private long _modulo;

    public SparseLongGenerator(uint modulo)
    {
        _random = new Random(0);
        _modulo = (modulo <= 0) ? 1 : modulo;
    }

    public long Generate()
    {
        return _random.Next() % _modulo;
    }

    public void Reset()
    {
        _random = new Random(0);
    }
}