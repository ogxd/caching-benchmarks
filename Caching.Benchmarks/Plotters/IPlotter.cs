using System.Collections.Generic;

namespace Caching.Benchmarks;

public interface IPlotter
{
    public void Plot(string name, string xlabel, string ylabel, IList<Serie> series);
}