using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Objects;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M77: the chart primitives' property surface — the block every plot object answers to, the
/// <c>*Mode</c> words, and the per-kind families for scatter, line, histogram, bar and stem.
/// <para>
/// Every behavior here was probed at the CLI before it was written down, and the pixel-level proofs
/// live in stess_49.m; these tests pin the property semantics. Where a name is answered but refused
/// by decision — the geographic and table-sourced families — the refusal is pinned too, because a
/// ceiling nobody checks is indistinguishable from an oversight.
/// </para>
/// </summary>
[Collection("JG facade")]
public class MatlabM77PlotPropertyTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();

    public MatlabM77PlotPropertyTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private Task<ScriptRunResult> RunMatlab(string code) =>
        new MatlabScriptEngine().RunAsync(
            code, new ScriptContext(_output, static (_, _) => { }), default);

    private static double Number(ScriptRunResult result, string name) =>
        Assert.IsType<double>(Assert.Single(result.Variables, v => v.Name == name).RawValue);

    private static double[] Row(ScriptRunResult result, string name) =>
        Assert.IsType<double[]>(Assert.Single(result.Variables, v => v.Name == name).RawValue);

    private static string Text(ScriptRunResult result, string name) =>
        Assert.IsType<string>(Assert.Single(result.Variables, v => v.Name == name).RawValue);

    private static void Succeeded(ScriptRunResult result) =>
        Assert.True(result.Success, result.Message);

    /// <summary>
    /// The reconciliation the renderer runs before each layout, spelled out here because this host
    /// draws nothing: the plots that can carry a row, minus the ones told not to.
    /// </summary>
    private static void Sync(AxesModel axes) =>
        axes.Legend.SyncEntries(axes.Plots.Where(static p => p is LinePlot && p.ShowsInLegend));

    private async Task Refuses(string code, string fragment)
    {
        ScriptRunResult result = await RunMatlab(code);
        Assert.False(result.Success, $"expected a refusal from: {code}");
        Assert.Contains(fragment, result.Message, StringComparison.OrdinalIgnoreCase);
    }

    // --- The block every plot answers to --------------------------------------------------------

    [Fact]
    public async Task EveryPlotAnswersItsSeatItsLegendSwitchAndItsTipRows()
    {
        ScriptRunResult result = await RunMatlab("""
            h = plot([1 2 3]);
            seat = get(h, 'SeriesIndex');
            clipping = get(h, 'Clipping');
            icon = h.Annotation.LegendInformation.IconDisplayStyle;
            rows = numel(get(get(h, 'DataTipTemplate'), 'DataTipRows'));
            firstLabel = get(h.DataTipTemplate.DataTipRows(1), 'Label');
            firstValue = get(h.DataTipTemplate.DataTipRows(1), 'Value');
            """);

        Succeeded(result);
        Assert.Equal(1, Number(result, "seat"));
        Assert.Equal("on", Text(result, "clipping"));
        Assert.Equal("on", Text(result, "icon"));
        Assert.Equal(2, Number(result, "rows"));
        Assert.Equal("X", Text(result, "firstLabel"));
        Assert.Equal("XData", Text(result, "firstValue"));
    }

    /// <summary>
    /// The one thing the legend switch is for: a series drawn but left out of the legend. The rows
    /// are counted through the reconciliation the renderer runs before each layout, because a
    /// legend's rows are settled at draw time and this host draws nothing; stess_49 counts the
    /// pixels instead.
    /// </summary>
    [Fact]
    public async Task ASeriesToldNotToShowLosesItsLegendRowAndKeepsItsInk()
    {
        ScriptRunResult result = await RunMatlab("""
            a = plot([1 2 3], 'DisplayName', 'first'); hold on;
            b = plot([3 2 1], 'DisplayName', 'second');
            legend();
            b.Annotation.LegendInformation.IconDisplayStyle = 'off';
            icon = b.Annotation.LegendInformation.IconDisplayStyle;
            stillVisible = get(b, 'Visible');
            """);

        Succeeded(result);
        Assert.Equal("off", Text(result, "icon"));

        // The series is left out of the legend, not put away: it is still drawn.
        Assert.Equal("on", Text(result, "stillVisible"));

        AxesModel axes = JG.Gca();
        Assert.Equal(2, axes.Plots.Count);

        // Both series can carry a row; only one of them will, and the difference between those two
        // reconciliations is the whole of what the switch does.
        axes.Legend.SyncEntries(axes.Plots.Where(static p => p is LinePlot));
        Assert.Equal(2, axes.Legend.Entries.Count);

        Sync(axes);
        Assert.Single(axes.Legend.Entries);
    }

    [Fact]
    public async Task TheModeWordsReadTheStateRatherThanASecondCopyOfIt()
    {
        ScriptRunResult result = await RunMatlab("""
            h = plot([1 2 3]);
            colorAuto = get(h, 'ColorMode');
            styleAuto = get(h, 'LineStyleMode');
            markerAuto = get(h, 'MarkerMode');

            set(h, 'Color', 'r');
            set(h, 'LineStyle', '--');
            set(h, 'Marker', 'o');
            colorManual = get(h, 'ColorMode');
            styleManual = get(h, 'LineStyleMode');
            markerManual = get(h, 'MarkerMode');

            set(h, 'ColorMode', 'auto', 'LineStyleMode', 'auto', 'MarkerMode', 'auto');
            released = get(h, 'LineStyle');
            releasedMarker = get(h, 'Marker');
            """);

        Succeeded(result);
        Assert.Equal("auto", Text(result, "colorAuto"));
        Assert.Equal("auto", Text(result, "styleAuto"));
        Assert.Equal("auto", Text(result, "markerAuto"));
        Assert.Equal("manual", Text(result, "colorManual"));
        Assert.Equal("manual", Text(result, "styleManual"));
        Assert.Equal("manual", Text(result, "markerManual"));
        Assert.Equal("-", Text(result, "released"));
        Assert.Equal("none", Text(result, "releasedMarker"));
    }

    /// <summary>
    /// <c>XDataMode</c> is the one mode with something to put back: releasing it counts the
    /// positions out again, which is what makes writing 'auto' mean anything.
    /// </summary>
    [Fact]
    public async Task ReleasingTheXModeCountsThePositionsOutAgain()
    {
        ScriptRunResult result = await RunMatlab("""
            h = plot([10 20 30]);
            implied = get(h, 'XDataMode');
            set(h, 'XData', [4 5 6]);
            chosen = get(h, 'XDataMode');
            set(h, 'XDataMode', 'auto');
            back = get(h, 'XData');
            kept = get(h, 'YData');
            """);

        Succeeded(result);
        Assert.Equal("auto", Text(result, "implied"));
        Assert.Equal("manual", Text(result, "chosen"));
        Assert.Equal([1, 2, 3], Row(result, "back"));
        Assert.Equal([10, 20, 30], Row(result, "kept"));
    }

    [Fact]
    public async Task RefreshdataReReadsTheVariablesTheSourcesName()
    {
        ScriptRunResult result = await RunMatlab("""
            x = 1:4; y = [1 4 9 16];
            h = plot(x, y);
            set(h, 'XDataSource', 'x', 'YDataSource', 'y');
            named = get(h, 'YDataSource');

            x = 1:6; y = (1:6).^2;
            refreshdata(h);
            grown = numel(get(h, 'YData'));
            last = max(get(h, 'YData'));
            """);

        Succeeded(result);
        Assert.Equal("y", Text(result, "named"));
        Assert.Equal(6, Number(result, "grown"));
        Assert.Equal(36, Number(result, "last"));
    }

    [Fact]
    public Task RefreshdataSaysWhichVariableIsMissing() => Refuses(
        "h = plot([1 2 3]); set(h, 'YDataSource', 'nosuch'); refreshdata(h);", "nosuch");

    // --- The histogram --------------------------------------------------------------------------

    /// <summary>
    /// Every way of saying where the bins go, each of which re-cuts them and counts again. Before
    /// this wave a histogram took a count at creation and could not be asked anything else.
    /// </summary>
    [Fact]
    public async Task AHistogramCanBeReBinnedEveryWayMatlabNames()
    {
        ScriptRunResult result = await RunMatlab("""
            x = [1 2 2 3 3 3 4 4 4 4];
            h = histogram(x);
            autoBins = get(h, 'NumBins');
            total = sum(get(h, 'BinCounts'));

            set(h, 'NumBins', 2);
            byCount = get(h, 'NumBins');

            set(h, 'BinWidth', 1);
            width = get(h, 'BinWidth');

            set(h, 'BinLimits', [2 4]);
            limited = sum(get(h, 'BinCounts'));
            limitsMode = get(h, 'BinLimitsMode');

            set(h, 'BinEdges', [0 2 4 6]);
            given = get(h, 'BinCounts');
            values = get(h, 'Values');
            samples = numel(get(h, 'Data'));
            """);

        Succeeded(result);
        Assert.Equal(4, Number(result, "autoBins"));   // MATLAB's integer rule over 1..4
        Assert.Equal(10, Number(result, "total"));
        Assert.Equal(2, Number(result, "byCount"));
        Assert.Equal(1, Number(result, "width"));
        Assert.Equal(9, Number(result, "limited"));    // the reading at 1 falls outside [2 4]
        Assert.Equal("manual", Text(result, "limitsMode"));
        Assert.Equal([1, 5, 4], Row(result, "given"));
        Assert.Equal([1, 5, 4], Row(result, "values")); // Values is the heights, Data the readings
        Assert.Equal(10, Number(result, "samples"));
    }

    [Fact]
    public async Task AHistogramOfNamesCountsThemAndOrdersThemOnAsking()
    {
        ScriptRunResult result = await RunMatlab("""
            h = histogram({'a','b','a','c','a','b','c','c','c'});
            given = get(h, 'BinCounts');
            set(h, 'DisplayOrder', 'ascend');
            up = get(h, 'BinCounts');
            firstUp = get(h, 'Categories');
            firstUp = firstUp{1};

            set(h, 'DisplayOrder', 'descend', 'NumDisplayBins', 2, 'ShowOthers', 'on');
            trimmed = get(h, 'BinCounts');
            names = get(h, 'Categories');
            gathered = names{end};
            """);

        Succeeded(result);
        Assert.Equal([3, 2, 4], Row(result, "given"));
        Assert.Equal([2, 3, 4], Row(result, "up"));
        Assert.Equal("b", Text(result, "firstUp"));
        Assert.Equal([4, 3, 2], Row(result, "trimmed"));  // the two biggest, then the rest
        Assert.Equal("Others", Text(result, "gathered"));
    }

    [Fact]
    public async Task AHistogramTakesItsOptionsInTheCallAsWellAsAfterIt()
    {
        ScriptRunResult result = await RunMatlab("""
            h = histogram([1 2 2 3 3 3], 'BinWidth', 1, 'FaceAlpha', 0.4, ...
                          'DisplayStyle', 'stairs', 'Orientation', 'horizontal', 'LineWidth', 2);
            bins = get(h, 'NumBins');
            alpha = get(h, 'FaceAlpha');
            style = get(h, 'DisplayStyle');
            orientation = get(h, 'Orientation');

            counted = histogram('BinEdges', [0 1 2 3], 'BinCounts', [5 2 7]);
            outright = get(counted, 'BinCounts');
            noData = numel(get(counted, 'Data'));
            """);

        Succeeded(result);
        Assert.Equal(3, Number(result, "bins"));
        Assert.Equal(0.4, Number(result, "alpha"), 6);
        Assert.Equal("stairs", Text(result, "style"));
        Assert.Equal("horizontal", Text(result, "orientation"));
        Assert.Equal([5, 2, 7], Row(result, "outright"));
        Assert.Equal(0, Number(result, "noData"));
    }

    [Fact]
    public Task AHistogramRefusesABinRuleItDoesNotKnow() => Refuses(
        "h = histogram([1 2 3]); set(h, 'BinMethod', 'vibes');", "BinMethod");

    // --- Bars, stems and the line they stand on -------------------------------------------------

    [Fact]
    public async Task ABarChartCanBeStackedAfterItIsDrawnAndUnstackedAgain()
    {
        ScriptRunResult result = await RunMatlab("""
            % Three series of two bars each: a column is a series, as in MATLAB.
            b = bar([1 2 3; 4 5 6]);
            grouped = get(b(1), 'BarLayout');
            set(b(1), 'BarLayout', 'stacked');
            first = get(b(1), 'BarLayout');
            second = get(b(2), 'BarLayout');
            stackedTop = get(b(2), 'YEndPoints');
            set(b(1), 'BarLayout', 'grouped');
            back = get(b(1), 'BarLayout');
            """);

        Succeeded(result);
        Assert.Equal("grouped", Text(result, "grouped"));

        // Both series answer 'stacked', including the bottom one, which has no floor under it.
        Assert.Equal("stacked", Text(result, "first"));
        Assert.Equal("stacked", Text(result, "second"));
        Assert.Equal([3, 9], Row(result, "stackedTop"));  // 1+2 and 4+5, the running totals
        Assert.Equal("grouped", Text(result, "back"));
    }

    [Fact]
    public async Task TheThreeChartsThatStandOnALineHandItBack()
    {
        ScriptRunResult result = await RunMatlab("""
            b = bar([1 2 3]);
            line = get(b, 'BaseLine');
            kind = get(line, 'Type');
            at = get(line, 'BaseValue');

            set(line, 'Color', [1 0 0], 'LineWidth', 2, 'LineStyle', '--');
            width = get(line, 'LineWidth');
            dash = get(line, 'LineStyle');

            set(b, 'ShowBaseLine', 'off');
            hidden = get(line, 'Visible');

            set(b, 'BaseValue', 2);
            moved = get(line, 'BaseValue');

            s = stem([1 2 3]);
            stemLine = get(get(s, 'BaseLine'), 'Type');
            a = area([1 2 3], [1 3 2]);
            areaLine = get(get(a, 'BaseLine'), 'Type');
            """);

        Succeeded(result);
        Assert.Equal("baseline", Text(result, "kind"));
        Assert.Equal(0, Number(result, "at"));
        Assert.Equal(2, Number(result, "width"));
        Assert.Equal("--", Text(result, "dash"));
        Assert.Equal("off", Text(result, "hidden"));

        // One number, two spellings: the chart's BaseValue is the line's own.
        Assert.Equal(2, Number(result, "moved"));
        Assert.Equal("baseline", Text(result, "stemLine"));
        Assert.Equal("baseline", Text(result, "areaLine"));
    }

    [Fact]
    public async Task ABarAnswersWhereEachBarActuallyLandedNotWhereTheDataSaid()
    {
        ScriptRunResult result = await RunMatlab("""
            b = bar([1 2; 3 4]);
            positions = get(b(1), 'XData');
            drawn = get(b(1), 'XEndPoints');
            tops = get(b(1), 'YEndPoints');
            """);

        Succeeded(result);
        Assert.Equal([1, 2], Row(result, "positions"));

        // A grouped series is shifted off its position to share the slot, and this is that shift.
        double[] drawn = Row(result, "drawn");
        Assert.True(drawn[0] < 1, $"the first bar sits left of its position, at {drawn[0]}");
        Assert.Equal([1, 3], Row(result, "tops"));
    }

    [Fact]
    public async Task AStemTakesASpecAndTheOptionsMatlabNames()
    {
        ScriptRunResult result = await RunMatlab("""
            s = stem([1 2 3], [3 1 2], 'filled', 'r--s', 'BaseValue', 1, ...
                     'MarkerEdgeColor', [0 0 1], 'LineWidth', 2);
            marker = get(s, 'Marker');
            style = get(s, 'LineStyle');
            base = get(s, 'BaseValue');
            edge = get(s, 'MarkerEdgeColor');
            face = get(s, 'MarkerFaceColor');
            """);

        Succeeded(result);

        // The marker reads back as MATLAB spells it. Before this wave a stem had no Marker alias and
        // answered with the enum's own word, 'circle'.
        Assert.Equal("s", Text(result, "marker"));
        Assert.Equal("--", Text(result, "style"));
        Assert.Equal(1, Number(result, "base"));
        Assert.Equal([0, 0, 1], Row(result, "edge"));
        Assert.Equal([1, 0, 0], Row(result, "face"));  // 'filled' takes the stem's own colour
    }

    // --- Lines and error bars -------------------------------------------------------------------

    [Fact]
    public async Task PlotTakesTheMarkerColoursItAlwaysRefused()
    {
        ScriptRunResult result = await RunMatlab("""
            h = plot(1:10, (1:10).^2, 'Marker', 'o', 'MarkerFaceColor', [1 0 0], ...
                     'MarkerEdgeColor', [0 0 1], 'MarkerIndices', [1 5 10], ...
                     'LineJoin', 'round', 'AlignVertexCenters', 'on');
            face = get(h, 'MarkerFaceColor');
            edge = get(h, 'MarkerEdgeColor');
            indices = get(h, 'MarkerIndices');
            join = get(h, 'LineJoin');
            aligned = get(h, 'AlignVertexCenters');

            set(h, 'MarkerFaceColor', 'none');
            cleared = get(h, 'MarkerFaceColor');
            """);

        Succeeded(result);
        Assert.Equal([1, 0, 0], Row(result, "face"));
        Assert.Equal([0, 0, 1], Row(result, "edge"));
        Assert.Equal([1, 5, 10], Row(result, "indices"));   // counted from one, as MATLAB counts
        Assert.Equal("round", Text(result, "join"));
        Assert.Equal("on", Text(result, "aligned"));
        Assert.Equal("none", Text(result, "cleared"));
    }

    [Fact]
    public async Task AnErrorBarsFourReachesAnswerToBothNamesAndCanBeWritten()
    {
        ScriptRunResult result = await RunMatlab("""
            e = errorbar([1 2 3], [1 2 3], [.2 .2 .2]);
            lower = get(e, 'LData');
            sameAsDelta = get(e, 'YNegativeDelta');

            set(e, 'UData', [.5 .5 .5]);
            upper = get(e, 'YPositiveDelta');

            set(e, 'XNegativeDelta', [.1 .1 .1], 'XPositiveDelta', [.3 .3 .3]);
            left = get(e, 'XNegativeDelta');
            right = get(e, 'XPositiveDelta');
            """);

        Succeeded(result);
        Assert.Equal([0.2, 0.2, 0.2], Row(result, "lower"));
        Assert.Equal([0.2, 0.2, 0.2], Row(result, "sameAsDelta"));
        Assert.Equal([0.5, 0.5, 0.5], Row(result, "upper"));
        Assert.Equal([0.1, 0.1, 0.1], Row(result, "left"));
        Assert.Equal([0.3, 0.3, 0.3], Row(result, "right"));
    }

    // --- The scatter ----------------------------------------------------------------------------

    [Fact]
    public async Task AScatterCarriesATransparencyPerPointAndSaysHowToReadIt()
    {
        ScriptRunResult result = await RunMatlab("""
            s = scatter([1 2 3 4], [1 2 3 4], 100);
            emptyAtFirst = numel(get(s, 'AlphaData'));
            autoAtFirst = get(s, 'AlphaDataMode');

            set(s, 'AlphaData', [0.1 0.4 0.7 1], 'AlphaDataMapping', 'none');
            alphas = get(s, 'AlphaData');
            mapping = get(s, 'AlphaDataMapping');
            manual = get(s, 'AlphaDataMode');

            set(s, 'AlphaDataMode', 'auto');
            released = numel(get(s, 'AlphaData'));
            """);

        Succeeded(result);
        Assert.Equal(0, Number(result, "emptyAtFirst"));
        Assert.Equal("auto", Text(result, "autoAtFirst"));
        Assert.Equal([0.1, 0.4, 0.7, 1], Row(result, "alphas"));
        Assert.Equal("none", Text(result, "mapping"));
        Assert.Equal("manual", Text(result, "manual"));
        Assert.Equal(0, Number(result, "released"));
    }

    // --- The ceilings, which are decisions rather than omissions ---------------------------------

    [Theory]
    [InlineData("h = plot([1 2 3]); set(h, 'ZData', [1 2 3]);", "spatial")]
    [InlineData("h = plot([1 2 3]); set(h, 'ThetaData', [1 2 3]);", "polar axes")]
    [InlineData("s = scatter([1 2], [1 2]); set(s, 'ZJitter', 'rand');", "third direction")]
    [InlineData("s = scatter([1 2], [1 2]); set(s, 'CDataMode', 'manual');", "CData")]
    public Task TheNamesThatCannotActRefuseByName(string code, string fragment) =>
        Refuses(code, fragment);

    /// <summary>
    /// The geographic and table-sourced families are not answered at all, by decision — there is no
    /// geographic axes and no table-backed chart to hang them on. This is the ceiling stated in
    /// ADR 0077, and it is pinned so that closing it later is a deliberate act.
    /// </summary>
    [Theory]
    [InlineData("LatitudeData")]
    [InlineData("LongitudeData")]
    [InlineData("SourceTable")]
    [InlineData("XVariable")]
    public Task TheGeographicAndTableFamiliesAreNotAnswered(string name) =>
        Refuses($"s = scatter([1 2], [1 2]); get(s, '{name}');", "no property");

    // --- The whole table ------------------------------------------------------------------------

    /// <summary>
    /// The census the coverage table measures, asserted here so a name that quietly stops being
    /// served fails a test rather than only moving a number in a document.
    /// </summary>
    [Theory]
    [InlineData("h = plot([1 2 3]);", 48)]
    [InlineData("h = histogram([1 2 2 3]);", 46)]
    [InlineData("h = bar([1 2 3]);", 44)]
    [InlineData("h = stem([1 2 3]);", 43)]
    [InlineData("h = errorbar([1 2 3], [1 2 3], [.1 .1 .1]);", 52)]
    [InlineData("h = scatter([1 2 3], [1 2 3]);", 59)]
    [InlineData("h = area([1 2 3], [1 2 3]);", 39)]
    [InlineData("h = stairs([1 2 3]);", 38)]
    public async Task EachKindAnswersAtLeastWhatTheCoverageTableCounted(string draw, int least)
    {
        ScriptRunResult result = await RunMatlab(draw + "\ncounted = numel(fieldnames(get(h)));");

        Succeeded(result);
        Assert.True(
            Number(result, "counted") >= least,
            $"{draw} answered {Number(result, "counted")} names, fewer than the {least} counted");
    }
}
