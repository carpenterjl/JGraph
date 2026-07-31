using JGraph.Api;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M45.B: the color and lighting control verbs — the colormap generators, <c>caxis</c>/<c>clim</c>,
/// <c>brighten</c>, <c>colororder</c>, <c>surfl</c> and <c>surfnorm</c>. These are all
/// <c>kind: function</c> in MATLAB's documentation, which is why the builtin coverage doc never
/// tracked them and the gap stayed invisible until M45.
/// </summary>
[Collection("JG facade")]
public class JgsColorControlTests : IDisposable
{
    private readonly JgsScriptEngine _engine = new();
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public JgsColorControlTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private Task<ScriptRunResult> Run(string code) =>
        _engine.RunAsync(code, new ScriptContext(_output, (_, figure) => _figures.Add(figure), null), default);

    private static async Task Succeeds(Task<ScriptRunResult> run)
    {
        ScriptRunResult result = await run;
        Assert.True(result.Success, result.Message);
    }

    // --- Colormap generators --------------------------------------------------------------------

    [Fact]
    public async Task AGenerator_ReturnsAnMby3TableOfComponents()
    {
        await Succeeds(Run("""
            let m = parula(4)
            print(size(m))
            print(m(0, :))
            """));

        Assert.Contains("[4, 3]", _output.NormalText);

        // The first row is the low end of the map, in components rather than bytes.
        Color low = Colormap.Parula.Sample(0);
        Assert.Contains($"{low.R / 255.0:0.####}", _output.NormalText);
    }

    /// <summary>A bare generator name is the table itself, the way <c>x = eps</c> is a number (M37).</summary>
    [Fact]
    public async Task ABareGeneratorName_IsTheDefaultLengthTable()
    {
        await Succeeds(Run("""
            let g = gray
            print(size(g))
            """));

        Assert.Contains("[256, 3]", _output.NormalText);
    }

    /// <summary>
    /// <c>lines</c> is a discrete palette, so resampling it cycles its seven colors rather than
    /// blending — MATLAB's <c>lines(10)</c> repeats, and rows 1 and 8 are the same color.
    /// </summary>
    [Fact]
    public async Task TheLinesPalette_Cycles()
    {
        await Succeeds(Run("""
            let c = lines(10)
            print(isequal(c(0, :), c(7, :)))
            print(isequal(c(0, :), c(1, :)))
            """));

        Assert.Contains("true", _output.NormalText);
        Assert.Contains("false", _output.NormalText);
    }

    [Fact]
    public async Task ColormapTakesATableAsWellAsAName()
    {
        await Succeeds(Run("""
            surf([[1, 2], [3, 4]])
            colormap([[1, 0, 0], [0, 0, 1]])
            show()
            """));

        var surface = (SurfacePlot)_figures[0].Axes[^1].Plots[0];
        Assert.Equal(Colors.Red, surface.Colormap.Sample(0));
        Assert.Equal(Colors.Blue, surface.Colormap.Sample(1));
    }

    [Fact]
    public async Task AGeneratorFeedsColormapDirectly()
    {
        await Succeeds(Run("""
            surf([[1, 2], [3, 4]])
            colormap(jet(64))
            show()
            """));

        var surface = (SurfacePlot)_figures[0].Axes[^1].Plots[0];
        Assert.Equal(Colormap.Jet.Sample(0), surface.Colormap.Sample(0));
    }

    // --- Color limits ---------------------------------------------------------------------------

    [Fact]
    public async Task CaxisPinsAndReleasesTheColorLimits()
    {
        await Succeeds(Run("""
            surf([[1, 2], [3, 4]])
            caxis([0, 10])
            print(caxis)
            """));

        var surface = (SurfacePlot)JG.Gca().Plots[0];
        Assert.False(surface.AutoScaleColor);
        Assert.Equal(0, surface.ColorMin);
        Assert.Equal(10, surface.ColorMax);
        Assert.Contains("[0, 10]", _output.NormalText);
    }

    [Fact]
    public async Task CaxisAuto_HandsTheLimitsBackToTheData()
    {
        await Succeeds(Run("""
            surf([[1, 2], [3, 4]])
            caxis([0, 10])
            caxis('auto')
            """));

        var surface = (SurfacePlot)JG.Gca().Plots[0];
        Assert.True(surface.AutoScaleColor);
        Assert.Equal((1, 4), surface.ColorRange);
    }

    /// <summary>Two plots sharing one axes share the limits, which is the whole point of pinning them.</summary>
    [Fact]
    public async Task ClimIsTheSameVerbUnderMatlabsNewerName()
    {
        await Succeeds(Run("""
            surf([[1, 2], [3, 4]])
            clim(-5, 5)
            """));

        var surface = (SurfacePlot)JG.Gca().Plots[0];
        Assert.Equal(-5, surface.ColorMin);
        Assert.Equal(5, surface.ColorMax);
    }

