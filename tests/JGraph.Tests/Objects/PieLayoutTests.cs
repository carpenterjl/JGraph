using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;
using Xunit;

namespace JGraph.Tests.Objects;

/// <summary>
/// M55 wave C: where the wedges of a pie land. The interesting parts are the rule that decides
/// whether the values are shares or have to be normalized, and the fact that a pie is drawn on a
/// unit circle whatever it was given.
/// </summary>
public class PieLayoutTests
{
    private const double Turn = System.Math.PI * 2;

    [Fact]
    public void TheWedgesStartAtTheTopAndRunCounterClockwise()
    {
        var pie = new PiePlot([1.0, 1, 2]);
        IReadOnlyList<PieSlice> slices = pie.Slices();

        Assert.Equal(3, slices.Count);
        Assert.Equal(System.Math.PI / 2, slices[0].Start, 12);
        Assert.Equal(0.25, slices[0].Fraction, 12);

        // Each wedge begins where the one before it ended, and a quarter turn is positive.
        Assert.Equal(slices[0].Start + (Turn * 0.25), slices[1].Start, 12);
        Assert.Equal(Turn * 0.5, slices[2].Sweep, 12);
    }

    [Fact]
    public void ATotalOverOneIsNormalizedAndATotalUnderOneIsTakenAsItStands()
    {
        var whole = new PiePlot([2.0, 3]);
        Assert.Equal(0.4, whole.Slices()[0].Fraction, 12);
        Assert.Equal(0.6, whole.Slices()[1].Fraction, 12);

        // Under one the values are the shares themselves, which is how a piece is left out.
        var partial = new PiePlot([0.2, 0.3]);
        Assert.Equal(0.2, partial.Slices()[0].Fraction, 12);
        Assert.Equal(0.5, partial.Slices()[0].Fraction + partial.Slices()[1].Fraction, 12);
    }

    [Fact]
    public void RunningClockwiseTurnsTheOtherWayFromTheSameStart()
    {
        var pie = new PiePlot([1.0, 3]) { Clockwise = true, StartAngle = 0 };
        IReadOnlyList<PieSlice> slices = pie.Slices();

        Assert.Equal(0, slices[0].Start, 12);
        Assert.Equal(-Turn * 0.25, slices[0].Sweep, 12);
        Assert.Equal(-Turn * 0.25, slices[1].Start, 12);
    }

    [Fact]
    public void AValueOfZeroTakesNoAngleButKeepsItsPlaceInTheOrder()
    {
        var pie = new PiePlot([1.0, 0, 1]);
        IReadOnlyList<PieSlice> slices = pie.Slices();

        Assert.Equal(3, slices.Count);
        Assert.Equal(0, slices[1].Fraction);
        Assert.Equal(2, slices[2].Index);
        Assert.Equal(0.5, slices[2].Fraction, 12);
    }

    [Fact]
    public void AnExplodedWedgeIsPushedOutAlongItsOwnMiddle()
    {
        var pie = new PiePlot([1.0, 1, 1, 1])
        {
            StartAngle = 0,
            Explode = [0, 0, 0.1, 0],
        };

        IReadOnlyList<PieSlice> slices = pie.Slices();
        Assert.Equal(0, slices[0].Offset);
        Assert.Equal(0.1, slices[2].Offset);

        // Pushing one wedge out is what makes the chart reach past the unit circle.
        Assert.Equal(1.1, new PiePlot([1.0]) { Explode = [0.1], ShowLabels = false }
            .GetXDataBounds().Max, 12);
    }

    [Fact]
    public void ALabelIsWhatTheCallerWroteOrElseTheShareAsAPercentage()
    {
        var automatic = new PiePlot([1.0, 3]);
        Assert.Equal("25%", automatic.LabelOf(0, 0.25));

        // A wedge too small to round to one percent still says it is there.
        Assert.Equal("< 1%", automatic.LabelOf(0, 0.002));

        var named = new PiePlot([1.0, 3]) { Labels = ["first", "second"] };
        Assert.Equal("second", named.LabelOf(1, 0.75));
    }

    [Fact]
    public void ThePieIsAUnitCircleWithRoomForItsLabels()
    {
        var bare = new PiePlot([1.0, 1]) { ShowLabels = false };
        Assert.Equal(new DataRange(-1, 1), bare.GetXDataBounds());
        Assert.Equal(bare.GetXDataBounds(), bare.GetYDataBounds());

        var labelled = new PiePlot([1.0, 1]) { LabelRadius = 1.2 };
        Assert.Equal(1.4, labelled.GetYDataBounds().Max, 12);
    }

    [Fact]
    public void AClickInsideAWedgeNamesTheValueItCameFrom()
    {
        // Four equal wedges from due east, so the second covers the upper-left quadrant.
        var pie = new PiePlot([1.0, 1, 1, 1]) { StartAngle = 0 };
        var mapper = new UnitMapper();

        PlotHitResult? first = pie.HitTest(new Point2D(0.5, 0.3), mapper, tolerancePixels: 5);
        Assert.NotNull(first);
        Assert.Equal(0, first!.PointIndex);

        PlotHitResult? second = pie.HitTest(new Point2D(-0.5, 0.3), mapper, tolerancePixels: 5);
        Assert.Equal(1, second?.PointIndex);

        // Outside the circle is not a hit, however close to it the point is.
        Assert.Null(pie.HitTest(new Point2D(1.5, 1.5), mapper, tolerancePixels: 5));
    }

    [Fact]
    public void AddingAPieMakesTheAxesRoundAndFrameless()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();

        PiePlot pie = axes.AddPie([1.0, 2, 3]);

        Assert.Same(pie, Assert.Single(axes.Plots));
        Assert.True(axes.EqualAspect);
        Assert.False(axes.FrameVisible);
        Assert.False(axes.PrimaryXAxis.ShowTickLabels);
        Assert.False(axes.PrimaryYAxis.ShowMajorTicks);
    }

    /// <summary>A mapper that leaves data coordinates alone, so a pie stays a unit circle.</summary>
    private sealed class UnitMapper : ICoordinateMapper
    {
        public Rect2D PlotArea => new(0, 0, 100, 100);

        public Point2D DataToPixel(double x, double y) => new(x, y);

        public Point2D PixelToData(double px, double py) => new(px, py);
    }
}
