using System.Collections.Generic;
using System.IO;

namespace Caching.Benchmarks;

public class DataBasedGenerator : IGenerator<long>
{
    private List<long> _values = new List<long>();

    private int _currentIndex;

    public DataBasedGenerator(string filePath)
    {
        using FileStream fs = new FileStream(filePath, FileMode.Open);
        using StreamReader sr = new StreamReader(fs);

        while (!sr.EndOfStream)
        {
            if (long.TryParse(sr.ReadLine(), out long value))
            {
                _values.Add(value);
            }
        }
    }

    public long Generate()
    {
        lock (_values)
        {
            long value = _values[_currentIndex];
            _currentIndex = (_currentIndex + 1) % _values.Count;
            return value;
        }
    }

    public void Reset()
    {
        _currentIndex = 0;
    }
}