    /// <summary><c>manual</c> freezes the limits where the data has them right now.</summary>
    [Fact]
    public async Task CaxisManual_FreezesTheAutomaticLimits()
    {
        await Succeeds(Run("""
            surf([[1, 2], [3, 4]])
            caxis('manual')
            """));

        var surface = (SurfacePlot)JG.Gca().Plots[0];
        Assert.False(surface.AutoScaleColor);
        Assert.Equal(1, surface.ColorMin);
        Assert.Equal(4, surface.ColorMax);
    }

    [Fact]
    public async Task CaxisRejectsBadLimits()
    {
        ScriptRunResult tooMany = await Run("surf([[1, 2], [3, 4]])\ncaxis([1, 2, 3])");
        Assert.False(tooMany.Success);
        Assert.Contains("two limits", tooMany.Message);

        ScriptRunResult inverted = await Run("surf([[1, 2], [3, 4]])\ncaxis([5, 5])");
        Assert.False(inverted.Success);
        Assert.Contains("increasing", inverted.Message);
    }

    // --- brighten ------------------------------------------------------------------------------

    /// <summary>
    /// Brightening raises every component toward 1 and darkening lowers it, with the ends of the map
    /// fixed — black and white are the two fixed points of a power law.
    /// </summary>
    [Fact]
    public async Task BrightenLightensAndDarkensTheCurrentMap()
    {
        await Succeeds(Run("""
            surf([[1, 2], [3, 4]])
            colormap('gray')
            brighten(0.5)
            """));

        var surface = (SurfacePlot)JG.Gca().Plots[0];
        Color mid = surface.Colormap.Sample(0.5);
        Assert.True(mid.R > Colormap.Grayscale.Sample(0.5).R, $"expected a lighter mid gray, got {mid}");
        Assert.Equal(Colors.Black, surface.Colormap.Sample(0));
        Assert.Equal(Colors.White, surface.Colormap.Sample(1));
    }

    /// <summary>
    /// Grayscale is the case that proves brightening cannot work on the stops: black and white are
    /// both fixed points of a power law, so a two-stop gray would come back unchanged while every
    /// value between the ends should have moved.
    /// </summary>
    [Fact]
    public void BrightenReshapesTheTableRatherThanTheStops()
    {
        Color plain = Colormap.Grayscale.Sample(0.5);
        Color lighter = Colormap.Grayscale.Brighten(0.5).Sample(0.5);
        Color darker = Colormap.Grayscale.Brighten(-0.5).Sample(0.5);

        Assert.True(lighter.R > plain.R, $"{lighter} should be lighter than {plain}");
        Assert.True(darker.R < plain.R, $"{darker} should be darker than {plain}");

        // The two ends stay put whichever way it goes.
        Assert.Equal(Colors.Black, Colormap.Grayscale.Brighten(0.5).Sample(0));
        Assert.Equal(Colors.White, Colormap.Grayscale.Brighten(-0.5).Sample(1));
    }

    /// <summary>A discrete palette has no in-between, so there the stops are the table.</summary>
    [Fact]
    public void BrightenLiftsEveryColorOfADiscretePalette()
    {
        Colormap lighter = Colormap.Lines.Brighten(0.5);

        Assert.True(lighter.Discrete);
        Assert.Equal(Colormap.Lines.Stops.Count, lighter.Stops.Count);
        Assert.True(lighter.Stops[0].R >= Colormap.Lines.Stops[0].R);
    }

    // --- colororder ----------------------------------------------------------------------------

    [Fact]
    public async Task ColorOrderSetsAndReportsTheAxesCycle()
    {
        await Succeeds(Run("""
            plot([1, 2], [1, 2])
            colororder([[1, 0, 0], [0, 0, 1]])
            print(size(colororder))
            """));

        IReadOnlyList<Color>? order = JG.Gca().ColorOrder;
        Assert.NotNull(order);
        Assert.Equal([Colors.Red, Colors.Blue], order);
        Assert.Contains("[2, 3]", _output.NormalText);
    }

    [Fact]
    public async Task ColorOrderTakesAColorName()
    {
        await Succeeds(Run("""
            plot([1, 2], [1, 2])
            colororder('red')
            """));

        Assert.Equal([Colors.Red], JG.Gca().ColorOrder);
    }

