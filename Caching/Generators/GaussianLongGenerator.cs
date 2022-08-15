using System;

namespace Caching;

public class GaussianLongGenerator : IGenerator<long>
{
    private readonly double _stdDev;
    private readonly double _mean;
    private Random _random;

    public GaussianLongGenerator(double mean, double stdDev)
    {
        _random = new Random(0);
        _stdDev = stdDev;
        _mean = mean;
    }

    private double SampleGaussian(double mean, double stddev)
    {
        // The method requires sampling from a uniform random of (0,1]
        // but Random.NextDouble() returns a sample of [0,1).
        double x1 = 1 - _random.NextDouble();
        double x2 = 1 - _random.NextDouble();

        double y1 = Math.Sqrt(-2.0 * Math.Log(x1)) * Math.Cos(2.0 * Math.PI * x2);
        return y1 * stddev + mean;
    }

    public long Generate()
    {
        return (long)Math.Round(SampleGaussian(_mean, _stdDev));
    }

    public void Reset()
    {
        _random = new Random(0);
    }
}