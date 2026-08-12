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
/// M56 wave A: polar as a mode an axes is in. These tests are about the mapping and the furniture —
/// that a circle is drawn, that it is turned and directed as asked, and that the Cartesian machinery
/// stands aside. What is drawn <em>on</em> it is an ordinary plot object, which is the point.
/// </summary>
public class PolarAxesTests
{
    private static (FigureModel Figure, AxesModel Axes) PolarFigure()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.MakePolar();
        return (figure, axes);
    }

    [Fact]
    public void ZeroDegreesIsToTheRightAndAQuarterTurnIsUp()
    {
        var area = new Rect2D(0, 0, 200, 200);
        var polar = new PolarTransform(
            area, new DataRange(0, 1), ThetaZeroLocation.Right, ThetaDirection.CounterClockwise);

        Point2D east = polar.DataToPixel(0, 1);
        Assert.Equal(200, east.X, 6);
        Assert.Equal(100, east.Y, 6);

        // Device Y grows downward, so anticlockwise a quarter turn is the top of the circle.
        Point2D north = polar.DataToPixel(System.Math.PI / 2, 1);
        Assert.Equal(100, north.X, 6);
        Assert.Equal(0, north.Y, 6);

        Point2D middle = polar.DataToPixel(0, 0);
        Assert.Equal(100, middle.X, 6);
        Assert.Equal(100, middle.Y, 6);
    }

    [Fact]
    public void TheZeroLocationAndTheDirectionTurnTheChartTogether()
    {
        var area = new Rect2D(0, 0, 200, 200);

        // A compass: north at the top, bearings running clockwise. 90° is then due east.
        var compass = new PolarTransform(
            area, new DataRange(0, 1), ThetaZeroLocation.Top, ThetaDirection.Clockwise);

        Point2D north = compass.DataToPixel(0, 1);
        Assert.Equal(100, north.X, 6);
        Assert.Equal(0, north.Y, 6);

        Point2D east = compass.DataToPixel(System.Math.PI / 2, 1);
        Assert.Equal(200, east.X, 6);
        Assert.Equal(100, east.Y, 6);
    }

    [Fact]
    public void APixelReadsBackAsTheAngleAndRadiusThatPutItThere()
    {
        var polar = new PolarTransform(
            new Rect2D(10, 20, 200, 200),
            new DataRange(0, 5),
            ThetaZeroLocation.Top,
            ThetaDirection.Clockwise);

        Point2D pixel = polar.DataToPixel(2.3, 3.75);
        Point2D back = polar.PixelToData(pixel.X, pixel.Y);

        Assert.Equal(2.3, back.X, 6);
        Assert.Equal(3.75, back.Y, 6);
    }

    [Fact]
    public void ARadiusOutsideTheLimitsIsDroppedRatherThanDrawnPastTheRim()
    {
        var polar = new PolarTransform(
            new Rect2D(0, 0, 100, 100),
            new DataRange(0, 1),
            ThetaZeroLocation.Right,
            ThetaDirection.CounterClockwise);

        Assert.True(double.IsNaN(polar.DataToPixel(0, 1.5).X));
        Assert.True(double.IsNaN(polar.DataToPixel(0, -0.5).X));
        Assert.False(double.IsNaN(polar.DataToPixel(0, 1).X));
    }

    /// <summary>
    /// M56 wave E: the wedge test. Angles are folded by whole turns before the gate, so a bearing of
    /// 405° lands on the 45° spoke the way a reader expects, and a negative quarter turn is the 270°
    /// it names — outside a half-circle chart, so gone.
    /// </summary>
    [Fact]
    public void AnAngleOutsideTheVisibleTurnVanishesTheWayARadiusPastTheRimDoes()
    {
        var wedge = new PolarTransform(
            new Rect2D(0, 0, 100, 100),
            new DataRange(0, 1),
            ThetaZeroLocation.Right,
            ThetaDirection.CounterClockwise,
            new DataRange(0, 180));

        Assert.False(double.IsNaN(wedge.DataToPixel(System.Math.PI / 4, 0.5).X));
        Assert.True(double.IsNaN(wedge.DataToPixel(3 * System.Math.PI / 2, 0.5).X));
        Assert.False(double.IsNaN(wedge.DataToPixel((System.Math.PI / 4) + System.Math.Tau, 0.5).X));
        Assert.True(double.IsNaN(wedge.DataToPixel(-System.Math.PI / 2, 0.5).X));
    }

    [Fact]
    public void APixelInsideAWedgeReadsBackInTheWedgesOwnAngles()
    {
        var wedge = new PolarTransform(
            new Rect2D(0, 0, 100, 100),
            new DataRange(0, 1),
            ThetaZeroLocation.Right,
            ThetaDirection.CounterClockwise,
            new DataRange(-90, 90));

        // A chart cut to [-90°, 90°] reads -45° at that spoke, not the 315° of another atan2 branch.
        Point2D pixel = wedge.DataToPixel(-System.Math.PI / 4, 0.5);
        Point2D back = wedge.PixelToData(pixel.X, pixel.Y);
        Assert.Equal(-System.Math.PI / 4, back.X, 6);
        Assert.Equal(0.5, back.Y, 6);
    }

    [Fact]
    public void TheRRulerFitsTheDataAndAlwaysReachesTheMiddle()
    {
        (FigureModel figure, AxesModel axes) = PolarFigure();
        axes.AddLine(new double[] { 0, 1, 2 }, new double[] { 3, 7, 5 });

        figure.RecomputeDataBounds();

        Assert.Equal(0, axes.RAxis.Range.Min, 10);
        Assert.Equal(7, axes.RAxis.Range.Max, 10);

        // A full turn is not fitted to the angles present: the circle is the whole point.
        Assert.Equal(0, axes.ThetaAxis.Range.Min, 10);
        Assert.Equal(360, axes.ThetaAxis.Range.Max, 10);
    }

    [Fact]
    public void RlimHoldsTheRingsWhereItWasToldTo()
    {
        (FigureModel figure, AxesModel axes) = PolarFigure();
        axes.AddLine(new double[] { 0, 1 }, new double[] { 1, 9 });
        axes.RAxis.AutoScale = false;
        axes.RAxis.Range = new DataRange(0, 4);

        figure.RecomputeDataBounds();

        Assert.Equal(4, axes.RAxis.Range.Max, 10);
    }

    [Fact]
    public void APolarAxesDrawsRingsAndSpokesAndNoRectangularFrame()
    {
        (FigureModel figure, AxesModel axes) = PolarFigure();
        axes.AddLine(new double[] { 0, 1, 2, 3 }, new double[] { 1, 2, 3, 4 });

        var context = new RecordingRenderContext(new Size2D(400, 400));
        new FigureRenderer().Render(figure, context, Theme.Light);

        // The rim plus one ring per r tick are polylines; the twelve spokes are straight lines.
        Assert.True(context.PolylineCount >= 2, $"expected rings, got {context.PolylineCount} polylines");
        Assert.Equal(12, context.Lines.Count);

        // Only the axes background rectangle: the rectangular frame belongs to square paper.
        Assert.Equal(1, context.RectangleCount);
    }

    [Fact]
    public void TurningTheGridOffLeavesTheRimAndTakesTheSpokes()
    {
        (FigureModel figure, AxesModel axes) = PolarFigure();
        axes.Grid.ShowMajor = false;

        var context = new RecordingRenderContext(new Size2D(400, 400));
        new FigureRenderer().Render(figure, context, Theme.Light);

        Assert.Empty(context.Lines);
        Assert.Equal(1, context.PolylineCount);
    }

    [Fact]
    public void TheAngleLabelsAreDegreesByDefaultAndRadiansWhenAsked()
    {
        (FigureModel figure, AxesModel axes) = PolarFigure();

        var degrees = new RecordingRenderContext(new Size2D(400, 400));
        new FigureRenderer().Render(figure, degrees, Theme.Light);
        Assert.Contains("30°", degrees.Texts);
        Assert.Contains("330°", degrees.Texts);

        axes.ThetaAxisUnits = AngleUnits.Radians;
        var radians = new RecordingRenderContext(new Size2D(400, 400));
        new FigureRenderer().Render(figure, radians, Theme.Light);
        Assert.DoesNotContain("30°", radians.Texts);
        Assert.Contains("1.571", radians.Texts);
    }

    [Fact]
    public void ThetalimNarrowsTheChartToAWedgeWithTwoStraightEdges()
    {
        (FigureModel figure, AxesModel axes) = PolarFigure();
        axes.ThetaAxis.Range = new DataRange(0, 90);

        var context = new RecordingRenderContext(new Size2D(400, 400));
        new FigureRenderer().Render(figure, context, Theme.Light);

        // Four spokes at 0, 30, 60 and 90 — and the two edges of the wedge on top of them.
        Assert.Equal(6, context.Lines.Count);
    }

    /// <summary>
    /// M56 wave B's whole claim: an ordinary plot object, given the polar mapper, reads its x as an
    /// angle and its y as a radius without knowing it moved. Nothing about <see cref="ScatterPlot"/>
    /// mentions circles, so if the four compass points land where they should, every other plot type
    /// works on a polar axes for the same reason.
    /// </summary>
    [Fact]
    public void AnOrdinaryPlotHandedThePolarMapperReadsItsXAsAnAngle()
    {
        (FigureModel figure, AxesModel axes) = PolarFigure();
        double half = System.Math.PI / 2;
        axes.AddScatter([0, half, 2 * half, 3 * half], [1, 1, 1, 1]);

        var context = new RecordingRenderContext(new Size2D(400, 400));
        new FigureRenderer().Render(figure, context, Theme.Light);

        Assert.Equal(4, context.MarkerPoints.Count);
        (Point2D east, Point2D north, Point2D west, Point2D south) = (
            context.MarkerPoints[0], context.MarkerPoints[1],
            context.MarkerPoints[2], context.MarkerPoints[3]);

        // Opposite points straddle the middle, so each pair averages to the same centre...
        Assert.Equal((east.X + west.X) / 2, (north.X + south.X) / 2, 6);
        Assert.Equal((east.Y + west.Y) / 2, (north.Y + south.Y) / 2, 6);

        // ...and the four sit on the compass, anticlockwise from the right with Y growing downward.
        Assert.True(east.X > west.X);
        Assert.True(north.Y < south.Y);
        Assert.Equal(east.Y, west.Y, 6);
        Assert.Equal(north.X, south.X, 6);
    }

    [Fact]
    public void ARulerToldWhereItsTicksGoIsObeyed()
    {
        (FigureModel figure, AxesModel axes) = PolarFigure();
        axes.ThetaAxis.TickPositions = new double[] { 0, 90, 180, 270 };
        axes.ThetaAxis.TickLabelOverrides = new[] { "N", "E", "S", "W" };

        var context = new RecordingRenderContext(new Size2D(400, 400));
        new FigureRenderer().Render(figure, context, Theme.Light);

        Assert.Equal(4, context.Lines.Count);
        Assert.Contains("N", context.Texts);
        Assert.Contains("W", context.Texts);
    }
}
