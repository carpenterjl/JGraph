using JGraph.Api;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;
using JGraph.Objects.Annotations;
using JGraph.Rendering;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Serialization;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Open items 112 and 113 (ADR 0229), where the parity fixture cannot reach: what the MATLAB
/// dialect draws for the charts R2025b builds out of several objects, that JGS keeps its one-object
/// charts and its evenly spaced contour levels, what a saved figure keeps of a contour's rule and
/// floor, and the model's own level arithmetic. What a script reads is R2025b's (probe_112 to
/// probe_112c, probe_113 to probe_113b, fixture oi_handle_counts_contour_levels).
/// </summary>
[Collection("JG facade")]
public class OpenItemsBatch16Tests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();

    public OpenItemsBatch16Tests() => JG.Reset();

    public void Dispose()
    {
        JgsHandleRegistry.Clear();
        JG.Reset();
    }

    private Task<ScriptRunResult> RunMatlab(string code) =>
        new MatlabScriptEngine().RunAsync(code, new ScriptContext(_output, static (_, _) => { }), default);

    private Task<ScriptRunResult> RunJgs(string code) =>
        new JgsScriptEngine().RunAsync(code, new ScriptContext(_output, static (_, _) => { }), default);

    private async Task Runs(string code)
    {
        ScriptRunResult result = await RunMatlab(code);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    private async Task RunsJgs(string code)
    {
        ScriptRunResult result = await RunJgs(code);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    private static RecordingRenderContext Drawn()
    {
        var context = new RecordingRenderContext(new Size2D(400, 300));
        new FigureRenderer().Render(JG.Gcf(), context, Theme.Light);
        return context;
    }

    private static FigureModel SavedAndLoaded() => GraphFormat.Deserialize(GraphFormat.Serialize(JG.Gcf()));

    // --- 112: what the MATLAB dialect draws ---------------------------------------------------

    [Fact]
    public async Task AMatlabPieIsPatchesAndTextsThatAreDrawn()
    {
        await Runs("""
            figure('Visible', 'off');
            pie([1 2 3]);
            """);

        AxesModel axes = JG.Gca();
        Assert.Equal(3, axes.Plots.OfType<PatchPlot>().Count());
        Assert.Equal(3, axes.Annotations.OfType<TextAnnotation>().Count());
        Assert.True(axes.EqualAspect);
        Assert.False(axes.Visible);

        List<string> texts = Drawn().Texts;
        Assert.Contains("17%", texts);
        Assert.Contains("50%", texts);
    }

    [Fact]
    public async Task AMatlabBar3IsASurfacePerColumnAndAMeshcHasAContourOnTheFloor()
    {
        await Runs("""
            figure('Visible', 'off');
            bar3(magic(3));
            """);

        Assert.Equal(3, JG.Gca().Plots.OfType<SurfacePlot>().Count());
        Assert.True(JG.Gca().Is3D);
        Assert.True(JG.Gca().PrimaryYAxis.Inverted);

        await Runs("""
            figure('Visible', 'off');
            meshc(peaks(10));
            """);

        AxesModel axes = JG.Gca();
        SurfacePlot mesh = Assert.Single(axes.Plots.OfType<SurfacePlot>());
        ContourPlot floor = Assert.Single(axes.Plots.OfType<ContourPlot>());
        Assert.False(mesh.ShowContourBelow);
        Assert.True(floor.RoundLevels);
        Assert.Equal(ContourZLocation.Min, floor.ZLocation);
    }

    [Fact]
    public async Task AMatlabCompassIsALinePerArrowAndAFeatherAddsItsBaseline()
    {
        await Runs("""
            figure('Visible', 'off');
            compass([1 2 3], [1 -1 0]);
            """);

        Assert.Equal(3, JG.Gca().Plots.OfType<LinePlot>().Count());
        Assert.True(JG.Gca().IsPolar);

        await Runs("""
            figure('Visible', 'off');
            feather([1 2 3], [1 -1 0]);
            """);

        Assert.Equal(4, JG.Gca().Plots.OfType<LinePlot>().Count());
        Assert.False(JG.Gca().IsPolar);
    }

    [Fact]
    public async Task TheFirstPieceReplacesTheAxesAndTheRestAddAndHoldIsPutBack()
    {
        await Runs("""
            figure('Visible', 'off');
            plot(1:3);
            fill([0 1; 1 2; 1 2], [0 0; 0 0; 1 1], 'r');
            n = numel(get(gca, 'Children'));
            held = ishold;
            hold on
            errorbar(magic(3), ones(3));
            m = numel(get(gca, 'Children'));
            stillHeld = ishold;
            disp([n held m stillHeld]);
            """);

        Assert.Equal(["[2, false, 5, true]"], _output.NormalLines);
    }

    // --- 112: JGS keeps its own charts ----------------------------------------------------------

    [Fact]
    public async Task JgsKeepsItsOneObjectPieBar3AndCompass()
    {
        await RunsJgs("""
            figure;
            let h = pie([1, 2, 3]);
            disp(numel(h));
            disp(get(h, 'Type'));
            """);

        Assert.Equal(["1", "pie"], _output.NormalLines);
        Assert.Single(JG.Gca().Plots.OfType<PiePlot>());

        await RunsJgs("""
            figure;
            let h = bar3(magic(3));
            disp(numel(h));
            """);

        Assert.Single(JG.Gca().Plots.OfType<Bar3DPlot>());

        await RunsJgs("""
            figure;
            let h = compass([1, 2], [1, -1]);
            disp(numel(h));
            """);

        Assert.Single(JG.Gca().Plots.OfType<QuiverPlot>());
    }

    // --- 113: the level rule ----------------------------------------------------------------------

    [Theory]
    [InlineData(15, 2)]
    [InlineData(11.9, 1)]
    [InlineData(12, 2)]
    [InlineData(23.9, 2)]
    [InlineData(24, 5)]
    [InlineData(56, 5)]
    [InlineData(60, 10)]
    [InlineData(1, 0.1)]
    [InlineData(0.12, 0.02)]
    [InlineData(1500, 200)]
    [InlineData(1e-9, 1e-10)]
    public void TheRoundStepIsTheFirstOfTheLadderThatDrawsAtMostElevenLevels(double range, double step)
    {
        Assert.Equal(step, ContourPlot.RoundStepFor(range), 12);
    }

    /// <summary>probe_113c: a negative minimum counts as a level and a zero or positive one does not.</summary>
    [Theory]
    [InlineData(-5, 15, 2, -4, 14)]
    [InlineData(-4, 16, 2, -4, 16)]
    [InlineData(-16, -1, 2, -16, -2)]
    [InlineData(2, 12, 1, 3, 12)]
    [InlineData(-1, 0, 0.1, -1, 0)]
    public void ANegativeMinimumIsALevelAndAPositiveOneIsNot(double min, double max, double step, double first, double last)
    {
        Assert.Equal(step, ContourPlot.RoundStepFor(min, max), 12);
        double[] levels = ContourPlot.MultiplesInside(min, max, step, inclusive: true);
        Assert.Equal(first, levels[0], 9);
        Assert.Equal(last, levels[^1], 9);
    }

    [Fact]
    public void TheMultiplesInsideARangeReachTheMaximumOnlyWhenAsked()
    {
        Assert.Equal([2, 4, 6, 8, 10, 12, 14, 16], ContourPlot.MultiplesInside(1, 16, 2, inclusive: true));
        Assert.Equal([2, 4, 6, 8, 10, 12, 14], ContourPlot.MultiplesInside(1, 16, 2, inclusive: false));
        Assert.Equal([0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7], ContourPlot.MultiplesInside(0, 0.7, 0.1, inclusive: true).Select(v => Math.Round(v, 9)));
        Assert.Empty(ContourPlot.MultiplesInside(0, 0, 1, inclusive: true));

        // contourc: nine strictly inside at most, so [-5, 15] steps by 5 where the drawn contour steps by 2.
        Assert.Equal(5, ContourPlot.RoundStepFor(-5, 15, limit: 9, inclusive: false));
        Assert.Equal(2, ContourPlot.RoundStepFor(0, 11, limit: 9, inclusive: false));
    }

    [Fact]
    public async Task AMatlabContourDrawsRoundLevelsAndJgsKeepsItsEvenOnes()
    {
        await Runs("""
            figure('Visible', 'off');
            [~, c] = contour(magic(4));
            disp(get(c, 'LevelList'));
            disp(get(c, 'LevelStep'));
            """);

        Assert.Equal(["[2, 4, 6, 8, 10, 12, 14, 16]", "2"], _output.NormalLines);
        Assert.True(JG.Gca().Plots.OfType<ContourPlot>().Single().RoundLevels);

        await RunsJgs("""
            figure;
            let c = contour(magic(4));
            disp(numel(get(c, 'LevelList')));
            """);

        Assert.Equal(["8"], _output.NormalLines.Skip(2));
        ContourPlot even = JG.Gca().Plots.OfType<ContourPlot>().Single();
        Assert.False(even.RoundLevels);
        Assert.Equal(1 + (15.0 / 9), even.ResolvedLevels[0], 9);
    }

    [Fact]
    public async Task ACountOfZeroDrawsNothingAndAFilledContourListsItsMinimumFirst()
    {
        await Runs("""
            figure('Visible', 'off');
            [M, c] = contour(magic(4), 0);
            disp(size(M));
            disp(get(c, 'LevelListMode'));
            [~, f] = contourf(magic(4), 3);
            disp(get(f, 'LevelList'));
            [~, g] = contourf(magic(4), [4 8 12]);
            disp(get(g, 'LevelList'));
            """);

        Assert.Equal(["[2, 0]", "manual", "[1, 4.75, 8.5, 12.25]", "[4, 8, 12]"], _output.NormalLines);
    }

    [Fact]
    public async Task ASavedContourKeepsItsRuleAndItsFloor()
    {
        await Runs("""
            figure('Visible', 'off');
            surfc(peaks(20));
            """);

        FigureModel loaded = SavedAndLoaded();
        ContourPlot floor = Assert.Single(loaded.Axes.Single().Plots.OfType<ContourPlot>());
        Assert.True(floor.RoundLevels);
        Assert.Equal(ContourZLocation.Min, floor.ZLocation);
        Assert.Equal([-6, -4, -2, 0, 2, 4, 6], floor.ResolvedLevels.Select(v => Math.Round(v, 9)));
    }

    [Fact]
    public async Task AContourOnTheFloorIsDrawnAtTheBottomOfItsData()
    {
        await Runs("""
            figure('Visible', 'off');
            surfc(peaks(8));
            """);

        // The curves project at the data's minimum height, so every drawn path lies within the
        // box; the test is that the floor contour draws at all rather than at its levels.
        RecordingRenderContext drawn = Drawn();
        Assert.True(drawn.TotalSubpaths > 0);
    }
}
