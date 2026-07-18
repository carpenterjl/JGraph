using JGraph.Core.Data;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Maths.Transforms;
using JGraph.Objects;
using Xunit;

namespace JGraph.Tests.Interaction;

/// <summary>
/// M22: the windowed nearest-point search must be indistinguishable from the legacy full scan —
/// same index, same distance, same misses — across ascending and shuffled data, linear and log
/// scales, axis inversion, non-finite points, and degenerate sizes. The property test hammers
/// random configurations; a brute-force reimplementation of the legacy loop is the oracle.
/// </summary>
public class SeriesHitTesterTests
{
    private static (int Index, double Distance)? BruteForce(
        IDataSeries data, ICoordinateMapper mapper, Point2D pixel, double tolerance)
    {
        double bestDistance = double.MaxValue;
        int bestIndex = -1;
        for (int i = 0; i < data.Count; i++)
        {
            double x = data.GetX(i);
            double y = data.GetY(i);
            if (!double.IsFinite(x) || !double.IsFinite(y))
            {
                continue;
            }

            double distance = mapper.DataToPixel(x, y).DistanceTo(pixel);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        return bestIndex >= 0 && bestDistance <= tolerance ? (bestIndex, bestDistance) : null;
    }

    private static AxisTransform Mapper(
        DataRange x, DataRange y, bool logX = false, bool invertX = false, bool invertY = false) => new(
        new Rect2D(40, 20, 600, 400),
        ScaleTransforms.For(logX ? AxisScaleType.Logarithmic : AxisScaleType.Linear), x, invertX,
        ScaleTransforms.For(AxisScaleType.Linear), y, invertY);

    private static PlotHitResult? HitLine(double[] xs, double[] ys, ICoordinateMapper mapper, Point2D pixel, double tol) =>
        new LinePlot(xs, ys).HitTest(pixel, mapper, tol);

    [Theory]
    [InlineData(false, false, false)] // linear
    [InlineData(false, true, false)]  // inverted x
    [InlineData(false, false, true)]  // inverted y
    [InlineData(true, false, false)]  // log x
    public void RandomSeries_MatchTheBruteForceOracle_Exactly(bool logX, bool invertX, bool invertY)
    {
        var random = new Random(logX ? 101 : invertX ? 202 : invertY ? 303 : 404);
        foreach (int count in new[] { 0, 1, 2, 5, 1000 })
        {
            foreach (bool ascending in new[] { true, false })
            {
                double[] xs = new double[count];
                double[] ys = new double[count];
                for (int i = 0; i < count; i++)
                {
                    xs[i] = (logX ? 0.1 : -50) + (random.NextDouble() * 100);
                    ys[i] = (random.NextDouble() - 0.5) * 60;
                }

                if (ascending)
                {
                    Array.Sort(xs);
                }

                if (count >= 5)
                {
                    ys[3] = double.NaN; // non-finite points are skipped by both paths
                    xs[4] = ascending ? xs[4] : double.PositiveInfinity;
                }

                var series = new ArrayDataSeries(xs, ys);
                var mapper = Mapper(
                    new DataRange(logX ? 0.1 : -50, 60), new DataRange(-30, 30), logX, invertX, invertY);

                for (int probe = 0; probe < 60; probe++)
                {
                    var pixel = new Point2D(random.NextDouble() * 700, random.NextDouble() * 460);
                    double tolerance = random.NextDouble() * 30;

                    var expected = BruteForce(series, mapper, pixel, tolerance);
                    PlotHitResult? actual = HitLine(xs, ys, mapper, pixel, tolerance);

                    Assert.Equal(expected is null, actual is null);
                    if (expected is var (index, distance) && actual is not null)
                    {
                        Assert.Equal(index, actual.PointIndex);
                        Assert.Equal(distance, actual.DistancePixels);
                    }
                }
            }
        }
    }

    [Fact]
    public void LinePlot_HitTest_FindsExactPointsOnAMillionPointAscendingSeries()
    {
        const int count = 1_000_000;
        double[] xs = new double[count];
        double[] ys = new double[count];
        for (int i = 0; i < count; i++)
        {
            xs[i] = i * 0.001;
            ys[i] = Math.Sin(xs[i]);
        }

        var mapper = Mapper(new DataRange(0, 1000), new DataRange(-1.2, 1.2));
        Point2D target = mapper.DataToPixel(xs[123_456], ys[123_456]);

        PlotHitResult? hit = HitLine(xs, ys, mapper, target, 5);
        Assert.NotNull(hit);
        Assert.Equal(123_456, hit!.PointIndex);
        Assert.Equal(0, hit.DistancePixels, 9);

        // A pixel far from the curve misses.
        Assert.Null(HitLine(xs, ys, mapper, new Point2D(-500, -500), 5));
    }
}
