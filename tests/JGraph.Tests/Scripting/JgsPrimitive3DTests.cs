using JGraph.Api;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Objects;
using JGraph.Objects.Annotations;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M45.D: the drawing primitives as script verbs — <c>plot3</c>, <c>scatter3</c>, <c>fill</c>,
/// <c>fill3</c>, <c>patch</c>, <c>line</c>, <c>text</c> and <c>surface</c>.
/// </summary>
[Collection("JG facade")]
public class JgsPrimitive3DTests : IDisposable
{
    private readonly JgsScriptEngine _engine = new();
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public JgsPrimitive3DTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private Task<ScriptRunResult> Run(string code) =>
        _engine.RunAsync(code, new ScriptContext(_output, (_, figure) => _figures.Add(figure), null), default);

    private async Task Succeeds(string code)
    {
        ScriptRunResult result = await Run(code);
        Assert.True(result.Success, result.Message);
    }

    private static T Single<T>()
        where T : PlotObject => Assert.Single(JG.Gca().Plots.OfType<T>());

    // --- plot3 --------------------------------------------------------------------------------

    [Fact]
    public async Task Plot3_DrawsALineAndSwitchesTheAxesTo3D()
    {
        await Succeeds("plot3([0, 1, 2], [0, 1, 0], [0, 1, 2])");

        Line3DPlot plot = Single<Line3DPlot>();
        Assert.True(JG.Gca().Is3D);
        Assert.Equal(3, plot.X.Count);
        Assert.Equal([0, 1, 2], plot.Z);
    }

    [Fact]
    public async Task Plot3_AppliesALineSpec()
    {
        await Succeeds("plot3([0, 1], [0, 1], [0, 1], 'r--o')");

        Line3DPlot plot = Single<Line3DPlot>();
        Assert.Equal(Colors.Red, plot.Color);
        Assert.Equal(DashStyle.Dash, plot.DashStyle);
        Assert.Equal(MarkerType.Circle, plot.Marker);
    }

    /// <summary>A matrix argument plots one line per column, the rule <c>plot</c> already follows.</summary>
    [Fact]
    public async Task Plot3_PlotsOneLinePerColumnOfAMatrix()
    {
        await Succeeds("""
            let t = [0, 1, 2]
            let Z = [[0, 10], [1, 11], [2, 12]]
            plot3(t, t, Z)
            """);

        Assert.Equal(2, JG.Gca().Plots.OfType<Line3DPlot>().Count());
    }

    [Fact]
    public async Task Plot3_TakesNameValueOptions()
    {
        await Succeeds("plot3([0, 1], [0, 1], [0, 1], 'LineWidth', 3, 'DisplayName', 'path')");

        Line3DPlot plot = Single<Line3DPlot>();
        Assert.Equal(3, plot.LineWidth);
        Assert.Equal("path", plot.DisplayName);
    }

    [Fact]
    public async Task Plot3_RejectsMismatchedLengths()
    {
        ScriptRunResult result = await Run("plot3([0, 1, 2], [0, 1], [0, 1, 2])");

        Assert.False(result.Success);
        Assert.Contains("same length", result.Message);
    }

    // --- scatter3 -----------------------------------------------------------------------------

    [Fact]
    public async Task Scatter3_DrawsAMarkerCloud()
    {
        await Succeeds("scatter3([0, 1, 2], [0, 1, 2], [0, 1, 4])");

        Scatter3DPlot plot = Single<Scatter3DPlot>();
        Assert.True(JG.Gca().Is3D);
        Assert.Equal(3, plot.X.Count);
        Assert.Null(plot.ColorData);
    }

    /// <summary>
    /// MATLAB's <c>s</c> is an area in points squared, so the model draws a marker of diameter
    /// sqrt(s) — a scalar 36 is a 6-unit marker.
    /// </summary>
    [Fact]
    public async Task Scatter3_ReadsAScalarSizeAsAnArea()
    {
        await Succeeds("scatter3([0, 1], [0, 1], [0, 1], 36)");

        Assert.Equal(6, Single<Scatter3DPlot>().MarkerSize, 6);
    }

