using NUnit.Framework;

namespace Caching.Benchmarks;

public class DataConvertion
{
    /// <summary>
    /// Converts from binary encoded 16bit hashes (or guids) to string based format (.dat)
    /// </summary>
    [Test]
    public void Convert()
    {
        // ConvertFile("../../../Datasets/equativ/all-2.bin");
        // ConvertFile("../../../Datasets/equativ/hbdr-2.bin");
        // ConvertFile("../../../Datasets/equativ/hbrmab-2.bin");
        // ConvertFile("../../../Datasets/equativ/vdas-2.bin");
    }
    
    public unsafe void ConvertFile(string filePath)
    {
        string outPath = Path.ChangeExtension(filePath, "dat");
        using FileStream fs = new FileStream(filePath, FileMode.Open);
        using FileStream outfs = new FileStream(outPath, FileMode.Create, FileAccess.Write);
        using TextWriter tw = new StreamWriter(outfs);
        
        Span<byte> buffer = stackalloc byte[16];
        
        while (fs.Position < fs.Length)
        {
            fs.Read(buffer);
            
            fixed (byte* bp = buffer)
            {
                long* lp = (long*)bp;

                tw.WriteLine($"{lp[0]};{lp[1]}");
            }
        }
    }
    
    [TestCase("../../../Datasets/umass/websearch-1.dat")]
    [TestCase("../../../Datasets/umass/websearch-2.dat")]
    [TestCase("../../../Datasets/umass/websearch-3.dat")]
    [TestCase("../../../Datasets/umass/financial-1.dat")]
    [TestCase("../../../Datasets/umass/financial-2.dat")]
    public void Count(string filePath)
    {
        UmassDataBasedGenerator x = new UmassDataBasedGenerator(filePath);
        Console.WriteLine("Values: " + x.Values.Count);
        Console.WriteLine("Unique: " + new HashSet<long>(x.Values).Count);
    }
}