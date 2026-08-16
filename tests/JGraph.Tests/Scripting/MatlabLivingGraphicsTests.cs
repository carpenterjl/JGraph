using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Objects;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M67 wave B: the objects a living figure is built from — an animated line, a rectangle in the
/// data's own coordinates, the root, groups and transforms, and the small verbs beside them.
/// </summary>
[Collection("JG facade")]
public class MatlabLivingGraphicsTests : IDisposable
{
    private readonly MatlabScriptEngine _engine = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory;

    public MatlabLivingGraphicsTests()
    {
        JG.Reset();
        _directory = Path.Combine(Path.GetTempPath(), "jgraph-tests", Path.GetRandomFileName());
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
        }

        GC.SuppressFinalize(this);
    }

    private ScriptRunResult Run(string code) =>
        _engine.RunAsync(
            code,
            new ScriptContext(
                _output,
                (_, _) => { },
                _directory,
                resolvePath: null,
                figureFiles: new TestFigureFiles()),
            CancellationToken.None).GetAwaiter().GetResult();

    private string Printed(ScriptRunResult result) => result.Message + _output.ErrorText;

    private void Ok(ScriptRunResult result) => Assert.True(result.Success, Printed(result));

    private string RunAndRead(string code)
    {
        Ok(Run(code));
        return _output.NormalText;
    }

    private string Error(string code)
    {
        ScriptRunResult result = Run(code);
        Assert.False(result.Success, "expected a refusal, got: " + _output.NormalText);
        return Printed(result);
    }

    // --- animatedline -------------------------------------------------------------------------------

    [Fact]
    public void PointsAccumulateOnAnAnimatedLine()
    {
        Assert.Equal("10 100\n", RunAndRead("""
            h = animatedline;
            for k = 1:10
                addpoints(h, k, k^2);
            end
            [x, y] = getpoints(h);
            fprintf('%d %g\n', numel(x), y(end));
            """));
    }

    [Fact]
    public void ABareAnimatedlineMakesALineRatherThanNamingTheVerb()
    {
        Ok(Run("h = animatedline; addpoints(h, 1, 1);"));
        Assert.IsType<LinePlot>(Assert.Single(JG.CurrentFigure.Axes[^1].Plots));
    }

    [Fact]
    public void TheOldestPointsFallOffOnceTheLineIsFull()
    {
        // MaximumNumPoints keeps the newest, which is what makes a rolling trace roll.
        Assert.Equal("3 8\n", RunAndRead("""
            h = animatedline('MaximumNumPoints', 3);
            addpoints(h, 1:10, (1:10).^2);
            x = getpoints(h);
            fprintf('%d %g\n', numel(x), x(1));
            """));
    }

    [Fact]
    public void ClearpointsEmptiesTheLineWithoutRemovingIt()
    {
        Assert.Equal("0 1\n", RunAndRead("""
            h = animatedline(1:5, 1:5);
            clearpoints(h);
            fprintf('%d %d\n', numel(getpoints(h)), numel(get(gca, 'Children')));
            """));
    }

    [Fact]
    public void AThreeCoordinateLineKeepsItsThirdCoordinate()
    {
        Assert.Equal("3 2\n", RunAndRead("""
            h = animatedline([0 1], [0 1], [0 1]);
            addpoints(h, 2, 2, 2);
            [x, ~, z] = getpoints(h);
            fprintf('%d %g\n', numel(x), z(end));
            """));
    }

    [Fact]
    public void AFlatLineRefusesAThirdCoordinateRatherThanDroppingIt()
    {
        Assert.Contains("made flat", Error("h = animatedline; addpoints(h, 1, 1, 1);"));
    }

    [Fact]
    public void ASpatialLineRefusesPointsWithoutAThirdCoordinate()
    {
        Assert.Contains("need a z", Error("h = animatedline([0 1], [0 1], [0 1]); addpoints(h, 2, 2);"));
    }

    [Fact]
    public void GetpointsRefusesToInventACoordinateTheLineHasNot()
    {
        Assert.Contains("2 coordinates", Error("h = animatedline(1:3, 1:3); [x, y, z] = getpoints(h);"));
    }

    // --- rectangle ----------------------------------------------------------------------------------

    [Fact]
    public void ARectangleIsFourCornersInTheDatasOwnCoordinates()
    {
        Ok(Run("figure(1); rectangle('Position', [1 2 3 4]);"));
        var patch = Assert.IsType<PatchPlot>(Assert.Single(JG.CurrentFigure.Axes[^1].Plots));
        Assert.Equal(4, patch.X.Count);
        Assert.Equal(1, patch.X.Min());
        Assert.Equal(4, patch.X.Max());
        Assert.Equal(6, patch.Y.Max());
    }

    [Fact]
    public void AFullCurvatureRoundsTheCornersRightOff()
    {
        Ok(Run("figure(1); rectangle('Position', [0 0 2 2], 'Curvature', [1 1]);"));
        var patch = Assert.IsType<PatchPlot>(Assert.Single(JG.CurrentFigure.Axes[^1].Plots));

        // A square curved all the way is a circle, so every vertex is the same distance from the
        // middle — which is the claim that tells a rounded rectangle from a square one.
        Assert.True(patch.X.Count > 4);
        for (int i = 0; i < patch.X.Count; i++)
        {
            double radius = System.Math.Sqrt(
                ((patch.X[i] - 1) * (patch.X[i] - 1)) + ((patch.Y[i] - 1) * (patch.Y[i] - 1)));
            Assert.Equal(1.0, radius, 9);
        }
    }

    [Fact]
    public void ARectangleWithNoFaceColourIsAnOutline()
    {
        Ok(Run("figure(1); rectangle('Position', [0 0 1 1]);"));
        var patch = Assert.IsType<PatchPlot>(Assert.Single(JG.CurrentFigure.Axes[^1].Plots));
        Assert.False(patch.FaceVisible);
    }

    [Fact]
    public void ARectangleWithNoSizeIsRefused()
    {
        Assert.Contains("positive width", Error("rectangle('Position', [0 0 0 1]);"));
    }

    // --- the root, reset and the axes constructor -----------------------------------------------------

    [Fact]
    public void TheRootAnswersForTheScreen()
    {
        Assert.Equal("root 1\n", RunAndRead("""
            s = get(groot, 'ScreenSize');
            fprintf('%s %d\n', get(groot, 'Type'), numel(s) == 4 && s(3) > 0);
            """));
    }

    [Fact]
    public void AxesMakesOneAndSelectsIt()
    {
        Assert.Equal("axes 1 2\n", RunAndRead("""
            figure(1); plot(1:3);
            ax = axes;
            fprintf('%s %d %d\n', get(ax, 'Type'), ax == gca, numel(get(gcf, 'Children')));
            """));
    }

    [Fact]
    public void AxesWithAHandleSelectsTheOneItNames()
    {
        Assert.Equal("1\n", RunAndRead("""
            figure(1); first = gca; second = axes;
            axes(first);
            fprintf('%d\n', gca == first);
            """));
    }

    [Fact]
    public void ResetEmptiesAnAxes()
    {
        Assert.Equal("0\n", RunAndRead("""
            figure(1); plot(1:3); reset(gca);
            fprintf('%d\n', numel(get(gca, 'Children')));
            """));
    }

    // --- frames both ways ----------------------------------------------------------------------------

    [Fact]
    public void AFrameGoesToAPictureAndBack()
    {
        Assert.Equal("1 1\n", RunAndRead("""
            figure(1); plot(1:10); f = getframe;
            im = frame2im(f);
            back = im2frame(im);
            fprintf('%d %d\n', isequal(size(im), size(f.cdata)), isequal(back.cdata, im));
            """));
    }

    [Fact]
    public void AnIndexedPictureBecomesTheColoursItNames()
    {
        // im2frame with a colour table is the one place the table is read, because a frame has
        // nowhere to keep one.
        Assert.Equal("2 2 3 255 0\n", RunAndRead("""
            f = im2frame([1 2; 2 1], [1 0 0; 0 0 1]);
            c = f.cdata;
            fprintf('%d %d %d %d %d\n', size(c, 1), size(c, 2), size(c, 3), c(1,1,1), c(1,1,3));
            """));
    }

    [Fact]
    public void SomethingThatIsNotAFrameIsRefused()
    {
        Assert.Contains("getframe", Error("frame2im(struct('a', 1));"));
    }

    // --- waitfor ------------------------------------------------------------------------------------

    [Fact]
    public void WaitforReturnsBecauseThereIsNobodyToWaitFor()
    {
        Ok(Run("figure(1); waitfor(gcf); waitfor(gca, 'XLim'); disp('through');"));
        Assert.Contains("through", _output.NormalText);
    }

    [Fact]
    public void WaitforStillChecksWhatItWasAskedToWaitOn()
    {
        Assert.Contains("no 'Wobble'", Error("figure(1); waitfor(gca, 'Wobble');"));
    }

    // --- groups and transforms -----------------------------------------------------------------------

    [Fact]
    public void AGroupCollectsTheObjectsGivenToIt()
    {
        Assert.Equal("hggroup 2\n", RunAndRead("""
            figure(1); a = plot(1:3); b = plot(4:6);
            g = hggroup;
            set(a, 'Parent', g); set(b, 'Parent', g);
            fprintf('%s %d\n', get(g, 'Type'), numel(get(g, 'Children')));
            """));
    }

    [Fact]
    public void HidingAGroupHidesWhatIsInIt()
    {
        Assert.Equal("off off\n", RunAndRead("""
            figure(1); a = plot(1:3);
            g = hggroup; set(a, 'Parent', g);
            set(g, 'Visible', 'off');
            fprintf('%s %s\n', get(g, 'Visible'), get(a, 'Visible'));
            """));
    }

    [Fact]
    public void ATransformMovesItsMembers()
    {
        Assert.Equal("11 12 13\n", RunAndRead("""
            figure(1); a = plot([1 2 3], [0 0 0]);
            t = hgtransform; set(a, 'Parent', t);
            set(t, 'Matrix', makehgtform('translate', [10 0 0]));
            x = get(a, 'XData');
            fprintf('%g %g %g\n', x(1), x(2), x(3));
            """));
    }

    [Fact]
    public void SettingTheMatrixAgainIsNotCumulative()
    {
        // The group remembers where its members started, which is what makes a matrix set in a loop
        // an animation rather than a drift.
        Assert.Equal("21\n", RunAndRead("""
            figure(1); a = plot([1 2 3], [0 0 0]);
            t = hgtransform; set(a, 'Parent', t);
            set(t, 'Matrix', makehgtform('translate', [10 0 0]));
            set(t, 'Matrix', makehgtform('translate', [20 0 0]));
            x = get(a, 'XData');
            fprintf('%g\n', x(1));
            """));
    }

    [Fact]
    public void APlainGroupHasNoMatrix()
    {
        Assert.Contains("no matrix", Error("g = hggroup; set(g, 'Matrix', eye(4));"));
    }

    [Fact]
    public void AnObjectCannotBeGivenAParentThatCannotHoldIt()
    {
        Assert.Contains("belongs to an axes or a group",
            Error("figure(1); a = plot(1:3); set(a, 'Parent', gcf);"));
    }

    // --- the small chart forms ------------------------------------------------------------------------

    [Theory]
    [InlineData("semilogx", "XScale")]
    [InlineData("semilogy", "YScale")]
    [InlineData("loglog", "YScale")]
    public void ALogPlotOfOneVectorCountsAlongTheWholeNumbers(string verb, string scale)
    {
        Assert.Equal($"10 10 log\n", RunAndRead($$"""
            figure(1);
            h = {{verb}}((1:10).^2);
            x = get(h, 'XData');
            fprintf('%d %g %s\n', numel(x), x(end), get(gca, '{{scale}}'));
            """));
    }

    [Fact]
    public void ALogPlotOfOneVectorStillTakesALineSpec()
    {
        // A word in the second slot is the style, not the y data — which is the one ambiguity the
        // shorter form introduces, and the reason it is told apart by type rather than by counting.
        Assert.Equal("5\n", RunAndRead("""
            figure(1);
            h = semilogy([1 10 100 1000 10000], 'r--');
            fprintf('%d\n', numel(get(h, 'YData')));
            """));
    }

    [Fact]
    public void ReparentingMovesAPlotBetweenAxes()
    {
        Assert.Equal("0 1\n", RunAndRead("""
            figure(1); first = gca; a = plot(1:3);
            second = axes;
            set(a, 'Parent', second);
            fprintf('%d %d\n', numel(get(first, 'Children')), numel(get(second, 'Children')));
            """));
    }
}
