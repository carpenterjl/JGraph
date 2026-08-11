using JGraph.Core.Primitives;
using JGraph.Maths.Transforms;
using Xunit;

namespace JGraph.Tests.Maths;

/// <summary>
/// M54 wave F: the two matrix builders, and the camera roll they share a convention with. Pure
/// arithmetic, so every one of these is an exact value rather than a tolerance.
/// </summary>
public class CameraMatrixTests
{
    [Fact]
    public void TheViewMatrixIsTheRotationTheProjectionUses()
    {
        double[,] m = CameraMatrices.ViewMatrix(0, 90);

        // Straight down: screen-right is +x, screen-up is +y, depth is +z.
        Assert.Equal(1, m[0, 0], 12);
        Assert.Equal(0, m[0, 1], 12);
        Assert.Equal(1, m[1, 1], 12);
        Assert.Equal(1, m[2, 2], 12);
        Assert.Equal(1, m[3, 3], 12);
    }

    [Fact]
    public void TheViewMatrixRowsAreOrthonormal()
    {
        double[,] m = CameraMatrices.ViewMatrix(-37.5, 30);

        for (int r = 0; r < 3; r++)
        {
            for (int s = 0; s < 3; s++)
            {
                double dot = (m[r, 0] * m[s, 0]) + (m[r, 1] * m[s, 1]) + (m[r, 2] * m[s, 2]);
                Assert.Equal(r == s ? 1 : 0, dot, 12);
            }
        }
    }

    [Fact]
    public void APerspectiveViewMatrixDividesByDepthAndAZeroAngleDoesNot()
    {
        double[,] flat = CameraMatrices.ViewMatrix(-37.5, 30, 0, new Vector3D(0, 0, 0));
        double[,] deep = CameraMatrices.ViewMatrix(-37.5, 30, 25, new Vector3D(0, 0, 0));

        Assert.Equal(0, flat[3, 2], 12);
        Assert.True(deep[3, 2] < 0, "a nearer viewpoint shortens what is behind it");

        // A wider angle is a closer camera, so it divides harder.
        double[,] wider = CameraMatrices.ViewMatrix(-37.5, 30, 60, new Vector3D(0, 0, 0));
        Assert.True(wider[3, 2] < deep[3, 2]);
    }

    [Fact]
    public void ATargetMovesTheOriginOfThePerspective()
    {
        double[,] centred = CameraMatrices.ViewMatrix(0, 90, 25, new Vector3D(0, 0, 0));
        double[,] offset = CameraMatrices.ViewMatrix(0, 90, 25, new Vector3D(1, 0, 0));

        Assert.Equal(0, centred[0, 3], 12);
        Assert.Equal(-1, offset[0, 3], 12);
    }

    [Fact]
    public void TranslateScaleAndRotateAreTheMatricesTheyClaim()
    {
        double[,] t = CameraMatrices.Translate(1, 2, 3);
        Assert.Equal(1, t[0, 3], 12);
        Assert.Equal(2, t[1, 3], 12);
        Assert.Equal(3, t[2, 3], 12);

        double[,] s = CameraMatrices.Scale(2, 3, 4);
        Assert.Equal(2, s[0, 0], 12);
        Assert.Equal(4, s[2, 2], 12);

        // A quarter turn about z takes +x to +y.
        double[,] r = CameraMatrices.RotateAbout(new Vector3D(0, 0, 1), System.Math.PI / 2);
        Assert.Equal(0, r[0, 0], 12);
        Assert.Equal(-1, r[0, 1], 12);
        Assert.Equal(1, r[1, 0], 12);
    }

    [Fact]
    public void RotatingAboutAnArbitraryAxisMatchesTheCoordinateForm()
    {
        double[,] about = CameraMatrices.RotateAbout(new Vector3D(0, 0, 5), 0.7);
        double[,] direct = CameraMatrices.RotateAbout(new Vector3D(0, 0, 1), 0.7);

        for (int r = 0; r < 4; r++)
        {
            for (int c = 0; c < 4; c++)
            {
                Assert.Equal(direct[r, c], about[r, c], 12);
            }
        }
    }

    [Fact]
    public void ACameraRollTurnsThePictureAndStillFitsTheBox()
    {
        var range = new DataRange(0, 1);
        var area = new Rect2D(0, 0, 400, 400);

        var upright = new Projection3D(range, range, range, 0, 90, area);
        var rolled = new Projection3D(range, range, range, 0, 90, area, null, 90);

        // Straight down with a quarter turn: what was to the right is now below.
        Point2D right = upright.ProjectPoint(1, 0.5, 0.5);
        Point2D turned = rolled.ProjectPoint(1, 0.5, 0.5);

        Assert.True(right.X > area.CenterX);
        Assert.Equal(area.CenterX, turned.X, 6);
        Assert.True(turned.Y > area.CenterY, "screen Y grows downward");

        // The fit is measured on the rolled box, so the corners are still inside the plot area.
        foreach ((double x, double y) in new[] { (0.0, 0.0), (1.0, 0.0), (0.0, 1.0), (1.0, 1.0) })
        {
            Point2D corner = rolled.ProjectPoint(x, y, 0.5);
            Assert.InRange(corner.X, area.Left - 0.001, area.Right + 0.001);
            Assert.InRange(corner.Y, area.Top - 0.001, area.Bottom + 0.001);
        }
    }

    [Fact]
    public void AZeroRollIsTheProjectionThatHasNoRollAtAll()
    {
        var range = new DataRange(-2, 3);
        var area = new Rect2D(10, 20, 300, 200);

        var plain = new Projection3D(range, range, range, -37.5, 30, area);
        var explicitZero = new Projection3D(range, range, range, -37.5, 30, area, null, 0);

        Point2D a = plain.ProjectPoint(1, 2, 0);
        Point2D b = explicitZero.ProjectPoint(1, 2, 0);
        Assert.Equal(a.X, b.X, 12);
        Assert.Equal(a.Y, b.Y, 12);
    }
}
