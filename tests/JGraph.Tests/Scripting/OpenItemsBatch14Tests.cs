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
/// Open items 106 to 108 (ADR 0227), where the parity fixture cannot reach: what a yyaxis side, a
/// marker chart's colour per point and alphas, and a shaded region draw, and what a saved figure
/// keeps of them. The answers a script reads are R2025b's (probe_106 to probe_108b).
/// </summary>
[Collection("JG facade")]
public class OpenItemsBatch14Tests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();

    public OpenItemsBatch14Tests() => JG.Reset();

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

    private async Task<string> Refused(string code)
    {
        ScriptRunResult result = await RunMatlab(code);
        Assert.False(result.Success);
        return result.Message + _output.ErrorText;
    }

    private static RecordingRenderContext Drawn()
    {
        var context = new RecordingRenderContext(new Size2D(400, 300));
        new FigureRenderer().Render(JG.Gcf(), context, Theme.Light);
        return context;
    }

    // --- 106: yyaxis -------------------------------------------------------------------------

    [Fact]
    public async Task YAxisIsBothRulersAndAWriteThroughOneOfThemLands()
    {
        await Runs("""
            figure('Visible', 'off');
            ax = gca;
            yyaxis left
            plot(1:3);
            yyaxis right
            plot([10 20 30]);
            disp(mat2str(size(ax.YAxis)));
            ax.YAxis(1).Limits = [-5 5];
            ax.YAxis(2).Color = [1 0 0];
            yyaxis left
            disp(mat2str(ylim));
            yyaxis right
            disp(mat2str(ax.YColor));
            c = {ax.YAxis.Color};
            disp(mat2str(size(c)));
            """);

        Assert.Equal(new[] { "[2 1]", "[-5 5]", "[1 0 0]", "[1 2]" }, _output.NormalLines);
    }

    [Fact]
    public async Task EachSideTakesItsColourAndStepsItsLineStyles()
    {
        await Runs("""
            figure('Visible', 'off');
            yyaxis left
            hold on
            a = plot(1:3);
            b = plot(2:4);
            yyaxis right
            c = plot(1:3);
            d = plot(2:4);
            e = plot(3:5, 'r');
            f = plot(4:6);
            disp(isequal(a.Color, b.Color));
            disp(isequal(c.Color, d.Color));
            disp(isequal(a.Color, c.Color));
            disp(strjoin({a.LineStyle, b.LineStyle, c.LineStyle, d.LineStyle, f.LineStyle}, ','));
            """);

        Assert.Equal(new[] { "true", "true", "false", "-,--,-,--,-." }, _output.NormalLines);
        AxesModel axes = JG.Gca();
        Assert.Equal(axes.PeekSeriesColor(axes.Plots[0]), Colors.DefaultSeriesOrder[0]);
        Assert.Equal(axes.PeekSeriesColor(axes.Plots[2]), Colors.DefaultSeriesOrder[1]);
    }

    [Fact]
    public async Task TheRulersAreInkedInTheirSidesColoursUnlessTheAxesAlreadyHeldPlots()
    {
        await Runs("""
            figure('Visible', 'off');
            yyaxis right
            """);
        Assert.Equal(Colors.DefaultSeriesOrder[0], JG.Gca().YAxes[0].RulerColor);
        Assert.Equal(Colors.DefaultSeriesOrder[1], JG.Gca().YAxes[1].RulerColor);
        Assert.Equal(Colors.DefaultSeriesOrder[1], JG.Gca().YAxes[1].TickLabelStyle.Color);

        JG.Reset();
        await Runs("""
            figure('Visible', 'off');
            plot(1:3);
            yyaxis right
            """);
        Assert.Null(JG.Gca().YAxes[0].RulerColor);
        Assert.Equal(Colors.DefaultSeriesOrder[1], JG.Gca().YAxes[1].RulerColor);
    }

    [Fact]
    public async Task ChildrenAndClaAndYAxisLocationAreTheActiveSides()
    {
        await Runs("""
            figure('Visible', 'off');
            ax = gca;
            yyaxis left
            plot(1:3);
            yyaxis right
            plot(1:3);
            hold on
            plot(2:4);
            disp(numel(ax.Children));
            disp(ax.YAxisLocation);
            cla
            yyaxis left
            disp(numel(ax.Children));
            disp(ax.YAxisLocation);
            disp(numel(findobj(ax, 'Type', 'line')));
            cla reset
            disp(mat2str(size(ax.YAxis)));
            """);

        Assert.Equal(new[] { "2", "right", "1", "left", "1", "[1 1]" }, _output.NormalLines);
        Assert.Null(JG.Gca().LineStyleOrder);
    }

    [Fact]
    public async Task TheLegendListsTheLeftSideFirst()
    {
        await Runs("""
            figure('Visible', 'off');
            yyaxis right
            plot(1:3, 'DisplayName', 'b');
            yyaxis left
            plot(1:3, 'DisplayName', 'a');
            l = legend;
            disp(strjoin(l.String, ','));
            """);

        Assert.Equal(new[] { "a,b" }, _output.NormalLines);
    }

    [Fact]
    public async Task ASideSeatIsSaved()
    {
        await Runs("""
            figure('Visible', 'off');
            yyaxis right
            plot(1:3);
            """);

        FigureModel back = GraphFormat.Deserialize(GraphFormat.Serialize(JG.Gcf()));
        PlotObject line = Assert.Single(back.Axes[0].Plots);
        Assert.Equal(1, line.SeriesSide);
        Assert.Equal(Colors.DefaultSeriesOrder[1], back.Axes[0].PeekSeriesColor(line));
    }

    [Fact]
    public async Task AnIndexedWriteIntoNumbersIsNotTakenForHandles()
    {
        // [1 2 3] names figures 1, 2 and 3 as well; the write is into the numbers.
        await Runs("""
            figure(1); figure(2); figure(3);
            p = plot(1:3, 1:3);
            p.YData(2) = 9;
            disp(mat2str(p.YData));
            """);

        Assert.Equal(new[] { "[1 9 3]" }, _output.NormalLines);
    }

    // --- 107: marker-chart remainders -----------------------------------------------------------

    [Fact]
    public async Task AColourPerPointDrawsEachPointInItsColour()
    {
        await Runs("""
            figure('Visible', 'off');
            s = scatter(1:3, 1:3, 20, [1 0 0; 0 1 0; 0 0 1], 'filled');
            disp(mat2str(s.CData));
            """);

        Assert.Equal(new[] { "[1 0 0;0 1 0;0 0 1]" }, _output.NormalLines);
        List<Color?> fills = Drawn().MarkerStyles.Select(static m => m.Fill).ToList();
        Assert.Contains(Color.FromRgb(255, 0, 0), fills);
        Assert.Contains(Color.FromRgb(0, 255, 0), fills);
        Assert.Contains(Color.FromRgb(0, 0, 255), fills);
    }

    [Fact]
    public async Task CDataReadsBackAsWrittenUntilSomethingElseColoursTheChart()
    {
        await Runs("""
            figure('Visible', 'off');
            s = scatter(1:4, 1:4, 20, [1; 2; 3; 4]);
            disp(mat2str(size(s.CData)));
            s.CData = [0.1 0.5 0.9];
            disp(mat2str(s.CData));
            s.CDataMode = 'auto';
            disp(mat2str(size(s.CData)));
            """);

        Assert.Equal(new[] { "[4 1]", "[0.1 0.5 0.9]", "[1 3]" }, _output.NormalLines);
        Assert.Null(((ScatterPlot)JG.Gca().Plots[0]).WrittenColorData);
    }

    [Fact]
    public void ASetChannelDropsTheNumbersAsWritten()
    {
        var plot = new ScatterPlot([1.0, 2], [1.0, 2]) { WrittenColorData = new WrittenColorData([0.1, 0.2, 0.3], 1, 3) };
        plot.DataColor = Colors.Black;
        Assert.Null(plot.WrittenColorData);
    }

    [Fact]
    public async Task Scatter3sEdgeColourPaintsTheEdgesAndItsAlphasAreDrawn()
    {
        await Runs("""
            figure('Visible', 'off');
            scatter3(1:3, 1:3, 1:3, 'MarkerEdgeColor', 'r', 'MarkerFaceColor', 'g', 'MarkerFaceAlpha', 0.5);
            """);

        var cloud = Assert.IsType<Scatter3DPlot>(Assert.Single(JG.Gca().Plots));
        Assert.Null(cloud.Color);
        Assert.Equal(Color.FromRgb(255, 0, 0), cloud.EdgeColor);
        MarkerStyle style = Assert.Single(Drawn().MarkerStyles);
        Assert.Equal(Color.FromRgb(255, 0, 0), style.Edge);
        Assert.Equal(Color.FromRgb(0, 255, 0).WithOpacity(0.5), style.Fill);
    }

    [Fact]
    public async Task ABubbleChartInSpaceIsFilledAndPartTransparent()
    {
        await Runs("""
            figure('Visible', 'off');
            q = bubblechart3(1:3, 1:3, 1:3, [1 2 3]);
            disp(q.MarkerFaceColor);
            disp(q.MarkerFaceAlpha);
            """);

        Assert.Equal(new[] { "flat", "0.6" }, _output.NormalLines);
    }

    [Theory]
    [InlineData("q = scatter(1:3, 1:3); q.MarkerFaceAlpha = 2;", "MATLAB:datatypes:OutsideRangeNaN0to1")]
    [InlineData("q = scatter(1:3, 1:3); q.MarkerFaceAlpha = NaN;", "MATLAB:datatypes:OutsideRangeNaN0to1")]
    [InlineData("q = scatter(1:3, 1:3); q.MarkerFaceAlpha = 'bogus';", "MATLAB:hg:dt_conv:Matrix_to_Real:ExpectedNumeric1")]
    [InlineData("q = scatter(1:3, 1:3); q.MarkerFaceAlpha = [0.1 0.2];", "MATLAB:hg:dt_conv:Matrix_to_Real:ExpectedScalar")]
    [InlineData("q = patch([0 1 1], [0 0 1], 'r'); q.FaceAlpha = 2;", "MATLAB:hg:dt_conv:Matrix_to_Real:ValueOutsideRange")]
    [InlineData("q = bar(1:3); q.FaceAlpha = 2;", "MATLAB:datatypes:OutsideRangeNaN0to1")]
    [InlineData("q = scatter(1:3, 1:3, 20, [1 0 0; 0 1 0]);", "MATLAB:scatter:InvalidCData")]
    [InlineData("q = scatter(1:3, 1:3, 20, 'q');", "MATLAB:checkpvpairs:UnrecognizedLineStyleParameter")]
    [InlineData("q = scatter3(1:3, 1:3, 1:3, 20, 'q');", "MATLAB:checkpvpairs:UnrecognizedLineStyleParameter")]
    public async Task AlphasAndColoursAreRefusedInR2025bsWords(string code, string identifier)
    {
        await Runs($$"""
            figure('Visible', 'off');
            try
                {{code}}
                disp('taken');
            catch e
                disp(e.identifier);
            end
            """);

        Assert.Equal(new[] { identifier }, _output.NormalLines);
    }

    [Fact]
    public async Task APatchsAlphasAndTheirWordsAreSaved()
    {
        await Runs("""
            figure('Visible', 'off');
            q = patch([0 1 1], [0 0 1], 'r', 'FaceAlpha', 'flat', 'FaceVertexAlphaData', 0.3, 'EdgeAlpha', 0.4);
            disp(q.FaceAlpha);
            disp(q.FaceVertexAlphaData);
            """);

        Assert.Equal(new[] { "flat", "0.3" }, _output.NormalLines);
        FigureModel back = GraphFormat.Deserialize(GraphFormat.Serialize(JG.Gcf()));
        var patch = Assert.IsType<PatchPlot>(Assert.Single(back.Axes[0].Plots));
        Assert.Equal(PatchAlphaSource.Flat, patch.FaceAlphaSource);
        Assert.Equal(0.4, patch.EdgeAlpha);
    }

    [Fact]
    public async Task AScattersColoursPerPointAndAsWrittenAreSaved()
    {
        await Runs("""
            figure('Visible', 'off');
            scatter(1:2, 1:2, 20, [0.1 0.2 0.3; 0.4 0.5 0.6]);
            """);
        var flat = (ScatterPlot)GraphFormat.Deserialize(GraphFormat.Serialize(JG.Gcf())).Axes[0].Plots[0];

        await Runs("""
            figure('Visible', 'off');
            scatter3(1:2, 1:2, 1:2, 20, [0.1 0.2 0.3; 0.4 0.5 0.6], 'MarkerEdgeAlpha', 0.25);
            """);
        var deep = (Scatter3DPlot)GraphFormat.Deserialize(GraphFormat.Serialize(JG.Gcf())).Axes[0].Plots[0];
        Assert.Equal(2, flat.PointColors!.Count);
        Assert.Equal([0.1, 0.4, 0.2, 0.5, 0.3, 0.6], flat.WrittenColorData!.Values);
        Assert.Equal(2, deep.WrittenColorData!.Rows);
        Assert.Equal(0.25, deep.EdgeAlpha);
    }

    // --- 108: xregion and yregion -------------------------------------------------------------

    [Fact]
    public async Task ARegionShadesUnderTheDataAndWidensTheLimits()
    {
        await Runs("""
            figure('Visible', 'off');
            plot(0:10);
            hold on
            q = xregion(20, 30);
            disp(xlim() * [0; 1] >= 30);
            """);

        Assert.Equal(new[] { "true" }, _output.NormalLines);
        var band = Assert.IsType<ConstantRegionPlot>(JG.Gca().Plots[1]);
        Assert.Same(band, JG.Gca().Plots.InDrawOrder().First());
        Assert.True(Drawn().RectangleCount > 0);
    }

    [Fact]
    public async Task TheValuesComeAsPairsColumnsOrRows()
    {
        await Runs("""
            figure('Visible', 'off');
            a = xregion([1 3], [2 4]);
            b = xregion([1 2; 3 4]);
            c = xregion([1 2; 3 4; 5 6]);
            d = yregion([1; 2]);
            disp(mat2str([size(a); size(b); size(c); size(d)]));
            disp(mat2str([a(2).Value; b(2).Value; c(2).Value; d.Value]));
            """);

        Assert.Equal(new[] { "[2 1;2 1;3 1;1 1]", "[3 4;2 4;3 4;1 2]" }, _output.NormalLines);
    }

    [Fact]
    public async Task ARegionAnswersR2025bsDefaults()
    {
        await Runs("""
            figure('Visible', 'off');
            q = xregion(1, 2);
            disp(q.Type);
            disp(mat2str(q.FaceColor, 6));
            disp(q.FaceAlpha);
            disp(q.EdgeColor);
            disp(q.Layer);
            disp(q.SeriesIndex);
            disp(q.InterceptAxis);
            r = yregion(1, 2, 'FaceColor', 'r', 'Layer', 'top');
            disp(r.FaceColorMode);
            disp(r.InterceptAxis);
            """);

        Assert.Equal(new[] { "constantregion", "[0.490196 0.490196 0.490196]", "0.3", "none", "bottom", "none", "x", "manual", "y" },
            _output.NormalLines);
        Assert.Equal(0, JG.Gca().Plots.OfType<ConstantRegionPlot>().Last().ZOrder);
    }

    [Theory]
    [InlineData("xregion(3);", "MATLAB:graphics:constantline:TooFewInput")]
    [InlineData("xregion([1 2], [3 4 5]);", "MATLAB:graphics:constantline:UnevenVectors")]
    [InlineData("xregion([], []);", "MATLAB:graphics:constantline:EmptyInputs")]
    [InlineData("xregion('a', 2);", "MATLAB:graphics:constantline:InvalidData")]
    [InlineData("xregion(1, 2, 'Bogus', 1);", "MATLAB:hg:InvalidProperty")]
    [InlineData("xregion(1, 2, 'FaceColor');", "MATLAB:class:BadParamValuePairs")]
    [InlineData("q = xregion(1, 2); q.Value = 5;", "MATLAB:graphics:constantline:ValueMustBeTwoElements")]
    [InlineData("q = xregion(1, 2); q.FaceAlpha = 'flat';", "MATLAB:datatypes:invalidZeroToOne")]
    [InlineData("q = xregion(1, 2); q.Layer = 'middle';", "MATLAB:datatypes:InvalidEnumValue")]
    [InlineData("polaraxes; xregion(1, 2);", "MATLAB:handle_graphics:exceptions:HandleGraphicsException")]
    public async Task ARegionIsRefusedInR2025bsWords(string code, string identifier)
    {
        await Runs($$"""
            figure('Visible', 'off');
            try
                {{code}}
                disp('taken');
            catch e
                disp(e.identifier);
            end
            """);

        Assert.Equal(new[] { identifier }, _output.NormalLines);
    }

    [Fact]
    public void ARegionIsSaved()
    {
        FigureModel figure = JG.Figure();
        ConstantRegionPlot band = JG.YRegion(2, -1);
        band.FaceColor = Color.FromRgb(255, 0, 0);
        band.OnTop = true;
        band.SeriesIndex = 1;

        var back = Assert.IsType<ConstantRegionPlot>(
            Assert.Single(GraphFormat.Deserialize(GraphFormat.Serialize(figure)).Axes[0].Plots));
        Assert.Equal((2.0, -1.0), (back.First, back.Second));
        Assert.Equal(ConstantLineDirection.Horizontal, back.Direction);
        Assert.Equal(Color.FromRgb(255, 0, 0), back.FaceColor);
        Assert.True(back.OnTop);
        Assert.Equal(0, back.ZOrder);
    }

    [Fact]
    public async Task TheJgsDialectDrawsRegionsToo()
    {
        ScriptRunResult result = await new JgsScriptEngine().RunAsync("""
            figure();
            let q = xregion(1, 2);
            disp(get(q, "FaceAlpha"));
            """, new ScriptContext(_output, static (_, _) => { }), default);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Single(JG.Gca().Plots.OfType<ConstantRegionPlot>());
    }
}
