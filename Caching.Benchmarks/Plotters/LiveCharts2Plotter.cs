using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Drawing;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Drawing.Geometries;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.SKCharts;
using LiveChartsCore.SkiaSharpView.VisualElements;
using SkiaSharp;

namespace Caching.Benchmarks;

public class LiveCharts2Plotter : IPlotter
{
    private readonly List<(string name, SKCartesianChart plot)> _plots = new();
    
    public void Plot(string name, string xlabel, string ylabel, IList<Serie> series)
    {
        var cartesianChart = new SKCartesianChart
        {
            Width = 1200,
            Height = 800,
            Title = new LabelVisual
            {
                Text = name,
                TextSize = 30,
                Padding = new Padding(15),
                Paint = new SolidColorPaint(0xff303030)
            },
            LegendPosition = LiveChartsCore.Measure.LegendPosition.Right,
            Background = SKColors.White
        };

        cartesianChart.XAxes.First().Name = xlabel;
        cartesianChart.YAxes.First().Name = ylabel;
        
        List<ISeries> transformedSeries = new();
        
        int i = 0;
        foreach (var serie in series)
        {
            var color = new SKColor(_visualDistinctColors[i].r, _visualDistinctColors[i].g, _visualDistinctColors[i].b);
            //var color = ColorFromHSV(255d * i / series.Count, 1, 1 - 0.2 * (i % 2));
            
            transformedSeries.Add(new LineSeries<ObservablePoint, RectangleGeometry>
            {
                Name = serie.Name,
                Values = serie.Points.Select(x => new ObservablePoint(x.x, x.y)),
                GeometrySize = 20 - 2 * i,
                GeometryStroke = new SolidColorPaint(color) { StrokeThickness = 0 },
                GeometryFill = new SolidColorPaint(color) { StrokeThickness = 0 },
                Stroke = new SolidColorPaint(color) { StrokeThickness = 2 },
                Fill = null,
                LineSmoothness = 0,
            });

            i++;
        }

        cartesianChart.Series = transformedSeries;

        _plots.Add((name, cartesianChart));
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(path);
        
        foreach (var plot in _plots)
        {
            string fileName = Path.Combine(path, $"{plot.name}.png");
            SaveToFile(fileName, plot.plot);
        }
    }
    
    private static void SaveToFile(string fileName, SKCartesianChart plot)
    {
        plot.SaveImage(fileName);
        
        Console.WriteLine($"Plot saved to {Path.Combine(Directory.GetCurrentDirectory(), fileName)}");
    }
    
    private static SKColor ColorFromHSV(double hue, double saturation, double value)
    {
        int hi = Convert.ToInt32(Math.Floor(hue / 60)) % 6;
        double f = hue / 60 - Math.Floor(hue / 60);
    
        value *= 255;
        byte v = Convert.ToByte(value);
        byte p = Convert.ToByte(value * (1 - saturation));
        byte q = Convert.ToByte(value * (1 - f * saturation));
        byte t = Convert.ToByte(value * (1 - (1 - f) * saturation));
    
        return hi switch
        {
            0 => new SKColor(v, t, p),
            1 => new SKColor(q, v, p),
            2 => new SKColor(p, v, t),
            3 => new SKColor(p, q, v),
            4 => new SKColor(t, p, v),
            _ => new SKColor(t, p, v)
        };
    }

    public (byte r, byte g, byte b)[] _visualDistinctColors = {
        (230, 25, 75),
        (60, 180, 75),
        (255, 225, 25),
        (0, 130, 200),
        (245, 130, 48),
        (145, 30, 180),
        (70, 240, 240),
        (240, 50, 230),
        (210, 245, 60),
        (250, 190, 212),
        (0, 128, 128),
        (220, 190, 255),
        (220, 190, 255),
        (170, 110, 40),
        (255, 250, 200)
    };
}