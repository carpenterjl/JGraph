using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Maths.Transforms;
using Xunit;

namespace JGraph.Tests.Maths;

/// <summary>
/// M74: the placed camera — position, target, up vector and view angle — and the perspective divide.
/// </summary>
public class Projection3DCameraTests
{
    private static readonly Rect2D Area = new(0, 0, 200, 160);
    private static readonly DataRange Unit = new(0, 10);

    /// <summary>The camera an axes derives when nothing has been said about it.</summary>
    private static (Vector3D Position, Vector3D Target, Vector3D Up) AutoCamera(double az, double el)
    {
        var axes = new AxesModel { Is3D = true };
        foreach (AxisModel ruler in new[] { axes.PrimaryXAxis, axes.ActiveYAxis, axes.ZAxis })
        {
            ruler.AutoScale = false;
            ruler.Range = Unit;
        }

        axes.SetViewAngles(az, el);
        return (axes.EffectiveCameraPosition(), axes.EffectiveCameraTarget(), axes.EffectiveCameraUpVector());
    }

    private static Projection3D Angles(double az, double el) =>
        new(Unit, Unit, Unit, az, el, Area);

    private static Projection3D Camera(
        Vector3D position, Vector3D target, Vector3D up, double? viewAngle = null, bool perspective = false) =>
        new(Unit, Unit, Unit, Area, null, 0, position, target, up, viewAngle, perspective);

    [Theory]
    [InlineData(-37.5, 30)]
    [InlineData(0, 90)]
    [InlineData(45, 15)]
    [InlineData(120, -20)]
    public void ThePlacedCameraDrawsWhatTheAnglesDrew(double az, double el)
    {
        // The camera an axes derives from its angles must project exactly as the angles do, or reading
        // campos and writing it straight back would move the picture.
        (Vector3D position, Vector3D target, Vector3D up) = AutoCamera(az, el);
        Projection3D byAngles = Angles(az, el);
        Projection3D byCamera = Camera(position, target, up);

        foreach ((double x, double y, double z) in new[]
                 {
                     (0.0, 0.0, 0.0), (10.0, 0.0, 0.0), (0.0, 10.0, 0.0), (0.0, 0.0, 10.0),
                     (10.0, 10.0, 10.0), (3.0, 7.0, 4.0),
                 })
        {
            (Point2D expected, _) = byAngles.Project(x, y, z);
            (Point2D actual, _) = byCamera.Project(x, y, z);
            Assert.Equal(expected.X, actual.X, 6);
            Assert.Equal(expected.Y, actual.Y, 6);
        }
    }

    [Fact]
    public void MovingTheTargetPansThePicture()
    {
        (Vector3D position, Vector3D target, Vector3D up) = AutoCamera(-37.5, 30);
        Projection3D centered = Camera(position, target, up);
        Projection3D shifted = Camera(position, new Vector3D(target.X + 2, target.Y, target.Z), up);

        // The target is what the plot area centers on, so naming a different one slides the scene.
        (Point2D before, _) = centered.Project(5, 5, 5);
        (Point2D after, _) = shifted.Project(5, 5, 5);
        Assert.NotEqual(before.X, after.X, 3);
    }

    [Fact]
    public void ANarrowerViewAngleMagnifies()
    {
        (Vector3D position, Vector3D target, Vector3D up) = AutoCamera(-37.5, 30);
        Projection3D wide = Camera(position, target, up, viewAngle: 20);
        Projection3D narrow = Camera(position, target, up, viewAngle: 10);

        // Halving the angle all but doubles the picture: the exact factor is the ratio of the two
        // half-angle tangents, which is what the scale is built from, and camzoom is this identity.
        double exact = System.Math.Tan(20 * System.Math.PI / 360) / System.Math.Tan(10 * System.Math.PI / 360);
        Assert.Equal(exact, Spread(narrow) / Spread(wide), 6);
        Assert.True(Spread(narrow) / Spread(wide) > 2);
    }

    [Fact]
    public void PerspectiveGrowsTheNearFaceAndShrinksTheFar()
    {
        (Vector3D position, Vector3D target, Vector3D up) = AutoCamera(0, 0);
        Projection3D flat = Camera(position, target, up, viewAngle: 40, perspective: false);
        Projection3D deep = Camera(position, target, up, viewAngle: 40, perspective: true);

        // Looking along +y from -y, the y = 0 wall is nearest the camera and y = 10 farthest.
        double flatNear = Width(flat, 0), flatFar = Width(flat, 10);
        double deepNear = Width(deep, 0), deepFar = Width(deep, 10);

        Assert.Equal(flatNear, flatFar, 6);          // parallel rays draw both walls the same size
        Assert.True(deepNear > deepFar);             // a viewpoint does not
        Assert.True(deepNear > flatNear);
        Assert.True(deepFar < flatFar);
    }

