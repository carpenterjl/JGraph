using BenchmarkDotNet.Attributes;
using JGraph.Core.Primitives;
using JGraph.Maths.Decimation;

namespace JGraph.Benchmarks;

/// <summary>
/// Measures the cost of preparing a large line series for drawing: narrowing to the visible window
/// and reducing it to a per-pixel-column min/max envelope. This is the hot path that must stay in the
/// low-millisecond range for the framework to pan and zoom smoothly over millions of points.
/// </summary>
[MemoryDiagnoser]
public class DecimationBenchmarks
{
    private double[] _xs = Array.Empty<double>();
    private double[] _ys = Array.Empty<double>();
    private Point2D[] _output = Array.Empty<Point2D>();

    [Params(1_000_000, 10_000_000)]
    public int PointCount { get; set; }

    [Params(1920)]
    public int Columns { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _xs = new double[PointCount];
        _ys = new double[PointCount];
        var random = new Random(1234);
        double y = 0;
        for (int i = 0; i < PointCount; i++)
        {
            _xs[i] = i;
            y += random.NextDouble() - 0.5; // random walk
            _ys[i] = y;
        }

        _output = new Point2D[MinMaxDecimator.RequiredBufferSize(Columns)];
    }

    [Benchmark]
    public int DecimateFullRange() =>
        MinMaxDecimator.Decimate(_xs, _ys, new DataRange(0, PointCount - 1), Columns, _output);

    [Benchmark]
    public int DecimateZoomedWindow() =>
        MinMaxDecimator.Decimate(_xs, _ys, new DataRange(PointCount * 0.4, PointCount * 0.5), Columns, _output);
}
