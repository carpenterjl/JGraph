using BenchmarkDotNet.Attributes;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Maths.Transforms;
using JGraph.Objects;

namespace JGraph.Benchmarks;

/// <summary>
/// Pointer-mode hover cost: one nearest-point query against a 10M-point ascending line, as issued
/// on every mouse move. The M22 windowed binary search versus the legacy full scan.
/// </summary>
[MemoryDiagnoser]
public class HitTestBenchmarks
{
    private const int Count = 10_000_000;
    private LinePlot _plot = null!;
    private AxisTransform _mapper = null!;
    private Point2D _cursor;

    [GlobalSetup]
    public void Setup()
    {
        var xs = new double[Count];
        var ys = new double[Count];
        for (int i = 0; i < Count; i++)
        {
            xs[i] = i * 0.001;
            ys[i] = Math.Sin(xs[i]);
        }

        _plot = new LinePlot(xs, ys);
        _mapper = new AxisTransform(
            new Rect2D(40, 20, 1200, 700),
            ScaleTransforms.For(AxisScaleType.Linear), new DataRange(0, Count * 0.001), false,
            ScaleTransforms.For(AxisScaleType.Linear), new DataRange(-1.2, 1.2), false);
        _cursor = _mapper.DataToPixel(xs[Count / 2], ys[Count / 2]);
    }

    [Benchmark]
    public PlotHitResult? WindowedHitTest() => _plot.HitTest(_cursor, _mapper, 14);

    [Benchmark(Baseline = true)]
    public int LegacyFullScan()
    {
        // The pre-M22 loop: every point mapped to pixels and measured.
        double bestDistance = double.MaxValue;
        int bestIndex = -1;
        for (int i = 0; i < _plot.Data.Count; i++)
        {
            double x = _plot.Data.GetX(i);
            double y = _plot.Data.GetY(i);
            if (!double.IsFinite(x) || !double.IsFinite(y))
            {
                continue;
            }

            double distance = _mapper.DataToPixel(x, y).DistanceTo(_cursor);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        return bestIndex;
    }
}