    [Fact]
    public async Task Scatter3_TakesPerPointSizesAndColors()
    {
        await Succeeds("scatter3([0, 1, 2], [0, 1, 2], [0, 1, 2], [4, 16, 36], [10, 20, 30], 'filled')");

        Scatter3DPlot plot = Single<Scatter3DPlot>();
        Assert.True(plot.Filled);
        Assert.Equal([4, 16, 36], plot.SizeData);
        Assert.Equal([10, 20, 30], plot.ColorData);
        Assert.Equal((10, 30), ((JGraph.Rendering.IColorMapped)plot).ColorRange);
    }

    /// <summary>A single color word colors the whole cloud rather than becoming mapped data.</summary>
    [Fact]
    public async Task Scatter3_TakesAColorName()
    {
        await Succeeds("scatter3([0, 1], [0, 1], [0, 1], 20, 'red')");

        Scatter3DPlot plot = Single<Scatter3DPlot>();
        Assert.Equal(Colors.Red, plot.Color);
        Assert.Null(plot.ColorData);
    }

    // --- fill, fill3, patch -------------------------------------------------------------------

    [Fact]
    public async Task Fill_DrawsOneClosedPolygonIn2D()
    {
        await Succeeds("fill([0, 1, 1, 0], [0, 0, 1, 1], 'b')");

        PatchPlot patch = Single<PatchPlot>();
        Assert.False(JG.Gca().Is3D);
        Assert.Single(patch.Faces);
        Assert.Equal(4, patch.Faces[0].Length);
        Assert.Equal(Colors.Blue, patch.FaceColor);
    }

    /// <summary>A matrix fills one polygon per column, which is how several are drawn at once.</summary>
    [Fact]
    public async Task Fill_DrawsOnePolygonPerColumn()
    {
        await Succeeds("""
            let X = [[0, 2], [1, 3], [1, 3]]
            let Y = [[0, 0], [0, 0], [1, 1]]
            fill(X, Y, 'g')
            """);

        PatchPlot patch = Single<PatchPlot>();
        Assert.Equal(2, patch.Faces.Count);
        Assert.Equal(6, patch.X.Count);
    }

    [Fact]
    public async Task Fill3_SwitchesTheAxesTo3D()
    {
        await Succeeds("fill3([0, 1, 1], [0, 0, 1], [0, 1, 2], 'r')");

        PatchPlot patch = Single<PatchPlot>();
        Assert.True(JG.Gca().Is3D);
        Assert.Equal([0, 1, 2], patch.Z);
    }

    /// <summary>
    /// An [r g b] triplet is a color; anything else with a length that matches the faces or the
    /// vertices is colormapped data.
    /// </summary>
    [Fact]
    public async Task Fill_ReadsARgbTripletAsAColor()
    {
        await Succeeds("fill([0, 1, 1, 0], [0, 0, 1, 1], [1, 0, 0])");

        Assert.Equal(Colors.Red, Single<PatchPlot>().FaceColor);
    }

    [Fact]
    public async Task Patch_TakesPerVertexColorData()
    {
        await Succeeds("patch([0, 1, 1, 0], [0, 0, 1, 1], [10, 20, 30, 40])");

        PatchPlot patch = Single<PatchPlot>();
        Assert.Equal([10, 20, 30, 40], patch.ColorData);
        Assert.Null(patch.FaceColor);
    }

    [Fact]
    public async Task Patch_TakesTheFacesAndVerticesForm()
    {
        await Succeeds("""
            let V = [[0, 0, 0], [1, 0, 0], [1, 1, 0], [0, 1, 1]]
            let F = [[1, 2, 3], [1, 3, 4]]
            patch('Faces', F, 'Vertices', V, 'FaceColor', 'c', 'EdgeColor', 'none')
            """);

        PatchPlot patch = Single<PatchPlot>();
        Assert.Equal(4, patch.X.Count);
        Assert.Equal(2, patch.Faces.Count);
        Assert.Equal([0, 2, 3], patch.Faces[1]);   // 1-based in the script, 0-based in the model
        Assert.Equal(Colors.Cyan, patch.FaceColor);
        Assert.Null(patch.EdgeColor);
    }

