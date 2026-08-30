using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Maths.Transforms;
using JGraph.Objects;
using JGraph.Rendering;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Rendering;

/// <summary>
/// The two things an axes was told and did not do. <c>camva</c> stored an angle nothing read: the
/// stand-off the automatic camera derived was itself computed from that angle, so the cone narrowed
/// and the camera stepped back by exactly as much, and every angle drew the identical picture. And
/// <c>axis off</c> was a word the parser accepted and threw away, while the one property that could
/// have hidden the frame — <c>Visible</c> — hid the plots along with it, which MATLAB never does.
/// </summary>
public class AxisOffAndViewAngleTests
{
    private static readonly Rect2D Area = new(0, 0, 200, 160);
    private static readonly DataRange Unit = new(0, 10);

    private static (double[] X, double[] Y, double[,] Z) Grid(int n = 9)
    {
        var x = new double[n];
        var y = new double[n];
        for (int i = 0; i < n; i++)
        {
            x[i] = i;
            y[i] = i;
        }

        var z = new double[n, n];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                z[r, c] = System.Math.Sin(x[c] * 0.5) * System.Math.Cos(y[r] * 0.5);
            }
        }

        return (x, y, z);
    }

    private static FigureModel Surface()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        (double[] x, double[] y, double[,] z) = Grid();
        axes.AddSurface(x, y, z);
        return figure;
    }

    private static RecordingRenderContext Render(FigureModel figure)
    {
        var context = new RecordingRenderContext(new Size2D(640, 480));
        new FigureRenderer().Render(figure, context, Theme.Light);
        return context;
    }

    /// <summary>The width of everything drawn as straight lines: the box, its grid, the frame.</summary>
    private static double LineSpan(RecordingRenderContext context)
    {
        double min = double.PositiveInfinity, max = double.NegativeInfinity;
        foreach ((Point2D from, Point2D to, LineStyle _) in context.Lines)
        {
            min = System.Math.Min(min, System.Math.Min(from.X, to.X));
            max = System.Math.Max(max, System.Math.Max(from.X, to.X));
        }

        return max - min;
    }

    // --- camva is a zoom, and a continuous one ----------------------------------------------------

    /// <summary>
    /// A cone half as wide shows half as much across the screen, so the picture is twice the size —
    /// and the factor is one at the default angle, which is what keeps an untouched figure untouched.
    /// </summary>
    [Fact]
    public void CameraZoomFactor_IsOneByDefaultAndGrowsAsTheConeNarrows()
    {
        var axes = new AxesModel { Is3D = true };
        Assert.Equal(1.0, axes.CameraZoomFactor, 12);

        axes.CameraViewAngle = AxesModel.DefaultCameraViewAngle;
        Assert.Equal(1.0, axes.CameraZoomFactor, 12);

        axes.CameraViewAngle = AxesModel.DefaultCameraViewAngle / 2;
        Assert.True(axes.CameraZoomFactor > 1.99 && axes.CameraZoomFactor < 2.01,
            $"halving a small angle nearly doubles the picture, not {axes.CameraZoomFactor}");

        axes.CameraViewAngle = AxesModel.DefaultCameraViewAngle * 2;
        Assert.True(axes.CameraZoomFactor is > 0.49 and < 0.51);
    }

    /// <summary>
    /// The bug in one assertion: the automatic camera used to step back as the cone narrowed, so the
    /// two cancelled. MATLAB leaves CameraPosition where it is, and so does this now.
    /// </summary>
    [Fact]
    public void AChosenViewAngleDoesNotMoveTheAutomaticCamera()
    {
        var axes = new AxesModel { Is3D = true };
        foreach (AxisModel ruler in new[] { axes.PrimaryXAxis, axes.ActiveYAxis, axes.ZAxis })
        {
            ruler.AutoScale = false;
            ruler.Range = Unit;
        }

        axes.SetViewAngles(35, 44);
        Vector3D before = axes.EffectiveCameraPosition();

        axes.CameraViewAngle = 4;
        Vector3D after = axes.EffectiveCameraPosition();

        Assert.Equal(before.X, after.X, 12);
        Assert.Equal(before.Y, after.Y, 12);
        Assert.Equal(before.Z, after.Z, 12);

        // And the angle alone does not count as placing a camera, which is what keeps the picture on
        // the fitting projection rather than sending it through the placed-camera one.
        Assert.True(axes.HasAutomaticCameraPlacement);
        Assert.False(axes.HasAutomaticCamera);
    }

    /// <summary>The zoom the fitting projection takes is a plain magnification about the centre.</summary>
    [Fact]
    public void TheFittingProjectionMagnifiesByTheZoomItIsGiven()
    {
        var plain = new Projection3D(Unit, Unit, Unit, 35, 44, Area);
        var zoomed = new Projection3D(Unit, Unit, Unit, 35, 44, Area, null, 0, 2);

        Point2D a = plain.ProjectPoint(0, 0, 0), b = plain.ProjectPoint(10, 10, 10);
        Point2D c = zoomed.ProjectPoint(0, 0, 0), d = zoomed.ProjectPoint(10, 10, 10);

        Assert.Equal(2 * (b.X - a.X), d.X - c.X, 9);
        Assert.Equal(2 * (b.Y - a.Y), d.Y - c.Y, 9);
    }

    /// <summary>
    /// And end to end: three angles the old code drew identically now draw three different sizes, in
    /// the order MATLAB draws them — a narrower cone is a tighter zoom.
    /// </summary>
    [Fact]
    public void ASmallerViewAngleDrawsTheBoxBigger()
    {
        var spans = new List<double>();
        foreach (double angle in new double[] { 18, 8, 4 })
        {
            FigureModel figure = Surface();
            figure.Axes[0].CameraViewAngle = angle;
            spans.Add(LineSpan(Render(figure)));
        }

        Assert.True(spans[0] < spans[1], $"18 degrees is wider than 8: {spans[0]} vs {spans[1]}");
        Assert.True(spans[1] < spans[2], $"8 degrees is wider than 4: {spans[1]} vs {spans[2]}");

        // The default angle sits between 4 and 8, so the automatic framing lands between them too:
        // there is no step between "camva never called" and "camva called".
        double automatic = LineSpan(Render(Surface()));
        Assert.True(automatic > spans[1] && automatic < spans[2],
            $"the automatic framing is continuous with the chosen ones, not {automatic}");
    }

    // --- an invisible axes keeps its children ------------------------------------------------------

    [Fact]
    public void AnInvisible2DAxesDrawsItsPlotsAndNoneOfItsFurniture()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.AddLine(new double[] { 1, 2, 3 }, new double[] { 1, 4, 9 });
        axes.Title = "kept";
        axes.PrimaryXAxis.Label = "gone";
        axes.Grid.Visible = true;

        RecordingRenderContext lit = Render(figure);
        axes.Visible = false;
        RecordingRenderContext dark = Render(figure);

        Assert.Equal(lit.PolylineCount, dark.PolylineCount);
        Assert.True(lit.PolylineCount > 0, "the line was drawn either way");
        Assert.Empty(dark.Lines);
        Assert.Contains("kept", dark.Texts);
        Assert.DoesNotContain("gone", dark.Texts);
        Assert.True(dark.Texts.Count < lit.Texts.Count, "the tick labels went with the rulers");
    }

    /// <summary>
    /// The furniture that is not drawn is not measured for either, so the plot box takes back the
    /// margin the rulers were holding — which is most of what a script says <c>axis off</c> for.
    /// </summary>
    [Fact]
    public void AnInvisibleAxesGivesItsMarginsBackToThePlotBox()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.AddLine(new double[] { 1, 2, 3 }, new double[] { 1, 4, 9 });
        axes.PrimaryYAxis.Label = "wide label";

        var lit = new RecordingRenderContext(new Size2D(640, 480));
        Rect2D framed = new FigureRenderer().Render(figure, lit, Theme.Light).Axes[0].PlotArea;

        axes.Visible = false;
        var dark = new RecordingRenderContext(new Size2D(640, 480));
        Rect2D bare = new FigureRenderer().Render(figure, dark, Theme.Light).Axes[0].PlotArea;

        Assert.True(bare.Width > framed.Width, $"{bare.Width} should exceed {framed.Width}");
        Assert.True(bare.Height > framed.Height, $"{bare.Height} should exceed {framed.Height}");
    }

    [Fact]
    public void AnInvisible3DAxesKeepsTheSurfaceAndDropsTheBox()
    {
        FigureModel figure = Surface();
        AxesModel axes = figure.Axes[0];
        axes.ZAxis.Label = "gone";

        RecordingRenderContext lit = Render(figure);
        axes.Visible = false;
        RecordingRenderContext dark = Render(figure);

        Assert.True(lit.Lines.Count > 0, "the box and its grid were drawn while the axes was visible");
        Assert.Empty(dark.Lines);
        Assert.Equal(lit.TriangleBatchCount, dark.TriangleBatchCount);
        Assert.True(dark.TriangleBatchCount > 0, "the surface is drawn either way");
        Assert.DoesNotContain("gone", dark.Texts);
    }
}
