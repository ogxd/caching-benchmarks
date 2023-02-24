namespace Caching.Benchmarks;

public class CsvPlotter : IPlotter
{
    private MemoryStream _csvData = new();
    
    public void Plot(string name, string xlabel, string ylabel, IList<Serie> series)
    {
        using (var sw = new StreamWriter(_csvData, leaveOpen: true))
        {
            sw.Write(name);
            
            int i = 0;
            foreach (var serie in series)
            {
                // Header
                if (i == 0)
                {
                    foreach (var point in serie.Points)
                    {
                        sw.Write($",{Math.Round(point.x)}");
                    }
                    
                    sw.Write(Environment.NewLine);
                }
                
                sw.Write($"{serie.Name}");

                foreach (var point in serie.Points)
                {
                    sw.Write($",{Math.Round(point.y)} %");
                }
                
                sw.Write(Environment.NewLine);
                
                i++;
            }
            
            sw.Flush();
        }
    }

    public void Save(string path)
    {
        string fileName = Path.Combine(path, "results.csv");
        File.WriteAllBytes(fileName, _csvData.ToArray());
    }
}