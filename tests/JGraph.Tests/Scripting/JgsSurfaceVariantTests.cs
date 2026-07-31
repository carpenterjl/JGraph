using JGraph.Api;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Objects;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M45.E: the surface variants and shape generators as script verbs — <c>surfc</c>, <c>meshz</c>,
/// <c>waterfall</c>, <c>ribbon</c>, <c>contour3</c>, <c>quiver</c>, <c>quiver3</c>, <c>trisurf</c>,
/// <c>trimesh</c>, <c>sphere</c>, <c>cylinder</c> and <c>ellipsoid</c>.
/// </summary>
[Collection("JG facade")]
public class JgsSurfaceVariantTests : IDisposable
{
    private readonly JgsScriptEngine _engine = new();
    private readonly MatlabScriptEngine _matlab = new();
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public JgsSurfaceVariantTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private ScriptContext Context => new(_output, (_, figure) => _figures.Add(figure), null);

    private async Task Succeeds(string code)
    {
        ScriptRunResult result = await _engine.RunAsync(code, Context, default);
        Assert.True(result.Success, result.Message);
    }

    private async Task MatlabSucceeds(string code)
    {
        ScriptRunResult result = await _matlab.RunAsync(code, Context, default);
        Assert.True(result.Success, result.Message);
    }

    private static T Single<T>()
        where T : PlotObject => Assert.Single(JG.Gca().Plots.OfType<T>());

    private const string Grid = "let Z = [[0, 1, 0], [1, 4, 1], [0, 1, 0]]\n";

    // --- surfc / meshz ------------------------------------------------------------------------

    [Fact]
    public async Task Surfc_IsAFilledSurfaceWithFloorContours()
    {
        await Succeeds(Grid + "surfc(Z)");

        SurfacePlot surface = Single<SurfacePlot>();
        Assert.Equal(SurfaceStyle.FilledWithWireframe, surface.Style);
        Assert.True(surface.ShowContourBelow);
        Assert.True(JG.Gca().Is3D);
    }

    /// <summary>
    /// The curtain is one extra ring of vertices, so a 3x3 grid becomes 5x5 and its whole border
    /// sits at the lowest height in the data.
    /// </summary>
    [Fact]
    public async Task Meshz_RingsTheGridWithACurtainToTheFloor()
    {
        await Succeeds(Grid + "meshz(Z)");

        SurfacePlot surface = Single<SurfacePlot>();
        Assert.Equal(SurfaceStyle.Wireframe, surface.Style);
        Assert.Equal(5, surface.Z.GetLength(0));
        Assert.Equal(5, surface.Z.GetLength(1));
        for (int c = 0; c < 5; c++)
        {
            Assert.Equal(0, surface.Z[0, c]);
            Assert.Equal(0, surface.Z[4, c]);
        }

        // The ring repeats the border positions rather than inventing new ones, which is what keeps
        // the curtain vertical instead of flaring outward.
        Assert.Equal(surface.X[0], surface.X[1]);
        Assert.Equal(surface.X[3], surface.X[4]);
    }

    // --- waterfall / ribbon -------------------------------------------------------------------

    /// <summary>One closed polygon per row, each carrying two extra vertices for the drop to the base.</summary>
    [Fact]
    public async Task Waterfall_MakesOneFilledCurvePerRow()
    {
        await Succeeds(Grid + "waterfall(Z)");

        PatchPlot patch = Single<PatchPlot>();
        Assert.Equal(3, patch.Faces.Count);
        Assert.All(patch.Faces, face => Assert.Equal(5, face.Length));
        Assert.NotNull(patch.ColorData);
        Assert.Equal(3, patch.ColorData!.Count);
        Assert.True(JG.Gca().Is3D);
    }

