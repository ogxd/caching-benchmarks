using System.Globalization;
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
        cartesianChart.XAxes.First().MinLimit = 0;
        cartesianChart.XAxes.First().MinStep = series[0].Points[^1].x / (series[0].Points.Length - 1);
        cartesianChart.XAxes.First().Labeler = x => Math.Round(x).ToString(CultureInfo.InvariantCulture);
        cartesianChart.XAxes.First().ForceStepToMin = true;
        cartesianChart.YAxes.First().MinLimit = 0;
        
        List<ISeries> transformedSeries = new();
        
        int i = 0;
        foreach (var serie in series)
        {
            var color = new SKColor(_visualDistinctColors[i].r, _visualDistinctColors[i].g, _visualDistinctColors[i].b);

            bool isBaseline = string.Equals("baseline", serie.Name, StringComparison.OrdinalIgnoreCase);
            
            transformedSeries.Add(new LineSeries<ObservablePoint, RectangleGeometry>
            {
                Name = serie.Name,
                IsVisibleAtLegend = !isBaseline,
                Values = serie.Points.Select(x => new ObservablePoint(x.x, x.y)),
                GeometrySize = isBaseline ? 0 : 20 - 2 * i,
                GeometryStroke = isBaseline ? _BaselineColor : new SolidColorPaint(color) { StrokeThickness = 0 },
                GeometryFill = isBaseline ? _BaselineColor : new SolidColorPaint(color) { StrokeThickness = 0 },
                Stroke = isBaseline ? _BaselineColor : new SolidColorPaint(color) { StrokeThickness = 2 },
                Fill = isBaseline ? _BaselineColor : null,
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

    private static readonly Paint _BaselineColor = new LinearGradientPaint(
        new SKColor(0, 0, 255, 15),
        new SKColor(255, 0, 0, 15),
        new SKPoint(0, 1),
        new SKPoint(0, 0)) { StrokeThickness = 0 };
    //private static readonly Paint _BaselineColor = new SolidColorPaint(0x22ff0000) { StrokeThickness = 0 };

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
        (170, 110, 40),
        (255, 250, 200),
        (130, 170, 44),
        (70, 134, 160),
    };
}