namespace Caching.Benchmarks;

public class DataBasedGenerator : IGenerator<long>
{
    private List<long> _values = new();

    private int _currentIndex;

    public DataBasedGenerator(string filePath, Func<string?, string?> filter = null)
    {
        using FileStream fs = new FileStream(filePath, FileMode.Open);
        using StreamReader sr = new StreamReader(fs);

        while (!sr.EndOfStream)
        {
            string? line = sr.ReadLine();
            if (filter != null)
            {
                line = filter(line);
            }

            if (!string.IsNullOrEmpty(line))
            {
                _values.Add(DoHash(line));
            }
        }
    }
    
    private static long DoHash(string str)
    {
        int FNV_Prime = 16777619;			// Long
        long FNV_OffsetBasis = 2166136261;	// Long
        long FNV_Modulo = 4294967296;		// Long = 2^32
        long FNV_MaxInt32 = 2147483648;		// Long

        long FNV_Hash = FNV_OffsetBasis;

        for (int n = 0; n < str.Length; n++)
        {
            FNV_Hash ^= (uint)str[n];
            FNV_Hash %= FNV_Modulo;
            FNV_Hash *= FNV_Prime;
        }
        FNV_Hash %= FNV_Modulo;
        //-- Conversion en un entier 32bits de type Integer
        return FNV_MaxInt32 - FNV_Hash;
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