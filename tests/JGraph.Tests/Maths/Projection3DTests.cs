using JGraph.Core.Primitives;
using JGraph.Maths.Transforms;
using Xunit;

namespace JGraph.Tests.Maths;

/// <summary>M20b: the axonometric 3D projection (MATLAB view(az, el) convention).</summary>
public class Projection3DTests
{
    private static readonly Rect2D Area = new(0, 0, 200, 200);

    private static Projection3D Make(double az, double el) => new(
        new DataRange(0, 10), new DataRange(0, 10), new DataRange(0, 10), az, el, Area);

    [Fact]
    public void TopDownView_ReducesToThe2DLayout()
    {
        // view(0, 90): u = x, v = y, depth = z.
        Projection3D projection = Make(0, 90);

        (Point2D p00, _) = projection.Project(0, 0, 5);
        (Point2D p10, _) = projection.Project(10, 0, 5);
        (Point2D p01, _) = projection.Project(0, 10, 5);

        // x grows rightward, y grows up-screen (pixel y down).
        Assert.True(p10.X > p00.X);
        Assert.Equal(p10.Y, p00.Y, 6);
        Assert.True(p01.Y < p00.Y);
        Assert.Equal(p01.X, p00.X, 6);
    }

    [Fact]
    public void TopDownView_DepthIsZ()
    {
        Projection3D projection = Make(0, 90);

        (_, double low) = projection.Project(5, 5, 0);
        (_, double high) = projection.Project(5, 5, 10);

        Assert.True(high > low); // larger z is closer to the viewer looking straight down
    }

    [Fact]
    public void DefaultView_HigherZ_ProjectsHigherOnScreen()
    {
        Projection3D projection = Make(-37.5, 30);

        (Point2D bottom, _) = projection.Project(5, 5, 0);
        (Point2D top, _) = projection.Project(5, 5, 10);

        Assert.True(top.Y < bottom.Y); // up-screen = smaller pixel y
        Assert.Equal(top.X, bottom.X, 6); // the vertical axis stays vertical under azimuth-only rotation
    }

    [Fact]
    public void DefaultView_DepthOrdersFrontCornerBeforeBackCorner()
    {
        Projection3D projection = Make(-37.5, 30);

        // With az=-37.5, el=30 the (0, 10) corner faces the viewer and (10, 0) is behind.
        (_, double front) = projection.Project(0, 10, 0);
        (_, double back) = projection.Project(10, 0, 0);

        Assert.True(front != back);
    }

    [Fact]
    public void ProjectedCube_FitsInsideThePlotArea()
    {
        Projection3D projection = Make(-37.5, 30);

        for (int corner = 0; corner < 8; corner++)
        {
            double x = (corner & 1) == 0 ? 0 : 10;
            double y = (corner & 2) == 0 ? 0 : 10;
            double z = (corner & 4) == 0 ? 0 : 10;
            Point2D p = projection.ProjectPoint(x, y, z);
            Assert.InRange(p.X, Area.Left - 0.5, Area.Right + 0.5);
            Assert.InRange(p.Y, Area.Top - 0.5, Area.Bottom + 0.5);
        }
    }

    [Fact]
    public void DegenerateRanges_DoNotProduceNaN()
    {
        var projection = new Projection3D(
            new DataRange(5, 5), new DataRange(0, 1), new DataRange(2, 2), 45, 45, Area);

        Point2D p = projection.ProjectPoint(5, 0.5, 2);
        Assert.True(double.IsFinite(p.X));
        Assert.True(double.IsFinite(p.Y));
    }

    [Fact]
    public void AzimuthRotation_MovesTheProjectedXAxis()
    {
        Point2D at0 = Make(0, 30).ProjectPoint(10, 5, 0);
        Point2D at90 = Make(90, 30).ProjectPoint(10, 5, 0);

        Assert.NotEqual(at0.X, at90.X, 3);
    }
}
