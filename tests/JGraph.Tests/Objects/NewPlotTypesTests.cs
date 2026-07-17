using JGraph.Core.Drawing;
using JGraph.Core.Primitives;
using JGraph.Objects;
using JGraph.Rendering;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Objects;

public class NewPlotTypesTests
{
    private static RenderState State() =>
        new(new IdentityMapper(new Rect2D(0, 0, 100, 100)), new Rect2D(0, 0, 100, 100), Colors.Blue);

    // ---- StemPlot ----

    [Fact]
    public void Stem_YBoundsIncludeBaseline()
    {
        var stem = new StemPlot(new double[] { 0, 1, 2 }, new double[] { 5, 8, 6 });
        Assert.Equal(0, stem.GetYDataBounds().Min);
        Assert.Equal(8, stem.GetYDataBounds().Max);
    }

    [Fact]
    public void Stem_DrawsOneStemAndMarkerPerPoint()
    {
        var stem = new StemPlot(new double[] { 0, 1, 2 }, new double[] { 5, 8, 6 });
        var ctx = new RecordingRenderContext(new Size2D(100, 100));
        ((IDrawable)stem).Render(ctx, State());

        Assert.Equal(3, ctx.LineCount);        // one stem per point
        Assert.Equal(3, ctx.MarkerBatchCount); // one marker per point
    }

    // ---- HistogramPlot ----

    [Fact]
    public void Histogram_CountsSamplesPerBin()
    {
        var hist = new HistogramPlot(new double[] { 1, 2, 2, 3, 3, 3 }) { BinCount = 3 };
        Assert.Equal(new double[] { 1, 2, 3 }, hist.BinHeights.ToArray());
        Assert.Equal(4, hist.BinEdges.Count);
        Assert.Equal(1, hist.BinEdges[0], 6);
        Assert.Equal(3, hist.BinEdges[^1], 6);
    }

    [Fact]
    public void Histogram_ProbabilitySumsToOne()
    {
        var hist = new HistogramPlot(new double[] { 1, 2, 2, 3, 3, 3 })
        {
            BinCount = 3,
            Normalization = HistogramNormalization.Probability,
        };
        Assert.Equal(1.0, hist.BinHeights.Sum(), 9);
    }

    [Fact]
    public void Histogram_DensityIntegratesToOne()
    {
        var hist = new HistogramPlot(new double[] { 1, 2, 2, 3, 3, 3 })
        {
            BinCount = 3,
            Normalization = HistogramNormalization.Density,
        };
        double binWidth = hist.BinEdges[1] - hist.BinEdges[0];
        double area = hist.BinHeights.Sum() * binWidth;
        Assert.Equal(1.0, area, 9);
    }

    [Fact]
    public void Histogram_CumulativeEndsAtSampleCount()
    {
        var hist = new HistogramPlot(new double[] { 1, 2, 2, 3, 3, 3 })
        {
            BinCount = 3,
            Normalization = HistogramNormalization.Cumulative,
        };
        Assert.Equal(6, hist.BinHeights[^1]);
    }

    [Fact]
    public void Histogram_YBoundsStartAtZero()
    {
        var hist = new HistogramPlot(new double[] { 1, 2, 2, 3, 3, 3 }) { BinCount = 3 };
        Assert.Equal(0, hist.GetYDataBounds().Min);
        Assert.Equal(3, hist.GetYDataBounds().Max);
    }

    [Fact]
    public void Histogram_RecomputesWhenBinCountChanges()
    {
        var hist = new HistogramPlot(new double[] { 1, 2, 3, 4 }) { BinCount = 2 };
        Assert.Equal(2, hist.BinHeights.Count);
        hist.BinCount = 4;
        Assert.Equal(4, hist.BinHeights.Count);
    }

    // ---- ErrorBarPlot ----

    [Fact]
    public void ErrorBar_YBoundsExpandByError()
    {
        var eb = new ErrorBarPlot(new double[] { 0, 1 }, new double[] { 10, 20 }, new double[] { 1, 2 });
        Assert.Equal(9, eb.GetYDataBounds().Min);
        Assert.Equal(22, eb.GetYDataBounds().Max);
    }

    [Fact]
    public void ErrorBar_AsymmetricErrorsRespected()
    {
        var eb = new ErrorBarPlot(
            new Core.Data.ArrayDataSeries(new double[] { 0 }, new double[] { 10 }),
            new double[] { 3 },
            new double[] { 5 });
        Assert.Equal(7, eb.GetYDataBounds().Min);
        Assert.Equal(15, eb.GetYDataBounds().Max);
    }

    [Fact]
    public void ErrorBar_DrawsWhiskersCapsAndMarkers()
    {
        var eb = new ErrorBarPlot(new double[] { 0, 1 }, new double[] { 10, 20 }, new double[] { 1, 2 });
        var ctx = new RecordingRenderContext(new Size2D(100, 100));
        ((IDrawable)eb).Render(ctx, State());

        // Per point: 1 whisker + 2 caps = 3 lines → 6 for two points.
        Assert.Equal(6, ctx.LineCount);
        Assert.Equal(2, ctx.MarkerBatchCount);
        Assert.Equal(1, ctx.PolylineCount); // connecting line
    }

    [Fact]
    public void ErrorBar_NoLineWhenDisabled()
    {
        var eb = new ErrorBarPlot(new double[] { 0, 1 }, new double[] { 10, 20 }, new double[] { 1, 2 })
        {
            ShowLine = false,
        };
        var ctx = new RecordingRenderContext(new Size2D(100, 100));
        ((IDrawable)eb).Render(ctx, State());
        Assert.Equal(0, ctx.PolylineCount);
    }

    [Fact]
    public void ErrorBar_MismatchedErrorLengthThrows()
    {
        Assert.Throws<System.ArgumentException>(() =>
            new ErrorBarPlot(new double[] { 0, 1 }, new double[] { 10, 20 }, new double[] { 1 }));
    }
}
