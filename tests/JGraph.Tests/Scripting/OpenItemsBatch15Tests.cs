using JGraph.Api;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;
using JGraph.Rendering;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Serialization;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Open items 109 to 111 (ADR 0228), where the parity fixture cannot reach: what a MATLAB legend and
/// a yyaxis side draw, what a saved figure keeps of the names, orders and bands, that JGS keeps its
/// own verbs, and that an object's own DeleteFcn still reads it. What a script reads is R2025b's
/// (probe_109 to probe_111b, fixture oi_get_legend_handles_sides).
/// </summary>
[Collection("JG facade")]
public class OpenItemsBatch15Tests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();

    public OpenItemsBatch15Tests() => JG.Reset();

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

    // --- 109: the legend -----------------------------------------------------------------------

    [Fact]
    public async Task AMatlabLegendDrawsItsUnnamedSeriesAsData()
    {
        await Runs("""
            figure('Visible', 'off');
            plot(1:3);
            hold on
            plot(2:4, 'DisplayName', 'n');
            plot(3:5);
            legend;
            """);

        List<string> texts = Drawn().Texts;
        Assert.Contains("data1", texts);
        Assert.Contains("n", texts);
        Assert.Contains("data2", texts);
    }

    [Theory]
    [InlineData("legend hide", false)]
    [InlineData("legend hide; legend show", true)]
    [InlineData("legend hide; legend toggle", true)]
    [InlineData("legend off", false)]
    public async Task AHiddenOrDeletedLegendIsNotDrawn(string then, bool drawn)
    {
        await Runs($"""
            figure('Visible', 'off');
            plot(1:3);
            legend;
            {then}
            """);

        Assert.Equal(drawn, Drawn().Texts.Contains("data1"));
        Assert.Equal(then == "legend off" ? string.Empty : "data1", JG.Gca().Plots[0].DisplayName);
    }

    [Fact]
    public async Task ALegendsNamesAndHidingAreSaved()
    {
        await Runs("""
            figure('Visible', 'off');
            hold on
            p = plot(1:3);
            q = plot(2:4);
            legend;
            legend hide
            """);

        FigureModel back = SavedAndLoaded();
        AxesModel axes = back.Axes[0];
        Assert.True(axes.Legend.Hidden);
        Assert.True(axes.Legend.NamesUnnamed);
        Assert.Equal("data1", axes.Plots[0].DisplayName);
        Assert.False(axes.Plots[0].DisplayNameIsManual);

        // A loaded auto name is still the legend's to renumber.
        axes.Plots[0].DisplayName = "x";
        axes.Legend.NameUnnamedRows();
        Assert.Equal("data1", axes.Plots[1].DisplayName);
    }

    [Fact]
    public void ANameInAnOlderDocumentWasGiven()
    {
        FigureModel figure = JG.Figure();
        LinePlot named = JG.Plot([1.0, 2], [1.0, 2]);
        JG.Hold(true);
        LinePlot unnamed = JG.Plot([1.0, 2], [2.0, 3]);
        named.RestoreDisplayName("given", manual: true);

        // Older files carry no mode: a name there was given, an empty one was not.
        string json = System.Text.RegularExpressions.Regex.Replace(
            GraphFormat.Serialize(figure), "\"displayNameManual\":\\s*(true|false),?", string.Empty);
        FigureModel back = GraphFormat.Deserialize(json);
        Assert.True(back.Axes[0].Plots[0].DisplayNameIsManual);
        Assert.False(back.Axes[0].Plots[1].DisplayNameIsManual);
        Assert.Equal(string.Empty, unnamed.DisplayName);
    }

    [Fact]
    public async Task JgsKeepsItsOwnLegend()
    {
        // JGS reads every word as a name and never names an unnamed series (its freeze).
        await RunsJgs("""
            figure();
            plot([1, 2, 3]);
            hold("on");
            plot([2, 3, 4]);
            legend("first");
            """);

        AxesModel axes = JG.Gca();
        Assert.Equal("first", axes.Plots[0].DisplayName);
        Assert.Equal(string.Empty, axes.Plots[1].DisplayName);
        Assert.False(axes.Legend.NamesUnnamed);
    }

    // --- 110: low-level verbs and replaced handles ---------------------------------------------

    [Fact]
    public async Task JgsPatchStillReplaces()
    {
        await RunsJgs("""
            figure();
            plot([1, 2, 3]);
            patch([0, 1, 1], [0, 0, 1], "r");
            """);

        Assert.IsType<PatchPlot>(Assert.Single(JG.Gca().Plots));
    }

    [Fact]
    public async Task ASurfaceOnAFlatAxesLeavesItFlatAndItsGridAlone()
    {
        await Runs("""
            figure('Visible', 'off');
            plot(1:3);
            surface(magic(3));
            """);

        AxesModel axes = JG.Gca();
        Assert.False(axes.Is3D);
        Assert.False(axes.Grid.ShowMajor);
        Assert.Equal(2, axes.Plots.Count);
    }


    [Fact]
    public async Task EveryColourGroupOfABoxChartIsDrawn()
    {
        // Each group's chart replaced the one before, which only a dead handle showed (item 110).
        await Runs("""
            figure('Visible', 'off');
            plot(1:3);
            h = boxchart([1 1 2 2 1 1 2 2], [1 2 10 20 3 4 30 40], ...
                'GroupByColor', {'x', 'x', 'x', 'x', 'y', 'y', 'y', 'y'});
            disp(numel(get(gca, 'Children')));
            fprintf('%d\n', all(isvalid(h)));
            """);

        Assert.Equal(new[] { "2", "1" }, _output.NormalLines.Select(static l => l.Trim()));
        Assert.Equal(2, JG.Gca().Plots.OfType<BoxChartPlot>().Count());
    }

    [Fact]
    public async Task EveryPlaneOfASliceIsDrawn()
    {
        // Each plane replaced the one before, which only a dead handle showed (item 110).
        await Runs("""
            figure('Visible', 'off');
            [X, Y, Z] = meshgrid(1:3);
            h = slice(X, Y, Z, X + Y + Z, 2, 2, 2);
            fprintf('%d\n', all(isvalid(h)));
            """);

        Assert.Equal(new[] { "1" }, _output.NormalLines);
        Assert.Equal(3, JG.Gca().Plots.OfType<PatchPlot>().Count());
    }

    [Fact]
    public async Task AQueuedDeleteFcnReadsTheObjectItRunsFor()
    {
        await Runs("""
            figure('Visible', 'off');
            h = plot(1:3, 'LineWidth', 3);
            """);

        PlotObject line = JG.Gca().Plots[0];
        JgsHandleEntry entry = JgsHandleRegistry.EntryFor(line);
        JgsValue handle = JgsHandleRegistry.For(line);
        GraphObjectLifecycle.TryBeginDeleting(line);
        JG.Gca().Plots.Remove(line);

        var running = new JgsRunningDialect(JgsDialect.Matlab); // this thread runs MATLAB code
        try
        {
            using (JgsHandleRegistry.DeliveringDeletionOf(line))
            {
                Assert.True(JgsHandleRegistry.TryGet(handle, out _));
            }

            Assert.False(JgsHandleRegistry.TryGet(handle, out _));
        }
        finally
        {
            running.Current = JgsDialect.Jgs;
        }

        Assert.Same(line, entry.Target);
    }

    // --- 111: yyaxis sides and bands -----------------------------------------------------------

    [Fact]
    public async Task ASidesColourOrderIsDrawnAndSaved()
    {
        await Runs("""
            figure('Visible', 'off');
            yyaxis right
            ax = gca;
            ax.ColorOrder = [1 0 0; 0 1 0];
            hold on
            plot(1:3);
            plot(2:4);
            """);

        List<Color> drawn = Drawn().PolylineColors;
        Assert.Contains(Color.FromRgb(255, 0, 0), drawn);
        Assert.Contains(Color.FromRgb(0, 255, 0), drawn);

        AxesModel back = SavedAndLoaded().Axes[0];
        Assert.Equal(new[] { Color.FromRgb(255, 0, 0), Color.FromRgb(0, 255, 0) }, back.SideColorOrder(1));
        Assert.True(back.YAxes[1].InkIsAutomatic);
        Assert.Equal(Color.FromRgb(0, 255, 0), back.PeekSeriesColor(back.Plots[1]));
    }

    [Fact]
    public async Task ALineStyleOrdersWordsAreSaved()
    {
        await Runs("""
            figure('Visible', 'off');
            ax = gca;
            ax.LineStyleOrder = {'o-', ':'};
            """);

        AxesModel back = SavedAndLoaded().Axes[0];
        Assert.Equal(new[] { "o-", ":" }, back.LineStyleOrderWords);
        Assert.True(back.LineStyleOrderIsList);
    }

    [Fact]
    public async Task AFlatAlphaFadesEachPointOfACloudInSpace()
    {
        await Runs("""
            figure('Visible', 'off');
            scatter3(1:3, 1:3, 1:3, 36, [1 0 0], 'filled', 'AlphaData', [0 0.5 1], ...
                'MarkerFaceAlpha', 'flat', 'AlphaDataMapping', 'none');
            """);

        List<byte> alphas = Drawn().MarkerStyles.Select(static m => m.Fill!.Value.A).ToList();
        Assert.Contains((byte)0, alphas);
        Assert.Contains((byte)255, alphas);
        Assert.Equal(3, alphas.Count);
    }

    [Fact]
    public async Task ABandsTimesShapeAndColourModeAreSaved()
    {
        await Runs("""
            figure('Visible', 'off');
            q = xregion(datetime(2020, 1, 2, 'Format', 'yyyy-MM-dd'), datetime(2020, 1, 3));
            r = xregion(datetime(2020, 1, 4), datetime(2020, 1, 5));
            r.Value = datetime(2020, 1, [6; 7]);
            r.FaceColor = 'g';
            r.FaceColorMode = 'auto';
            """);

        List<ConstantRegionPlot> bands = [.. SavedAndLoaded().Axes[0].Plots.OfType<ConstantRegionPlot>()];
        Assert.Equal(RegionValueKind.Datetime, bands[0].ValueKind);
        Assert.Equal("yyyy-MM-dd", bands[0].TimeFormat);
        Assert.True(bands[0].TimeFormatSet);
        Assert.True(bands[1].ValueIsColumn);
        Assert.False(bands[1].FaceColorManual);
        Assert.Equal(Color.FromRgb(0, 255, 0), bands[1].FaceColor);
        Assert.Equal(AxisScaleType.DateTime, JG.Gca().PrimaryXAxis.Scale);
    }

    [Fact]
    public async Task ABandWithSeriesIndexZeroIsDrawnGrey()
    {
        await Runs("""
            figure('Visible', 'off');
            q = xregion(1, 2);
            q.SeriesIndex = 2;
            q.SeriesIndex = 0;
            """);

        Drawn();
        var band = Assert.IsType<ConstantRegionPlot>(Assert.Single(JG.Gca().Plots));
        Assert.Equal(-2, band.SeriesIndex);
        Assert.Null(band.FaceColor);
    }
}