    [Fact]
    public void AnUpVectorTurnsTheScene()
    {
        (Vector3D position, Vector3D target, _) = AutoCamera(0, 0);
        Projection3D upright = Camera(position, target, new Vector3D(0, 0, 1));
        Projection3D tipped = Camera(position, target, new Vector3D(1, 0, 1));

        (Point2D straight, _) = upright.Project(10, 5, 10);
        (Point2D leaned, _) = tipped.Project(10, 5, 10);
        Assert.True(System.Math.Abs(straight.X - leaned.X) > 1 || System.Math.Abs(straight.Y - leaned.Y) > 1);
    }

    [Fact]
    public void AnUpVectorAlongTheViewStillDrawsSomething()
    {
        // Looking down the up vector leaves no screen-right to derive; the projection must still
        // produce finite pixels rather than dividing by a zero-length cross product.
        (Vector3D position, Vector3D target, _) = AutoCamera(0, 90);
        Projection3D projection = Camera(position, target, new Vector3D(0, 0, 1));

        (Point2D point, _) = projection.Project(5, 5, 5);
        Assert.True(double.IsFinite(point.X));
        Assert.True(double.IsFinite(point.Y));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnprojectAnswersTheSightLineThroughThePixel(bool perspective)
    {
        (Vector3D position, Vector3D target, Vector3D up) = AutoCamera(-37.5, 30);
        Projection3D projection = Camera(position, target, up, viewAngle: 30, perspective: perspective);

        // A point inside the box projects to a pixel; that pixel's sight line must pass back through it.
        var inside = new Vector3D(3, 7, 4);
        (Point2D pixel, _) = projection.Project(inside.X, inside.Y, inside.Z);
        (Vector3D front, Vector3D back) = projection.Unproject(pixel.X, pixel.Y);

        double t = Parameter(front, back, inside);
        Assert.InRange(t, -0.001, 1.001);

        var recovered = new Vector3D(
            front.X + ((back.X - front.X) * t),
            front.Y + ((back.Y - front.Y) * t),
            front.Z + ((back.Z - front.Z) * t));
        Assert.Equal(inside.X, recovered.X, 3);
        Assert.Equal(inside.Y, recovered.Y, 3);
        Assert.Equal(inside.Z, recovered.Z, 3);
    }

    [Fact]
    public void UnprojectPutsTheNearEndFirst()
    {
        (Vector3D position, Vector3D target, Vector3D up) = AutoCamera(-37.5, 30);
        Projection3D projection = Camera(position, target, up);

        (Vector3D front, Vector3D back) = projection.Unproject(Area.CenterX, Area.CenterY);
        (_, double frontDepth) = projection.Project(front.X, front.Y, front.Z);
        (_, double backDepth) = projection.Project(back.X, back.Y, back.Z);

        // Depth grows toward the viewer, so the entry point is the deeper number.
        Assert.True(frontDepth > backDepth);
    }

    [Fact]
    public void APixelThatMissesTheBoxStillAnswersAPoint()
    {
        (Vector3D position, Vector3D target, Vector3D up) = AutoCamera(-37.5, 30);
        Projection3D projection = Camera(position, target, up);

        (Vector3D front, Vector3D back) = projection.Unproject(-10_000, -10_000);
        Assert.Equal(front.X, back.X, 9);
        Assert.True(double.IsFinite(front.X));
    }

    /// <summary>Where a point falls along the segment from one end to the other.</summary>
    private static double Parameter(Vector3D from, Vector3D to, Vector3D point)
    {
        double dx = to.X - from.X, dy = to.Y - from.Y, dz = to.Z - from.Z;
        double lengthSquared = (dx * dx) + (dy * dy) + (dz * dz);
        if (lengthSquared < 1e-12)
        {
            return 0;
        }

        return (((point.X - from.X) * dx) + ((point.Y - from.Y) * dy) + ((point.Z - from.Z) * dz))
            / lengthSquared;
    }

    /// <summary>How wide the whole box is drawn.</summary>
    private static double Spread(Projection3D projection)
    {
        double min = double.PositiveInfinity, max = double.NegativeInfinity;
        for (int corner = 0; corner < 8; corner++)
        {
            (Point2D p, _) = projection.Project(
                (corner & 1) == 0 ? 0 : 10, (corner & 2) == 0 ? 0 : 10, (corner & 4) == 0 ? 0 : 10);
            min = System.Math.Min(min, p.X);
            max = System.Math.Max(max, p.X);
        }

        return max - min;
    }

    /// <summary>How wide the wall at a given y is drawn.</summary>
    private static double Width(Projection3D projection, double y)
    {
        (Point2D left, _) = projection.Project(0, y, 5);
        (Point2D right, _) = projection.Project(10, y, 5);
        return System.Math.Abs(right.X - left.X);
    }
}
