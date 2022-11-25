extern alias ScottPlot5;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ScottPlot5.ScottPlot;
using ScottPlot5.ScottPlot.Style;

namespace Caching.Tests;

public class ScotPlott5Plotter : IPlotter
{
    private readonly List<(string name, Plot plot)> _plots = new();
    
    public void Plot(string name, string xlabel, string ylabel, IList<Serie> series)
    {
        var plot = new Plot();
        plot.XAxis.Label.Text = xlabel;
        plot.YAxis.Label.Text = ylabel;

        var legend = new List<LegendItem>();
        
        int i = 0;
        foreach (var serie in series)
        {
            var color = ColorFromHSV(255d * i / series.Count, 1, 1);
            var marker = new Marker(MarkerShape.Circle, color, 5);
            
            plot.Add.Scatter(
                serie.Point.Select(x => x.x).ToArray(),
                serie.Point.Select(x => x.y).ToArray(),
                marker);

            var legendItem = new LegendItem();
            legendItem.Label = serie.Name;
            legendItem.Line = new Stroke(color, 2);
            legendItem.Marker = marker;
            
            legend.Add(legendItem);

            i++;
        }
        
        plot.Add.Legend(legend);

        _plots.Add((name, plot));
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
    
    private static void SaveToFile(string fileName, Plot plot)
    {
        plot.SavePng(fileName, 1000, 800);
        
        Console.WriteLine($"Plot saved to {Path.Combine(Directory.GetCurrentDirectory(), fileName)}");
    }
    
    private static Color ColorFromHSV(double hue, double saturation, double value)
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
            0 => new Color(v, t, p),
            1 => new Color(q, v, p),
            2 => new Color(p, v, t),
            3 => new Color(p, q, v),
            4 => new Color(t, p, v),
            _ => new Color(t, p, v)
        };
    }
}