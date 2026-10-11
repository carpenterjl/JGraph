using JGraph.Api;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Maths.Ticks;
using JGraph.Objects;
using JGraph.Rendering;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Serialization;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Open items 100 to 104 (ADR 0226), where the parity fixture cannot reach: what 'none' and a
/// marker chart's channels draw, the cell labels a heatmap writes (R2025b keeps them in a hidden
/// object, read in probe_103), the new fields in a saved figure, and the ×10ⁿ mark a script hides or
/// rewrites through a ruler's SecondaryLabel.
/// </summary>
[Collection("JG facade")]
public class OpenItemsBatch13Tests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();

    public OpenItemsBatch13Tests() => JG.Reset();

    public void Dispose()
    {
        JgsHandleRegistry.Clear();
        JG.Reset();
    }

    private Task<ScriptRunResult> RunMatlab(string code) =>
        new MatlabScriptEngine().RunAsync(code, new ScriptContext(_output, static (_, _) => { }), default);

    private async Task Runs(string code)
    {
        ScriptRunResult result = await RunMatlab(code);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    private static RecordingRenderContext Drawn()
    {
        var context = new RecordingRenderContext(new Size2D(400, 300));
        new FigureRenderer().Render(JG.Gcf(), context, Theme.Light);
        return context;
    }

    [Fact]
    public async Task NoneIsNoColourAndEveryPrefixOfItSaysSo()
    {
        await Runs("""
            figure('Visible', 'off');
            q = line(1:2, 1:2, 'Color', 'n');
            disp(q.Color);
            q.Color = 'r';
            disp(mat2str(q.Color));
            """);

        Assert.Equal(new[] { "none", "[1 0 0]" }, _output.NormalLines);
        Assert.True(JgsBuiltins.IsNoneColorWord("No"));
        Assert.False(JgsBuiltins.IsNoneColorWord("nonee"));
        Assert.False(JgsBuiltins.IsNoneColorWord(string.Empty));
    }

    [Fact]
    public async Task ALineWithNoColourDrawsNoStroke()
    {
        await Runs("""
            figure('Visible', 'off');
            line(1:3, [1 3 2], 'Color', 'none');
            """);

        var plot = Assert.IsType<LinePlot>(Assert.Single(JG.Gca().Plots));
        Assert.Equal(Colors.Transparent, plot.Color);
        int unseen = Drawn().PolylineColors.Count(static c => c.A > 0);

        plot.Color = Color.FromRgb(255, 0, 0);
        Assert.Equal(unseen + 1, Drawn().PolylineColors.Count(static c => c.A > 0));
    }

    [Fact]
    public async Task APlainScatterDrawsOpenMarkersAndFilledOnesHaveNoEdge()
    {
        await Runs("""
            figure('Visible', 'off');
            scatter(1:3, [2 1 3], 'r');
            """);
        MarkerStyle open = Assert.Single(Drawn().MarkerStyles);
        Assert.Null(open.Fill);
        Assert.Equal(Color.FromRgb(255, 0, 0), open.Edge);

        JG.Reset();
        await Runs("""
            figure('Visible', 'off');
            scatter(1:3, [2 1 3], 'r', 'filled');
            """);
        MarkerStyle filled = Assert.Single(Drawn().MarkerStyles);
        Assert.Equal(Color.FromRgb(255, 0, 0), filled.Fill);
        Assert.Equal(0, filled.Edge!.Value.A);
    }

    [Fact]
    public async Task AScatterOfValuesFillsEachMarkerInItsOwnColourOnlyWhenFilled()
    {
        await Runs("""
            figure('Visible', 'off');
            scatter(1:3, [2 1 3], 20, [1 2 3]);
            """);
        RecordingRenderContext open = Drawn();
        Assert.Equal(3, open.MarkerStyles.Count);
        Assert.All(open.MarkerStyles, static s => Assert.Null(s.Fill));
        Assert.Equal(3, open.MarkerStyles.Select(static s => s.Edge).Distinct().Count());

        JG.Reset();
        await Runs("""
            figure('Visible', 'off');
            scatter(1:3, [2 1 3], 20, [1 2 3], 'filled');
            """);
        RecordingRenderContext filled = Drawn();
        Assert.All(filled.MarkerStyles, static s => Assert.NotNull(s.Fill));
        Assert.Equal(3, filled.MarkerStyles.Select(static s => s.Fill).Distinct().Count());
    }

    [Fact]
    public async Task ABubbleChartIsFilledAtItsOwnAlphaAndKeepsTheAlphaWritten()
    {
        await Runs("""
            figure('Visible', 'off');
            b = bubblechart(1:3, 1:3, [1 2 3], 'r', 'MarkerFaceAlpha', 0.25);
            fprintf('%.17g\n', b.MarkerFaceAlpha);
            """);

        Assert.Equal(new[] { "0.25" }, _output.NormalLines);
        var bubbles = Assert.IsType<ScatterPlot>(Assert.Single(JG.Gca().Plots));
        Assert.True(bubbles.FillFollowsData);
        Assert.Equal(0.25, bubbles.FaceAlpha);
        Assert.All(Drawn().MarkerStyles, static s => Assert.Equal((byte)64, s.Fill!.Value.A));
    }

    [Fact]
    public void AScatterKeepsItsChannelsInASavedFigure()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        ScatterPlot cloud = axes.AddScatter([1, 2], [3, 4]);
        cloud.DataColor = Color.FromRgb(0, 255, 0);
        cloud.FillFollowsData = true;
        cloud.FaceAlpha = double.NaN;
        cloud.EdgeAlpha = 0.3;

        var back = Assert.IsType<ScatterPlot>(Assert.Single(GraphFormat.Deserialize(GraphFormat.Serialize(figure)).Axes[0].Plots));
        Assert.Equal(Color.FromRgb(0, 255, 0), back.DataColor);
        Assert.True(back.FillFollowsData);
        Assert.True(double.IsNaN(back.FaceAlpha));
        Assert.Equal(0.3, back.EdgeAlpha);
    }

    [Fact]
    public async Task APatchsAlphasGivenAtCreationAreItsOwn()
    {
        await Runs("""
            figure('Visible', 'off');
            p = patch([0 1 1], [0 0 1], 'r', 'FaceAlpha', 0.3, 'EdgeAlpha', 0.45);
            fprintf('%.17g %.17g\n', p.FaceAlpha, p.EdgeAlpha);
            """);

        Assert.Equal(new[] { "0.29999999999999999 0.45000000000000001" }, _output.NormalLines);
        var patch = Assert.IsType<PatchPlot>(Assert.Single(JG.Gca().Plots));
        Assert.Equal(1, patch.Opacity);
    }

    /// <summary>R2025b's cell labels, read out of its heatmap's hidden label grid (probe_103), column by column.</summary>
    [Theory]
    [InlineData(null, "1;1.235e+06;0;2.5;0.000123;0.5;-3;;42")]
    [InlineData("%.2f", "1.00;1234567.89;0.00;2.50;0.00;0.50;-3.00;;42.00")]
    [InlineData("%g%%", "1%;1.23457e+06%;0%;2.5%;0.000123%;0.5%;-3%;;42%")]
    [InlineData("%+g", "+1;+1.23457e+06;+0;+2.5;+0.000123;+0.5;-3;;+42")]
    [InlineData("%5.1f", "  1.0;1234567.9;  0.0;  2.5;  0.0;  0.5; -3.0;; 42.0")]
    [InlineData("%d", "1;1.234568e+06;0;2.500000e+00;1.230000e-04;5.000000e-01;-3;;42")]
    [InlineData("%e", "1.000000e+00;1.234568e+06;0.000000e+00;2.500000e+00;1.230000e-04;5.000000e-01;-3.000000e+00;;4.200000e+01")]
    [InlineData("%.0e", "1e+00;1e+06;0e+00;2e+00;1e-04;5e-01;-3e+00;;4e+01")]
    [InlineData("%g V", "1 V;1.23457e+06 V;0 V;2.5 V;0.000123 V;0.5 V;-3 V;;42 V")]
    [InlineData("%.3g", "1;1.23e+06;0;2.5;0.000123;0.5;-3;;42")]
    [InlineData("%i", "1;1.234568e+06;0;2.500000e+00;1.230000e-04;5.000000e-01;-3;;42")]
    [InlineData("%G", "1;1.23457E+06;0;2.5;0.000123;0.5;-3;;42")]
    [InlineData("%E", "1.000000E+00;1.234568E+06;0.000000E+00;2.500000E+00;1.230000E-04;5.000000E-01;-3.000000E+00;;4.200000E+01")]
    [InlineData("%f%%", "1.000000%;1234567.890000%;0.000000%;2.500000%;0.000123%;0.500000%;-3.000000%;;42.000000%")]
    [InlineData("%-8.2f|", "1.00    |;1234567.89|;0.00    |;2.50    |;0.00    |;0.50    |;-3.00   |;;42.00   |")]
    [InlineData("%08.2f", "00001.00;1234567.89;00000.00;00002.50;00000.00;00000.50;-0003.00;;00042.00")]
    [InlineData("% g", " 1; 1.23457e+06; 0; 2.5; 0.000123; 0.5;-3;; 42")]
    [InlineData("%#g", "1.00000;1.23457e+06;0.00000;2.50000;0.000123000;0.500000;-3.00000;;42.0000")]
    [InlineData("\\x00B0%g", "°1;°1.23457e+06;°0;°2.5;°0.000123;°0.5;°-3;;°42")]
    [InlineData("%.2f\\n", "1.00;1234567.89;0.00;2.50;0.00;0.50;-3.00;;42.00")]
    public void AHeatmapWritesItsCellsAsR2025bDoes(string? format, string expected)
    {
        var heatmap = new HeatmapPlot(new[,] { { 1, 2.5, -3 }, { 1234567.89, 0.000123, double.NaN }, { 0, 0.5, 42 } })
        {
            CellLabelFormat = format,
        };

        Assert.Equal(expected, string.Join(";", ColumnByColumn(heatmap)));
    }

    [Theory]
    [InlineData(null, "1e-07;1.235e+08;-0.5;Inf")]
    [InlineData("%d", "1.000000e-07;123456789;-5.000000e-01;Inf")]
    [InlineData("%.1f", "0.0;123456789.0;-0.5;Inf")]
    public void AHeatmapWritesTinyHugeAndInfiniteCellsAsR2025bDoes(string? format, string expected)
    {
        var heatmap = new HeatmapPlot(new[,] { { 1e-7, 123456789, -0.5, double.PositiveInfinity } })
        {
            CellLabelFormat = format,
            MissingDataLabel = "gone",
        };

        Assert.Equal(expected, string.Join(";", ColumnByColumn(heatmap)));
    }

    [Fact]
    public void AHeatmapSavedWithADotNetFormatStillWritesIt()
    {
        var heatmap = new HeatmapPlot(new[,] { { 1.2345 } }) { CellLabelFormat = "0.0" };
        Assert.Equal("1.2", heatmap.CellLabel(0, 0));
    }

    [Theory]
    [InlineData("%q", TickFormatProblem.Invalid)]
    [InlineData("%x", TickFormatProblem.UnsupportedType)]
    [InlineData("%*d", TickFormatProblem.DynamicNumber)]
    [InlineData("%g%g", TickFormatProblem.Invalid)]
    public void AFormatsRefusalIsTheOneR2025bGives(string format, TickFormatProblem problem)
    {
        Assert.Equal(problem, TickFormats.Check(format));
    }

    [Fact]
    public async Task AHiddenOrRewrittenSecondaryLabelIsWhatTheRulerDraws()
    {
        await Runs("""
            f = figure('Visible', 'off');
            ax = axes(f);
            plot(ax, [0 50000], [0 1]);
            ax.XLim = [0 50000];
            ax.XTick = 0:10000:50000;
            """);
        Assert.Contains("\\times10^{4}", Drawn().Texts);

        await Runs("""
            ax = gca;
            ax.XAxis.SecondaryLabel.Visible = 'off';
            """);
        Assert.DoesNotContain("\\times10^{4}", Drawn().Texts);

        await Runs("""
            ax = gca;
            ax.XAxis.SecondaryLabel.Visible = 'on';
            ax.XAxis.SecondaryLabel.String = 'thousands of m';
            """);
        List<string> texts = Drawn().Texts;
        Assert.Contains("thousands of m", texts);
        Assert.DoesNotContain("\\times10^{4}", texts);
    }

    [Fact]
    public void ARulersExponentLabelIsSaved()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.PrimaryXAxis.ExponentLabel = "custom";
        axes.PrimaryYAxis.ShowExponentLabel = false;

        AxesModel back = GraphFormat.Deserialize(GraphFormat.Serialize(figure)).Axes[0];
        Assert.Equal("custom", back.PrimaryXAxis.ExponentLabel);
        Assert.True(back.PrimaryXAxis.ShowExponentLabel);
        Assert.False(back.PrimaryYAxis.ShowExponentLabel);
        Assert.Null(back.PrimaryYAxis.ExponentLabel);
    }

    [Fact]
    public void APolarRRulerWritesThePowerIntoEachLabel()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.RAxis.Range = new DataRange(0, 50000);
        axes.RAxis.TickPositions = [0, 10000, 20000, 30000, 40000, 50000];

        TickSet ticks = TickGenerators.For(axes.RAxis).Generate(axes.RAxis.Range, 6);
        Assert.Equal(
            new[] { "0", "1\\times10^{4}", "2\\times10^{4}", "3\\times10^{4}", "4\\times10^{4}", "5\\times10^{4}" },
            ticks.MajorTicks.Select(static t => t.Label));
        Assert.Equal(0, ticks.Exponent);
    }

    [Fact]
    public async Task NumelGivenSubscriptsCountsWhatTheyWouldIndex()
    {
        await Runs("""
            A = ones(3, 4);
            disp(numel(A, ':', 1));
            disp(numel(A, 1, ':', 1));
            disp(numel(A, logical([1 0 1])));
            u = struct('f', {1, 2, 3});
            disp(numel(u.f));
            try
                numel(A, {1});
            catch err
                disp(err.identifier);
            end
            """);

        Assert.Equal(new[] { "3", "4", "2", "1", "MATLAB:numel:BadSubscriptingIndex" }, _output.NormalLines);
    }

    private static IEnumerable<string> ColumnByColumn(HeatmapPlot heatmap)
    {
        for (int c = 0; c < heatmap.Columns; c++)
        {
            for (int r = 0; r < heatmap.Rows; r++)
            {
                yield return heatmap.CellLabel(r, c);
            }
        }
    }
}
