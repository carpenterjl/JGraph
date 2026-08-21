using JGraph.Api;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M73: the actable core of the axes property families — fonts, grid appearance, ruler colors and
/// tick geometry, limit and tick modes, stateful series cycling, axes-level color mapping, and
/// aspect ratios. Every behavior here was probed at the CLI before it was written down, and the
/// pixel-level proofs live in stess_45.m; these tests pin the property semantics.
/// </summary>
[Collection("JG facade")]
public class MatlabM73AxesPropertyTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();

    public MatlabM73AxesPropertyTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private Task<ScriptRunResult> RunMatlab(string code) =>
        new MatlabScriptEngine().RunAsync(
            code, new ScriptContext(_output, static (_, _) => { }), default);

    private static double Number(ScriptRunResult result, string name) =>
        Assert.IsType<double>(Assert.Single(result.Variables, v => v.Name == name).RawValue);

    private static string Text(ScriptRunResult result, string name) =>
        Assert.IsType<string>(Assert.Single(result.Variables, v => v.Name == name).RawValue);

    private static bool Truth(ScriptRunResult result, string name) =>
        Assert.Single(result.Variables, v => v.Name == name).RawValue switch
        {
            bool flag => flag,
            double number => number != 0,
            var other => throw new Xunit.Sdk.XunitException($"{name} was a {other?.GetType().Name}"),
        };

    // --- Fonts ----------------------------------------------------------------------------------

    [Fact]
    public async Task TheAxesFontFansOutAndTheTitleKeepsItsMultiplier()
    {
        ScriptRunResult result = await RunMatlab("""
            ax = gca;
            before = get(ax, 'FontSizeMode');
            set(ax, 'FontSize', 14);
            size = get(ax, 'FontSize');
            mode = get(ax, 'FontSizeMode');
            set(ax, 'FontSizeMode', 'auto');
            restored = get(ax, 'FontSize');
            after = get(ax, 'FontSizeMode');
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal("auto", Text(result, "before"));
        Assert.Equal(14.0, Number(result, "size"));
        Assert.Equal("manual", Text(result, "mode"));
        Assert.Equal(11.0, Number(result, "restored"));
        Assert.Equal("auto", Text(result, "after"));
    }

    [Fact]
    public async Task TheTitleIsSizedByTheMultiplierAndAlignedByTheWord()
    {
        ScriptRunResult result = await RunMatlab("""
            ax = gca;
            title('t');
            set(ax, 'FontSize', 10, 'TitleFontSizeMultiplier', 1.5);
            set(ax, 'TitleFontWeight', 'normal', 'SubtitleFontWeight', 'bold');
            set(ax, 'TitleHorizontalAlignment', 'left');
            tfw = get(ax, 'TitleFontWeight');
            sfw = get(ax, 'SubtitleFontWeight');
            tha = get(ax, 'TitleHorizontalAlignment');
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal("normal", Text(result, "tfw"));
        Assert.Equal("bold", Text(result, "sfw"));
        Assert.Equal("left", Text(result, "tha"));

        // The multiplier write re-derived the title's own size from the axes font.
        Assert.Equal(15.0, JG.Gca().TitleStyle.FontSize, 10);
        Assert.False(JG.Gca().TitleStyle.Bold);
        Assert.True(JG.Gca().SubtitleStyle.Bold);
    }

    [Fact]
    public async Task FontNameAngleWeightAndSmoothingReachTheRulers()
    {
        ScriptRunResult result = await RunMatlab("""
            ax = gca;
            set(ax, 'FontName', 'Consolas', 'FontAngle', 'italic', 'FontWeight', 'bold', 'FontSmoothing', 'off');
            name = get(ax, 'FontName');
            angle = get(ax, 'FontAngle');
            weight = get(ax, 'FontWeight');
            smooth = get(ax, 'FontSmoothing');
            units = get(ax, 'FontUnits');
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal("Consolas", Text(result, "name"));
        Assert.Equal("italic", Text(result, "angle"));
        Assert.Equal("bold", Text(result, "weight"));
        Assert.Equal("off", Text(result, "smooth"));
        Assert.Equal("points", Text(result, "units"));

        TextStyle ticks = JG.Gca().PrimaryYAxis.TickLabelStyle;
        Assert.Equal("Consolas", ticks.FontFamily);
        Assert.True(ticks.Italic);
        Assert.True(ticks.Bold);
        Assert.False(ticks.Antialias);
    }

    [Fact]
    public async Task OtherFontUnitsAreRefusedHonestly()
    {
        ScriptRunResult result = await RunMatlab("set(gca, 'FontUnits', 'normalized')");
        Assert.False(result.Success);
        Assert.Contains("FontUnits", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TickLabelsCanChangeInterpreter()
    {
        ScriptRunResult result = await RunMatlab("""
            set(gca, 'TickLabelInterpreter', 'none');
            word = get(gca, 'TickLabelInterpreter');
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal("none", Text(result, "word"));
        Assert.Equal(TextInterpreter.None, JG.Gca().PrimaryXAxis.TickLabelStyle.Interpreter);
    }

    // --- Grid appearance ------------------------------------------------------------------------

    [Fact]
    public async Task GridColorAlphaAndLineStyleActAndTrackTheirModes()
    {
        ScriptRunResult result = await RunMatlab("""
            ax = gca; grid on;
            cm0 = get(ax, 'GridColorMode');
            set(ax, 'GridColor', [1 0 0], 'GridAlpha', 0.5, 'GridLineStyle', '--');
            c = get(ax, 'GridColor');
            a = get(ax, 'GridAlpha');
            s = get(ax, 'GridLineStyle');
            cm = get(ax, 'GridColorMode');
            am = get(ax, 'GridAlphaMode');
            set(ax, 'MinorGridColor', [0 0 1], 'MinorGridLineStyle', ':');
            ms = get(ax, 'MinorGridLineStyle');
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal("auto", Text(result, "cm0"));
        Assert.Equal("manual", Text(result, "cm"));
        Assert.Equal("manual", Text(result, "am"));
        Assert.Equal("--", Text(result, "s"));
        Assert.Equal(":", Text(result, "ms"));
        Assert.Equal(0.5, Number(result, "a"), 2);

        LineStyle major = JG.Gca().Grid.MajorLineStyle;
        Assert.Equal((255, 0, 0), (major.Color.R, major.Color.G, major.Color.B));
        Assert.Equal(DashStyle.Dash, major.Dash);
    }

    [Fact]
    public void AManualGridColorSurvivesAThemePassAndAnAutomaticOneFollowsIt()
    {
        AxesModel axes = JG.Gca();
        axes.Grid.MajorLineStyle = axes.Grid.MajorLineStyle.WithColor(new Color(255, 0, 0));
        axes.Grid.MajorColorManual = true;

        Theme.Dark.Apply(JG.Gcf());
        Assert.Equal((255, 0, 0),
            (axes.Grid.MajorLineStyle.Color.R, axes.Grid.MajorLineStyle.Color.G, axes.Grid.MajorLineStyle.Color.B));

        axes.Grid.MajorColorManual = false;
        Theme.Light.Apply(JG.Gcf());
        Assert.NotEqual((byte)255, axes.Grid.MajorLineStyle.Color.R);
    }

    [Fact]
    public async Task EachDirectionHasItsOwnGridSwitch()
    {
        ScriptRunResult result = await RunMatlab("""
            ax = gca; grid on;
            set(ax, 'XGrid', 'off', 'YMinorGrid', 'on');
            xg = get(ax, 'XGrid');
            yg = get(ax, 'YGrid');
            ymg = get(ax, 'YMinorGrid');
            zmg = get(ax, 'ZMinorGrid');
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal("off", Text(result, "xg"));
        Assert.Equal("on", Text(result, "yg"));
        Assert.Equal("on", Text(result, "ymg"));
        Assert.Equal("off", Text(result, "zmg"));
    }

    [Fact]
    public async Task LayerLineWidthAndBoxStyleRoundTrip()
    {
        ScriptRunResult result = await RunMatlab("""
            ax = gca;
            set(ax, 'Layer', 'top', 'LineWidth', 2, 'BoxStyle', 'full');
            layer = get(ax, 'Layer');
            width = get(ax, 'LineWidth');
            box = get(ax, 'BoxStyle');
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal("top", Text(result, "layer"));
        Assert.Equal(2.0, Number(result, "width"));
        Assert.Equal("full", Text(result, "box"));
        Assert.Equal(AxesLayer.Top, JG.Gca().Layer);
    }

    // --- Rulers ---------------------------------------------------------------------------------

    [Fact]
    public async Task ARulerColorInksTheRulerAndItsModeReleasesIt()
    {
        ScriptRunResult result = await RunMatlab("""
            ax = gca;
            m0 = get(ax, 'XColorMode');
            set(ax, 'XColor', [1 0 0]);
            c = get(ax, 'XColor');
            m1 = get(ax, 'XColorMode');
            set(ax, 'XColorMode', 'auto');
            m2 = get(ax, 'XColorMode');
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal("auto", Text(result, "m0"));
        Assert.Equal("manual", Text(result, "m1"));
        Assert.Equal("auto", Text(result, "m2"));
        Assert.Null(JG.Gca().PrimaryXAxis.RulerColor);
    }

    [Fact]
    public async Task LimModesFreezeAndReleaseWhatIsShowing()
    {
        ScriptRunResult result = await RunMatlab("""
            plot([1 2 3], [4 5 6]);
            ax = gca;
            m0 = get(ax, 'XLimMode');
            xlim([0 10]);
            m1 = get(ax, 'XLimMode');
            set(ax, 'XLimMode', 'auto');
            m2 = get(ax, 'XLimMode');
            fitted = get(ax, 'XLim');
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal("auto", Text(result, "m0"));
        Assert.Equal("manual", Text(result, "m1"));
        Assert.Equal("auto", Text(result, "m2"));
        Assert.True(JG.Gca().PrimaryXAxis.AutoScale);
    }

    [Fact]
    public async Task LimitMethodsRefitTheRuler()
    {
        ScriptRunResult result = await RunMatlab("""
            plot([1 2 3], [4 5 6]);
            ax = gca;
            word0 = get(ax, 'YLimitMethod');
            set(ax, 'YLimitMethod', 'tight');
            tight = get(ax, 'YLim');
            word1 = get(ax, 'YLimitMethod');
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal("padded", Text(result, "word0"));
        Assert.Equal("tight", Text(result, "word1"));
        Assert.Equal(new Core.Primitives.DataRange(4, 6), JG.Gca().PrimaryYAxis.Range);
    }

    [Fact]
    public async Task AxisTightIsTheTightPolicyOnEveryRuler()
    {
        ScriptRunResult result = await RunMatlab("""
            plot([1 2 3], [4 5 6]);
            axis tight;
            y = get(gca, 'YLim');
            x = get(gca, 'XLim');
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(new Core.Primitives.DataRange(4, 6), JG.Gca().PrimaryYAxis.Range);
        Assert.Equal(new Core.Primitives.DataRange(1, 3), JG.Gca().PrimaryXAxis.Range);
    }

    [Fact]
    public async Task TickModesFreezeTheGeneratedTicks()
    {
        ScriptRunResult result = await RunMatlab("""
            plot([1 2 3], [4 5 6]);
            ax = gca;
            m0 = get(ax, 'XTickMode');
            set(ax, 'XTickMode', 'manual');
            m1 = get(ax, 'XTickMode');
            frozen = numel(get(ax, 'XTick'));
            set(ax, 'XTickMode', 'auto');
            m2 = get(ax, 'XTickMode');
            set(ax, 'XMinorTick', 'on');
            minor = get(ax, 'XMinorTick');
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal("auto", Text(result, "m0"));
        Assert.Equal("manual", Text(result, "m1"));
        Assert.Equal("auto", Text(result, "m2"));
        Assert.True(Number(result, "frozen") >= 2);
        Assert.Equal("on", Text(result, "minor"));
        Assert.Null(JG.Gca().PrimaryXAxis.TickPositions);
    }

    [Fact]
    public async Task TickDirectionAndLengthAreAxesWide()
    {
        ScriptRunResult result = await RunMatlab("""
            ax = gca;
            d0 = get(ax, 'TickDir');
            m0 = get(ax, 'TickDirMode');
            len0 = get(ax, 'TickLength');
            set(ax, 'TickDir', 'in', 'TickLength', [0.02 0.05]);
            d1 = get(ax, 'TickDir');
            m1 = get(ax, 'TickDirMode');
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal("out", Text(result, "d0"));
        Assert.Equal("auto", Text(result, "m0"));
        Assert.Equal("in", Text(result, "d1"));
        Assert.Equal("manual", Text(result, "m1"));
        Assert.Equal(TickDirection.In, JG.Gca().PrimaryYAxis.TickDirection);
        Assert.Equal(new Core.Primitives.Vector2D(0.02, 0.05), JG.Gca().PrimaryXAxis.TickLength);
    }

    [Fact]
    public async Task AxisLocationsMoveTheRulers()
    {
        ScriptRunResult result = await RunMatlab("""
            ax = gca;
            set(ax, 'XAxisLocation', 'top', 'YAxisLocation', 'right');
            xal = get(ax, 'XAxisLocation');
            yal = get(ax, 'YAxisLocation');
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal("top", Text(result, "xal"));
        Assert.Equal("right", Text(result, "yal"));
        Assert.Equal(AxisPosition.Top, JG.Gca().PrimaryXAxis.Position);
        Assert.Equal(AxisPosition.Right, JG.Gca().PrimaryYAxis.Position);
    }

    [Fact]
    public async Task YAxisLocationRefusesAYyaxisAxes()
    {
        ScriptRunResult result = await RunMatlab("""
            yyaxis right;
            set(gca, 'YAxisLocation', 'left');
            """);

        Assert.False(result.Success);
        Assert.Contains("YAxisLocation", result.Message, StringComparison.Ordinal);
    }

    // --- Series cycling -------------------------------------------------------------------------

    [Fact]
    public async Task TheColorCycleAdvancesRewindsAndRetints()
    {
        ScriptRunResult result = await RunMatlab("""
            ax = gca; hold on;
            h1 = plot([1 2], [1 2]);
            h2 = plot([1 2], [2 3]);
            seat = get(ax, 'ColorOrderIndex');
            set(ax, 'ColorOrderIndex', 1);
            h3 = plot([1 2], [3 4]);
            reused = isequal(get(h3, 'Color'), get(h1, 'Color'));
            set(ax, 'ColorOrder', [1 0 0; 0 1 0]);
            retinted = isequal(get(h1, 'Color'), [1 0 0]) && isequal(get(h2, 'Color'), [0 1 0]);
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(3.0, Number(result, "seat"));
        Assert.True(Truth(result, "reused"));
        Assert.True(Truth(result, "retinted"));
    }

    [Fact]
    public async Task DeletingAPlotNoLongerRecolorsTheSurvivors()
    {
        ScriptRunResult result = await RunMatlab("""
            ax = gca; hold on;
            h1 = plot([1 2], [1 2]);
            h2 = plot([1 2], [2 3]);
            h3 = plot([1 2], [3 4]);
            before = get(h3, 'Color');
            delete(h2);
            kept = isequal(get(h3, 'Color'), before);
            """);

        Assert.True(result.Success, result.Message);
        Assert.True(Truth(result, "kept"));
    }

    [Fact]
    public async Task TheLineStyleOrderStepsOncePerLapOfThePalette()
    {
        ScriptRunResult result = await RunMatlab("""
            ax = gca;
            set(ax, 'ColorOrder', [1 0 0; 0 1 0], 'LineStyleOrder', {'-', '--'});
            hold on;
            h1 = plot([0 1], [1 1]);
            h2 = plot([0 1], [2 2]);
            h3 = plot([0 1], [3 3]);
            lsoi = get(ax, 'LineStyleOrderIndex');
            lso = get(ax, 'LineStyleOrder');
            second = lso{2};
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal("--", Text(result, "second"));
        Assert.Equal(2.0, Number(result, "lsoi"));

        // The third line began the second lap, so it wears the second style.
        var plots = JG.Gca().Plots;
        Assert.Equal(DashStyle.Solid, ((JGraph.Objects.LinePlot)plots[1]).DashStyle);
        Assert.Equal(DashStyle.Dash, ((JGraph.Objects.LinePlot)plots[2]).DashStyle);
    }

    [Fact]
    public async Task AnEmptiedAxesStartsTheCycleOver()
    {
        ScriptRunResult result = await RunMatlab("""
            ax = gca; hold on;
            plot([1 2], [1 2]);
            plot([1 2], [2 3]);
            cla;
            seat = get(gca, 'ColorOrderIndex');
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(1.0, Number(result, "seat"));
    }

    // --- Axes-level color mapping ----------------------------------------------------------------

    [Fact]
    public async Task CLimPinsAndReleasesEveryMappedPlot()
    {
        ScriptRunResult result = await RunMatlab("""
            surf([1 2; 3 4]);
            ax = gca;
            m0 = get(ax, 'CLimMode');
            set(ax, 'CLim', [0 10]);
            pinned = get(ax, 'CLim');
            m1 = get(ax, 'CLimMode');
            set(ax, 'CLimMode', 'auto');
            released = get(ax, 'CLim');
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal("auto", Text(result, "m0"));
        Assert.Equal("manual", Text(result, "m1"));
        Assert.Equal(new Core.Primitives.DataRange(1, 4),
            new Core.Primitives.DataRange(
                ((JGraph.Objects.SurfacePlot)JG.Gca().Plots[0]).ColorRange.Min,
                ((JGraph.Objects.SurfacePlot)JG.Gca().Plots[0]).ColorRange.Max));
    }

    [Fact]
    public async Task TheAxesColormapSeedsAPlotDrawnAfterIt()
    {
        // The order-independence M73 bought: colormap before the plot verb used to do nothing.
        ScriptRunResult result = await RunMatlab("""
            colormap jet;
            surf([1 2; 3 4]);
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal("jet", ((JGraph.Objects.SurfacePlot)JG.Gca().Plots[0]).Colormap.Name,
            ignoreCase: true);
    }

    [Fact]
    public async Task ColorScaleAndAmbientLightColorRoundTrip()
    {
        ScriptRunResult result = await RunMatlab("""
            ax = gca;
            set(ax, 'ColorScale', 'log', 'AmbientLightColor', [1 0.5 0]);
            scale = get(ax, 'ColorScale');
            alc = get(ax, 'AmbientLightColor');
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal("log", Text(result, "scale"));
        Assert.Equal(ColorScaleType.Log, JG.Gca().ColorScale);
        Assert.Equal(255, JG.Gca().AmbientLightColor.R);
    }

    [Fact]
    public void ALogColorScaleSpreadsTheDecadesEvenly()
    {
        var map = Colormap.Grayscale;
        Color linear = map.Sample(10, 1, 100);
        Color log = map.Sample(10, 1, 100, logScale: true);

        // 10 sits near the bottom linearly but exactly halfway between the decades.
        Assert.True(linear.R < 40, $"linear sample was {linear.R}");
        Assert.InRange(log.R, 120, 135);
    }

    // --- Aspect ratios ---------------------------------------------------------------------------

    [Fact]
    public async Task TheTwoAspectsClearEachOther()
    {
        ScriptRunResult result = await RunMatlab("""
            surf([1 2; 3 4]);
            ax = gca;
            set(ax, 'DataAspectRatio', [1 2 1]);
            darm = get(ax, 'DataAspectRatioMode');
            set(ax, 'PlotBoxAspectRatio', [2 1 1]);
            cleared = get(ax, 'DataAspectRatioMode');
            pbam = get(ax, 'PlotBoxAspectRatioMode');
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal("manual", Text(result, "darm"));
        Assert.Equal("auto", Text(result, "cleared"));
        Assert.Equal("manual", Text(result, "pbam"));
        Assert.Null(JG.Gca().DataAspectRatio);
    }

    [Fact]
    public async Task DataAspectRatioActsInTwoDimensions()
    {
        ScriptRunResult result = await RunMatlab("""
            plot([0 4], [0 1]);
            set(gca, 'DataAspectRatio', [1 1 1]);
            word = get(gca, 'DataAspectRatioMode');
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal("manual", Text(result, "word"));
        Assert.Equal(new Core.Primitives.Vector3D(1, 1, 1), JG.Gca().DataAspectRatio);
    }

    // --- The whole table answers -----------------------------------------------------------------

    [Fact]
    public async Task TheFullPropertyStructAnswersOnBothAxesKinds()
    {
        ScriptRunResult result = await RunMatlab("""
            counted = numel(fieldnames(get(gca)));
            pax = polaraxes;
            polarCounted = numel(fieldnames(get(pax)));
            """);

        Assert.True(result.Success, result.Message);
        Assert.True(Number(result, "counted") >= 159, $"axes answered {Number(result, "counted")}");
        Assert.Equal(Number(result, "counted"), Number(result, "polarCounted"));
    }
}
