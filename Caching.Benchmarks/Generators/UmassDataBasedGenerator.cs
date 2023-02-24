using System.Globalization;

namespace Caching.Benchmarks;

public class UmassDataBasedGenerator : IGenerator<long>
{ 
    private List<long> _values = new();

    public IReadOnlyList<long> Values => _values;

    private int _currentIndex;

    public UmassDataBasedGenerator(string filePath, int skipFirst = 0)
    {
        using FileStream fs = new FileStream(filePath, FileMode.Open);
        using StreamReader sr = new StreamReader(fs);

        while (!sr.EndOfStream)
        {
            string? line = sr.ReadLine();
            
            if (string.IsNullOrEmpty(line))
                throw new InvalidOperationException();

            // Split into 5 parts
            // ASU |  LBA | SIZE | OPCODE | TIMESTAMP
            //    1, 55674,  3072,       w,   4.272947
            var split = line.Split(',');

            if (split.Length < 5)
                continue;
            
            long asu = long.Parse(split[0]);
            long lba = long.Parse(split[1]);
            long size = long.Parse(split[2]);
            string opcode = split[3];
            double timestamp = double.Parse(split[4], CultureInfo.InvariantCulture);

            // Only consider reads (ignore writes)
            if (opcode.Equals("w", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (long value in EnumerateEntryValues_1(asu, lba, size))
            {
                if (--skipFirst < 0)
                {
                    _values.Add(value);
                }
            }
        }
    }

    // Spec is not very clear, nor they explain how data is parsed in papers
    // Logic taken from: https://github.com/ben-manes/caffeine/blob/7f98672657a6e3515eac00ec86990b877a9d10dc/simulator/src/main/java/com/github/benmanes/caffeine/cache/simulator/parser/umass/storage/StorageTraceReader.java#L39
    // See DLIRS paper: https://www.systor.org/2018/pdf/systor18-4.pdf
    // The paper has some typos (eg Financial2 unique is not 827801 but 831654) but otherwise it seems we have the same number of the Financial datasets, so we can at least refer to this paper for comparisons.
    // WebSearch datasets however seem off by a lot, so for comparison it's best to stick with financial
    public static IEnumerable<long> EnumerateEntryValues_1(long asu, long lba, long size)
    {
        const int BLOCK_SIZE = 512;
        
        long startBlock = lba;
        int sequence = (int)Math.Floor(1d * size / BLOCK_SIZE);
        for (long i = startBlock; i < startBlock + sequence; i++)
        {
            yield return i;
        }
    }
    
    // Found on some github repo, but could not correlate results with any paper results
    public static IEnumerable<long> EnumerateEntryValues_2(long asu, long lba, long size)
    {
        const int ASU_SPACE = 128;
        const int BLOCK_SIZE = 512;
        
        lba = lba * ASU_SPACE + asu;
        long count = size / BLOCK_SIZE - 1;
        do
        {
            yield return lba;
            lba += ASU_SPACE;
            count--;
        } while(count > 0);
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