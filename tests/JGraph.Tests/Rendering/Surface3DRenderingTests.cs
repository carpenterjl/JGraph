using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;
using JGraph.Rendering;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Rendering;

/// <summary>M20b: headless rendering of surface/mesh/contour plots, the 3D axes box, and the colorbar.</summary>
public class Surface3DRenderingTests
{
    private static (double[] X, double[] Y, double[,] Z) Grid(int n = 11)
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

    private static RecordingRenderContext Render(FigureModel figure)
    {
        var context = new RecordingRenderContext(new Size2D(640, 480));
        new FigureRenderer().Render(figure, context);
        return context;
    }

    [Fact]
    public void Surf_RendersOneQuadPerCell_AndTheAxesBox()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        (double[] x, double[] y, double[,] z) = Grid(11);
        axes.AddSurface(x, y, z);

        RecordingRenderContext context = Render(figure);

        Assert.Equal(100, context.PolygonCount); // 10x10 cells
        Assert.True(context.LineCount >= 8, $"expected box/grid lines, got {context.LineCount}");
        Assert.True(context.TextCount > 0, "expected tick labels");
        Assert.Equal(0, context.ClipDepth); // balanced push/pop
    }

    [Fact]
    public void AddSurface_Switches_AxesTo3D()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        (double[] x, double[] y, double[,] z) = Grid(3);
        axes.AddSurface(x, y, z);

        Assert.True(axes.Is3D);
    }

    [Fact]
    public void Mesh_WithContourBelow_AddsFloorLines()
    {
        (double[] x, double[] y, double[,] z) = Grid(11);

        var figure = new FigureModel();
        figure.AddAxes().AddSurface(x, y, z, SurfaceStyle.Wireframe);
        RecordingRenderContext plain = Render(figure);

        var figure2 = new FigureModel();
        SurfacePlot surface = figure2.AddAxes().AddSurface(x, y, z, SurfaceStyle.Wireframe);
        surface.ShowContourBelow = true;
        RecordingRenderContext withContours = Render(figure2);

        Assert.True(withContours.LineCount > plain.LineCount, "meshc should draw extra floor contour lines");
    }

    [Fact]
    public void SurfaceWithNaN_SkipsAffectedCells()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        (double[] x, double[] y, double[,] z) = Grid(11);
        z[5, 5] = double.NaN; // kills the 4 cells sharing this vertex
        axes.AddSurface(x, y, z);

        RecordingRenderContext context = Render(figure);

        Assert.Equal(96, context.PolygonCount);
    }

    [Fact]
    public void ZAutoscale_PicksUpSurfaceHeights()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        (double[] x, double[] y, double[,] z) = Grid(5);
        axes.AddSurface(x, y, z);
        axes.AutoScalePadding = 0;

        figure.RecomputeDataBounds();

        Assert.True(axes.ZAxis.Range.Min < 0);
        Assert.True(axes.ZAxis.Range.Max > 0);
    }

    [Fact]
    public void ContourLines_Render_InA2DAxes()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        (double[] x, double[] y, double[,] z) = Grid(21);
        axes.AddContour(x, y, z);

        RecordingRenderContext context = Render(figure);

        Assert.False(axes.Is3D);
        Assert.True(context.LineCount > 20, $"expected contour segments, got {context.LineCount}");
    }

    [Fact]
    public void ContourFilled_RendersBandPolygons()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        (double[] x, double[] y, double[,] z) = Grid(21);
        axes.AddContour(x, y, z, filled: true);

        RecordingRenderContext context = Render(figure);

        Assert.True(context.PolygonCount > 100, $"expected filled band polygons, got {context.PolygonCount}");
    }

    [Fact]
    public void Colorbar_DrawsGradientStrip_AndReservesRoom()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        (double[] x, double[] y, double[,] z) = Grid(11);
        axes.AddSurface(x, y, z);
        axes.Colorbar.Visible = true;

        RecordingRenderContext context = Render(figure);

        Assert.Equal(1, context.ImageCount); // the 1x256 gradient
        Assert.True(context.LastImageDestination.Width > 0);
        Assert.True(context.LastImageDestination.Right <= 640);
    }

    [Fact]
    public void Colorbar_WithoutColorMappedPlot_DrawsNothing()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.AddLine([1.0, 2, 3], [1.0, 4, 9]);
        axes.Colorbar.Visible = true;

        RecordingRenderContext context = Render(figure);

        Assert.Equal(0, context.ImageCount);
    }

    [Fact]
    public void PainterOrder_IsDeterministic()
    {
        // Rendering the same figure twice issues identical call counts.
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        (double[] x, double[] y, double[,] z) = Grid(15);
        axes.AddSurface(x, y, z);

        RecordingRenderContext first = Render(figure);
        RecordingRenderContext second = Render(figure);

        Assert.Equal(first.PolygonCount, second.PolygonCount);
        Assert.Equal(first.LineCount, second.LineCount);
        Assert.Equal(first.TextCount, second.TextCount);
    }
}
