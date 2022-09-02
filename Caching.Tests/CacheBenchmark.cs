using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using ScottPlot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Caching.Tests;

public class CacheBenchmark
{
    public static void Analyze<K, V>(string analysisName, ObserverTest metricService, Func<K, V> factory, IGenerator<K> generator, params (string testName, ICache<K, V> cache)[] tests)
    {
        Dictionary<string, Plot> plots = new Dictionary<string, Plot>();

        plots.Add("efficiency", new Plot(1000, 800));
        plots["efficiency"].Title(analysisName);
        plots["efficiency"].XLabel("cache size");
        plots["efficiency"].YLabel("efficiency %");
        //plots["efficiency"].Legend(location: legendLocation.upperLeft);

        //AnalyzeBaseline(plots, generator);

        int i = 0;
        foreach (var test in tests)
        {
            AnalyzeCache(i, test.testName.Split('`')[0], test.cache, factory, metricService, plots, generator);
            i++;
        }

        string dirName = "graphs"; //analysisName.ToLower().Replace(' ', '_');
        Directory.CreateDirectory(dirName);

        foreach (var pair in plots)
        {
            string filename = $"{analysisName} - {pair.Key}.png";
            pair.Value.SaveFig(Path.Combine(dirName, filename));
            Console.WriteLine($"Plot saved to {Path.Combine(Directory.GetCurrentDirectory(), dirName, filename)}");
        }
    }

    private static void AnalyzeBaseline<K>(Dictionary<string, Plot> plots, IGenerator<K> generator)
    {
        generator.Reset();

        Dictionary<K, int> hashByOccurrences = new Dictionary<K, int>();

        for (int i = 0; i < 0; i++)
        {
            generator.Generate();
        }

        for (int i = 0; i < 1_000_000; i++)
        {
            ref var count = ref CollectionsMarshal.GetValueRefOrAddDefault(hashByOccurrences, generator.Generate(), out _);
            count++;
        }

        var orderedOccurrences = hashByOccurrences.OrderByDescending(x => x.Value).Select(x => x.Value).ToArray();

        var measurements = new List<(int size, int calls, int misses)>();

        for (int i = 1; i < 10; i++)
        {
            int size = 1000 * i * i;

            int hits = orderedOccurrences.Take(size).Sum();
            int misses = orderedOccurrences.Skip(size).Sum();

            measurements.Add((size, hits + misses, misses));
        }

        double[] sizes = measurements.Select(x => (double)x.size).ToArray();

        double[] efficiency = measurements.Select(x => (x.calls == 0) ? 0 : 100d * (x.calls - x.misses) / x.calls).ToArray();
        plots["efficiency"].PlotScatter(sizes, efficiency, label: "Baseline", color: Color.Black);
    }

    private static void AnalyzeCache<K, V>(int c, string testName, ICache<K, V> cache, Func<K, V> factory, ObserverTest metricService, Dictionary<string, Plot> plots, IGenerator<K> generator)
    {
        var measurements = new List<(int size, int calls, int misses)>();

        int iterations = 21;

        Stopwatch sw = Stopwatch.StartNew();

        for (int i = 1; i < iterations; i++)
        {
            int size = 1000 * i * i;

            cache.Clear();
            cache.MaxSize = size;

            generator.Reset();

            // Warmup
            for (int j = 0; j < 100_000; j++)
            {
                K key = generator.Generate();
                _ = cache.GetOrCreate(key, factory);
            }

            metricService.Reset();

            for (int j = 0; j < 500_000; j++)
            {
                K key = generator.Generate();
                _ = cache.GetOrCreate(key, factory);
            }

            measurements.Add((size, metricService.CacheCalls, metricService.CacheMisses));
        }

        sw.Stop();

        Console.WriteLine($"{testName} in {sw.Elapsed}");

        double[] sizes = measurements.Select(x => (double)x.size).ToArray();
        
        double[] efficiency = measurements.Select(x => (x.calls == 0) ? 0 : 100d * (x.calls - x.misses) / x.calls).ToArray();

        int textPos = c % (iterations - 4 - 1) + 4;

        plots["efficiency"].PlotScatter(sizes, efficiency, label: testName, color: _colors[c]);
        plots["efficiency"].PlotText(testName, sizes[textPos], efficiency[textPos], color: _colors[c], frameColor: Color.White);
    }

    private static readonly Color[] _colors = new Color[] { Color.SandyBrown, Color.RebeccaPurple, Color.Blue, Color.Orange, Color.Green, Color.HotPink, Color.Khaki, Color.PeachPuff, Color.Salmon };
}