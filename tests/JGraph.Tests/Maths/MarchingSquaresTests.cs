using JGraph.Core.Primitives;
using JGraph.Maths.Contours;
using Xunit;

namespace JGraph.Tests.Maths;

/// <summary>M20b: marching-squares contour lines and filled band polygons.</summary>
public class MarchingSquaresTests
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
    public void CircleLevel_AllSegmentPoints_LieNearTheCircle()
    {
        (double[] x, double[] y, double[,] z) = Paraboloid();

        IReadOnlyList<Point2D[]> segments = MarchingSquares.Lines(x, y, z, 1.0);

        Assert.NotEmpty(segments);
        foreach (Point2D[] segment in segments)
        {
            foreach (Point2D p in segment)
            {
                double radius = System.Math.Sqrt((p.X * p.X) + (p.Y * p.Y));
                Assert.InRange(radius, 0.93, 1.07); // linear interpolation on a 0.1 grid
            }
        }
    }

    [Fact]
    public void LevelBelowMinimum_YieldsNoSegments()
    {
        (double[] x, double[] y, double[,] z) = Paraboloid();

        Assert.Empty(MarchingSquares.Lines(x, y, z, -1));
        Assert.Empty(MarchingSquares.Lines(x, y, z, 100));
    }

    [Fact]
    public void SaddleCell_ProducesTwoSegments()
    {
        double[] x = [0, 1];
        double[] y = [0, 1];
        var z = new double[2, 2] { { 1, 0 }, { 0, 1 } }; // opposite corners high

        IReadOnlyList<Point2D[]> segments = MarchingSquares.Lines(x, y, z, 0.5);

        Assert.Equal(2, segments.Count);
    }

    [Fact]
    public void NonFiniteCells_AreSkipped()
    {
        double[] x = [0, 1, 2];
        double[] y = [0, 1];
        var z = new double[2, 3] { { 0, double.NaN, 1 }, { 0, 0.4, 1 } };

        // Both cells touch the NaN sample, so no segments at all.
        Assert.Empty(MarchingSquares.Lines(x, y, z, 0.5));
        Assert.Empty(MarchingSquares.FilledCells(x, y, z, 0.25, 0.75));
    }

    [Fact]
    public void FilledBand_AreaApproximatesTheAnnulus()
    {
        (double[] x, double[] y, double[,] z) = Paraboloid(81);

        // Band 1 <= z <= 4 over the paraboloid is the annulus 1 <= r² <= 4 clipped to the [-2,2]²
        // square. Its exact area is (area of r<=2 disc within square) - pi*1². The r=2 disc touches
        // the square edges only at 4 points, so area = 4*pi - pi = 3*pi ≈ 9.4248.
        IReadOnlyList<Point2D[]> polygons = MarchingSquares.FilledCells(x, y, z, 1, 4);

        double total = polygons.Sum(PolygonArea);
        Assert.InRange(total, 3 * System.Math.PI - 0.15, 3 * System.Math.PI + 0.15);
    }

    [Fact]
    public void FilledBand_WholeRange_TilesTheWholeGrid()
    {
        double[] x = [0, 1, 2];
        double[] y = [0, 1];
        var z = new double[2, 3] { { 0, 1, 2 }, { 3, 4, 5 } };

        IReadOnlyList<Point2D[]> polygons = MarchingSquares.FilledCells(x, y, z, -10, 10);

        Assert.Equal(2, polygons.Count); // both cells kept whole
        Assert.Equal(2.0, polygons.Sum(PolygonArea), 6);
    }

    [Fact]
    public void MismatchedDimensions_Throw()
    {
        Assert.Throws<ArgumentException>(() =>
            MarchingSquares.Lines([0, 1], [0, 1], new double[3, 3], 0.5));
    }

    private static double PolygonArea(Point2D[] polygon)
    {
        double sum = 0;
        for (int i = 0; i < polygon.Length; i++)
        {
            Point2D a = polygon[i];
            Point2D b = polygon[(i + 1) % polygon.Length];
            sum += (a.X * b.Y) - (b.X * a.Y);
        }

        return System.Math.Abs(sum) / 2;
    }
}
