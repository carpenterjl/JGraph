using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;
using JGraph.Rendering;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Objects;

/// <summary>
/// A dense marker series draws (M121). Until then a line with more than five thousand samples had
/// its markers suppressed, which for <c>plot(x, y, '.')</c> — no line, markers only — meant an axes
/// with correct limits, correct ticks and nothing at all inside them.
/// </summary>
public class MarkerDensityM121Tests
{
    private const int Threshold = 5000;

    private static (RecordingRenderContext Context, LinePlot Line) Draw(int points, double area = 600)
    {
        var x = new double[points];
        var y = new double[points];
        for (int i = 0; i < points; i++)
        {
            x[i] = i;

            // Five distinct levels, which is the shape the text script's panel has: many samples,
            // few distinct places to put them.
            y[i] = 11 + (i % 5);
        }

        var line = new LinePlot(x, y)
        {
            Marker = MarkerType.Point,
            MarkerSize = 2,
            DashStyle = DashStyle.None,
        };

        var context = new RecordingRenderContext(new Size2D(area, area));
        var mapper = new StretchMapper(new Rect2D(0, 0, area, area), points, 10, 16);
        line.Render(context, new RenderState(mapper, new Rect2D(0, 0, area, area), Colors.Blue));
        return (context, line);
    }

    [Fact]
    public void AMarkerOnlySeriesLongerThanTheThresholdStillDrawsSomething()
    {
        (RecordingRenderContext context, _) = Draw(20_000);

        // The number that matters is "not zero". This is the whole defect: the panel was blank.
        Assert.True(
            context.TotalMarkerPoints > 0,
            "a 20,000-point marker-only series drew no markers at all");
    }

    [Fact]
    public void EverySampleUnderTheThresholdIsStillDrawnOneForOne()
    {
        // The guarantee that keeps every picture that already worked exactly as it was: below the
        // threshold nothing is merged, so the draw call carries one point per sample in order.
        (RecordingRenderContext context, _) = Draw(Threshold);

        Assert.Equal(Threshold, context.TotalMarkerPoints);
        Assert.Equal(1, context.MarkerBatchCount);
    }

    [Fact]
    public void SamplesSharingADevicePixelAreDrawnOnce()
    {
        // Twenty thousand samples over five levels and six hundred pixels of width cannot need more
        // than a few thousand marks. The point of the collapse is that the work stops growing with
        // the data — so the count must be far below the sample count, and every mark distinct.
        (RecordingRenderContext context, _) = Draw(20_000);

        Assert.True(
            context.TotalMarkerPoints < 20_000,
            $"nothing was merged: {context.TotalMarkerPoints} marks for 20,000 samples");

        var seen = new HashSet<(long, long)>();
        foreach (Point2D at in context.MarkerPoints)
        {
            Assert.True(
                seen.Add(((long)System.Math.Round(at.X), (long)System.Math.Round(at.Y))),
                $"pixel ({at.X}, {at.Y}) was drawn twice");
        }
    }

    [Fact]
    public void TheCollapseKeepsEveryDistinctPlaceTheDataVisits()
    {
        // Merging must lose duplicates and nothing else. Five levels are five rows of marks, and a
        // collapse that dropped one would leave a picture that still looked plausible.
        (RecordingRenderContext context, _) = Draw(20_000);

        var rows = new HashSet<long>();
        foreach (Point2D at in context.MarkerPoints)
        {
            rows.Add((long)System.Math.Round(at.Y));
        }

        Assert.Equal(5, rows.Count);
    }

    /// <summary>Maps a run of indices and a small value range across the whole plot area.</summary>
    private sealed class StretchMapper : ICoordinateMapper
    {
        private readonly double _points;
        private readonly double _low;
        private readonly double _high;

        public StretchMapper(Rect2D area, int points, double low, double high)
        {
            PlotArea = area;
            _points = System.Math.Max(1, points - 1);
            _low = low;
            _high = high;
        }

        public Rect2D PlotArea { get; }

        public Point2D DataToPixel(double x, double y) => new(
            PlotArea.Left + (PlotArea.Width * x / _points),
            PlotArea.Bottom - (PlotArea.Height * (y - _low) / (_high - _low)));

        public Point2D PixelToData(double px, double py) => new(
            (px - PlotArea.Left) * _points / PlotArea.Width,
            _low + ((PlotArea.Bottom - py) * (_high - _low) / PlotArea.Height));
    }
}
