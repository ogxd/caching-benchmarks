using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using ScottPlot;

namespace Caching.Benchmarks;

public class ScotPlottPlotter : IPlotter
{
    private List<(string name, Plot plot)> _plots = new();
    
    public void Plot(string name, string xlabel, string ylabel, IList<Serie> series)
    {
        var plot = new Plot(1000, 800);
        plot.Title(name);
        plot.XLabel(xlabel);
        plot.YLabel(xlabel);
        
        int i = 0;
        foreach (var serie in series)
        {
            plot.PlotScatter(
                serie.Points.Select(x => x.x).ToArray(),
                serie.Points.Select(x => x.y).ToArray(),
                label: serie.Name,
                color: ColorFromHSV(1d * i / series.Count, 1, 1));
            
            i++;
        }

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
        Bitmap bmpPlot = plot.GetBitmap();
        Bitmap bmpLegend = plot.RenderLegend();

        // Glue the legend on the right so that it does not overlay the chart
        Bitmap bmp = new Bitmap(bmpPlot.Width + bmpLegend.Width, bmpPlot.Height);
        using Graphics gfx = Graphics.FromImage(bmp);
        gfx.Clear(Color.White);
        gfx.DrawImage(bmpPlot, 0, 0);
        gfx.DrawImage(bmpLegend, bmpPlot.Width, (bmp.Height - bmpLegend.Height) / 2);
        
        bmp.Save(fileName);
        
        Console.WriteLine($"Plot saved to {Path.Combine(Directory.GetCurrentDirectory(), fileName)}");
    }
    
    private static Color ColorFromHSV(double hue, double saturation, double value)
    {
        int hi = Convert.ToInt32(Math.Floor(hue / 60)) % 6;
        double f = hue / 60 - Math.Floor(hue / 60);

        value = value * 255;
        int v = Convert.ToInt32(value);
        int p = Convert.ToInt32(value * (1 - saturation));
        int q = Convert.ToInt32(value * (1 - f * saturation));
        int t = Convert.ToInt32(value * (1 - (1 - f) * saturation));

        return hi switch
        {
            0 => Color.FromArgb(255, v, t, p),
            1 => Color.FromArgb(255, q, v, p),
            2 => Color.FromArgb(255, p, v, t),
            3 => Color.FromArgb(255, p, q, v),
            4 => Color.FromArgb(255, t, p, v),
            _ => Color.FromArgb(255, t, p, v)
        };
    }
}