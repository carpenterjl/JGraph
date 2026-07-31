using JGraph.Core.Primitives;
using JGraph.Maths.Contours;
using Xunit;

namespace JGraph.Tests.Maths;

/// <summary>
/// M44 wave 2: the single-sweep band clip and the assembled iso-line set. The band clip replaces a
/// per-band sweep of the whole grid, so the thing worth pinning is that it produces exactly the same
/// geometry — the equivalence is what makes the optimization safe.
/// </summary>
public class ContourBandsTests
{
    /// <summary>A grid sampling z = x² + y² over [-2, 2]².</summary>
    private static (double[] X, double[] Y, double[,] Z) Paraboloid(int n = 41)
    {
        var x = new double[n];
        var y = new double[n];
        for (int i = 0; i < n; i++)
        {
            x[i] = -2 + (4.0 * i / (n - 1));
            y[i] = -2 + (4.0 * i / (n - 1));
        }

        var z = new double[n, n];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                z[r, c] = (x[c] * x[c]) + (y[r] * y[r]);
            }
        }

        return (x, y, z);
    }

    [Fact]
    public void EveryBand_MatchesTheOneAtATimeClip()
    {
        (double[] x, double[] y, double[,] z) = Paraboloid();
        double[] boundaries = [0, 1, 2, 4, 8];

        var bands = new ContourBands();
        bands.Build(x, y, z, boundaries);

        Assert.Equal(4, bands.BandCount);
        for (int b = 0; b < bands.BandCount; b++)
        {
            IReadOnlyList<Point2D[]> expected =
                MarchingSquares.FilledCells(x, y, z, boundaries[b], boundaries[b + 1]);

            Assert.Equal(expected.Count, bands.BandPolygonCount(b));
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.Equal(expected[i], bands.BandPolygon(b, i).ToArray());
            }
        }
    }

    /// <summary>
    /// The point of the single sweep: a cell is only clipped against the bands its own corner values
    /// can reach. Piling on bands the data never enters must therefore cost nothing but the search.
    /// </summary>
    [Fact]
    public void BandsTheDataNeverReaches_ProduceNothing()
    {
        (double[] x, double[] y, double[,] z) = Paraboloid(21);
        double[] boundaries = [-100, -50, -0.5, 8.5, 50, 100];

        var bands = new ContourBands();
        bands.Build(x, y, z, boundaries);

        Assert.Equal(0, bands.BandPolygonCount(0));
        Assert.Equal(0, bands.BandPolygonCount(1));
        Assert.Equal(0, bands.BandPolygonCount(3));
        Assert.Equal(0, bands.BandPolygonCount(4));
        Assert.Equal(20 * 20, bands.BandPolygonCount(2)); // the one band the data lives in
    }

    /// <summary>
    /// The band search has to be inclusive at both ends. A corner sitting exactly on a boundary is
    /// still clipped against the band on the far side of it — that yields a zero-area sliver rather
    /// than anything visible, but it is what clipping one band at a time does, and the point of the
    /// single sweep is to be indistinguishable from it.
    /// </summary>
    [Fact]
    public void CornersExactlyOnABoundary_StillReachTheBandBelow()
    {
        (double[] x, double[] y, double[,] z) = Paraboloid(21);
        double[] boundaries = [0, 8]; // z bottoms out at exactly 0 in the middle of the grid

        var bands = new ContourBands();
        bands.Build(x, y, z, boundaries);

        Assert.Equal(MarchingSquares.FilledCells(x, y, z, 0, 8).Count, bands.BandPolygonCount(0));
    }

    [Fact]
    public void NonFiniteCells_AreSkipped()
    {
        double[] x = [0, 1, 2];
        double[] y = [0, 1];
        var z = new double[2, 3] { { 0, double.NaN, 1 }, { 0, 0.4, 1 } };

        var bands = new ContourBands();
        bands.Build(x, y, z, [0, 0.5, 1]);

        Assert.Equal(0, bands.PolygonCount);
    }

    [Fact]
    public void Matches_TracksTheBoundariesItWasBuiltFor()
    {
        (double[] x, double[] y, double[,] z) = Paraboloid(11);
        var bands = new ContourBands();
        bands.Build(x, y, z, [0, 4, 8]);

        Assert.True(bands.Matches([0, 4, 8]));
        Assert.False(bands.Matches([0, 4, 8, 12]));
        Assert.False(bands.Matches([0, 5, 8]));
    }

    /// <summary>
    /// Marching squares emits two-point segments in whatever order the grid sweep finds them, and a
    /// dash pattern restarts at the beginning of every sub-path — so drawing a contour as loose
    /// segments makes a dashed line impossible. Assembling collapses a closed iso-line into one
    /// path, which is what the renderer now strokes.
    /// </summary>
    [Fact]
    public void LineSet_ChainsTheLooseSegmentsIntoWholeCurves()
    {
        (double[] x, double[] y, double[,] z) = Paraboloid();
        double[] levels = [1.0, 2.0];

        ContourLineSet lines = ContourLineSet.Build(x, y, z, levels);

        // Each level here is a single closed circle, however many cells it crossed.
        Assert.Equal(1, lines.PathCount(0));
        Assert.Equal(1, lines.PathCount(1));
        Assert.True(
            lines.Path(0, 0).Length > 20,
            $"expected the whole circle in one path, got {lines.Path(0, 0).Length} points");

        // Closed, so the curve comes back to where it started.
        ReadOnlySpan<Point2D> circle = lines.Path(0, 0);
        Assert.Equal(circle[0].X, circle[^1].X, 9);
        Assert.Equal(circle[0].Y, circle[^1].Y, 9);
    }

    [Fact]
    public void LineSet_Matches_TracksTheLevelsItWasBuiltFor()
    {
        (double[] x, double[] y, double[,] z) = Paraboloid(11);
        ContourLineSet lines = ContourLineSet.Build(x, y, z, [1.0, 2.0]);

        Assert.True(lines.Matches([1.0, 2.0]));
        Assert.False(lines.Matches([1.0]));
        Assert.False(lines.Matches([1.0, 2.5]));
    }
}
