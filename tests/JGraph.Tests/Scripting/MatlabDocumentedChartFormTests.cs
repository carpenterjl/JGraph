using JGraph.Api;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Objects;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The documented argument forms M69's syntax-form probe recorded as refused, and M70.B added
/// (<c>image</c>, <c>imagesc</c>, <c>pcolor</c>, <c>subplot</c>, <c>tiledlayout</c>, <c>errorbar</c>).
/// <para>
/// Each of these is a form MATLAB's own reference documents and this build answered with an arity
/// refusal naming the verb — the one signal in M69's `error` bucket that cannot be the prober's own
/// sample being wrong. What is asserted here is therefore the *model* each form reached, not merely
/// that the call stopped refusing: an accepted call that drew the wrong thing would be the worse
/// outcome of the two.
/// </para>
/// </summary>
[Collection("JG facade")]
public class MatlabDocumentedChartFormTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabDocumentedChartFormTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private async Task Run(string code)
    {
        await using IScriptSession session = Assert
            .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
            .CreateSession(new ScriptContext(_output, (number, figure) => _figures.Add((number, figure))));
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    private static T Single<T>()
        where T : PlotObject => Assert.Single(JG.Gca().Plots.OfType<T>());

    // --- image and imagesc ----------------------------------------------------------------------

    [Fact]
    public async Task Image_TakesTheXAndYSpansTheRasterCovers()
    {
        await Run("image([10 20], [5 9], [1 2; 3 4]);");

        ImagePlot plot = Single<ImagePlot>();

        // MATLAB reads only the first and last element of x and y, whatever length they are: they
        // give the two ends of the span, not one coordinate per column.
        Assert.Equal(10, plot.XExtent.Min);
        Assert.Equal(20, plot.XExtent.Max);
        Assert.Equal(5, plot.YExtent.Min);
        Assert.Equal(9, plot.YExtent.Max);
    }

    [Fact]
    public async Task Image_TakesTheNameValueSpellingOfTheSameThing()
    {
        await Run("image('XData', [10 20], 'YData', [5 9], 'CData', [1 2; 3 4]);");

        ImagePlot plot = Single<ImagePlot>();
        Assert.Equal(10, plot.XExtent.Min);
        Assert.Equal(9, plot.YExtent.Max);
    }

    [Fact]
    public async Task Imagesc_TakesTheColourLimitsAsATrailingPair()
    {
        await Run("imagesc([1 2; 3 4], [0 10]);");

        ImagePlot plot = Single<ImagePlot>();
        Assert.False(plot.AutoScaleColor);
        Assert.Equal(0, plot.ColorMin);
        Assert.Equal(10, plot.ColorMax);
    }

    [Fact]
    public async Task ImageWithoutLimits_KeepsScalingItsOwnColour()
    {
        await Run("imagesc([1 2; 3 4]);");
        Assert.True(Single<ImagePlot>().AutoScaleColor);
    }

    [Fact]
    public Task AnUnknownNameValuePair_SaysWhatItTakes() => Run("""
        ok = false;
        try
            image('ZData', [1 2; 3 4]);
        catch err
            ok = ~isempty(strfind(err.message, 'CData'));
        end
        assert(ok);
        """);

    // --- pcolor ---------------------------------------------------------------------------------

    [Fact]
    public async Task Pcolor_GeneratesTheGridItsCellsSitOnWhenGivenOnlyTheMatrix()
    {
        await Run("pcolor([1 2 3; 4 5 6]);");

        // pcolor draws through the image path in this build, so what it lands on is the raster and
        // the span it covers. The generated grid is the one meshgrid would have made: the cells sit
        // on their own column and row numbers, so a 2-by-3 matrix spans x 1..3 and y 1..2.
        ImagePlot cells = Single<ImagePlot>();
        Assert.Equal(1, cells.XExtent.Min);
        Assert.Equal(3, cells.XExtent.Max);
        Assert.Equal(1, cells.YExtent.Min);
        Assert.Equal(2, cells.YExtent.Max);
    }

    // --- subplot and tiledlayout ----------------------------------------------------------------

    [Fact]
    public Task Subplot_AcceptsTheTwoWordsThatNameHowThePanelIsMade() => Run("""
        subplot(2, 2, 1, 'replace');
        subplot(2, 2, 2, 'align');
        ok = false;
        try
            subplot(2, 2, 3, 'sideways');
        catch err
            ok = ~isempty(strfind(err.message, 'replace'));
        end
        assert(ok);
        """);

    [Fact]
    public async Task TiledlayoutFlow_GrowsItsGridAsTilesAreAskedFor()
    {
        await Run("""
            tiledlayout('flow');
            for k = 1:4
                nexttile;
                plot(1:3, 1:3);
            end
            """);

        // Four tiles asked for, four axes made — the point of 'flow' being that the count is not
        // known when the layout is declared.
        Assert.Equal(4, _figures[^1].Figure.Axes.Count);
    }

    [Fact]
    public Task Tiledlayout_RefusesAWordItDoesNotKnow() => Run("""
        ok = false;
        try
            tiledlayout('sideways');
        catch err
            ok = ~isempty(strfind(err.message, 'flow'));
        end
        assert(ok);
        """);

    // --- the surface colour grid ------------------------------------------------------------------

    [Fact]
    public async Task Surf_ColoursByATrailingArrayRatherThanByHeight()
    {
        await Run("""
            Z = [1 2; 3 4];
            C = [10 20; 30 40];
            surf(Z, C);
            """);

        SurfacePlot surface = Single<SurfacePlot>();
        Assert.NotNull(surface.CData);
        Assert.Equal(10, surface.CData![0, 0]);

        // The colour range spans C, not Z. Spanning Z would squeeze every value of C into whatever
        // part of the colormap the heights happened to occupy — the same picture as no C at all.
        Assert.Equal(10, surface.ColorRange.Min);
        Assert.Equal(40, surface.ColorRange.Max);
    }

    [Fact]
    public async Task TheFourArgumentFormReachesTheSamePlace()
    {
        await Run("""
            [X, Y] = meshgrid(1:2, 1:2);
            surf(X, Y, [1 2; 3 4], [10 20; 30 40]);
            """);

        Assert.Equal(40, Single<SurfacePlot>().CData![1, 1]);
    }

    [Fact]
    public Task EveryVerbInTheSurfaceFamilyReadsIt() => Run("""
        Z = [1 2; 3 4];
        C = [10 20; 30 40];
        mesh(Z, C); meshc(Z, C); meshz(Z, C); surfc(Z, C);
        [X, Y] = meshgrid(1:2, 1:2);
        surface(X, Y, Z, C);
        """);

    [Fact]
    public async Task Meshz_GivesItsSkirtTheColourOfTheEdgeItHangsFrom()
    {
        // The skirt makes the drawn grid two rows and columns bigger than Z, so a C of exactly the
        // documented size has to be grown rather than refused.
        await Run("meshz([1 2; 3 4], [10 20; 30 40]);");

        SurfacePlot surface = Single<SurfacePlot>();
        Assert.Equal(surface.Z.GetLength(0), surface.CData!.GetLength(0));
        Assert.Equal(10, surface.CData[0, 0]);   // the skirt corner took the corner it hangs from
    }

    [Fact]
    public Task AColourGridOfTheWrongSize_SaysSoRatherThanDrawingSomethingElse() => Run("""
        ok = false;
        try
            surf([1 2; 3 4], [1 2 3; 4 5 6]);
        catch err
            ok = ~isempty(strfind(err.message, 'one value per grid vertex'));
        end
        assert(ok);
        """);

    [Fact]
    public Task Surfl_ReadsItsSecondArgumentAsALightAndNotAsColour() => Run("""
        % surfl's second argument is the light source's direction in MATLAB, so it must not be
        % swallowed as colour data by the shared dispatcher the rest of the family uses.
        surfl([1 2; 3 4]);
        ok = false;
        try
            surfl([1 2; 3 4], [10 20; 30 40]);
        catch err
            ok = true;
        end
        assert(ok);
        """);

    // --- errorbar -------------------------------------------------------------------------------

    [Fact]
    public async Task Errorbar_PutsTheSamplesOnTheirOwnPositionsWhenGivenOnlyYAndTheError()
    {
        await Run("errorbar([2 4 3], [0.1 0.2 0.3]);");

        ErrorBarPlot bars = Single<ErrorBarPlot>();
        Assert.Equal(3, bars.ErrorNeg.Count);
        Assert.Equal(bars.ErrorNeg, bars.ErrorPos);   // one array means symmetric
    }

    [Fact]
    public async Task Errorbar_ReachesADifferentDistanceBelowAndAbove()
    {
        await Run("errorbar(1:3, [2 4 3], [0.1 0.2 0.3], [0.5 0.6 0.7]);");

        ErrorBarPlot bars = Single<ErrorBarPlot>();
        Assert.Equal(0.1, bars.ErrorNeg[0], 12);
        Assert.Equal(0.5, bars.ErrorPos[0], 12);
    }

    [Fact]
    public async Task Errorbar_ReadsATrailingLineSpec()
    {
        await Run("errorbar(1:3, [2 4 3], [0.1 0.2 0.3], 'ro');");

        ErrorBarPlot bars = Single<ErrorBarPlot>();
        Assert.Equal(Colors.Red, bars.Color);
        Assert.Equal(MarkerType.Circle, bars.Marker);
        Assert.False(bars.ShowLine);   // a marker with no dash is markers alone, as in plot
    }

    [Fact]
    public Task Errorbar_RefusesAHorizontalWhiskerByNameRatherThanDrawingAVerticalOne() => Run("""
        errorbar(1:3, [2 4 3], [0.1 0.2 0.3], 'vertical');   % the direction that is drawn
        ok = false;
        try
            errorbar(1:3, [2 4 3], [0.1 0.2 0.3], 'horizontal');
        catch err
            ok = ~isempty(strfind(err.message, 'along x'));
        end
        assert(ok);
        """);
}
