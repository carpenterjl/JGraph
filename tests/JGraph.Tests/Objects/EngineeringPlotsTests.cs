using System;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;
using JGraph.Objects.Engineering;
using JGraph.Rendering;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Objects;

public class EngineeringPlotsTests
{
    private static RenderState State() =>
        new(new IdentityMapper(new Rect2D(0, 0, 200, 200)), new Rect2D(0, 0, 200, 200), Colors.Blue);

    // ---- PolarGrid ----

    [Fact]
    public void PolarGrid_DrawsRingsAndSpokes()
    {
        var grid = new PolarGrid { MaxRadius = 1, RadialDivisions = 5, AngularDivisions = 12 };
        var ctx = new RecordingRenderContext(new Size2D(200, 200));
        ((IDrawable)grid).Render(ctx, State());

        Assert.Equal(5, ctx.PolylineCount);  // one polyline per radius ring
        Assert.Equal(12, ctx.LineCount);     // one line per angular spoke
    }

    [Fact]
    public void PolarGrid_BoundsAreSymmetric()
    {
        var grid = new PolarGrid { MaxRadius = 3 };
        Assert.Equal(-3, grid.GetXDataBounds().Min);
        Assert.Equal(3, grid.GetYDataBounds().Max);
    }

    [Fact]
    public void AddPolar_ConvertsToCartesianAndConfiguresAxes()
    {
        var axes = new AxesModel();
        LinePlot line = axes.AddPolar(new[] { 0.0, System.Math.PI / 2 }, new[] { 1.0, 2.0 });

        // (r cosθ, r sinθ): (1,0) and (0,2).
        Assert.Equal(1.0, line.Data.GetX(0), 9);
        Assert.Equal(0.0, line.Data.GetY(0), 9);
        Assert.Equal(0.0, line.Data.GetX(1), 9);
        Assert.Equal(2.0, line.Data.GetY(1), 9);

        Assert.True(axes.EqualAspect);
        Assert.False(axes.FrameVisible);
        Assert.False(axes.Grid.Visible);
        Assert.Contains(axes.Plots, p => p is PolarGrid);
        Assert.False(axes.PrimaryXAxis.ShowTickLabels);
    }

    [Fact]
    public void AddPolar_ReusesOneGridForMultipleSeries()
    {
        var axes = new AxesModel();
        axes.AddPolar(new[] { 0.0 }, new[] { 1.0 });
        axes.AddPolar(new[] { 0.0 }, new[] { 3.0 });

        int gridCount = 0;
        foreach (PlotObject p in axes.Plots)
        {
            if (p is PolarGrid)
            {
                gridCount++;
            }
        }

        Assert.Equal(1, gridCount);
    }

    // ---- SmithGrid ----

    [Fact]
    public void SmithGrid_DrawsGridGeometry()
    {
        var grid = new SmithGrid();
        var ctx = new RecordingRenderContext(new Size2D(200, 200));
        ((IDrawable)grid).Render(ctx, State());

        Assert.True(ctx.PolylineCount >= 6, "expected the unit circle plus resistance/reactance arcs");
        Assert.True(ctx.LineCount >= 1, "expected the real axis line");
    }

    [Fact]
    public void AddSmith_ConvertsImpedanceToReflection()
    {
        var axes = new AxesModel();
        // z = 1 (matched) → Γ = 0; z = 0 (short) → Γ = −1.
        LinePlot line = axes.AddSmith(new[] { 1.0, 0.0 }, new[] { 0.0, 0.0 });

        Assert.Equal(0.0, line.Data.GetX(0), 9);
        Assert.Equal(0.0, line.Data.GetY(0), 9);
        Assert.Equal(-1.0, line.Data.GetX(1), 9);
        Assert.Equal(0.0, line.Data.GetY(1), 9);

        Assert.True(axes.EqualAspect);
        Assert.Contains(axes.Plots, p => p is SmithGrid);
    }

    // ---- Eye diagram ----

    [Fact]
    public void EyeDiagram_DrawsOneTracePerSymbolStep()
    {
        // length 100, 10 samples/symbol, 2 symbols/trace (20 samples) → starts at 0,10,...,80 = 9 traces.
        var eye = new EyeDiagramPlot(new double[100], samplesPerSymbol: 10, symbolsPerTrace: 2);
        var ctx = new RecordingRenderContext(new Size2D(200, 200));
        ((IDrawable)eye).Render(ctx, State());

        Assert.Equal(9, ctx.PolylineCount);
    }

    [Fact]
    public void EyeDiagram_XBoundsSpanSymbolWindow()
    {
        var eye = new EyeDiagramPlot(new double[40], samplesPerSymbol: 10, symbolsPerTrace: 2);
        Assert.Equal(-1.0, eye.GetXDataBounds().Min);
        Assert.Equal(1.0, eye.GetXDataBounds().Max);
    }

    [Fact]
    public void EyeDiagram_RejectsBadSamplesPerSymbol() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new EyeDiagramPlot(new double[10], 0));

    // ---- Nyquist ----

    [Fact]
    public void AddNyquist_AddsLocusAndCriticalPoint()
    {
        var axes = new AxesModel();
        LinePlot locus = axes.AddNyquist(new[] { 1.0 }, new[] { 1.0, 1.0 }, 0.1, 10, points: 50);

        Assert.Equal(2, axes.Plots.Count);
        Assert.IsType<LinePlot>(axes.Plots[0]);
        Assert.IsType<ScatterPlot>(axes.Plots[1]);
        Assert.Equal(100, locus.Data.Count); // both frequency branches
        Assert.True(axes.EqualAspect);
    }

    // ---- Spectrogram ----

    [Fact]
    public void AddSpectrogram_AddsImageWithFrequencyExtent()
    {
        var axes = new AxesModel();
        ImagePlot image = axes.AddSpectrogram(new double[1024], sampleRate: 1024, windowSize: 256, overlap: 128);

        Assert.Equal(0.0, image.YExtent.Min);
        Assert.Equal(512.0, image.YExtent.Max, 6); // Nyquist
        Assert.False(image.RowZeroAtTop);           // DC at the bottom
        Assert.Contains(axes.Plots, p => ReferenceEquals(p, image));
    }

    // ---- Bode ----

    [Fact]
    public void AddBode_CreatesStackedLogFrequencySubplots()
    {
        var figure = new FigureModel();
        BodeChart bode = figure.AddBode(new[] { 1.0 }, new[] { 1.0, 1.0 }, 0.1, 100, points: 50);

        Assert.Equal(2, figure.Axes.Count);
        Assert.Equal(AxisScaleType.Logarithmic, bode.Magnitude.PrimaryXAxis.Scale);
        Assert.Equal(AxisScaleType.Logarithmic, bode.Phase.PrimaryXAxis.Scale);
        // Magnitude panel sits above the phase panel.
        Assert.True(bode.Magnitude.NormalizedBounds.Y < bode.Phase.NormalizedBounds.Y);
    }
}
