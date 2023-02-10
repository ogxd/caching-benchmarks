using System.Globalization;

namespace Caching.Benchmarks;

public class UmassDataBasedGenerator : IGenerator<long>
{ 
    const int ASU_SPACE = 128;
    const int BLOCK_SIZE = 512;
    
    private List<long> _values = new();

    public IReadOnlyList<long> Values => _values;

    private int _currentIndex;

    public UmassDataBasedGenerator(string filePath)
    {
        using FileStream fs = new FileStream(filePath, FileMode.Open);
        using StreamReader sr = new StreamReader(fs);

        while (!sr.EndOfStream)
        {
            string? line = sr.ReadLine();
            
            if (string.IsNullOrEmpty(line))
                throw new InvalidOperationException();
            
            // ASU |  LBA | SIZE | OPCODE | TIMESTAMP
            //    1, 55674,  3072,       w,   4.272947
            var split = line.Split(',');

            if (split.Length < 5)
                continue;
            
            if (split[3].ToUpperInvariant() == "W")
                continue;

            int asu = int.Parse(split[0]);
            long lba = int.Parse(split[1]) * ASU_SPACE + asu;
            int size = int.Parse(split[2]);

            int count = size / BLOCK_SIZE - 1;

            do
            {
                _values.Add(lba);
                lba += ASU_SPACE;
                count--;
            } while(count > 0);
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