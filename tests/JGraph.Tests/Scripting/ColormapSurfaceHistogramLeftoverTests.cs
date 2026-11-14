using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Maths;
using JGraph.Maths.Transforms;
using JGraph.Objects;
using JGraph.Rendering;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Chips wave 4, agent V (ADR 0361): open item 293 (what a colormap reads back, generators at the
/// figure's height, the figure's map taking its axes back, a bare prism), 314 (a surface's and a
/// patch's default readings, a surface beside a log ruler) and 271 (a histogram's limits set
/// afterwards keep its width; the rules under limits are histcounts.m's). Every expectation is
/// R2025b's (probes cm1, cm2, sf1, hp1, hp2); the fixtures oi_colormap_reads,
/// oi_surface_patch_defaults and oi_histogram_bin_limits hold the full recordings.
/// </summary>
[Collection("JG facade")]
public sealed class ColormapSurfaceHistogramLeftoverTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "jgraph-v4-" + Guid.NewGuid().ToString("N"));

    public ColormapSurfaceHistogramLeftoverTests()
    {
        JG.Reset();
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        JG.Reset();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary directory is not worth failing a test over.
        }
    }

    private async Task Asserts(string code)
    {
        await using IScriptSession session = Assert
            .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
            .CreateSession(new ScriptContext(
                _output, (_, _) => { }, _directory, resolvePath: null, new TestFigureFiles()));
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    // --- 271: histogram limits ---------------------------------------------------------------------

    [Fact]
    public void TheRulesUnderLimitsFitWholeBinsToTheSpan()
    {
        // Scott's width over the readings inside, then ceil(span / width) bins spaced by linspace.
        double[] x = [.. Enumerable.Range(0, 2000).Select(i => System.Math.Sin(i * 0.37) * 3)];
        double[] scott = Binning.EdgesFor(x, null, null, [-1, 2], "scott");
        Assert.Equal(-1, scott[0]);
        Assert.Equal(2, scott[^1]);
        Assert.True(scott.Length > 2);
        Assert.Equal(scott[1] - scott[0], scott[^1] - scott[^2], 12);

        // The integer rule under limits: the limits, then the half-integers inside them.
        Assert.Equal([-1, -0.5, 0.5, 1.5, 2], Binning.EdgesFor([0, 1, 1, 2], null, null, [-1, 2], "integers"));
        Assert.Equal([0.5, 1.5, 2.5, 3], Binning.EdgesFor([1, 1, 1, 5], null, null, [0.5, 3], "auto"));

        // A width under limits steps by the colon and closes the last bin on the high limit.
        double[] stepped = Binning.EdgesFor(x, null, 0.7, [-1, 2], "auto");
        double[] expected = [-1, -0.3, 0.4, 1.1, 1.8, 2];
        Assert.Equal(expected.Length, stepped.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], stepped[i], 12);
        }

        // Constant readings inside are one bin; equal limits are edges on the one value.
        Assert.Equal([1.4, 1.6], Binning.EdgesFor([1, 2, 3, 1.5], null, null, [1.4, 1.6], "auto"));
        Assert.Equal([2, 2, 2, 2], Binning.EdgesFor([1, 2, 3], 3, null, [2, 2], "auto"));
    }

    [Fact]
    public void ABinWidthOfTenthsIsR2025bsProductNotTheDecimal() =>

        // Seven bins over [-4.15, 4.8]: binpicker's width is 0.1 * 14 = 1.4000000000000001, so the fourth
        // edge is -0.7999999999999998 and not -0.8000000000000007 (hp1's h_n7).
        Assert.Equal(-5 + (3 * (0.1 * 14)), Binning.CountedEdges(-4.15, 4.79, 7)[3]);

    [Fact]
    public async Task AHistogramsLimitsSetAfterwardsKeepItsWidth() => await Asserts("""
        x = sqrt(2)*erfinv(2*((1:30000)-0.5)/30000-1);
        f = figure('Visible', 'off');
        h = histogram(x, 'BinWidth', 0.5); h.BinLimits = [-1 2];
        assert(isequal(h.BinEdges, -1:0.5:2) && strcmp(h.BinMethod, 'manual'));
        h = histogram(x, 'BinWidth', 0.7); h.BinLimits = [-1 2];
        assert(max(abs(h.BinEdges - [-1 -0.3 0.4 1.1 1.8 2])) < 1e-12 && abs(h.BinWidth - 0.7) < 1e-12);
        h.BinLimits = [-1.5 2.5];
        assert(h.NumBins == 6 && abs(h.BinWidth - 0.7) < 1e-12);
        h.BinLimitsMode = 'auto';
        assert(h.NumBins == 12 && strcmp(h.BinLimitsMode, 'auto') && strcmp(h.BinMethod, 'manual'));
        h = histogram(x, [-4 -1 0 2 5]);
        assert(strcmp(h.BinWidth, 'nonuniform') && strcmp(h.BinLimitsMode, 'manual'));
        h.BinLimits = [-1 2];
        assert(max(abs(h.BinEdges - [-1 -0.25 0.5 1.25 2])) < 1e-15);
        h = histogram(x); h.BinLimits = [-1 2];
        assert(h.NumBins == 31 && strcmp(h.BinMethod, 'manual'));
        h = histogram(x, 'BinLimits', [-1 2]);
        assert(h.NumBins == 35 && strcmp(h.BinMethod, 'auto'));
        h = histogram(x, 'BinLimits', [-1 2], 'BinMethod', 'sqrt');
        assert(h.NumBins == 157);
        h = histogram(x); h.BinLimits = [-1 2]; h.BinWidth = 0.4;
        assert(strcmp(h.BinWidth, 'nonuniform') && h.NumBins == 8);
        close(f);
        """);

    [Fact]
    public async Task Histcounts2ReadsThePairsInsideBothLimits() => await Asserts("""
        x = sqrt(2)*erfinv(2*((1:5000)-0.5)/5000-1);
        y = mod((1:5000)*0.618034, 1)*7 - 2;
        [~, xe, ye] = histcounts2(x, y, 'XBinLimits', [-1 2], 'YBinLimits', [0 3], 'BinWidth', [0.7 1]);
        assert(numel(xe) == 6 && max(abs(xe - [-1 -0.3 0.4 1.1 1.8 2])) < 1e-12 && isequal(ye, [0 1 2 3]));
        [~, xe] = histcounts2(x, y, 'XBinLimits', [-1 2], 'YBinLimits', [0 3]);
        assert(xe(1) == -1 && xe(end) == 2);
        """);

    // --- 293: colormap reads -------------------------------------------------------------------------

    [Fact]
    public async Task AGeneratorNamedWithoutASizeTakesTheFiguresHeight() => await Asserts("""
        f = figure('Visible', 'off');
        ax = axes(f);
        assert(isequal(size(f.Colormap), [256 3]) && isequal(get(gca, 'Colormap'), parula(256)));
        colormap(jet(5));
        assert(isequal(f.Colormap, jet(5)) && isequal(ax.Colormap, jet(5)));
        colormap('hot');
        assert(isequal(colormap, hot(5)) && isequal(f.Colormap, hot(5)));
        c = colormap('gray');
        assert(isequal(c, gray(5)));
        c = colormap('hot(3)');
        assert(isequal(c, hot(3)));
        colormap('default');
        assert(isequal(f.Colormap, parula(256)));
        close(f);
        """);

    [Fact]
    public async Task AFiguresMapTakesItsAxesBack() => await Asserts("""
        f = figure('Visible', 'off');
        ax = axes(f);
        assert(strcmp(ax.ColormapMode, 'auto'));
        colormap(ax, 'copper');
        assert(strcmp(ax.ColormapMode, 'manual') && isequal(ax.Colormap, copper(256)));
        colormap(f, parula(7));
        assert(strcmp(ax.ColormapMode, 'auto') && isequal(ax.Colormap, parula(7)));
        assert(isequal(size(bone), [7 3]));
        ax.Colormap = summer(3);
        f.Colormap = hot(4);
        assert(isequal(ax.Colormap, hot(4)));
        close(f);
        """);

    [Fact]
    public async Task ABarePrismRecoloursTheAxesLines() => await Asserts("""
        f = figure('Visible', 'off');
        plot(1:3); hold on; plot(2:4); plot(3:5); hold off;
        prism;
        ch = get(gca, 'Children');
        assert(isequal(round(vertcat(ch.Color), 2), [1 0 0; 1 0.5 0; 1 1 0]));
        assert(isequal(size(prism), [256 3]) && isequal(prism(4), [1 0 0; 1 0.5 0; 1 1 0; 0 1 0]));
        close(f);
        """);

    // --- 314: surface and patch readings -------------------------------------------------------------

    [Fact]
    public async Task ASurfacesDefaultsAreR2025bs() => await Asserts("""
        f = figure('Visible', 'off');
        Z = [1 2 3; 4 5 6];
        [X, Y] = meshgrid(1:3, 10:11);
        s = surface(Z);
        assert(isequal(s.EdgeColor, [33 33 33] / 255) && s.LineWidth == 0.5);
        assert(isequal(s.XData, 1:3) && isequal(s.YData, [1; 2]));
        assert(strcmp(s.XDataMode, 'manual') && strcmp(s.CDataMode, 'manual'));
        cla; s = surf(Z);
        assert(isequal(s.XData, 1:3) && strcmp(s.XDataMode, 'auto') && strcmp(s.CDataMode, 'auto'));
        cla; s = surf(X, Y, Z);
        assert(isequal(s.XData, X) && isequal(s.YData, Y));
        s.XData = [5 6 7];
        assert(isequal(s.XData, [5 6 7]) && isequal(s.YData, Y));
        cla; s = mesh(Z);
        assert(strcmp(s.EdgeColor, 'flat') && strcmp(s.FaceLighting, 'none') && strcmp(s.EdgeLighting, 'flat'));
        cla; p = patch([0 1 1], [0 0 1], 'r');
        assert(isequal(squeeze(p.CData)', [33 33 33] / 255) && strcmp(p.CDataMode, 'auto'));
        cla; p = patch([0 1 1], [0 0 1], 5);
        assert(strcmp(p.CDataMode, 'manual'));
        close(f);
        """);

    [Fact]
    public void ALogRulerIsReadInDecadesByTheFlatCamera()
    {
        var area = new Rect2D(0, 0, 300, 200);
        var above = new Projection3D(
            new DataRange(1, 100), new DataRange(0, 1), new DataRange(-1, 1), 0, 90, area, new Vector3D(300, 200, 200));
        Projection3D logged = above.WithLogarithmicRulers(logX: true, logY: false);

        // Ten is half-way along a ruler from 1 to 100 read in decades, as the log ruler's ticks are.
        Assert.Equal(area.CenterX, logged.ProjectPoint(10, 0.5, 0).X, 9);
        Assert.Equal(above.ProjectPoint(1, 0.5, 0).X, logged.ProjectPoint(1, 0.5, 0).X, 9);
        Assert.Equal(above.ProjectPoint(100, 0.5, 0).X, logged.ProjectPoint(100, 0.5, 0).X, 9);
        Assert.Equal(10, logged.Denormalize(logged.Normalize(10, 0.5, 0)).X, 9);
    }

    [Fact]
    public void ASurfaceBesideALogRulerInAFlatAxesIsDrawn()
    {
        var surface = new SurfacePlot([1, 10, 100], [0, 1], new double[2, 3]);
        var axes = new AxesModel();
        axes.PrimaryXAxis.Scale = AxisScaleType.Logarithmic;
        axes.Plots.Add(surface);
        var figure = new FigureModel();
        figure.Axes.Add(axes);
        var context = new RecordingRenderContext(new Size2D(640, 480));

        new FigureRenderer().Render(figure, context);

        Assert.True(context.TotalTriangleVertices > 0);
    }
}
