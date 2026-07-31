using JGraph.Maths.Geometry;
using Xunit;

namespace JGraph.Tests.Maths;

/// <summary>
/// M45.E: the parametric grids behind <c>sphere</c>, <c>cylinder</c> and <c>ellipsoid</c>. What is
/// worth pinning is not that the formulas were typed in correctly but that the degeneracies are
/// exact — a pole that collapses to a point, a seam that closes — because those are the places a
/// rounding error shows up as visible geometry.
/// </summary>
public class ShapeGridsTests
{
    [Fact]
    public void Sphere_IsSquareWithOneMoreVertexThanFacets()
    {
        (double[,] x, double[,] y, double[,] z) = ShapeGrids.Sphere(8);

        Assert.Equal(9, x.GetLength(0));
        Assert.Equal(9, x.GetLength(1));
        Assert.Equal(9, y.GetLength(0));
        Assert.Equal(9, z.GetLength(1));
    }

    [Fact]
    public void Sphere_PutsEveryVertexAtUnitRadius()
    {
        (double[,] x, double[,] y, double[,] z) = ShapeGrids.Sphere(12);

        for (int r = 0; r < x.GetLength(0); r++)
        {
            for (int c = 0; c < x.GetLength(1); c++)
            {
                double radius = System.Math.Sqrt(
                    (x[r, c] * x[r, c]) + (y[r, c] * y[r, c]) + (z[r, c] * z[r, c]));
                Assert.Equal(1, radius, 12);
            }
        }
    }

    /// <summary>
    /// The two pole rows collapse to a single point exactly. Left to cos(pi/2) they would sit a few
    /// times 1e-17 off the axis, which is a ring of degenerate slivers rather than a point.
    /// </summary>
    [Fact]
    public void Sphere_CollapsesBothPolesExactly()
    {
        (double[,] x, double[,] y, double[,] z) = ShapeGrids.Sphere(10);
        int last = x.GetLength(0) - 1;

        for (int c = 0; c < x.GetLength(1); c++)
        {
            Assert.Equal(0, x[0, c]);
            Assert.Equal(0, y[0, c]);
            Assert.Equal(-1, z[0, c]);
            Assert.Equal(0, x[last, c]);
            Assert.Equal(0, y[last, c]);
            Assert.Equal(1, z[last, c]);
        }
    }

    /// <summary>The first and last column are the same meridian, so the surface closes without a gap.</summary>
    [Fact]
    public void Sphere_ClosesItsSeamExactly()
    {
        (double[,] x, double[,] y, double[,] z) = ShapeGrids.Sphere(10);
        int last = x.GetLength(1) - 1;

        for (int r = 0; r < x.GetLength(0); r++)
        {
            Assert.Equal(x[r, 0], x[r, last]);
            Assert.Equal(y[r, 0], y[r, last]);
            Assert.Equal(z[r, 0], z[r, last]);
        }
    }

    [Fact]
    public void Ellipsoid_ScalesAndOffsetsEachAxisIndependently()
    {
        (double[,] x, double[,] y, double[,] z) = ShapeGrids.Ellipsoid(10, 20, 30, 1, 2, 3, 16);

        for (int r = 0; r < x.GetLength(0); r++)
        {
            for (int c = 0; c < x.GetLength(1); c++)
            {
                double dx = (x[r, c] - 10) / 1;
                double dy = (y[r, c] - 20) / 2;
                double dz = (z[r, c] - 30) / 3;
                Assert.Equal(1, (dx * dx) + (dy * dy) + (dz * dz), 12);
            }
        }
    }

    /// <summary>
    /// A single radius is a cylinder of constant width, which needs two profile points; MATLAB
    /// duplicates it and so does this, or the height division would be by zero.
    /// </summary>
    [Fact]
    public void Cylinder_ReadsAScalarRadiusAsAConstantProfile()
    {
        (double[,] x, double[,] y, double[,] z) = ShapeGrids.Cylinder([2], 20);

        Assert.Equal(2, x.GetLength(0));
        Assert.Equal(21, x.GetLength(1));
        Assert.Equal(0, z[0, 0]);
        Assert.Equal(1, z[1, 0]);
        Assert.Equal(2, System.Math.Sqrt((x[0, 5] * x[0, 5]) + (y[0, 5] * y[0, 5])), 12);
    }

    [Fact]
    public void Cylinder_FollowsAProfileCurveUpTheHeight()
    {
        (double[,] x, double[,] _, double[,] z) = ShapeGrids.Cylinder([1, 2, 3], 8);

        Assert.Equal(3, x.GetLength(0));
        Assert.Equal(1, x[0, 0]);
        Assert.Equal(2, x[1, 0]);
        Assert.Equal(3, x[2, 0]);
        Assert.Equal(0, z[0, 0]);
        Assert.Equal(0.5, z[1, 0]);
        Assert.Equal(1, z[2, 0]);
    }

    [Fact]
    public void Cylinder_ClosesItsSeamExactly()
    {
        (double[,] x, double[,] y, double[,] _) = ShapeGrids.Cylinder([1, 1], 7);
        int last = x.GetLength(1) - 1;

        Assert.Equal(x[0, 0], x[0, last]);
        Assert.Equal(y[0, 0], y[0, last]);
    }

    [Fact]
    public void Cylinder_RejectsAnEmptyProfile() =>
        Assert.Throws<ArgumentException>(() => ShapeGrids.Cylinder([], 8));

    [Fact]
    public void Sphere_RejectsAFacetCountBelowOne() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ShapeGrids.Sphere(0));
}
