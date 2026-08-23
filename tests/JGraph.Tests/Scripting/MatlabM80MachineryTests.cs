using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Interaction;
using JGraph.Maths.Transforms;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M80: the three names that had no machinery behind them — <c>Layout</c>, <c>Interactions</c> and
/// <c>Toolbar</c> — and the objects built so they could mean something.
/// <para>
/// Every form here was run at the CLI before it was written down. The pixel proofs live in
/// stess_52.m; these tests pin what the properties mean. The hovering toolbar itself is window
/// chrome and cannot be pressed without a window, so what is pinned here is the model it is drawn
/// from and the verbs that shape it.
/// </para>
/// </summary>
[Collection("JG facade")]
public class MatlabM80MachineryTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();

    public MatlabM80MachineryTests() => JG.Reset();

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

    private async Task Refuses(string code, string fragment)
    {
        ScriptRunResult result = await RunMatlab(code);
        Assert.False(result.Success, $"expected a refusal from: {code}");
        Assert.Contains(fragment, result.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Several refusals in a row, one after another. Deliberately not <c>Task.WhenAll</c>: the facade
    /// these scripts run against is one static figure stack, so two scripts at once are two scripts
    /// editing the same figure — which passes alone and fails beside its neighbours.
    /// </summary>
    private async Task RefusesEach(params (string Code, string Fragment)[] cases)
    {
        foreach ((string code, string fragment) in cases)
        {
            await Refuses(code, fragment);
        }
    }

    // --- The tiled layout, and the Layout name it made answerable ---------------------------------

    /// <summary>
    /// <c>tiledlayout</c> answers with the layout, which is the whole reason it became an object: a
    /// script that cannot name the grid cannot set its spacing or ask what shape it is.
    /// </summary>
    [Fact]
    public async Task ATiledLayoutIsAnObjectAScriptCanName()
    {
        ScriptRunResult result = await RunMatlab("""
            t = tiledlayout(2, 3);
            kind = get(t, 'Type');
            grid = get(t, 'GridSize');
            spacing = get(t, 'TileSpacing');
            padding = get(t, 'Padding');
            indexing = get(t, 'TileIndexing');
            arrangement = get(t, 'TileArrangement');
            """);

        Succeeded(result);
        Assert.Equal("tiledlayout", Text(result, "kind"));
        Assert.Equal([2, 3], Row(result, "grid"));
        Assert.Equal("loose", Text(result, "spacing"));
        Assert.Equal("loose", Text(result, "padding"));
        Assert.Equal("rowmajor", Text(result, "indexing"));
        Assert.Equal("fixed", Text(result, "arrangement"));
    }

    /// <summary>
    /// The spacing is a setting rather than a word: closing it up moves every tile. This is the test
    /// that would fail if <c>TileSpacing</c> were remembered and not obeyed.
    /// </summary>
    [Fact]
    public async Task ClosingTheSpacingMovesEveryTile()
    {
        ScriptRunResult result = await RunMatlab("""
            t = tiledlayout(2, 2);
            a = nexttile;
            before = get(a, 'Position');
            set(t, 'TileSpacing', 'none');
            after = get(a, 'Position');
            set(t, 'Padding', 'tight');
            tighter = get(a, 'Position');
            """);

        Succeeded(result);
        double[] before = Row(result, "before");
        double[] after = Row(result, "after");
        double[] tighter = Row(result, "tighter");
        Assert.True(after[2] > before[2], "a tile with no gutter is wider than one with");
        Assert.True(tighter[2] > after[2], "a grid with no padding gives its tiles more room again");
    }

    /// <summary>
    /// An axes in a grid answers where it sits, and moving it there moves the axes. Before M80 this
    /// name answered nothing at all, because there was no grid object to hold a cell.
    /// </summary>
    [Fact]
    public async Task AnAxesInAGridSaysWhichTileItHoldsAndCanBeMoved()
    {
        ScriptRunResult result = await RunMatlab("""
            tiledlayout(2, 2);
            a = nexttile;
            place = get(a, 'Layout');
            kind = get(place, 'Type');
            tile = get(place, 'Tile');
            span = get(place, 'TileSpan');
            first = get(a, 'Position');
            set(place, 'Tile', 4);
            moved = get(a, 'Position');
            """);

        Succeeded(result);
        Assert.Equal("tiledlayoutoptions", Text(result, "kind"));
        Assert.Equal(1, Number(result, "tile"));
        Assert.Equal([1, 1], Row(result, "span"));
        Assert.True(
            Row(result, "moved")[1] < Row(result, "first")[1],
            "tile 4 of a two-by-two grid is below tile 1");
    }

    /// <summary>A tile can cover more than one cell, which is what <c>nexttile(n, [r c])</c> asks for.</summary>
    [Fact]
    public async Task ATileCanSpanSeveralCells()
    {
        ScriptRunResult result = await RunMatlab("""
            tiledlayout(3, 3);
            big = nexttile(1, [2 2]);
            small = nexttile(9);
            span = get(get(big, 'Layout'), 'TileSpan');
            wide = get(big, 'Position');
            narrow = get(small, 'Position');
            """);

        Succeeded(result);
        Assert.Equal([2, 2], Row(result, "span"));
        Assert.True(
            Row(result, "wide")[2] > 2 * Row(result, "narrow")[2],
            "a two-by-two tile is more than twice the width of a single one");
    }

    /// <summary>
    /// A flowing layout grows as tiles are asked for, and every tile already handed out moves with
    /// it. This is the behaviour the closure had and the object had to keep.
    /// </summary>
    [Fact]
    public async Task AFlowingLayoutGrowsAndMovesTheTilesAlreadyHandedOut()
    {
        ScriptRunResult result = await RunMatlab("""
            t = tiledlayout('flow');
            a = nexttile;
            alone = get(a, 'Position');
            b = nexttile;
            c = nexttile;
            d = nexttile;
            shared = get(a, 'Position');
            grid = get(t, 'GridSize');
            arrangement = get(t, 'TileArrangement');
            axes = numel(findobj(gcf, 'Type', 'axes'));
            """);

        Succeeded(result);
        Assert.Equal(4, Number(result, "axes"));
        Assert.Equal([2, 2], Row(result, "grid"));
        Assert.Equal("flow", Text(result, "arrangement"));
        Assert.True(
            Row(result, "shared")[2] < Row(result, "alone")[2],
            "the first tile shrank when the grid grew under it");
    }

    /// <summary>The words written over the whole grid take room from the tiles, which is what they are.</summary>
    [Fact]
    public async Task TheGridsSharedTextTakesRoomFromItsTiles()
    {
        ScriptRunResult result = await RunMatlab("""
            t = tiledlayout(2, 2);
            a = nexttile;
            plain = get(a, 'Position');
            set(t, 'Title', 'over the whole grid');
            set(t, 'XLabel', 'shared');
            titled = get(a, 'Position');
            words = get(t, 'Title');
            """);

        Succeeded(result);
        Assert.Equal("over the whole grid", Text(result, "words"));
        Assert.True(
            Row(result, "titled")[3] < Row(result, "plain")[3],
            "a title and a shared label leave the tiles less height");
    }

    // --- The gestures an axes answers to ----------------------------------------------------------

    /// <summary>
    /// What a fresh axes answers to, and what happens when a script says otherwise. Every one of
    /// these gestures was already happening; the list is what lets a script say so.
    /// </summary>
    [Fact]
    public async Task AnAxesSaysWhichGesturesItAnswersTo()
    {
        ScriptRunResult result = await RunMatlab("""
            plot([1 2 3]);
            defaults = numel(get(gca, 'Interactions'));
            p = panInteraction;
            z = zoomInteraction('Dimensions', 'x');
            kind = get(p, 'Type');
            dims = get(z, 'Dimensions');
            set(gca, 'Interactions', [p z]);
            chosen = numel(get(gca, 'Interactions'));
            disableDefaultInteractivity(gca);
            off = numel(get(gca, 'Interactions'));
            enableDefaultInteractivity(gca);
            back = numel(get(gca, 'Interactions'));
            """);

        Succeeded(result);
        Assert.Equal(3, Number(result, "defaults"));
        Assert.Equal("pan", Text(result, "kind"));
        Assert.Equal("x", Text(result, "dims"));
        Assert.Equal(2, Number(result, "chosen"));
        Assert.Equal(0, Number(result, "off"));

        // Enabling gives back what the script chose, not the defaults: the pair is a switch over the
        // list rather than a reset of it.
        Assert.Equal(2, Number(result, "back"));
    }

    /// <summary>A three-dimensional axes answers to one more gesture, because it has one more to make.</summary>
    [Fact]
    public async Task AThreeDimensionalAxesAlsoAnswersToRotate()
    {
        ScriptRunResult result = await RunMatlab("""
            surf(peaks(8));
            count = numel(get(gca, 'Interactions'));
            kinds = '';
            all = get(gca, 'Interactions');
            for k = 1:numel(all)
                kinds = [kinds get(all(k), 'Type') ' '];
            end
            """);

        Succeeded(result);
        Assert.Equal(4, Number(result, "count"));
        Assert.Contains("rotate", Text(result, "kinds"));
    }

    /// <summary>
    /// The gate is real: an axes told to answer no gestures does not zoom when the wheel turns. This
    /// drives the interaction controller directly, because that is the only place the answer shows.
    /// </summary>
    [Fact]
    public void TurningTheGesturesOffStopsTheWheelZooming()
    {
        AxesModel axes = FlatAxes();

        Assert.NotNull(axes.InteractionOf<ZoomInteractionModel>());

        axes.InteractionsDisabled = true;
        Assert.Null(axes.InteractionOf<ZoomInteractionModel>());

        axes.InteractionsDisabled = false;
        Assert.NotNull(axes.InteractionOf<ZoomInteractionModel>());
    }

    /// <summary>
    /// A zoom aimed along one direction leaves the other range where it was — the setting acting,
    /// checked on the navigation the wheel actually performs.
    /// </summary>
    [Fact]
    public void AZoomHeldToOneDirectionLeavesTheOtherAlone()
    {
        AxesModel axes = FlatAxes();
        var mapper = new AxisTransform(
            new Rect2D(0, 0, 100, 100),
            LinearScaleTransform.Instance, new DataRange(0, 10), false,
            LinearScaleTransform.Instance, new DataRange(0, 10), false);

        Navigation.ZoomAboutPixel(
            axes, mapper, new Point2D(50, 50), 0.5, InteractionDimensions.X);

        Assert.True(
            axes.PrimaryXAxis.Range.Max - axes.PrimaryXAxis.Range.Min < 10,
            "the named direction zoomed");
        Assert.Equal(0, axes.PrimaryYAxis.Range.Min);
        Assert.Equal(10, axes.PrimaryYAxis.Range.Max);
    }

    /// <summary>A plain axes with known ranges, which is what the two gate tests act on.</summary>
    private static AxesModel FlatAxes()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.PrimaryXAxis.AutoScale = false;
        axes.PrimaryXAxis.Range = new DataRange(0, 10);
        axes.PrimaryYAxis.AutoScale = false;
        axes.PrimaryYAxis.Range = new DataRange(0, 10);
        return axes;
    }

    // --- The toolbar over an axes -----------------------------------------------------------------

    /// <summary>
    /// The strip of buttons, and the two verbs that shape it. Every button in the default set is one
    /// this build acts on, which is why the set is shorter than MATLAB's.
    /// </summary>
    [Fact]
    public async Task AnAxesCarriesAToolbarOfButtonsItCanAct()
    {
        ScriptRunResult result = await RunMatlab("""
            plot([1 2 3]);
            tb = get(gca, 'Toolbar');
            kind = get(tb, 'Type');
            count = numel(get(tb, 'Children'));
            replaced = axtoolbar({'zoomin', 'zoomout', 'restoreview'});
            fewer = numel(get(replaced, 'Children'));
            restored = axtoolbar(gca, 'default');
            again = numel(get(restored, 'Children'));
            """);

        Succeeded(result);
        Assert.Equal("axestoolbar", Text(result, "kind"));
        Assert.Equal(6, Number(result, "count"));
        Assert.Equal(3, Number(result, "fewer"));
        Assert.Equal(6, Number(result, "again"));
    }

    /// <summary>A button added by a script goes on the left and holds its own state.</summary>
    [Fact]
    public async Task AButtonAddedByAScriptHoldsItsOwnState()
    {
        ScriptRunResult result = await RunMatlab("""
            plot([1 2 3]);
            tb = axtoolbar;
            btn = axtoolbarbtn(tb, 'state');
            set(btn, 'Icon', 'rotate');
            set(btn, 'Tooltip', 'turn it');
            kind = get(btn, 'Type');
            icon = get(btn, 'Icon');
            tip = get(btn, 'Tooltip');
            before = get(btn, 'Value');
            set(btn, 'Value', 'on');
            after = get(btn, 'Value');
            count = numel(get(tb, 'Children'));
            """);

        Succeeded(result);
        Assert.Equal("toolbarstatebutton", Text(result, "kind"));
        Assert.Equal("rotate", Text(result, "icon"));
        Assert.Equal("turn it", Text(result, "tip"));
        Assert.Equal("off", Text(result, "before"));
        Assert.Equal("on", Text(result, "after"));
        Assert.Equal(7, Number(result, "count"));
    }

    // --- What is refused, and why ----------------------------------------------------------------

    /// <summary>
    /// The refusals that are decisions rather than typos. Each says what this build does not have,
    /// because a property that refused silently would read as a bug.
    /// </summary>
    [Fact]
    public Task TheCeilingsRefuseByNameAndSayWhatIsMissing() =>
        RefusesEach(
            ("tiledlayout('sideways');", "row and column count"),
            ("t = tiledlayout(2, 2); set(t, 'Padding', 'sideways');", "'loose', 'compact' or 'tight'"),
            ("t = tiledlayout(2, 2); set(t, 'TileArrangement', 'sideways');", "'fixed' or 'flow'"),
            ("t = tiledlayout(2, 2); set(t, 'Toolbar', 'x');", "a layout is not one"),
            ("t = tiledlayout(2, 2); set(t, 'Units', 'pixels');", "fractions of the figure"),
            ("plot([1 2 3]); set(zoomInteraction, 'Dimensions', 'sideways');", "'xy', 'x' or 'y'"),
            ("plot([1 2 3]); set(dataTipInteraction, 'SnapToDataVertex', 'off');",
                "pinned to a data point"),
            ("plot([1 2 3]); t = text(1, 1, 'x'); set(t, 'Interactions', panInteraction);",
                "answers no interactions of its own"),
            ("plot([1 2 3]); axtoolbar({'nope'});", "has no 'nope' button"),
            ("plot([1 2 3]); b = axtoolbarbtn(axtoolbar); set(b, 'Value', 'on');",
                "a state button holds a value"));

    /// <summary>
    /// An axes outside a grid has no tile, and says so with an empty answer rather than a made-up
    /// cell number. Nothing else could be true: it is not in a layout.
    /// </summary>
    [Fact]
    public async Task AnAxesOutsideAGridHasNoTile()
    {
        ScriptRunResult result = await RunMatlab("""
            plot([1 2 3]);
            place = get(gca, 'Layout');
            held = numel(place);
            """);

        Succeeded(result);
        Assert.Equal(0, Number(result, "held"));
    }

    // --- The census -----------------------------------------------------------------------------

    /// <summary>
    /// What each kind answers, counted the way the coverage probe counts it. The three at the top are
    /// the ones this milestone closed, and every name they were missing was one of the three the
    /// property table has carried as a ceiling since M73.
    /// </summary>
    [Theory]
    [InlineData("h = tiledlayout(2, 2);", 28)]
    [InlineData("plot([1 2 3]); h = axtoolbar;", 15)]
    [InlineData("plot([1 2 3]); h = gca;", 147)]
    [InlineData("polaraxes; h = gca;", 107)]
    [InlineData("plot([1 2 3]); h = text(0.5, 0.5, 'label');", 41)]
    [InlineData("plot([1 2 3]); h = legend('a');", 39)]
    [InlineData("surf(peaks(8)); h = colorbar;", 42)]
    [InlineData("h = heatmap(magic(4));", 39)]
    public async Task EachKindAnswersAtLeastTheNamesThisMilestoneClaims(string build, int wanted)
    {
        ScriptRunResult result = await RunMatlab(build + "\ncount = numel(fieldnames(get(h)));");

        Succeeded(result);
        Assert.True(
            Number(result, "count") >= wanted,
            $"{build} answered {Number(result, "count")} names, wanted at least {wanted}");
    }
}