    [Fact]
    public async Task Patch_RejectsAFaceThatNamesAMissingVertex()
    {
        ScriptRunResult result = await Run("""
            let V = [[0, 0], [1, 0], [1, 1]]
            let F = [[1, 2, 9]]
            patch('Faces', F, 'Vertices', V)
            """);

        Assert.False(result.Success);
        Assert.Contains("only 3", result.Message);
    }

    [Fact]
    public async Task Patch_TakesTheThreeDimensionalCoordinateForm()
    {
        await Succeeds("patch([0, 1, 1], [0, 0, 1], [0, 1, 2], 'm')");

        Assert.True(JG.Gca().Is3D);
        Assert.Equal(Colors.Magenta, Single<PatchPlot>().FaceColor);
    }

    [Fact]
    public async Task Patch_AppliesFaceAlphaAndLineWidth()
    {
        await Succeeds("patch([0, 1, 1], [0, 0, 1], 'r', 'FaceAlpha', 0.25, 'LineWidth', 2)");

        PatchPlot patch = Single<PatchPlot>();
        Assert.Equal(0.25, patch.Opacity, 6);
        Assert.Equal(2, patch.EdgeWidth);
    }

    // --- line, text, surface ------------------------------------------------------------------

    /// <summary>
    /// <c>line</c> is the low-level primitive: it adds to the current axes without the replace that
    /// a fresh <c>plot</c> would do, so a line drawn after a plot leaves that plot alone.
    /// </summary>
    [Fact]
    public async Task Line_AddsToTheCurrentAxesWithoutClearingIt()
    {
        await Succeeds("""
            plot([0, 1], [0, 1])
            line([0, 1], [1, 0], 'Color', 'k', 'LineWidth', 2)
            """);

        Assert.Equal(2, JG.Gca().Plots.OfType<LinePlot>().Count());
        LinePlot added = JG.Gca().Plots.OfType<LinePlot>().Last();
        Assert.Equal(Colors.Black, added.Color);
        Assert.Equal(2, added.LineWidth);
    }

    [Fact]
    public async Task Line_WithAThirdArrayIsA3DLine()
    {
        await Succeeds("line([0, 1], [0, 1], [0, 1])");

        Assert.Single(JG.Gca().Plots.OfType<Line3DPlot>());
    }

    [Fact]
    public async Task Text_PlacesALabelAtAPoint()
    {
        await Succeeds("plot([0, 1], [0, 1])\ntext(0.5, 0.5, 'here')");

        TextAnnotation label = Assert.Single(JG.Gca().Annotations.OfType<TextAnnotation>());
        Assert.Equal("here", label.Text);
        Assert.Equal(0, label.Z);
    }

    [Fact]
    public async Task Text_TakesAHeightAndOptions()
    {
        await Succeeds("""
            surf([[1, 2], [3, 4]])
            text(1, 1, 4, 'peak', 'FontSize', 16, 'Color', 'r', 'HorizontalAlignment', 'center')
            """);

        TextAnnotation label = Assert.Single(JG.Gca().Annotations.OfType<TextAnnotation>());
        Assert.Equal(4, label.Z);
        Assert.Equal(16, label.FontSize);
        Assert.Equal(Colors.Red, label.Color);
        Assert.Equal(HorizontalAlignment.Center, label.HorizontalAlignment);
    }

    [Fact]
    public async Task Surface_DrawsTheSameThingAsSurf()
    {
        await Succeeds("surface([[1, 2], [3, 4]])");

        Assert.Single(JG.Gca().Plots.OfType<SurfacePlot>());
        Assert.True(JG.Gca().Is3D);
    }
}
