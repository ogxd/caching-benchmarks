namespace Caching.Benchmarks;

public class MultiPlotter : IPlotter
{
    private readonly IPlotter[] _plotters;

    public MultiPlotter(params IPlotter[] plotters)
    {
        _plotters = plotters;
    }
    
    public void Plot(string name, string xlabel, string ylabel, IList<Serie> series)
    {
        foreach (var plotter in _plotters)
        {
            plotter.Plot(name, xlabel, ylabel, series);
        }
    }
}