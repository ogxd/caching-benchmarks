using System.Collections.Generic;

namespace Caching.Tests;

public interface IPlotter
{
    public void Plot(string name, string xlabel, string ylabel, IList<Serie> series);
}