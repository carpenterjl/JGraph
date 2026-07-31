using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;
using JGraph.Rendering;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Rendering;

/// <summary>
/// M45.E: the surface variants and the arrow field. Each is checked for the one thing that makes it
/// more than the plot it is built on — a curtain that reaches the floor, rows that occlude each
/// other, contours that leave the floor, a head on every arrow.
/// </summary>
public class SurfaceVariantRenderingTests
{
    private static RecordingRenderContext Render(FigureModel figure)
    {
        var context = new RecordingRenderContext(new Size2D(640, 480));
        new FigureRenderer().Render(figure, context);
        return context;
    }

    private static double[,] Bump() => new double[,]
    {
        { 0, 1, 0 },
        { 1, 4, 1 },
        { 0, 1, 0 },
    };

    // --- contour3 -----------------------------------------------------------------------------

    /// <summary>
    /// The whole of contour3: the same traced curves, but each riding at the height of its own
    /// level rather than lying flat. Two levels of a single bump land at two different screen
    /// heights, which a floor contour could not do.
    /// </summary>
    [Fact]
    public void Contour3_DrawsEachLevelAtItsOwnHeight()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.AddContour([0, 1, 2], [0, 1, 2], Bump(), [1.0, 3.0]);
        axes.Is3D = true;

        RecordingRenderContext context = Render(figure);

        Assert.Equal(2, context.PathBatchCount);
        Assert.NotEqual(context.PathMeanY[0], context.PathMeanY[1], 3);
    }

    /// <summary>And a contour in a plain 2D axes still draws flat, one path per level.</summary>
    [Fact]
    public void Contour_In2D_StillDrawsOnePathPerLevel()
    {
        var figure = new FigureModel();
        figure.AddAxes().AddContour([0, 1, 2], [0, 1, 2], Bump(), [1.0, 3.0]);

        RecordingRenderContext context = Render(figure);

        Assert.Equal(2, context.PathBatchCount);
    }

    [Fact]
    public void Contour_ReportsItsFieldExtentAsTheZRange()
    {
        var contour = new ContourPlot([0, 1, 2], [0, 1, 2], Bump());

        Assert.Equal(0, contour.GetZDataBounds().Min);
        Assert.Equal(4, contour.GetZDataBounds().Max);
    }

    // --- trisurf / trimesh --------------------------------------------------------------------

    /// <summary>
    /// A triangulated mesh is a triangulated surface with the fill taken away and the color moved
    /// onto the outline, so every face is still stroked and none of them is filled.
    /// </summary>
    [Fact]
    public void TriMesh_StrokesEachFaceInItsOwnColorAndFillsNone()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        PatchPlot patch = axes.AddPatch(
            [0, 1, 0, 1], [0, 0, 1, 1], [0, 1, 2, 3], [[0, 1, 2], [1, 3, 2]]);
        patch.ColorData = new double[] { 0, 1, 2, 3 };
        patch.Colormap = Colormap.Grayscale;
        patch.FaceVisible = false;
        axes.Is3D = true;

        RecordingRenderContext context = Render(figure);

        Assert.Equal(2, context.PolygonCount);
        Assert.All(context.PolygonFills, fill => Assert.Null(fill));
        Assert.Equal(2, context.PolygonStrokes.Select(s => s?.Color).Distinct().Count());
    }

    /// <summary>A filled triangulation is the ordinary path: one colored fill per face.</summary>
    [Fact]
    public void TriSurf_FillsEachFace()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        PatchPlot patch = axes.AddPatch(
            [0, 1, 0, 1], [0, 0, 1, 1], [0, 1, 2, 3], [[0, 1, 2], [1, 3, 2]]);
        patch.ColorData = new double[] { 0, 1, 2, 3 };
        axes.Is3D = true;

        RecordingRenderContext context = Render(figure);

        Assert.Equal(2, context.PolygonCount);
        Assert.All(context.PolygonFills, fill => Assert.NotNull(fill));
    }

    // --- quiver -------------------------------------------------------------------------------

    /// <summary>Each arrow is a shaft plus a barb, so two sub-paths, all in one batched draw.</summary>
    [Fact]
    public void Quiver_DrawsAShaftAndAHeadPerArrowInOneBatch()
    {
        var figure = new FigureModel();
        figure.AddAxes().AddQuiver([0, 1, 2], [0, 0, 0], [1, 1, 1], [0, 1, 0]);

        RecordingRenderContext context = Render(figure);

        Assert.Equal(1, context.PathBatchCount);
        Assert.Equal(6, context.TotalSubpaths);
    }

    [Fact]
    public void Quiver_WithoutHeads_DrawsBareShafts()
    {
        var figure = new FigureModel();
        QuiverPlot plot = figure.AddAxes().AddQuiver([0, 1], [0, 0], [1, 1], [0, 1]);
        plot.ShowArrowHead = false;

        RecordingRenderContext context = Render(figure);

        Assert.Equal(2, context.TotalSubpaths);
    }

    /// <summary>An arrow whose tail or components are not finite is skipped, not drawn at the origin.</summary>
    [Fact]
    public void Quiver_SkipsNonFiniteArrows()
    {
        var figure = new FigureModel();
        figure.AddAxes().AddQuiver([0, 1, 2], [0, double.NaN, 0], [1, 1, 1], [0, 1, 0]);

        RecordingRenderContext context = Render(figure);

        Assert.Equal(4, context.TotalSubpaths);
    }

    /// <summary>
    /// Auto-scaling fits the longest arrow into about one grid step, so a field whose components
    /// dwarf its spacing is shrunk rather than drawn across the whole axes.
    /// </summary>
    [Fact]
    public void Quiver_AutoScalesTheLongestArrowToAboutOneStep()
    {
        var plot = new QuiverPlot([0, 1, 2, 3], [0, 0, 0, 0], [100, 100, 100, 100], [0, 0, 0, 0]);

        Assert.True(plot.EffectiveScale < 0.02, $"scale was {plot.EffectiveScale}");
        Assert.Equal(3 + (100 * plot.EffectiveScale), plot.GetXDataBounds().Max, 9);
    }

    [Fact]
    public void Quiver_WithAutoScaleOff_UsesTheScaleAsGiven()
    {
        var plot = new QuiverPlot([0, 1], [0, 0], [2, 2], [0, 0]) { AutoScale = false, Scale = 3 };

        Assert.Equal(3, plot.EffectiveScale);
        Assert.Equal(7, plot.GetXDataBounds().Max);
    }

    [Fact]
    public void Quiver3_DrawsInA3DAxesAndReportsAZRange()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        QuiverPlot plot = axes.AddQuiver3([0, 1], [0, 1], [0, 1], [1, 1], [1, 1], [1, 1]);

        RecordingRenderContext context = Render(figure);

        Assert.True(axes.Is3D);
        Assert.Equal(2, context.TotalSubpaths / 2);
        Assert.True(plot.GetZDataBounds().Max > 1);
    }

    [Fact]
    public void Quiver_RejectsComponentsOfTheWrongLength() =>
        Assert.Throws<ArgumentException>(() => new QuiverPlot([0, 1], [0, 1], [1], [1]));
}