    /// <summary>The cell form is MATLAB's own, so it needs the MATLAB dialect to parse the braces.</summary>
    [Fact]
    public async Task ColorOrderTakesACellOfColorNames()
    {
        ScriptRunResult result = await new MatlabScriptEngine().RunAsync(
            """
            plot([1, 2], [1, 2])
            colororder({'red', 'k'})
            """,
            new ScriptContext(_output, (_, figure) => _figures.Add(figure), null),
            default);

        Assert.True(result.Success, result.Message);
        Assert.Equal([Colors.Red, Colors.Black], JG.Gca().ColorOrder);
    }

    /// <summary>
    /// The order has to reach the renderer, which otherwise takes its palette from the theme. It is
    /// per axes rather than per figure, so one panel of a subplot grid can differ from the next.
    /// </summary>
    [Fact]
    public void ColorOrder_OverridesTheThemePalette()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.AddLine([0.0, 1.0], [0.0, 1.0]);
        axes.AddLine([0.0, 1.0], [1.0, 0.0]);
        axes.ColorOrder = [Colors.Red, Colors.Blue];

        var context = new JGraph.Tests.TestDoubles.RecordingRenderContext(new Size2D(640, 480));
        new JGraph.Rendering.FigureRenderer().Render(figure, context);

        Assert.Contains(Colors.Red, context.PolylineColors);
        Assert.Contains(Colors.Blue, context.PolylineColors);
    }

    /// <summary>A saved figure keeps its color order; one saved before M45 has none and follows the theme.</summary>
    [Fact]
    public void ColorOrder_RoundTrips()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.AddLine([0.0, 1.0], [0.0, 1.0]);
        axes.ColorOrder = [Colors.Red, Colors.Blue];

        FigureModel loaded = JGraph.Serialization.GraphFormat.Deserialize(
            JGraph.Serialization.GraphFormat.Serialize(figure));

        Assert.Equal([Colors.Red, Colors.Blue], loaded.Axes[0].ColorOrder);

        axes.ColorOrder = null;
        FigureModel plain = JGraph.Serialization.GraphFormat.Deserialize(
            JGraph.Serialization.GraphFormat.Serialize(figure));
        Assert.Null(plain.Axes[0].ColorOrder);
    }

    [Fact]
    public async Task ColorOrderRejectsAMatrixThatIsNotRgb()
    {
        ScriptRunResult result = await Run("plot([1, 2], [1, 2])\ncolororder([[1, 0], [0, 1]])");

        Assert.False(result.Success);
        Assert.Contains("three columns", result.Message);
    }

    // --- surfl and surfnorm ---------------------------------------------------------------------

    [Fact]
    public async Task SurflLightsTheSurfaceFromBesideTheCamera()
    {
        await Succeeds(Run("""
            surfl([[1, 2], [3, 4]])
            show()
            """));

        AxesModel axes = _figures[0].Axes[^1];
        var surface = (SurfacePlot)axes.Plots[0];
        Assert.Equal(SurfaceLighting.Gouraud, surface.FaceLighting);
        LightModel light = Assert.Single(axes.Lights);
        Assert.True(light.FollowsCamera);
    }

    /// <summary>A level sheet has the normal straight up everywhere, whatever its spacing.</summary>
    [Fact]
    public async Task SurfnormOfALevelSheet_PointsStraightUp()
    {
        await Succeeds(Run("""
            let [nx, ny, nz] = surfnorm([[5, 5, 5], [5, 5, 5], [5, 5, 5]])
            print(nx(1, 1), ny(1, 1), nz(1, 1))
            """));

        Assert.Contains("0 0 1", _output.NormalText);
    }

    /// <summary>
    /// A plane sloping one unit in z per unit in x has the normal <c>(-1, 0, 1)/sqrt(2)</c>, which is
    /// the sign convention every surface normal here follows: away from the surface, upward.
    /// </summary>
    [Fact]
    public async Task SurfnormOfARamp_LeansAgainstTheSlope()
    {
        await Succeeds(Run("""
            let [nx, ny, nz] = surfnorm([0, 1, 2], [0, 1, 2], [[0, 1, 2], [0, 1, 2], [0, 1, 2]])
            print(round(nx(1, 1) * 1000) / 1000)
            print(round(ny(1, 1) * 1000) / 1000)
            print(round(nz(1, 1) * 1000) / 1000)
            """));

        Assert.Contains("-0.707", _output.NormalText);
        Assert.Contains("0.707", _output.NormalText);
    }

    [Fact]
    public async Task SurfnormRejectsMismatchedGrids()
    {
        ScriptRunResult result = await Run("surfnorm([0, 1], [0, 1, 2], [[0, 1], [2, 3]])");

        Assert.False(result.Success);
        Assert.Contains("same size", result.Message);
    }
}
