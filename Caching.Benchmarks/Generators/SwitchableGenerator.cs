namespace Caching.Benchmarks;

public class SwitchableGenerator<T> : IGenerator<T>
{
    private readonly IGenerator<T>[] _generators;
    private readonly int _switchAfter;
    private readonly bool _switchOnce;

    private int _callsForCurrentGenerator;
    private int _currentGeneratorIndex;

    public SwitchableGenerator(int switchAfter, bool switchOnce, params IGenerator<T>[] generators)
    {
        _generators = generators;
        _switchAfter = switchAfter;
        _switchOnce = switchOnce;
    }

    public T Generate()
    {
        int currentGenerator;

        lock (_generators)
        {
            _callsForCurrentGenerator++;
            if (_callsForCurrentGenerator > _switchAfter)
            {
                _currentGeneratorIndex++;
                _currentGeneratorIndex = _currentGeneratorIndex % _generators.Length; // Wrap
                _callsForCurrentGenerator = _switchOnce ? int.MinValue : 0;
            }
            currentGenerator = _currentGeneratorIndex;
        }

        return _generators[currentGenerator].Generate();
    }

    public void Reset()
    {
        lock (_generators)
        {
            _callsForCurrentGenerator = 0;
            _currentGeneratorIndex = 0;
            foreach (var generator in _generators)
            {
                generator.Reset();
            }
        }
    }
}