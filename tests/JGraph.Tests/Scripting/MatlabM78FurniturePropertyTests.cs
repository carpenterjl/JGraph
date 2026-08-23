using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M78: the furniture a chart is read and legended by — the polar rulers, the legend box, a text
/// label, the colorbar's ruler, and the property families of the surface, contour, heatmap, patch,
/// image and arrow-field charts.
/// <para>
/// Every form here was run at the CLI before it was written down. The pixel proofs live in
/// stess_50.m; these tests pin what the properties mean. The refusals are pinned beside the
/// capabilities, because a ceiling nobody checks reads the same as an oversight.
/// </para>
/// </summary>
[Collection("JG facade")]
public class MatlabM78FurniturePropertyTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();

    public MatlabM78FurniturePropertyTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private Task<ScriptRunResult> RunMatlab(string code) =>
        new MatlabScriptEngine().RunAsync(
            code, new ScriptContext(_output, static (_, _) => { }), default);

    private static double Number(ScriptRunResult result, string name) =>
        Assert.IsType<double>(Assert.Single(result.Variables, v => v.Name == name).RawValue);

    private static double[] Row(ScriptRunResult result, string name) =>
        Assert.IsType<double[]>(Assert.Single(result.Variables, v => v.Name == name).RawValue);

    /// <summary>
    /// The top-left cell of a matrix-valued answer. A matrix reaches the host as formatted text
    /// rather than as numbers, which is the engine's own projection — so this reads that text back.
    /// </summary>
    private static double Cell(ScriptRunResult result, string name)
    {
        object? raw = Assert.Single(result.Variables, v => v.Name == name).RawValue;
        return raw switch
        {
            double one => one,
            double[] row => row[0],
            ScriptValueGrid grid => double.Parse(
                grid.Rows[0][0], System.Globalization.CultureInfo.InvariantCulture),
            _ => throw new Xunit.Sdk.XunitException(
                $"{name} is a {raw?.GetType().Name ?? "null"}, not a grid"),
        };
    }

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

    // --- The polar rulers -----------------------------------------------------------------------

    /// <summary>
    /// The r and θ rulers answer the same letter-shaped block X, Y and Z do. This is the whole point
    /// of serving them from <c>AddRulerWave</c> rather than writing a polar copy: if the block is
    /// shared, the words are the same words, and a mode means on a circle what it means on a grid.
    /// </summary>
    [Fact]
    public async Task ThePolarRulersAnswerTheSameBlockTheCartesianOnesDo()
    {
        ScriptRunResult result = await RunMatlab("""
            polaraxes;
            ax = gca;
            beforeR = ax.RColorMode;
            ax.RColor = [1 0 0];
            afterR = ax.RColorMode;
            ink = ax.RColor;
            ax.ThetaColor = 'b';
            thetaInk = ax.ThetaColor;
            beforeTick = ax.RTickMode;
            ax.RTick = [0 0.5 1];
            afterTick = ax.RTickMode;
            ax.ThetaMinorTick = 'on';
            minor = ax.ThetaMinorTick;
            limMode = ax.ThetaLimMode;
            rLimMode = ax.RLimMode;
            """);

        Succeeded(result);
        Assert.Equal("auto", Text(result, "beforeR"));
        Assert.Equal("manual", Text(result, "afterR"));
        Assert.Equal([1, 0, 0], Row(result, "ink"));
        Assert.Equal([0, 0, 1], Row(result, "thetaInk"));
        Assert.Equal("auto", Text(result, "beforeTick"));
        Assert.Equal("manual", Text(result, "afterTick"));
        Assert.Equal("on", Text(result, "minor"));
        Assert.Equal("auto", Text(result, "rLimMode"));

        // A polar axes is created with its angular ruler pinned to a full turn, so its limit mode
        // is honestly manual where MATLAB's reads 'auto'. Recorded as a divergence in ADR 0078
        // rather than papered over: the pin is what keeps a circle a circle.
        Assert.Equal("manual", Text(result, "limMode"));
    }

    /// <summary>
    /// The rings and the spokes are two families with two switches. Before M78 the polar renderer
    /// took one flag for the whole grid, so <c>RGrid 'off'</c> would have taken the spokes with it.
    /// </summary>
    [Fact]
    public async Task TheRingsAndTheSpokesSwitchSeparately()
    {
        ScriptRunResult result = await RunMatlab("""
            polaraxes;
            ax = gca;
            ax.RGrid = 'off';
            ax.ThetaGrid = 'on';
            ax.RMinorGrid = 'on';
            rings = ax.RGrid;
            spokes = ax.ThetaGrid;
            minorRings = ax.RMinorGrid;
            minorSpokes = ax.ThetaMinorGrid;
            """);

        Succeeded(result);
        Assert.Equal("off", Text(result, "rings"));
        Assert.Equal("on", Text(result, "spokes"));
        Assert.Equal("on", Text(result, "minorRings"));
        Assert.Equal("off", Text(result, "minorSpokes"));
    }

    /// <summary>
    /// The r label angle carries a real flag rather than a nullable, because its automatic value —
    /// 80° — is also an angle a script may ask for, so the value alone cannot tell the two apart.
    /// </summary>
    [Fact]
    public async Task TheRLabelAngleRemembersWhetherItWasChosen()
    {
        ScriptRunResult result = await RunMatlab("""
            polaraxes;
            ax = gca;
            before = ax.RAxisLocationMode;
            ax.RAxisLocation = 45;
            after = ax.RAxisLocationMode;
            chosen = ax.RAxisLocation;
            ax.RAxisLocationMode = 'auto';
            released = ax.RAxisLocation;
            """);

        Succeeded(result);
        Assert.Equal("auto", Text(result, "before"));
        Assert.Equal("manual", Text(result, "after"));
        Assert.Equal(45, Number(result, "chosen"));
        Assert.Equal(80, Number(result, "released"));
    }

    // --- The legend -----------------------------------------------------------------------------

    [Fact]
    public async Task TheLegendAnswersItsBoxItsInkAndItsFont()
    {
        ScriptRunResult result = await RunMatlab("""
            plot([1 2 3]);
            lgd = legend('a');
            lgd.Box = 'off';
            lgd.Color = [0.9 0.9 0.9];
            lgd.EdgeColor = [1 0 0];
            lgd.LineWidth = 2;
            lgd.TextColor = [0 0 1];
            lgd.FontName = 'Consolas';
            lgd.FontSize = 14;
            lgd.FontWeight = 'bold';
            lgd.FontAngle = 'italic';
            lgd.Interpreter = 'none';
            box = lgd.Box;
            face = lgd.Color;
            edge = lgd.EdgeColor;
            width = lgd.LineWidth;
            ink = lgd.TextColor;
            font = lgd.FontName;
            size = lgd.FontSize;
            weight = lgd.FontWeight;
            slant = lgd.FontAngle;
            markup = lgd.Interpreter;
            """);

        Succeeded(result);
        Assert.Equal("off", Text(result, "box"));

        // A colour is kept as three bytes, so 0.9 comes back as 230/255 — within half a step.
        Assert.All(Row(result, "face"), channel => Assert.Equal(0.9, channel, 2));
        Assert.Equal([1, 0, 0], Row(result, "edge"));
        Assert.Equal(2, Number(result, "width"));
        Assert.Equal([0, 0, 1], Row(result, "ink"));
        Assert.Equal("Consolas", Text(result, "font"));
        Assert.Equal(14, Number(result, "size"));
        Assert.Equal("bold", Text(result, "weight"));
        Assert.Equal("italic", Text(result, "slant"));
        Assert.Equal("none", Text(result, "markup"));
    }

    /// <summary>
    /// A horizontal legend is one row deep, so its column count is the number of entries; naming a
    /// number of columns instead is what moves the mode to manual. The chosen number answers as
    /// itself rather than clamped to the row count, because rows are settled at draw time and this
    /// host draws nothing.
    /// </summary>
    [Fact]
    public async Task TheLegendDealsItsRowsIntoColumns()
    {
        ScriptRunResult result = await RunMatlab("""
            plot([1 2 3]);
            lgd = legend('a');
            before = lgd.NumColumnsMode;
            lgd.Orientation = 'horizontal';
            orient = lgd.Orientation;
            lgd.NumColumns = 3;
            columns = lgd.NumColumns;
            after = lgd.NumColumnsMode;
            lgd.NumColumnsMode = 'auto';
            released = lgd.NumColumnsMode;
            """);

        Succeeded(result);
        Assert.Equal("auto", Text(result, "before"));
        Assert.Equal("horizontal", Text(result, "orient"));
        Assert.Equal(3, Number(result, "columns"));
        Assert.Equal("manual", Text(result, "after"));
        Assert.Equal("auto", Text(result, "released"));
    }

    /// <summary>
    /// MATLAB's <c>Position</c> on a legend is a rectangle in figure fractions, not the name of a
    /// corner — which is what this model's own <c>Position</c> answered until M78. Pinning the box is
    /// what takes the location off its preset, and MATLAB reports that as 'none'.
    /// </summary>
    [Fact]
    public async Task PinningALegendsBoxTakesItsLocationOffThePreset()
    {
        ScriptRunResult result = await RunMatlab("""
            plot([1 2 3]);
            lgd = legend('a');
            lgd.Position = [0.2 0.3 0.25 0.1];
            box = lgd.Position;
            where = lgd.Location;
            units = lgd.Units;
            """);

        Succeeded(result);

        // Y is stored downward and reported upward, so the round trip is a subtraction and lands
        // within a rounding step rather than exactly.
        double[] box = Row(result, "box");
        Assert.Equal(0.2, box[0], 12);
        Assert.Equal(0.3, box[1], 12);
        Assert.Equal(0.25, box[2], 12);
        Assert.Equal(0.1, box[3], 12);
        Assert.Equal("none", Text(result, "where"));
        Assert.Equal("normalized", Text(result, "units"));
    }

    /// <summary>
    /// AutoUpdate off freezes the rows: a series drawn afterwards is not legended. The reconciliation
    /// is the model's own, so this is checked without drawing anything.
    /// </summary>
    [Fact]
    public void ALegendToldNotToUpdateKeepsTheRowsItHas()
    {
        JG.Plot([1.0, 2, 3]);
        AxesModel axes = JG.Gca();
        axes.Hold = true;
        Assert.True(axes.Legend.SyncEntries(axes.Plots));
        Assert.Single(axes.Legend.Entries);

        axes.Legend.AutoUpdate = false;
        JG.Plot([3.0, 2, 1]);
        Assert.False(axes.Legend.SyncEntries(axes.Plots));
        Assert.Single(axes.Legend.Entries);

        axes.Legend.AutoUpdate = true;
        Assert.True(axes.Legend.SyncEntries(axes.Plots));
        Assert.Equal(2, axes.Legend.Entries.Count);
    }

    // --- A text label ---------------------------------------------------------------------------

    [Fact]
    public async Task ATextLabelAnswersItsTurnItsEdgeAndItsSpace()
    {
        ScriptRunResult result = await RunMatlab("""
            plot([1 2 3]);
            t = text(2, 2, 'hello');
            t.Rotation = 30;
            t.Margin = 6;
            t.LineWidth = 2;
            t.LineStyle = '--';
            t.FontWeight = 'bold';
            t.FontAngle = 'italic';
            t.FontSmoothing = 'off';
            t.Clipping = 'on';
            turn = t.Rotation;
            margin = t.Margin;
            width = t.LineWidth;
            dash = t.LineStyle;
            weight = t.FontWeight;
            slant = t.FontAngle;
            smoothing = t.FontSmoothing;
            clipping = t.Clipping;
            fontUnits = t.FontUnits;
            units = t.Units;
            editing = t.Editing;
            """);

        Succeeded(result);
        Assert.Equal(30, Number(result, "turn"));
        Assert.Equal(6, Number(result, "margin"));
        Assert.Equal(2, Number(result, "width"));
        Assert.Equal("--", Text(result, "dash"));
        Assert.Equal("bold", Text(result, "weight"));
        Assert.Equal("italic", Text(result, "slant"));
        Assert.Equal("off", Text(result, "smoothing"));
        Assert.Equal("on", Text(result, "clipping"));
        Assert.Equal("points", Text(result, "fontUnits"));
        Assert.Equal("data", Text(result, "units"));
        Assert.Equal("off", Text(result, "editing"));
    }

    /// <summary>
    /// A label placed in figure fractions says so, which is the one honest reading of MATLAB's Units
    /// this build has: an annotation is anchored either among the data or on the page.
    /// </summary>
    [Fact]
    public async Task ALabelSaysWhichSpaceItIsAnchoredIn()
    {
        ScriptRunResult result = await RunMatlab("""
            plot([1 2 3]);
            t = text(2, 2, 'hello');
            before = t.Units;
            t.Units = 'normalized';
            after = t.Units;
            """);

        Succeeded(result);
        Assert.Equal("data", Text(result, "before"));
        Assert.Equal("normalized", Text(result, "after"));
    }

    /// <summary>
    /// Extent is a measurement of a drawing, and a label that has never been drawn has no measured
    /// size — so it answers the empty box at its own anchor rather than guessing. stess_50 checks the
    /// measured case, after an export has actually laid a figure out.
    /// </summary>
    [Fact]
    public async Task AnUndrawnLabelsExtentIsTheEmptyBoxAtItsAnchor()
    {
        ScriptRunResult result = await RunMatlab("""
            plot([1 2 3]);
            t = text(2, 2.5, 'hello');
            extent = t.Extent;
            """);

        Succeeded(result);
        Assert.Equal([2, 2.5, 0, 0], Row(result, "extent"));
    }

    // --- The colorbar ---------------------------------------------------------------------------

    [Fact]
    public async Task TheColorbarAnswersItsSideItsRulerAndItsInk()
    {
        ScriptRunResult result = await RunMatlab("""
            surf(peaks(8));
            cb = colorbar;
            side = cb.Location;
            cb.Location = 'southoutside';
            moved = cb.Location;
            cb.Limits = [-4 6];
            span = cb.Limits;
            limitsMode = cb.LimitsMode;
            cb.Ticks = [-4 0 6];
            ticks = cb.Ticks;
            ticksMode = cb.TicksMode;
            cb.TickDirection = 'in';
            direction = cb.TickDirection;
            cb.Direction = 'reverse';
            way = cb.Direction;
            cb.AxisLocation = 'in';
            labels = cb.AxisLocation;
            cb.Box = 'off';
            box = cb.Box;
            cb.LineWidth = 1.5;
            width = cb.LineWidth;
            units = cb.Units;
            """);

        Succeeded(result);
        Assert.Equal("eastoutside", Text(result, "side"));
        Assert.Equal("southoutside", Text(result, "moved"));
        Assert.Equal([-4, 6], Row(result, "span"));
        Assert.Equal("manual", Text(result, "limitsMode"));
        Assert.Equal([-4, 0, 6], Row(result, "ticks"));
        Assert.Equal("manual", Text(result, "ticksMode"));
        Assert.Equal("in", Text(result, "direction"));
        Assert.Equal("reverse", Text(result, "way"));
        Assert.Equal("in", Text(result, "labels"));
        Assert.Equal("off", Text(result, "box"));
        Assert.Equal(1.5, Number(result, "width"));
        Assert.Equal("normalized", Text(result, "units"));
    }

    /// <summary>
    /// A colorbar with no limits of its own shows the range of the plot it legends, and says so when
    /// asked — an unset slot is not an answer a script can use. Releasing the limits goes back to it.
    /// </summary>
    [Fact]
    public async Task AColorbarWithoutLimitsAnswersTheRangeItLegends()
    {
        ScriptRunResult result = await RunMatlab("""
            surf(peaks(8));
            cb = colorbar;
            auto = cb.Limits;
            mode = cb.LimitsMode;
            cb.Limits = [0 1];
            pinned = cb.Limits;
            cb.LimitsMode = 'auto';
            released = cb.Limits;
            """);

        Succeeded(result);
        double[] auto = Row(result, "auto");
        Assert.True(auto[1] > auto[0], "the automatic limits are a real span");
        Assert.Equal("auto", Text(result, "mode"));
        Assert.Equal([0, 1], Row(result, "pinned"));
        Assert.Equal(auto, Row(result, "released"));
    }

    /// <summary>Chosen labels are cycled over the ticks, exactly as a ruler's overrides are.</summary>
    [Fact]
    public async Task AColorbarsChosenLabelsCycleOverItsTicks()
    {
        ScriptRunResult result = await RunMatlab("""
            surf(peaks(8));
            cb = colorbar;
            cb.Ticks = [-4 0 4 8];
            cb.TickLabels = {'lo', 'hi'};
            labels = cb.TickLabels;
            first = labels{1};
            second = labels{2};
            third = labels{3};
            count = numel(labels);
            mode = cb.TickLabelsMode;
            """);

        Succeeded(result);
        Assert.Equal("lo", Text(result, "first"));
        Assert.Equal("hi", Text(result, "second"));
        Assert.Equal("lo", Text(result, "third"));
        Assert.Equal(4, Number(result, "count"));
        Assert.Equal("manual", Text(result, "mode"));
    }

    // --- The surface ----------------------------------------------------------------------------

    [Fact]
    public async Task TheSurfaceAnswersItsMeshItsMarkersAndItsMappings()
    {
        ScriptRunResult result = await RunMatlab("""
            s = surf(peaks(8));
            s.MeshStyle = 'row';
            mesh = s.MeshStyle;
            s.LineStyle = ':';
            dash = s.LineStyle;
            s.LineWidth = 1.5;
            width = s.LineWidth;
            s.Marker = 'o';
            marker = s.Marker;
            s.MarkerSize = 4;
            size = s.MarkerSize;
            s.MarkerEdgeColor = [1 0 0];
            edge = s.MarkerEdgeColor;
            s.MarkerFaceColor = [0 1 0];
            fill = s.MarkerFaceColor;
            s.CDataMapping = 'direct';
            colours = s.CDataMapping;
            s.AlphaDataMapping = 'none';
            alphas = s.AlphaDataMapping;
            s.EdgeLighting = 'flat';
            lit = s.EdgeLighting;
            s.BackFaceLighting = 'unlit';
            back = s.BackFaceLighting;
            s.AlignVertexCenters = 'on';
            snapped = s.AlignVertexCenters;
            """);

        Succeeded(result);
        Assert.Equal("row", Text(result, "mesh"));
        Assert.Equal(":", Text(result, "dash"));
        Assert.Equal(1.5, Number(result, "width"));
        Assert.Equal("o", Text(result, "marker"));
        Assert.Equal(4, Number(result, "size"));
        Assert.Equal([1, 0, 0], Row(result, "edge"));
        Assert.Equal([0, 1, 0], Row(result, "fill"));
        Assert.Equal("direct", Text(result, "colours"));
        Assert.Equal("none", Text(result, "alphas"));
        Assert.Equal("flat", Text(result, "lit"));
        Assert.Equal("unlit", Text(result, "back"));
        Assert.Equal("on", Text(result, "snapped"));
    }

    /// <summary>
    /// <c>surf(Z)</c> counted its positions out of the grid, and says so; giving it real ones is what
    /// moves the mode to manual, and releasing it counts them out again.
    /// </summary>
    [Fact]
    public async Task ASurfaceSaysWhetherItsPositionsWereGivenOrCounted()
    {
        ScriptRunResult result = await RunMatlab("""
            s = surf(peaks(8));
            counted = s.XDataMode;
            s.XData = (1:8) * 10;
            given = s.XDataMode;
            wide = s.XData;
            s.XDataMode = 'auto';
            released = s.XDataMode;
            back = s.XData;
            gridded = surf(1:8, 1:8, peaks(8));
            explicitMode = gridded.XDataMode;
            """);

        Succeeded(result);
        Assert.Equal("auto", Text(result, "counted"));
        Assert.Equal("manual", Text(result, "given"));
        Assert.Equal(80, Row(result, "wide")[^1]);
        Assert.Equal("auto", Text(result, "released"));
        Assert.Equal(8, Row(result, "back")[^1]);
        Assert.Equal("manual", Text(result, "explicitMode"));
    }

    /// <summary>
    /// The normals are worked out from the grid every time, so they agree with the lighting by
    /// construction — which is why a written one is refused rather than stored.
    /// </summary>
    [Fact]
    public async Task ASurfacesNormalsAreOnePerFacetAndOnePerVertex()
    {
        ScriptRunResult result = await RunMatlab("""
            s = surf(peaks(8));
            faces = size(s.FaceNormals);
            vertices = size(s.VertexNormals);
            mode = s.FaceNormalsMode;
            """);

        Succeeded(result);
        Assert.Equal([7, 7, 3], Row(result, "faces"));
        Assert.Equal([8, 8, 3], Row(result, "vertices"));
        Assert.Equal("auto", Text(result, "mode"));
    }

    // --- The contour ----------------------------------------------------------------------------

    [Fact]
    public async Task TheContourAnswersItsLevelsItsInkAndItsLabels()
    {
        ScriptRunResult result = await RunMatlab("""
            c = contour(peaks(8));
            c.Fill = 'on';
            filled = c.Fill;
            c.LineColor = [0 0 0];
            ink = c.LineColor;
            c.LineStyle = '--';
            dash = c.LineStyle;
            c.LabelSpacing = 200;
            spacing = c.LabelSpacing;
            c.ZLocation = 'zero';
            where = c.ZLocation;
            beforeStep = c.LevelStepMode;
            c.LevelStep = 2;
            afterStep = c.LevelStepMode;
            step = c.LevelStep;
            c.TextStep = 4;
            textMode = c.TextStepMode;
            """);

        Succeeded(result);
        Assert.Equal("on", Text(result, "filled"));
        Assert.Equal([0, 0, 0], Row(result, "ink"));
        Assert.Equal("--", Text(result, "dash"));
        Assert.Equal(200, Number(result, "spacing"));
        Assert.Equal("zero", Text(result, "where"));
        Assert.Equal("auto", Text(result, "beforeStep"));
        Assert.Equal("manual", Text(result, "afterStep"));
        Assert.Equal(2, Number(result, "step"));
        Assert.Equal("manual", Text(result, "textMode"));
    }

    /// <summary>
    /// A step says where the levels are rather than how many, so the levels become the multiples of
    /// it inside the data — which is the whole reason to prefer a step to a count.
    /// </summary>
    [Fact]
    public async Task AContoursLevelStepPutsTheLevelsOnRoundNumbers()
    {
        ScriptRunResult result = await RunMatlab("""
            c = contour(peaks(8));
            c.LevelStep = 2;
            levels = c.LevelList;
            remainder = max(abs(levels - 2 * round(levels / 2)));
            count = numel(levels);
            """);

        Succeeded(result);
        Assert.True(Number(result, "count") > 1, "a step across peaks(8) yields several levels");
        Assert.True(Number(result, "remainder") < 1e-9, "every level is a multiple of the step");
    }

    /// <summary>The matrix a script reads the curves out of is the one clabel and contourc already give.</summary>
    [Fact]
    public async Task AContourHandsBackTheMatrixItsCurvesAreIn()
    {
        ScriptRunResult result = await RunMatlab("""
            c = contour(peaks(8));
            m = c.ContourMatrix;
            rows = size(m, 1);
            columns = size(m, 2);
            firstLevel = m(1, 1);
            firstCount = m(2, 1);
            """);

        Succeeded(result);
        Assert.Equal(2, Number(result, "rows"));
        Assert.True(Number(result, "columns") > 2, "the matrix carries curves, not just a header");
        Assert.True(Number(result, "firstCount") >= 2, "a curve has at least two points");
    }

    // --- The heatmap ----------------------------------------------------------------------------

    /// <summary>
    /// Writing the display data reorders and narrows the chart, and the values follow their own
    /// columns — a heatmap showing three of its five categories is three columns wide.
    /// </summary>
    [Fact]
    public async Task AHeatmapReordersAndNarrowsToTheCategoriesNamed()
    {
        ScriptRunResult result = await RunMatlab("""
            h = heatmap(magic(4));
            before = numel(h.XDisplayData);
            h.XDisplayData = {'3', '1'};
            after = numel(h.XDisplayData);
            shown = size(h.ColorDisplayData);
            first = h.XDisplayData{1};
            topLeft = h.ColorDisplayData(1, 1);
            """);

        Succeeded(result);
        Assert.Equal(4, Number(result, "before"));
        Assert.Equal(2, Number(result, "after"));
        Assert.Equal([4, 2], Row(result, "shown"));
        Assert.Equal("3", Text(result, "first"));

        // magic(4)'s first row is [16 2 3 13]; column 3 is what now stands first.
        Assert.Equal(3, Number(result, "topLeft"));
    }

    /// <summary>Display labels are kept apart from the names, so relabelling does not lose the identities.</summary>
    [Fact]
    public async Task AHeatmapsLabelsAreSeparateFromTheNamesItKnowsItsCellsBy()
    {
        ScriptRunResult result = await RunMatlab("""
            h = heatmap(magic(3));
            h.XDisplayLabels = {'left', 'mid', 'right'};
            labels = h.XDisplayLabels;
            names = h.XDisplayData;
            firstLabel = labels{1};
            firstName = names{1};
            """);

        Succeeded(result);
        Assert.Equal("left", Text(result, "firstLabel"));
        Assert.Equal("1", Text(result, "firstName"));
    }

    /// <summary>
    /// A heatmap summarised from a table remembers which one and how, so changing the variable it
    /// reduces over recounts the grid. Naming one moves the method off counting, as MATLAB does —
    /// counting ignores the variable entirely, so the write would otherwise mean nothing.
    /// </summary>
    [Fact]
    public async Task AHeatmapFromATableRecountsWhenItsVariablesChange()
    {
        ScriptRunResult result = await RunMatlab("""
            t = table({'a'; 'a'; 'b'}, {'x'; 'y'; 'x'}, [1; 3; 5], ...
                'VariableNames', {'g', 'k', 'v'});
            h = heatmap(t, 'g', 'k');
            xVar = h.XVariable;
            yVar = h.YVariable;
            counted = h.ColorMethod;
            counts = h.ColorDisplayData;
            h.ColorVariable = 'v';
            averaged = h.ColorMethod;
            means = h.ColorDisplayData;
            h.ColorMethod = 'sum';
            sums = h.ColorDisplayData;
            rows = height(h.SourceTable);
            """);

        Succeeded(result);
        Assert.Equal("g", Text(result, "xVar"));
        Assert.Equal("k", Text(result, "yVar"));
        Assert.Equal("count", Text(result, "counted"));
        Assert.Equal("mean", Text(result, "averaged"));
        Assert.Equal(3, Number(result, "rows"));

        // Group (a, x) holds the single row v = 1: one row counted, mean 1, sum 1.
        Assert.Equal(1, Cell(result, "counts"));
        Assert.Equal(1, Cell(result, "means"));
        Assert.Equal(1, Cell(result, "sums"));
    }

    /// <summary>
    /// A heatmap is a plot on ordinary axes here rather than MATLAB's chart container, so the four
    /// rectangles that belong to the container answer for the axes it is drawn on.
    /// </summary>
    [Fact]
    public async Task AHeatmapsRectangleIsTheAxesItIsDrawnOn()
    {
        ScriptRunResult result = await RunMatlab("""
            h = heatmap(magic(3));
            h.Position = [0.2 0.2 0.5 0.5];
            box = h.Position;
            constraint = h.PositionConstraint;
            axesBox = get(gca, 'Position');
            units = h.Units;
            """);

        Succeeded(result);
        double[] box = Row(result, "box");
        Assert.Equal(0.2, box[0], 12);
        Assert.Equal(0.2, box[1], 12);
        Assert.Equal(0.5, box[2], 12);
        Assert.Equal(0.5, box[3], 12);
        Assert.Equal("innerposition", Text(result, "constraint"));
        Assert.Equal(box, Row(result, "axesBox"));
        Assert.Equal("normalized", Text(result, "units"));
    }

    // --- Patch, image and the arrow field -------------------------------------------------------

    [Fact]
    public async Task ThePatchAnswersTheSurfacesBlockAndItsOwnTwoVertexArrays()
    {
        ScriptRunResult result = await RunMatlab("""
            p = patch([0 1 1], [0 0 1], 'r');
            p.LineStyle = '-.';
            dash = p.LineStyle;
            p.LineJoin = 'round';
            join = p.LineJoin;
            p.Marker = 's';
            marker = p.Marker;
            p.CData = [1 2 3];
            colours = p.FaceVertexCData;
            p.FaceVertexAlphaData = [1 0.5 0.2];
            alphas = p.FaceVertexAlphaData;
            p.CDataMapping = 'direct';
            mapping = p.CDataMapping;
            faces = size(p.FaceNormals);
            vertices = size(p.VertexNormals);
            """);

        Succeeded(result);
        Assert.Equal("-.", Text(result, "dash"));
        Assert.Equal("round", Text(result, "join"));
        Assert.Equal("s", Text(result, "marker"));
        Assert.Equal([1, 2, 3], Row(result, "colours"));
        Assert.Equal([1, 0.5, 0.2], Row(result, "alphas"));
        Assert.Equal("direct", Text(result, "mapping"));
        Assert.Equal([1, 3], Row(result, "faces"));
        Assert.Equal([3, 3], Row(result, "vertices"));
    }

    /// <summary>
    /// Direct is an image's default mapping, because a picture's numbers are usually colour numbers
    /// already — which is the one place this build's default differs from a surface's.
    /// </summary>
    [Fact]
    public async Task AnImageIndexesItsColormapDirectlyUnlessToldOtherwise()
    {
        ScriptRunResult result = await RunMatlab("""
            im = image(magic(4));
            mapping = im.CDataMapping;
            interpolation = im.Interpolation;
            im.CDataMapping = 'scaled';
            scaled = im.CDataMapping;
            im.Interpolation = 'bilinear';
            smooth = im.Interpolation;
            im.XData = [0 10];
            span = im.XData;
            grid = size(im.CData);
            """);

        Succeeded(result);
        Assert.Equal("direct", Text(result, "mapping"));
        Assert.Equal("nearest", Text(result, "interpolation"));
        Assert.Equal("scaled", Text(result, "scaled"));
        Assert.Equal("bilinear", Text(result, "smooth"));
        Assert.Equal([0, 10], Row(result, "span"));
        Assert.Equal([4, 4], Row(result, "grid"));
    }

    [Fact]
    public async Task TheArrowFieldAnswersItsLineItsMarkerAndItsFourModes()
    {
        ScriptRunResult result = await RunMatlab("""
            q = quiver([0 1], [0 1], [1 1], [1 1]);
            colourMode = q.ColorMode;
            beforeDash = q.LineStyleMode;
            q.LineStyle = ':';
            afterDash = q.LineStyleMode;
            dash = q.LineStyle;
            q.Marker = 'd';
            markerMode = q.MarkerMode;
            marker = q.Marker;
            q.MarkerSize = 5;
            size = q.MarkerSize;
            q.UDataSource = 'us';
            source = q.UDataSource;
            q.MarkerMode = 'auto';
            released = q.Marker;
            """);

        Succeeded(result);
        Assert.Equal("auto", Text(result, "colourMode"));
        Assert.Equal("auto", Text(result, "beforeDash"));
        Assert.Equal("manual", Text(result, "afterDash"));
        Assert.Equal(":", Text(result, "dash"));
        Assert.Equal("manual", Text(result, "markerMode"));
        Assert.Equal("d", Text(result, "marker"));
        Assert.Equal(5, Number(result, "size"));
        Assert.Equal("us", Text(result, "source"));
        Assert.Equal("none", Text(result, "released"));
    }

    // --- What is refused, and why ----------------------------------------------------------------

    [Fact]
    public Task AWordOutsideAPropertysVocabularyIsRefusedByName() =>
        RefusesEach(
            ("polaraxes; set(gca, 'RGrid', 'sometimes');", "RGrid is 'on' or 'off'"),
            ("plot([1 2 3]); set(legend('a'), 'Orientation', 'sideways');", "Orientation is"),
            ("surf(peaks(8)); set(colorbar, 'Location', 'sideways');", "Location is one of"),
            ("set(surf(peaks(8)), 'MeshStyle', 'diagonal');", "MeshStyle is"),
            ("set(contour(peaks(8)), 'ZLocation', 'floor');", "ZLocation is"));

    /// <summary>
    /// The four refusals that are decisions rather than typos. Each names what this build does not
    /// have and what to do instead, because a property that refused silently would read as a bug.
    /// </summary>
    [Fact]
    public Task TheCeilingsRefuseByNameAndSayWhatIsMissing() =>
        RefusesEach(
            ("plot([1 2 3]); t = text(1, 1, 'x'); t.Editing = 'on';", "in-place text cursor"),
            ("plot([1 2 3]); t = text(1, 1, 'x'); t.FontUnits = 'pixels';", "in points here"),
            ("set(surf(peaks(8)), 'FaceNormals', [1 2 3]);", "worked out from the grid"),
            ("set(surf(peaks(8)), 'FaceNormalsMode', 'manual');", "nothing to freeze"),
            ("set(heatmap(magic(3)), 'XVariable', 'g');", "given its numbers directly"),
            ("plot([1 2 3]); set(legend('a'), 'Units', 'pixels');", "fractions of the figure"));

    /// <summary>
    /// Naming a category the chart does not have lists the ones it does, because the whole reason to
    /// write display data is that the names are the chart's own and a typo is easy.
    /// </summary>
    [Fact]
    public Task AHeatmapNamesTheCategoriesItActuallyHas() =>
        Refuses("set(heatmap(magic(3)), 'XDisplayData', {'nope'});", "It has 1, 2, 3");

    // --- The census -----------------------------------------------------------------------------

    /// <summary>
    /// What each kind answers, counted the way the coverage probe counts it. These are the numbers
    /// the milestone claims, pinned so that a later wave cannot quietly lose one: a property surface
    /// that shrinks without anyone noticing is the failure this whole table exists to prevent.
    /// </summary>
    [Theory]
    [InlineData("polaraxes;", 201)]
    [InlineData("plot([1 2 3]); h = legend('a');", 49)]
    [InlineData("plot([1 2 3]); h = text(0.5, 0.5, 'label');", 57)]
    [InlineData("h = surf(peaks(8));", 83)]
    [InlineData("h = contour(peaks(8));", 65)]
    [InlineData("h = heatmap(magic(4));", 70)]
    [InlineData("surf(peaks(8)); h = colorbar;", 55)]
    [InlineData("h = patch([0 1 1], [0 0 1], 'r');", 79)]
    [InlineData("h = image(magic(4));", 45)]
    [InlineData("h = quiver([0 1], [0 1], [1 1], [1 1]);", 65)]
    [InlineData("bubblechart([1 2], [1 2], [10 20]); h = bubblelegend();", 49)]
    public async Task EachKindAnswersAtLeastTheNamesThisMilestoneClaims(string build, int wanted)
    {
        string subject = build.Contains("h = ", StringComparison.Ordinal) ? "h" : "gca";
        ScriptRunResult result = await RunMatlab($"""
            {build}
            count = numel(fieldnames(get({subject})));
            """);

        Succeeded(result);
        Assert.True(
            Number(result, "count") >= wanted,
            $"{build} answered {Number(result, "count")} names, wanted at least {wanted}");
    }
}
