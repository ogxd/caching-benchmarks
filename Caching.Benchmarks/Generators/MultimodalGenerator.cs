using System;

namespace Caching.Benchmarks;

public class MultimodalGenerator<T> : IGenerator<T>
{
    private readonly IGenerator<T>[] _generators;
    private Random _random;

    public MultimodalGenerator(params IGenerator<T>[] generators)
    {
        _random = new Random(0);
        _generators = generators;
    }

    public T Generate()
    {
        return _generators[_random.Next(0, _generators.Length)].Generate();
    }

    public void Reset()
    {
        foreach (IGenerator<T> generator in _generators)
        {
            generator.Reset();
        }
        _random = new Random(0);
    }
}