    [Fact]
    public async Task Ribbon_MakesOneStripPerColumnOnASharedColorRange()
    {
        await Succeeds(Grid + "ribbon(Z)");

        var strips = JG.Gca().Plots.OfType<SurfacePlot>().ToList();
        Assert.Equal(3, strips.Count);
        Assert.All(strips, strip =>
        {
            Assert.True(strip.IsParametric);
            Assert.Equal(2, strip.Z.GetLength(1));
            Assert.False(strip.AutoScaleColor);
            Assert.Equal(0, strip.ColorMin);
            Assert.Equal(4, strip.ColorMax);
        });
    }

    [Fact]
    public async Task Ribbon_TakesAnExplicitWidth()
    {
        await Succeeds(Grid + "ribbon([1, 2, 3], Z, 0.2)");

        SurfacePlot strip = JG.Gca().Plots.OfType<SurfacePlot>().First();
        Assert.Equal(0.2, strip.XGrid![0, 1] - strip.XGrid[0, 0], 12);
    }

    // --- contour3 -----------------------------------------------------------------------------

    [Fact]
    public async Task Contour3_AddsAContourAndSwitchesTo3D()
    {
        await Succeeds(Grid + "contour3([0, 1, 2], [0, 1, 2], Z)");

        ContourPlot contour = Single<ContourPlot>();
        Assert.False(contour.Filled);
        Assert.True(JG.Gca().Is3D);
    }

    // --- quiver -------------------------------------------------------------------------------

    [Fact]
    public async Task Quiver_TakesPositionsAndComponents()
    {
        await Succeeds("quiver([0, 1], [0, 1], [1, 0], [0, 1])");

        QuiverPlot plot = Single<QuiverPlot>();
        Assert.Equal([0, 1], plot.X);
        Assert.Equal([0, 1], plot.V);
        Assert.False(JG.Gca().Is3D);
    }

    /// <summary>Components alone place the arrows on the grid their own shape implies.</summary>
    [Fact]
    public async Task Quiver_TakesComponentsAlone()
    {
        await Succeeds("quiver([1, 1, 1], [0, 1, 0])");

        QuiverPlot plot = Single<QuiverPlot>();
        Assert.Equal(3, plot.U.Count);
        Assert.Equal([0, 1, 2], plot.X);
    }

    /// <summary>A trailing zero is MATLAB's "leave the components alone", not a scale of nothing.</summary>
    [Fact]
    public async Task Quiver_ReadsATrailingZeroAsNoScaling()
    {
        await Succeeds("quiver([0, 1], [0, 0], [1, 1], [0, 0], 0)");

        QuiverPlot plot = Single<QuiverPlot>();
        Assert.False(plot.AutoScale);
        Assert.Equal(1, plot.EffectiveScale);
    }

    [Fact]
    public async Task Quiver_AppliesOptionsAndALineSpec()
    {
        await Succeeds("quiver([0, 1], [0, 0], [1, 1], [0, 0], 'r', 'LineWidth', 2, 'MaxHeadSize', 0.5)");

        QuiverPlot plot = Single<QuiverPlot>();
        Assert.Equal(Colors.Red, plot.Color);
        Assert.Equal(2, plot.LineWidth);
        Assert.Equal(0.5, plot.MaxHeadSize);
    }

    [Fact]
    public async Task Quiver3_TakesSixArrays()
    {
        await Succeeds("quiver3([0, 1], [0, 1], [0, 1], [1, 1], [1, 1], [1, 1])");

        QuiverPlot plot = Single<QuiverPlot>();
        Assert.Equal([0, 1], plot.Z);
        Assert.Equal([1, 1], plot.W);
        Assert.True(JG.Gca().Is3D);
    }

    [Fact]
    public async Task Quiver_RejectsAnUnknownOption()
    {
        ScriptRunResult result = await _engine.RunAsync(
            "quiver([0], [0], [1], [1], 'Wobble', 3)", Context, default);

        Assert.False(result.Success);
    }

    // --- trisurf / trimesh --------------------------------------------------------------------

