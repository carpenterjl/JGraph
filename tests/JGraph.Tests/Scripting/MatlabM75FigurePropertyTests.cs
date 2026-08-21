using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M75: the figure's own forty-two names and the axes layout family. Every behavior here was probed
/// at the CLI before it was written down, and the pixel-level proofs live in stess_47.m; these tests
/// pin the property semantics.
/// </summary>
[Collection("JG facade")]
public class MatlabM75FigurePropertyTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory;

    public MatlabM75FigurePropertyTests()
    {
        JG.Reset();

        // Half of this wave is about files — a printed page, a saved picture, a figure exported so
        // the renderer has measured a layout to report. A host with no file services could not
        // exercise any of it, so this one has the same services the launcher has.
        _directory = Path.Combine(Path.GetTempPath(), "jgraph-m75", Path.GetRandomFileName());
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
    }

    private Task<ScriptRunResult> RunMatlab(string code) =>
        new MatlabScriptEngine().RunAsync(
            code,
            new ScriptContext(
                _output, static (_, _) => { }, _directory, null, new TestFigureFiles()),
            default);

    private static double Number(ScriptRunResult result, string name) =>
        Assert.IsType<double>(Assert.Single(result.Variables, v => v.Name == name).RawValue);

    private static double[] Row(ScriptRunResult result, string name) =>
        Assert.IsType<double[]>(Assert.Single(result.Variables, v => v.Name == name).RawValue);

    private static string Text(ScriptRunResult result, string name) =>
        Assert.IsType<string>(Assert.Single(result.Variables, v => v.Name == name).RawValue);

    private static bool Truth(ScriptRunResult result, string name) =>
        Assert.IsType<bool>(Assert.Single(result.Variables, v => v.Name == name).RawValue);

    private static void Succeeded(ScriptRunResult result) =>
        Assert.True(result.Success, result.Message);

    // --- The axes layout family -----------------------------------------------------------------

    [Fact]
    public async Task TheOuterRectangleIsThePlotBoxPlusTheMarginTheTextClaimed()
    {
        ScriptRunResult result = await RunMatlab("""
            plot(1:5); title('t'); xlabel('x'); ylabel('y');
            exportgraphics(gcf, 'layout-parity.png');
            inner = get(gca, 'Position');
            outer = get(gca, 'OuterPosition');
            inset = get(gca, 'TightInset');
            """);

        Succeeded(result);
        double[] inner = Row(result, "inner");
        double[] outer = Row(result, "outer");
        double[] inset = Row(result, "inset");

        // The three rectangles are one statement said three ways, and this is the statement.
        Assert.Equal(inner[0] - inset[0], outer[0], 9);
        Assert.Equal(inner[1] - inset[1], outer[1], 9);
        Assert.Equal(inner[2] + inset[0] + inset[2], outer[2], 9);
        Assert.Equal(inner[3] + inset[1] + inset[3], outer[3], 9);

    }

    [Fact]
    public async Task PositionAndInnerPositionAreTwoNamesForTheSameRectangle()
    {
        ScriptRunResult result = await RunMatlab("""
            plot(1:5);
            exportgraphics(gcf, 'layout-alias.png');
            a = get(gca, 'Position');
            b = get(gca, 'InnerPosition');
            same = max(abs(a - b));
            """);

        Succeeded(result);
        Assert.Equal(0, Number(result, "same"), 12);
    }

    [Fact]
    public async Task WritingThePlotBoxPinsItAndSaysSo()
    {
        ScriptRunResult result = await RunMatlab("""
            plot(1:5);
            set(gca, 'Position', [0.2 0.25 0.6 0.55]);
            exportgraphics(gcf, 'layout-pin.png');
            box = get(gca, 'Position');
            constraint = get(gca, 'PositionConstraint');
            """);

        Succeeded(result);
        double[] box = Row(result, "box");
        Assert.Equal(0.2, box[0], 9);
        Assert.Equal(0.25, box[1], 9);
        Assert.Equal(0.6, box[2], 9);
        Assert.Equal(0.55, box[3], 9);
        Assert.Equal("innerposition", Text(result, "constraint"));

    }

    [Fact]
    public async Task APinnedPlotBoxStaysWhereItIsWhenTheTitleGrows()
    {
        ScriptRunResult result = await RunMatlab("""
            plot(1:5); title('t');
            set(gca, 'Position', [0.2 0.2 0.6 0.6]);
            exportgraphics(gcf, 'layout-grow-a.png');
            before = get(gca, 'Position');
            title('a considerably longer title than the one before it');
            exportgraphics(gcf, 'layout-grow-b.png');
            after = get(gca, 'Position');
            moved = max(abs(after - before));
            """);

        Succeeded(result);

        // The whole of what 'innerposition' means: the margins move, the plot box does not.
        Assert.Equal(0, Number(result, "moved"), 9);

    }

    [Fact]
    public async Task WritingTheOuterRectangleReleasesThePinAndReadsBackExactly()
    {
        ScriptRunResult result = await RunMatlab("""
            plot(1:5);
            set(gca, 'Position', [0.2 0.2 0.6 0.6]);
            set(gca, 'OuterPosition', [0.1 0.15 0.5 0.55]);
            outer = get(gca, 'OuterPosition');
            constraint = get(gca, 'PositionConstraint');
            """);

        Succeeded(result);
        double[] outer = Row(result, "outer");
        Assert.Equal(0.1, outer[0], 9);
        Assert.Equal(0.15, outer[1], 9);
        Assert.Equal(0.5, outer[2], 9);
        Assert.Equal(0.55, outer[3], 9);
        Assert.Equal("outerposition", Text(result, "constraint"));
    }

    [Fact]
    public async Task TheOuterRectangleCountsUpFromTheBottomOfTheFigure()
    {
        ScriptRunResult result = await RunMatlab("""
            subplot(2, 1, 1); plot(1:5);
            top = get(gca, 'OuterPosition');
            subplot(2, 1, 2); plot(1:5);
            bottom = get(gca, 'OuterPosition');
            """);

        Succeeded(result);

        // MATLAB's Y counts up, so the first cell of a two-row grid is the higher number.
        Assert.True(Row(result, "top")[1] > Row(result, "bottom")[1]);
    }

    [Fact]
    public async Task AnAxesIsMeasuredInFractionsAndRefusesAnyOtherUnit()
    {
        ScriptRunResult result = await RunMatlab("units = get(gca, 'Units');");
        Succeeded(result);
        Assert.Equal("normalized", Text(result, "units"));

        ScriptRunResult refused = await RunMatlab("set(gca, 'Units', 'points');");
        Assert.False(refused.Success);
        Assert.Contains("normalized", refused.Message);
    }

    [Fact]
    public async Task AnAxesAnswersItsLayoutBeforeAnythingHasBeenDrawn()
    {
        // No render has happened, so this is the estimate — but a rectangle inside the figure with
        // room for the labels, not a refusal and not zeros.
        ScriptRunResult result = await RunMatlab("""
            ax = axes;
            box = get(ax, 'Position');
            """);

        Succeeded(result);
        double[] box = Row(result, "box");
        Assert.InRange(box[0], 0.001, 0.5);
        Assert.InRange(box[1], 0.001, 0.5);
        Assert.InRange(box[2], 0.5, 1);
        Assert.InRange(box[3], 0.5, 1);
    }

    // --- The figure's window ---------------------------------------------------------------------

    [Fact]
    public async Task AFigureAnswersToEverySixtySixOfItsNames()
    {
        ScriptRunResult result = await RunMatlab("""
            f = figure();
            count = numel(fieldnames(get(f)));
            """);

        Succeeded(result);

        // The probe is the real measure; this is the guard that stops a name going missing quietly.
        Assert.True(Number(result, "count") >= 66);
    }

    [Fact]
    public async Task APositionCarriesWhereTheWindowIsAsWellAsHowBigItIs()
    {
        ScriptRunResult result = await RunMatlab("""
            f = figure('Position', [120 240 500 400]);
            box = get(f, 'Position');
            inner = get(f, 'InnerPosition');
            same = max(abs(box - inner));
            """);

        Succeeded(result);
        double[] box = Row(result, "box");
        Assert.Equal(120, box[0], 9);
        Assert.Equal(240, box[1], 9);
        Assert.Equal(500, box[2], 9);
        Assert.Equal(400, box[3], 9);

        // A figure has no decoration of its own between the two, so they are one rectangle.
        Assert.Equal(0, Number(result, "same"), 12);
    }

    [Fact]
    public async Task TheWindowWordsAreRealAndTheUnknownOnesAreRefused()
    {
        ScriptRunResult result = await RunMatlab("""
            f = figure();
            set(f, 'WindowState', 'maximized');
            set(f, 'ToolBar', 'none');
            set(f, 'Resize', 'off');
            set(f, 'Pointer', 'watch');
            set(f, 'NumberTitle', 'off');
            state = get(f, 'WindowState');
            bar = get(f, 'ToolBar');
            resize = get(f, 'Resize');
            pointer = get(f, 'Pointer');
            numbered = get(f, 'NumberTitle');
            """);

        Succeeded(result);
        Assert.Equal("maximized", Text(result, "state"));
        Assert.Equal("none", Text(result, "bar"));
        Assert.Equal("off", Text(result, "resize"));
        Assert.Equal("watch", Text(result, "pointer"));
        Assert.Equal("off", Text(result, "numbered"));

        ScriptRunResult refused = await RunMatlab("set(gcf, 'Pointer', 'banana');");
        Assert.False(refused.Success);
        Assert.Contains("Unknown pointer", refused.Message);
    }

    [Theory]
    [InlineData("Renderer", "opengl", "painters")]
    [InlineData("RendererMode", "manual", "not chosen by hand")]
    [InlineData("WindowStyle", "modal", "ordinary windows")]
    [InlineData("MenuBar", "figure", "no menu bar")]
    [InlineData("DockControls", "on", "cannot be docked")]
    [InlineData("IntegerHandle", "off", "are numbered")]
    [InlineData("Units", "normalized", "measured in pixels")]
    public async Task APropertyWithOneTrueAnswerRefusesEveryOtherWord(
        string name, string wrong, string reason)
    {
        ScriptRunResult result = await RunMatlab($"set(gcf, '{name}', '{wrong}');");

        Assert.False(result.Success);
        Assert.Contains(reason, result.Message);
    }

    // --- The page ----------------------------------------------------------------------------------

    [Fact]
    public async Task ChangingThePaperUnitsChangesTheNumbersWithoutMovingThePage()
    {
        ScriptRunResult result = await RunMatlab("""
            f = figure();
            set(f, 'PaperUnits', 'inches');
            set(f, 'PaperPosition', [0 0 4 3]);
            set(f, 'PaperUnits', 'centimeters');
            metric = get(f, 'PaperPosition');
            """);

        Succeeded(result);
        double[] metric = Row(result, "metric");
        Assert.Equal(4 * 2.54, metric[2], 6);
        Assert.Equal(3 * 2.54, metric[3], 6);
    }

    [Fact]
    public async Task TurningThePageOverSwapsTheSizeItReports()
    {
        ScriptRunResult result = await RunMatlab("""
            f = figure();
            portrait = get(f, 'PaperSize');
            set(f, 'PaperOrientation', 'landscape');
            landscape = get(f, 'PaperSize');
            """);

        Succeeded(result);
        double[] portrait = Row(result, "portrait");
        double[] landscape = Row(result, "landscape");
        Assert.Equal(8.5, portrait[0], 6);
        Assert.Equal(11, portrait[1], 6);
        Assert.Equal(11, landscape[0], 6);
        Assert.Equal(8.5, landscape[1], 6);
    }

    [Fact]
    public async Task ASizeNoStandardPageHasMakesTheTypeCustom()
    {
        ScriptRunResult result = await RunMatlab("""
            f = figure();
            set(f, 'PaperSize', [5 7]);
            custom = get(f, 'PaperType');
            set(f, 'PaperType', 'a4');
            named = get(f, 'PaperSize');
            """);

        Succeeded(result);
        Assert.Equal("<custom>", Text(result, "custom"));

        // Naming a type releases the size that was set directly, or the two would contradict.
        Assert.Equal(8.2639, Row(result, "named")[0], 3);
    }

    [Fact]
    public async Task SayingWhereOnThePageStopsTakingTheSizeOffTheScreen()
    {
        ScriptRunResult result = await RunMatlab("""
            f = figure();
            before = get(f, 'PaperPositionMode');
            set(f, 'PaperPosition', [1 1 4 3]);
            after = get(f, 'PaperPositionMode');
            """);

        Succeeded(result);
        Assert.Equal("auto", Text(result, "before"));
        Assert.Equal("manual", Text(result, "after"));
    }

    [Fact]
    public async Task FreezingThePagePositionTakesTheSizeThatWouldHaveBeenUsed()
    {
        ScriptRunResult result = await RunMatlab("""
            f = figure('Position', [0 0 480 384]);
            set(f, 'PaperPositionMode', 'manual');
            box = get(f, 'PaperPosition');
            """);

        Succeeded(result);
        double[] box = Row(result, "box");

        // 480 by 384 pixels at ninety-six to the inch is five inches by four.
        Assert.Equal(5, box[2], 6);
        Assert.Equal(4, box[3], 6);
    }

    // --- Printing ------------------------------------------------------------------------------------

    [Fact]
    public async Task PrintWritesTheFileItsDeviceNames()
    {
        ScriptRunResult result = await RunMatlab("""
            plot(1:5);
            print(gcf, 'm75-print', '-dpng');
            wrote = exist('m75-print.png', 'file');
            """);

        Succeeded(result);
        Assert.Equal(2, Number(result, "wrote"));
    }

    [Fact]
    public async Task AResolutionMultipliesThePixelsPrintProduces()
    {
        ScriptRunResult result = await RunMatlab("""
            figure('Position', [0 0 320 240]); plot(1:5);
            print(gcf, 'm75-lo.png', '-r96');
            print(gcf, 'm75-hi.png', '-r192');
            lo = size(imread('m75-lo.png'), 2);
            hi = size(imread('m75-hi.png'), 2);
            """);

        Succeeded(result);
        Assert.Equal(2 * Number(result, "lo"), Number(result, "hi"));

    }

    [Fact]
    public async Task APageSizeSetByHandIsTheSizeThatGetsPrinted()
    {
        ScriptRunResult result = await RunMatlab("""
            plot(1:5);
            set(gcf, 'PaperUnits', 'inches');
            set(gcf, 'PaperPosition', [0 0 4 3]);
            print(gcf, 'm75-paper.png');
            wide = size(imread('m75-paper.png'), 2);
            high = size(imread('m75-paper.png'), 1);
            """);

        Succeeded(result);
        Assert.Equal(384, Number(result, "wide"));
        Assert.Equal(288, Number(result, "high"));
    }

    [Fact]
    public async Task SaveasTakesItsFormatFromTheNameOrFromTheWord()
    {
        ScriptRunResult result = await RunMatlab("""
            plot(1:5);
            saveas(gcf, 'm75-a.svg');
            saveas(gcf, 'm75-b', 'png');
            saveas(gcf, 'm75-c');
            svg = exist('m75-a.svg', 'file');
            png = exist('m75-b.png', 'file');
            document = exist('m75-c.fig', 'file');
            named = get(gcf, 'FileName');
            """);

        Succeeded(result);
        Assert.Equal(2, Number(result, "svg"));
        Assert.Equal(2, Number(result, "png"));
        Assert.Equal(2, Number(result, "document"));

        // Saving the document is what sets FileName; writing a picture is not saving the figure.
        Assert.Equal("m75-c.fig", Text(result, "named"));

    }

    [Fact]
    public async Task PrintWithNoFileIsRefusedRatherThanQuietlyDoingNothing()
    {
        ScriptRunResult result = await RunMatlab("plot(1:5); print();");

        Assert.False(result.Success);
        Assert.Contains("printing to a printer is not supported", result.Message);
    }

    // --- The maps, and the events ---------------------------------------------------------------------

    [Fact]
    public async Task AFigureColormapReachesAnAxesThatNeverChoseItsOwn()
    {
        ScriptRunResult result = await RunMatlab("""
            surf(peaks(8));
            colormap(gcf, 'hot');
            fromAxes = get(gca, 'Colormap');
            fromFigure = get(gcf, 'Colormap');
            same = max(max(abs(fromAxes - fromFigure)));
            """);

        Succeeded(result);
        Assert.Equal(0, Number(result, "same"), 12);
    }

    [Fact]
    public async Task AnAxesThatChoseItsOwnColormapKeepsIt()
    {
        ScriptRunResult result = await RunMatlab("""
            surf(peaks(8));
            colormap(gca, 'jet');
            colormap(gcf, 'hot');
            axesMap = get(gca, 'Colormap');
            figureMap = get(gcf, 'Colormap');
            differ = max(max(abs(axesMap - figureMap)));
            """);

        Succeeded(result);
        Assert.True(Number(result, "differ") > 0.1);
    }

    [Fact]
    public async Task EveryEventCallbackIsSettableAndReadsBackAsAHandle()
    {
        ScriptRunResult result = await RunMatlab("""
            f = figure();
            names = {'KeyPressFcn', 'KeyReleaseFcn', 'WindowKeyPressFcn', 'WindowKeyReleaseFcn', ...
                     'WindowButtonDownFcn', 'WindowButtonUpFcn', 'WindowButtonMotionFcn', ...
                     'WindowScrollWheelFcn'};
            handles = 0;
            for i = 1:numel(names)
                set(f, names{i}, @(s, e) disp('hi'));
                if isa(get(f, names{i}), 'function_handle')
                    handles = handles + 1;
                end
            end
            """);

        Succeeded(result);
        Assert.Equal(8, Number(result, "handles"));
    }

    [Fact]
    public async Task ResizeFcnAndSizeChangedFcnAreTwoNamesForOneSlot()
    {
        ScriptRunResult result = await RunMatlab("""
            f = figure();
            set(f, 'ResizeFcn', @(s, e) disp('r'));
            shared = isa(get(f, 'SizeChangedFcn'), 'function_handle');
            set(f, 'SizeChangedFcn', []);
            cleared = isa(get(f, 'ResizeFcn'), 'function_handle');
            """);

        Succeeded(result);
        Assert.True(Truth(result, "shared"));
        Assert.False(Truth(result, "cleared"));
    }

    [Fact]
    public async Task AFigureNobodyHasPointedAtAnswersTheOrigin()
    {
        ScriptRunResult result = await RunMatlab("""
            f = figure();
            here = get(f, 'CurrentPoint');
            character = get(f, 'CurrentCharacter');
            selection = get(f, 'SelectionType');
            """);

        Succeeded(result);
        double[] here = Row(result, "here");
        Assert.Equal(0, here[0]);
        Assert.Equal(0, here[1]);
        Assert.Equal(string.Empty, Text(result, "character"));
        Assert.Equal("normal", Text(result, "selection"));
    }

    // --- NextPlot ---------------------------------------------------------------------------------------

    [Fact]
    public async Task ReplaceChildrenClearsTheFigureBeforeEachPlot()
    {
        ScriptRunResult result = await RunMatlab("""
            f = figure();
            subplot(2, 1, 1); plot(1:5);
            subplot(2, 1, 2); plot(1:5);
            twoCells = numel(get(f, 'Children'));
            set(f, 'NextPlot', 'replacechildren');
            plot(1:3);
            oneCell = numel(get(f, 'Children'));
            """);

        Succeeded(result);
        Assert.Equal(2, Number(result, "twoCells"));
        Assert.Equal(1, Number(result, "oneCell"));
    }

    [Fact]
    public async Task ReplaceTakesTheFiguresOwnPropertiesWithIt()
    {
        ScriptRunResult result = await RunMatlab("""
            f = figure();
            set(f, 'Color', [0 0 0]);
            set(f, 'NextPlot', 'replace');
            plot(1:5);
            colour = get(f, 'Color');
            """);

        Succeeded(result);
        Assert.Equal(1, Row(result, "colour")[0], 9);
    }

    [Fact]
    public async Task HoldingOverridesWhatTheFigureWouldOtherwiseDo()
    {
        ScriptRunResult result = await RunMatlab("""
            f = figure();
            set(f, 'NextPlot', 'replacechildren');
            plot(1:5);
            hold on;
            plot(2:6);
            plot(3:7);
            series = numel(get(gca, 'Children'));
            """);

        Succeeded(result);
        Assert.Equal(3, Number(result, "series"));
    }

    // --- What the model carries -----------------------------------------------------------------------

    [Fact]
    public void APageSizeIsHeldInInchesAndTurnsWithTheOrientation()
    {
        var figure = new FigureModel();
        Assert.Equal(8.5, figure.EffectivePaperSize().Width, 6);

        figure.PaperOrientation = PaperOrientationType.Landscape;
        Assert.Equal(11, figure.EffectivePaperSize().Width, 6);

        figure.PaperSize = new Size2D(5, 7);
        Assert.Equal(PaperSizes.CustomName, figure.PaperType);
        Assert.Equal(7, figure.EffectivePaperSize().Width, 6);
    }

    [Fact]
    public void PlacingAFigureIsWhatSaysItHasBeenPlaced()
    {
        var figure = new FigureModel();
        Assert.False(figure.PositionSpecified);

        figure.Position = new Point2D(10, 20);
        Assert.True(figure.PositionSpecified);
    }

    [Fact]
    public void AnAxesReadsItsColormapThroughToTheFigure()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        Assert.Null(axes.ResolveColormap());

        figure.Colormap = Core.Drawing.Colormap.Hot;
        Assert.Same(Core.Drawing.Colormap.Hot, axes.ResolveColormap());

        axes.Colormap = Core.Drawing.Colormap.Jet;
        Assert.Same(Core.Drawing.Colormap.Jet, axes.ResolveColormap());
    }
}