    /// <summary>The triangle table is one-based, as everything <c>delaunay</c> produces is.</summary>
    [Fact]
    public async Task Trisurf_ReadsAOneBasedTriangleTable()
    {
        await Succeeds(
            "let T = [[1, 2, 3], [2, 4, 3]]\n"
            + "trisurf(T, [0, 1, 0, 1], [0, 0, 1, 1], [0, 1, 2, 3])");

        PatchPlot patch = Single<PatchPlot>();
        Assert.Equal(2, patch.Faces.Count);
        Assert.Equal([0, 1, 2], patch.Faces[0]);
        Assert.Equal([1, 3, 2], patch.Faces[1]);
        Assert.True(patch.FaceVisible);
        Assert.Equal([0, 1, 2, 3], patch.ColorData!);
    }

    [Fact]
    public async Task Trimesh_TurnsTheFacesOff()
    {
        await Succeeds(
            "let T = [[1, 2, 3]]\n"
            + "trimesh(T, [0, 1, 0], [0, 0, 1], [0, 1, 2])");

        Assert.False(Single<PatchPlot>().FaceVisible);
    }

    [Fact]
    public async Task Trisurf_RejectsAFractionalVertexNumber()
    {
        ScriptRunResult result = await _engine.RunAsync(
            "let T = [[1, 2, 2.5]]\ntrisurf(T, [0, 1, 0], [0, 0, 1], [0, 1, 2])", Context, default);

        Assert.False(result.Success);
    }

    // --- shape generators ---------------------------------------------------------------------

    /// <summary>In JGS a generator hands back all three grids, the way <c>meshgrid</c> does.</summary>
    [Fact]
    public async Task Sphere_InJgs_ReturnsTheThreeGrids()
    {
        await Succeeds("let [X, Y, Z] = sphere(8)\nlet n = size(X, 0)\nprint(n)");

        Assert.Contains("9", _output.NormalText);
        Assert.Empty(JG.Gca().Plots);
    }

    /// <summary>In MATLAB a graphics verb with no outputs draws, so a bare sphere is a picture.</summary>
    [Fact]
    public async Task Sphere_InMatlab_DrawsWhenNoOutputIsAskedFor()
    {
        await MatlabSucceeds("sphere(8);");

        SurfacePlot surface = Single<SurfacePlot>();
        Assert.True(surface.IsParametric);
        Assert.Equal(9, surface.Z.GetLength(0));
    }

    /// <summary>
    /// And asking for the grids draws nothing — including from the bare name, which auto-calls, so
    /// <c>[x, y, z] = sphere</c> has to reach the multi-output form rather than the drawing one.
    /// </summary>
    [Fact]
    public async Task Sphere_InMatlab_WithOutputsDrawsNothing()
    {
        await MatlabSucceeds("[x, y, z] = sphere;\ndisp(size(x, 1));");

        Assert.Empty(JG.Gca().Plots);
        Assert.Contains("21", _output.NormalText);
    }

    [Fact]
    public async Task Cylinder_InMatlab_TakesAProfileAndFacetCount()
    {
        await MatlabSucceeds("[x, y, z] = cylinder([1 2 3], 8);\ndisp(size(x, 2));");

        Assert.Contains("9", _output.NormalText);
    }

    [Fact]
    public async Task Ellipsoid_InMatlab_TakesItsCentreAndSemiAxes()
    {
        await MatlabSucceeds("ellipsoid(1, 2, 3, 1, 2, 3, 10);");

        SurfacePlot surface = Single<SurfacePlot>();
        Assert.Equal(11, surface.Z.GetLength(0));
        Assert.Equal(0, surface.GetZDataBounds().Min, 9);
        Assert.Equal(6, surface.GetZDataBounds().Max, 9);
    }

    [Fact]
    public async Task Ellipsoid_RejectsAZeroFacetCount()
    {
        ScriptRunResult result = await _engine.RunAsync(
            "ellipsoid(0, 0, 0, 1, 1, 1, 0)", Context, default);

        Assert.False(result.Success);
    }
}
