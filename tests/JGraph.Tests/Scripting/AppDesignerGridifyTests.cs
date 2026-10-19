using System.Text.Json;
using JGraph.Core.Primitives;
using JGraph.Scripting.Designer;
using Xunit;
using Xunit.Abstractions;

namespace JGraph.Tests.Scripting;

/// <summary>
/// DS4 of the designer plan: App Designer's "Apply Grid Layout", transcribed from R2025b's
/// <c>appdesigner/gridify</c> modules, against App Designer's own output - 308 scenes its unmodified
/// modules gridified under node (<c>gridify-golden.json</c>), the samples of the reader's notes with
/// their values written out, the empty-container rule, and <c>shouldComponentGrow</c>'s table.
/// </summary>
public class AppDesignerGridifyTests(ITestOutputHelper output)
{
    private static readonly double[] GridPadding = [10, 10, 10, 10];

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Scripting", "Designer", name);

    private static DesignComponent Make(string type, string name, params double[] position)
    {
        DesignComponentClass? known = DesignCatalog.R2025b.ByType(type);
        DesignComponent component = known is null ? new DesignComponent(name, type, "uicomponent", string.Empty) : new DesignComponent(name, known);
        if (position.Length == 4)
        {
            component.Set("Position", NumericValue.Row(position));
        }

        return component;
    }

    /// <summary>
    /// A component at MATLAB's <c>Position</c> [x y w h] in a container <paramref name="height"/>
    /// pixels high, growing as its class does.
    /// </summary>
    private static GridifyComponent At(string id, string type, double height, double x, double y, double w, double h) =>
        Placed(Make(type, id), height, x, y, w, h);

    private static GridifyComponent Placed(DesignComponent component, double height, double x, double y, double w, double h)
    {
        (bool across, bool down) = DesignGridify.Growth(component);
        return new GridifyComponent(component.CodeName, new Rect2D(x - 1, height - (y - 1) - h, w, h), across, down);
    }

    private static GridifyResult Gridify(double width, double height, params GridifyComponent[] components) =>
        DesignGridify.Gridify(new Size2D(width, height), components, GridPadding, 10, 10);

    /// <summary>The tracks as App Designer writes them: <c>1.18x</c> for a weight, the number for pixels.</summary>
    private static string[] Tracks(IReadOnlyList<GridifyTrack> tracks) => tracks.Select(static t => t.ToString()).ToArray();

    // --- App Designer's own output ----------------------------------------------------------------------

    /// <summary>
    /// Every one of the 308 scenes App Designer's own gridifier was run on (8 named, 300 random:
    /// forms, free scatter, near-aligned lattices, tiny components, components over the edge) gives
    /// its rows and columns (weights as text, pixels to the bit), spacing, padding and every
    /// component's cells exactly.
    /// </summary>
    [Fact]
    public void EveryGoldenSceneGivesAppDesignersOwnGrid()
    {
        using JsonDocument golden = JsonDocument.Parse(File.ReadAllText(Fixture("gridify-golden.json")));
        var failures = new List<string>();
        int cases = 0;
        foreach (JsonElement scene in golden.RootElement.EnumerateArray())
        {
            cases++;
            string name = scene.GetProperty("name").GetString()!;
            JsonElement grow = scene.GetProperty("grow");
            var components = new List<GridifyComponent>();
            foreach (JsonElement c in scene.GetProperty("components").EnumerateArray())
            {
                string id = c.GetProperty("id").GetString()!;
                JsonElement g = grow.GetProperty(id);
                var box = new Rect2D(c.GetProperty("left").GetDouble(), c.GetProperty("top").GetDouble(), c.GetProperty("width").GetDouble(), c.GetProperty("height").GetDouble());
                components.Add(new GridifyComponent(id, box, g.GetProperty("horizontal").GetBoolean(), g.GetProperty("vertical").GetBoolean()));
            }

            JsonElement container = scene.GetProperty("container");
            JsonElement defaults = scene.GetProperty("defaults");
            GridifyResult result = DesignGridify.Gridify(
                new Size2D(container.GetProperty("width").GetDouble(), container.GetProperty("height").GetDouble()),
                components,
                Numbers(defaults.GetProperty("Padding")),
                defaults.GetProperty("RowSpacing").GetDouble(),
                defaults.GetProperty("ColumnSpacing").GetDouble(),
                scene.GetProperty("zoom").GetDouble());

            JsonElement expected = scene.GetProperty("expected");
            CompareTracks(name, "RowHeight", expected.GetProperty("RowHeight"), result.RowHeight, failures);
            CompareTracks(name, "ColumnWidth", expected.GetProperty("ColumnWidth"), result.ColumnWidth, failures);
            CompareNumber(name, "RowSpacing", expected.GetProperty("RowSpacing").GetDouble(), result.RowSpacing, failures);
            CompareNumber(name, "ColumnSpacing", expected.GetProperty("ColumnSpacing").GetDouble(), result.ColumnSpacing, failures);
            double[] padding = Numbers(expected.GetProperty("Padding"));
            if (!padding.SequenceEqual(result.Padding))
            {
                failures.Add($"{name}: Padding is [{string.Join(' ', result.Padding)}], App Designer's [{string.Join(' ', padding)}]");
            }

            JsonElement layout = expected.GetProperty("Layout");
            if (layout.EnumerateObject().Count() != result.Cells.Count)
            {
                failures.Add($"{name}: {result.Cells.Count} components placed, App Designer placed {layout.EnumerateObject().Count()}");
            }

            foreach (JsonProperty placed in layout.EnumerateObject())
            {
                if (!result.Cells.TryGetValue(placed.Name, out GridifyCell? cell))
                {
                    failures.Add($"{name}: '{placed.Name}' is not placed");
                    continue;
                }

                double[] row = Numbers(placed.Value.GetProperty("Row"));
                double[] column = Numbers(placed.Value.GetProperty("Column"));
                if (!row.SequenceEqual(cell.ToRowValue().Data) || !column.SequenceEqual(cell.ToColumnValue().Data))
                {
                    failures.Add($"{name}: '{placed.Name}' is in {cell}, App Designer's Row {placed.Value.GetProperty("Row").GetRawText()} Column {placed.Value.GetProperty("Column").GetRawText()}");
                }
            }
        }

        output.WriteLine($"{cases} scenes, {failures.Count} differences");
        Assert.Equal(308, cases);
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static double[] Numbers(JsonElement element) => element.ValueKind == JsonValueKind.Array
        ? element.EnumerateArray().Select(static e => e.GetDouble()).ToArray()
        : [element.GetDouble()];

    private static void CompareTracks(string name, string property, JsonElement expected, IReadOnlyList<GridifyTrack> actual, List<string> failures)
    {
        JsonElement[] wanted = expected.EnumerateArray().ToArray();
        bool same = wanted.Length == actual.Count && wanted.Zip(actual).All(static p => p.First.ValueKind == JsonValueKind.String
            ? p.Second.IsWeight && p.Second.WeightText == p.First.GetString()
            : !p.Second.IsWeight && p.Second.Value == p.First.GetDouble());
        if (!same)
        {
            failures.Add($"{name}: {property} is {{{string.Join(", ", actual)}}}, App Designer's {expected.GetRawText()}");
        }
    }

    private static void CompareNumber(string name, string property, double expected, double actual, List<string> failures)
    {
        if (expected != actual)
        {
            failures.Add($"{name}: {property} is {actual:R}, App Designer's {expected:R}");
        }
    }

    // --- the named samples ------------------------------------------------------------------------------

    /// <summary>
    /// Sample A: three buttons in a row in a 640-by-480 figure. The empty edge tracks give up the
    /// overflow equally; the buttons, which do not grow, keep 22 and 100 pixels.
    /// </summary>
    [Fact]
    public void ThreeButtonsInARowSitInPixelTracksBetweenWeights()
    {
        GridifyResult result = Gridify(
            640,
            480,
            At("Button", "matlab.ui.control.Button", 480, 100, 400, 100, 22),
            At("Button2", "matlab.ui.control.Button", 480, 250, 400, 100, 22),
            At("Button3", "matlab.ui.control.Button", 480, 400, 400, 100, 22));
        Assert.Equal(["1x", "22", "9.72x"], Tracks(result.RowHeight));
        Assert.Equal(["1.18x", "100", "1x", "100", "1x", "100", "2.02x"], Tracks(result.ColumnWidth));
        Assert.Equal([10.0, 10, 10, 10], result.Padding);
        Assert.Equal(10, result.RowSpacing);
        Assert.Equal(10, result.ColumnSpacing);
        Assert.Equal(new GridifyCell(2, 2, 2, 2), result.Cells["Button"]);
        Assert.Equal(new GridifyCell(2, 2, 4, 4), result.Cells["Button2"]);
        Assert.Equal(new GridifyCell(2, 2, 6, 6), result.Cells["Button3"]);
        Assert.False(result.RowHeight[1].IsWeight);
        Assert.Equal(22, result.RowHeight[1].Value);
        Assert.True(result.RowHeight[2].IsWeight);
        Assert.Equal(9.72, result.RowHeight[2].Value);
    }

    /// <summary>
    /// Sample B: the same at canvas zoom 1.5. The pixels come back divided by the zoom, but the
    /// weights differ from A, because App Designer scales neither the padding nor the spacing.
    /// </summary>
    [Fact]
    public void TheCanvasZoomChangesTheWeightsButNotThePixels()
    {
        GridifyComponent[] buttons =
        [
            At("Button", "matlab.ui.control.Button", 480, 100, 400, 100, 22),
            At("Button2", "matlab.ui.control.Button", 480, 250, 400, 100, 22),
            At("Button3", "matlab.ui.control.Button", 480, 400, 400, 100, 22),
        ];
        GridifyResult result = DesignGridify.Gridify(new Size2D(640, 480), buttons, GridPadding, 10, 10, zoom: 1.5);
        Assert.Equal(["1x", "22", "8.45x"], Tracks(result.RowHeight));
        Assert.Equal(["1.45x", "100", "1x", "100", "1x", "100", "2.29x"], Tracks(result.ColumnWidth));
        Assert.Equal([10.0, 10, 10, 10], result.Padding);
        Assert.Equal(new GridifyCell(2, 2, 4, 4), result.Cells["Button2"]);
    }

    /// <summary>
    /// Sample C: a three-row form of labels and edit fields. The 18-pixel gaps between rows exceed the
    /// 15-pixel tolerance and stay as empty rows; the edit fields' column is a weight, as an edit field
    /// grows across.
    /// </summary>
    [Fact]
    public void AFormKeepsItsGapsAsEmptyRowsAndItsFieldsColumnAsAWeight()
    {
        GridifyResult result = Gridify(
            640,
            480,
            At("NameLabel", "matlab.ui.control.Label", 480, 41, 420, 55, 22),
            At("NameEditField", "matlab.ui.control.EditField", 480, 111, 420, 100, 22),
            At("AgeLabel", "matlab.ui.control.Label", 480, 41, 380, 55, 22),
            At("AgeEditField", "matlab.ui.control.EditField", 480, 111, 380, 100, 22),
            At("CityLabel", "matlab.ui.control.Label", 480, 41, 340, 55, 22),
            At("CityEditField", "matlab.ui.control.EditField", 480, 111, 340, 100, 22));
        Assert.Equal(["1x", "22", "1.2x", "22", "1.2x", "22", "18.87x"], Tracks(result.RowHeight));
        Assert.Equal(["1x", "55", "4x", "16.4x"], Tracks(result.ColumnWidth));
        Assert.Equal([10.0, 10, 10, 10], result.Padding);
        Assert.Equal(new GridifyCell(2, 2, 2, 2), result.Cells["NameLabel"]);
        Assert.Equal(new GridifyCell(2, 2, 3, 3), result.Cells["NameEditField"]);
        Assert.Equal(new GridifyCell(4, 4, 2, 2), result.Cells["AgeLabel"]);
        Assert.Equal(new GridifyCell(4, 4, 3, 3), result.Cells["AgeEditField"]);
        Assert.Equal(new GridifyCell(6, 6, 2, 2), result.Cells["CityLabel"]);
        Assert.Equal(new GridifyCell(6, 6, 3, 3), result.Cells["CityEditField"]);
    }

    /// <summary>
    /// Sample D: two overlapping buttons and an axes. Overlaps become spans sharing tracks, and a
    /// shared track is in pixels when any component in it does not grow.
    /// </summary>
    [Fact]
    public void OverlappingComponentsSpanTheTracksTheyShare()
    {
        GridifyResult result = Gridify(
            640,
            480,
            At("Button", "matlab.ui.control.Button", 480, 100, 300, 100, 50),
            At("Button2", "matlab.ui.control.Button", 480, 150, 320, 100, 50),
            At("UIAxes", "matlab.ui.control.UIAxes", 480, 300, 150, 300, 185));
        Assert.Equal(["1x", "20", "30", "20", "1.78x", "1.7x"], Tracks(result.RowHeight));
        Assert.Equal(["1.4x", "50", "50", "50", "1x", "6x"], Tracks(result.ColumnWidth));
        Assert.Equal(new GridifyCell(3, 4, 2, 3), result.Cells["Button"]);
        Assert.Equal(new GridifyCell(2, 3, 3, 4), result.Cells["Button2"]);
        Assert.Equal(new GridifyCell(3, 5, 6, 6), result.Cells["UIAxes"]);
        Assert.Equal([3.0, 4], result.Cells["Button"].ToRowValue().Data);
        Assert.Equal([6.0], result.Cells["UIAxes"].ToColumnValue().Data);
    }

    /// <summary>
    /// Sample E: buttons stacked 8 pixels apart. The gaps are within the tolerance and are cut out, so
    /// the buttons sit in adjacent rows with the grid's spacing between them.
    /// </summary>
    [Fact]
    public void ButtonsStackedCloseTogetherTakeAdjacentRows()
    {
        GridifyResult result = Gridify(
            640,
            480,
            At("B1", "matlab.ui.control.Button", 480, 20, 440, 100, 22),
            At("B2", "matlab.ui.control.Button", 480, 20, 410, 100, 22),
            At("B3", "matlab.ui.control.Button", 480, 20, 380, 100, 22));
        Assert.Equal(["1x", "22", "22", "22", "22.6x"], Tracks(result.RowHeight));
        Assert.Equal(["100", "1x"], Tracks(result.ColumnWidth));
        Assert.Equal(new GridifyCell(2, 2, 1, 1), result.Cells["B1"]);
        Assert.Equal(new GridifyCell(3, 3, 1, 1), result.Cells["B2"]);
        Assert.Equal(new GridifyCell(4, 4, 1, 1), result.Cells["B3"]);
    }

    /// <summary>
    /// Sample F: the classic app - an axes, two buttons and a text area. The rows overflow by more than
    /// the empty rows hold, so the dynamic step takes 20/7 from the top and bottom padding and the row
    /// spacing: fractional padding and spacing are App Designer's real output.
    /// </summary>
    [Fact]
    public void AnOverflowingAppGivesUpPaddingAndSpacingInFractions()
    {
        GridifyResult result = Gridify(
            640,
            480,
            At("UIAxes", "matlab.ui.control.UIAxes", 480, 220, 180, 400, 290),
            At("PlotButton", "matlab.ui.control.Button", 480, 40, 420, 100, 22),
            At("ClearButton", "matlab.ui.control.Button", 480, 40, 380, 100, 22),
            At("TextArea", "matlab.ui.control.TextArea", 480, 40, 20, 580, 140));
        Assert.Equal(["1.56x", "22", "1x", "22", "11.11x", "7.78x"], Tracks(result.RowHeight));
        Assert.Equal(["100", "1x", "5x"], Tracks(result.ColumnWidth));
        Assert.Equal([10, 7.142857142857142, 10, 7.142857142857142], result.Padding);
        Assert.Equal(7.142857142857142, result.RowSpacing);
        Assert.Equal(10 - (20.0 / 7), result.RowSpacing);
        Assert.Equal(10, result.ColumnSpacing);
        Assert.Equal(new GridifyCell(1, 5, 3, 3), result.Cells["UIAxes"]);
        Assert.Equal(new GridifyCell(2, 2, 1, 1), result.Cells["PlotButton"]);
        Assert.Equal(new GridifyCell(4, 4, 1, 1), result.Cells["ClearButton"]);
        Assert.Equal(new GridifyCell(6, 6, 1, 3), result.Cells["TextArea"]);
    }

    /// <summary>Sample G: one button near the middle sits in a pixel cell between two weights.</summary>
    [Fact]
    public void ASingleButtonSitsBetweenTwoWeights()
    {
        GridifyResult result = Gridify(640, 480, At("Button", "matlab.ui.control.Button", 480, 270, 229, 100, 22));
        Assert.Equal(["1.01x", "22", "1x"], Tracks(result.RowHeight));
        Assert.Equal(["1x", "100", "1.01x"], Tracks(result.ColumnWidth));
        Assert.Equal(new GridifyCell(2, 2, 2, 2), Assert.Single(result.Cells).Value);
    }

    /// <summary>
    /// Sample H: an axes 5 pixels inside every edge makes a single track each way, centred, its
    /// padding the 5 pixels it leaves.
    /// </summary>
    [Fact]
    public void AnAxesFillingTheFigureMakesOneCellWithItsMarginsAsPadding()
    {
        GridifyResult result = Gridify(640, 480, At("UIAxes", "matlab.ui.control.UIAxes", 480, 6, 6, 630, 470));
        Assert.Equal(["1x"], Tracks(result.RowHeight));
        Assert.Equal(["1x"], Tracks(result.ColumnWidth));
        Assert.Equal([5.0, 5, 5, 5], result.Padding);
        Assert.Equal(10, result.RowSpacing);
        Assert.Equal(new GridifyCell(1, 1, 1, 1), result.Cells["UIAxes"]);
    }

    /// <summary>Sample I: a list box beside two buttons and a lamp in a 400-by-300 figure; the buttons span the lamp's columns.</summary>
    [Fact]
    public void AListBoxBesideButtonsSpansItsRows()
    {
        GridifyResult result = Gridify(
            400,
            300,
            At("ListBox", "matlab.ui.control.ListBox", 300, 20, 60, 200, 220),
            At("AddButton", "matlab.ui.control.Button", 300, 260, 258, 100, 22),
            At("RemoveButton", "matlab.ui.control.Button", 300, 260, 220, 100, 22),
            At("Lamp", "matlab.ui.control.Lamp", 300, 300, 60, 20, 20));
        Assert.Equal(["22", "1x", "22", "8.75x", "20"], Tracks(result.RowHeight));
        Assert.Equal(["5x", "1x", "40", "20", "40"], Tracks(result.ColumnWidth));
        Assert.Equal(new GridifyCell(1, 5, 1, 1), result.Cells["ListBox"]);
        Assert.Equal(new GridifyCell(1, 1, 3, 5), result.Cells["AddButton"]);
        Assert.Equal(new GridifyCell(3, 3, 3, 5), result.Cells["RemoveButton"]);
        Assert.Equal(new GridifyCell(5, 5, 4, 4), result.Cells["Lamp"]);
    }

    /// <summary>
    /// Sample J: an empty panel is fixed both ways, and a panel holding a table grows, the growth
    /// coming from <see cref="DesignGridify.Growth"/> on the design's own components.
    /// </summary>
    [Fact]
    public void AnEmptyPanelIsFixedAndAPanelHoldingATableGrows()
    {
        DesignComponent empty = Make("matlab.ui.container.Panel", "EmptyPanel", 20, 250, 260, 221);
        DesignComponent holding = Make("matlab.ui.container.Panel", "TablePanel", 320, 250, 300, 221);
        holding.Add(Make("matlab.ui.control.Table", "UITable"));
        GridifyResult result = Gridify(640, 480, Placed(empty, 480, 20, 250, 260, 221), Placed(holding, 480, 320, 250, 300, 221));
        Assert.Equal(["221", "1x"], Tracks(result.RowHeight));
        Assert.Equal(["260", "1x", "7.5x"], Tracks(result.ColumnWidth));
        Assert.Equal(new GridifyCell(1, 1, 1, 1), result.Cells["EmptyPanel"]);
        Assert.Equal(new GridifyCell(1, 1, 3, 3), result.Cells["TablePanel"]);
    }

    /// <summary>
    /// Sample K: two lamps hanging over the edges of a 377-by-288 container, whose overflow is more than
    /// any weight can give: App Designer's arithmetic makes a row of -3.5 pixels, and it is returned as
    /// App Designer gives it, not clamped.
    /// </summary>
    [Fact]
    public void ComponentsHangingOverTheEdgeCanMakeANegativeTrackWhichIsReturnedAsItIs()
    {
        GridifyResult result = Gridify(
            377,
            288,
            new GridifyComponent("Lamp", new Rect2D(-27.5, 112, 225, 286), false, false),
            new GridifyComponent("Lamp2", new Rect2D(202.5, -6, 85, 142), false, false));
        Assert.Equal(["90.5", "-3.5", "124.5", "82.5"], Tracks(result.RowHeight));
        Assert.Equal(["27.5", "197.5", "85", "1x"], Tracks(result.ColumnWidth));
        Assert.Equal(-3.5, result.RowHeight[1].Value);
        Assert.Equal([10.0, 0, 10, 0], result.Padding);
        Assert.Equal(0, result.RowSpacing);
        Assert.Equal(10, result.ColumnSpacing);
        Assert.Equal(new GridifyCell(2, 4, 1, 2), result.Cells["Lamp"]);
        Assert.Equal(new GridifyCell(1, 2, 3, 3), result.Cells["Lamp2"]);
    }

    /// <summary>A zero spacing is taken as 10 when there are components (<c>RowSpacing || 10</c>), as App Designer's parameters factory does.</summary>
    [Fact]
    public void AZeroSpacingIsTakenAsTenWhenThereAreComponents()
    {
        GridifyComponent button = At("Button", "matlab.ui.control.Button", 480, 270, 229, 100, 22);
        GridifyResult zero = DesignGridify.Gridify(new Size2D(640, 480), [button], GridPadding, 0, 0);
        GridifyResult ten = Gridify(640, 480, button);
        Assert.Equal(Tracks(ten.RowHeight), Tracks(zero.RowHeight));
        Assert.Equal(Tracks(ten.ColumnWidth), Tracks(zero.ColumnWidth));
        Assert.Equal(10, zero.RowSpacing);
        Assert.Equal(10, zero.ColumnSpacing);
    }

    /// <summary>Two components with one id cannot be told apart in the result, and are refused.</summary>
    [Fact]
    public void ARepeatedIdIsRefused()
    {
        GridifyComponent a = At("Button", "matlab.ui.control.Button", 480, 20, 20, 100, 22);
        GridifyComponent b = At("Button", "matlab.ui.control.Button", 480, 200, 20, 100, 22);
        Assert.Throws<ArgumentException>(() => Gridify(640, 480, a, b));
    }

    // --- the empty container ---------------------------------------------------------------------------

    /// <summary>
    /// An empty container gets as many 200-pixel columns and 50-pixel rows as fit between the padding
    /// with the spacing between them, rounded: 640 by 480 makes 8 rows of 3 columns.
    /// </summary>
    [Fact]
    public void AnEmpty640By480ContainerGetsEightRowsOfThreeColumns()
    {
        GridifyResult result = Gridify(640, 480);
        Assert.Equal(Enumerable.Repeat("1x", 8), Tracks(result.RowHeight));
        Assert.Equal(Enumerable.Repeat("1x", 3), Tracks(result.ColumnWidth));
        Assert.Equal([10.0, 10, 10, 10], result.Padding);
        Assert.Equal(10, result.RowSpacing);
        Assert.Equal(10, result.ColumnSpacing);
        Assert.Empty(result.Cells);
    }

    /// <summary>A 300-by-200 cell would fit one column; the rule gives at least two each way, so 3 rows of 2.</summary>
    [Fact]
    public void AnEmpty300By200ContainerGetsThreeRowsOfTwoColumns()
    {
        GridifyResult result = Gridify(300, 200);
        Assert.Equal(Enumerable.Repeat("1x", 3), Tracks(result.RowHeight));
        Assert.Equal(Enumerable.Repeat("1x", 2), Tracks(result.ColumnWidth));
        Assert.All(result.RowHeight, static t => Assert.Equal(new GridifyTrack(1, true), t));
    }

    /// <summary>
    /// The empty-container rule takes the spacing and padding as given - a zero spacing stays 0 and
    /// fits more tracks - and a container too small for two of either still gets two.
    /// </summary>
    [Fact]
    public void TheEmptyContainerRuleTakesTheSpacingAsGivenAndGivesAtLeastTwoTracks()
    {
        GridifyResult tight = DesignGridify.Gridify(new Size2D(640, 480), [], GridPadding, 0, 0);
        Assert.Equal(9, tight.RowHeight.Count);
        Assert.Equal(3, tight.ColumnWidth.Count);
        Assert.Equal(0, tight.RowSpacing);
        Assert.Equal(0, tight.ColumnSpacing);

        GridifyResult small = Gridify(40, 30);
        Assert.Equal(2, small.RowHeight.Count);
        Assert.Equal(2, small.ColumnWidth.Count);
    }

    // --- the tracks' values ------------------------------------------------------------------------------

    /// <summary>
    /// A weight is written as JavaScript prints the number, then <c>x</c>: the shortest digits, no
    /// trailing zeros, either zero as 0, exponents from 1e21 and below 1e-6.
    /// </summary>
    [Theory]
    [InlineData(1.0, "1x")]
    [InlineData(1.18, "1.18x")]
    [InlineData(16.4, "16.4x")]
    [InlineData(-0.05, "-0.05x")]
    [InlineData(-0.0, "0x")]
    [InlineData(0.000001, "0.000001x")]
    [InlineData(1e-7, "1e-7x")]
    [InlineData(123456789012.0, "123456789012x")]
    [InlineData(1e21, "1e+21x")]
    [InlineData(double.PositiveInfinity, "Infinityx")]
    public void AWeightIsWrittenAsJavaScriptPrintsItsNumber(double weight, string text)
    {
        Assert.Equal(text, new GridifyTrack(weight, true).WeightText);
    }

    /// <summary>A weight becomes the grid's char row, pixels its number; a pixel track has no weight text.</summary>
    [Fact]
    public void ATrackBecomesTheValueAGridHolds()
    {
        Assert.Equal("1.18x", Assert.IsType<CharValue>(new GridifyTrack(1.18, true).ToDesignValue()).Text);
        Assert.Equal([22.0], Assert.IsType<NumericValue>(new GridifyTrack(22, false).ToDesignValue()).Data);
        Assert.Equal(string.Empty, new GridifyTrack(22, false).WeightText);
        Assert.Equal([2.0], new GridifyCell(2, 2, 3, 4).ToRowValue().Data);
        Assert.Equal([3.0, 4], new GridifyCell(2, 2, 3, 4).ToColumnValue().Data);
    }

    /// <summary>App Designer rounds with JavaScript's <c>Math.round</c>: halves go up, -2.5 to -2, not to even.</summary>
    [Theory]
    [InlineData(2.5, 3.0)]
    [InlineData(3.5, 4.0)]
    [InlineData(-2.5, -2.0)]
    [InlineData(-2.6, -3.0)]
    [InlineData(0.49999999999999994, 0.0)]
    [InlineData(972.0000000000001, 972.0)]
    public void RoundingTakesHalvesUpward(double value, double rounded)
    {
        Assert.Equal(rounded, DesignGridify.JsRound(value));
    }

    // --- which components grow -----------------------------------------------------------------------------

    /// <summary>
    /// <c>shouldComponentGrow</c>'s table by class: a button neither way, an edit field across, a list
    /// box both; a slider along its orientation, its default horizontal and the case ignored; a range
    /// slider never up and down; the classes App Designer's table names that are not in the catalog,
    /// and one it never matches (a time scope, compared after lowercasing).
    /// </summary>
    [Theory]
    [InlineData("matlab.ui.control.Button", null, false, false)]
    [InlineData("matlab.ui.control.Label", null, false, false)]
    [InlineData("matlab.ui.control.Lamp", null, false, false)]
    [InlineData("matlab.ui.control.Image", null, false, false)]
    [InlineData("matlab.ui.control.CheckBox", null, false, false)]
    [InlineData("matlab.ui.control.Switch", "vertical", false, false)]
    [InlineData("matlab.ui.control.EditField", null, true, false)]
    [InlineData("matlab.ui.control.NumericEditField", null, true, false)]
    [InlineData("matlab.ui.control.DropDown", null, true, false)]
    [InlineData("matlab.ui.control.DatePicker", null, true, false)]
    [InlineData("matlab.ui.control.Spinner", null, true, false)]
    [InlineData("matlab.ui.control.ListBox", null, true, true)]
    [InlineData("matlab.ui.control.Table", null, true, true)]
    [InlineData("matlab.ui.control.TextArea", null, true, true)]
    [InlineData("matlab.ui.control.UIAxes", null, true, true)]
    [InlineData("matlab.ui.control.Knob", null, true, true)]
    [InlineData("matlab.ui.control.Gauge", null, true, true)]
    [InlineData("matlab.ui.container.Tree", null, true, true)]
    [InlineData("matlab.ui.control.Slider", null, true, false)]
    [InlineData("matlab.ui.control.Slider", "horizontal", true, false)]
    [InlineData("matlab.ui.control.Slider", "vertical", false, true)]
    [InlineData("matlab.ui.control.Slider", "Vertical", false, true)]
    [InlineData("matlab.ui.control.RangeSlider", "horizontal", true, false)]
    [InlineData("matlab.ui.control.RangeSlider", "vertical", false, false)]
    [InlineData("matlab.ui.control.LinearGauge", null, true, false)]
    [InlineData("matlab.ui.control.LinearGauge", "vertical", false, true)]
    [InlineData("aero.ui.control.Altimeter", null, true, true)]
    [InlineData("matlab.ui.control.internal.ProgressIndicator", null, true, false)]
    [InlineData("matlab.ui.control.internal.WorkspaceBrowser", null, true, false)]
    [InlineData("matlab.ui.scope.TimeScope", null, false, false)]
    [InlineData("matlab.ui.container.Toolbar", null, false, false)]
    public void AComponentGrowsAsAppDesignersTableSays(string type, string? orientation, bool horizontal, bool vertical)
    {
        DesignComponent component = Make(type, "Component");
        if (orientation is not null)
        {
            component.Set("Orientation", new CharValue(orientation));
        }

        Assert.Equal((horizontal, vertical), DesignGridify.Growth(component));
    }

    /// <summary>
    /// For every component of the 300 random scenes, its growth from the design's own component - its
    /// class from the catalog, its orientation set - is what App Designer's own
    /// <c>ComponentAutoLayoutInfo</c> computed.
    /// </summary>
    [Fact]
    public void EveryGoldenComponentGrowsAsAppDesignerComputed()
    {
        using JsonDocument golden = JsonDocument.Parse(File.ReadAllText(Fixture("gridify-golden.json")));
        var failures = new List<string>();
        int components = 0;
        foreach (JsonElement scene in golden.RootElement.EnumerateArray())
        {
            JsonElement grow = scene.GetProperty("grow");
            foreach (JsonElement c in scene.GetProperty("components").EnumerateArray())
            {
                components++;
                string id = c.GetProperty("id").GetString()!;
                string type = c.GetProperty("type").GetString()!;
                DesignComponent component = Make(type, id);
                if (component.Class?.Property("Orientation") is not null)
                {
                    component.Set("Orientation", new CharValue(c.GetProperty("orientation").GetString()!));
                }

                (bool, bool) expected = (grow.GetProperty(id).GetProperty("horizontal").GetBoolean(), grow.GetProperty(id).GetProperty("vertical").GetBoolean());
                (bool, bool) actual = DesignGridify.Growth(component);
                if (expected != actual)
                {
                    failures.Add($"{scene.GetProperty("name").GetString()}: {id} ({type}, {c.GetProperty("orientation").GetString()}) grows {actual}, App Designer's {expected}");
                }
            }
        }

        output.WriteLine($"{components} components");
        Assert.True(components > 1000, $"only {components} components");
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>An empty container does not grow; one grows as any of the components inside it does.</summary>
    [Fact]
    public void AContainerGrowsAsTheComponentsInsideItDo()
    {
        Assert.Equal((false, false), DesignGridify.Growth(Make("matlab.ui.container.Panel", "Panel", 20, 20, 260, 221)));

        DesignComponent table = Make("matlab.ui.container.Panel", "TablePanel", 20, 20, 300, 221);
        table.Add(Make("matlab.ui.control.Table", "UITable"));
        Assert.Equal((true, true), DesignGridify.Growth(table));

        DesignComponent form = Make("matlab.ui.container.Panel", "FormPanel", 20, 20, 300, 221);
        form.Add(Make("matlab.ui.control.Button", "Button", 20, 20, 100, 22));
        form.Add(Make("matlab.ui.control.EditField", "EditField", 20, 60, 100, 22));
        Assert.Equal((true, false), DesignGridify.Growth(form));

        DesignComponent outer = Make("matlab.ui.container.Panel", "Outer", 20, 20, 300, 221);
        DesignComponent inner = Make("matlab.ui.container.Panel", "Inner", 10, 10, 200, 150);
        outer.Add(inner);
        inner.Add(Make("matlab.ui.control.Slider", "Slider", 10, 10, 3, 100));
        inner.Children[0].Set("Orientation", new CharValue("vertical"));
        Assert.Equal((false, true), DesignGridify.Growth(outer));

        DesignComponent group = Make("matlab.ui.container.ButtonGroup", "ButtonGroup", 20, 20, 120, 106);
        group.Add(Make("matlab.ui.control.RadioButton", "Button1", 11, 60, 70, 22));
        group.Add(Make("matlab.ui.control.RadioButton", "Button2", 11, 38, 70, 22));
        Assert.Equal((false, false), DesignGridify.Growth(group));

        DesignComponent tabs = Make("matlab.ui.container.TabGroup", "TabGroup", 20, 20, 250, 210);
        DesignComponent tab = Make("matlab.ui.container.Tab", "Tab");
        tabs.Add(tab);
        tab.Add(Make("matlab.ui.control.ListBox", "ListBox", 20, 20, 100, 74));
        Assert.Equal((true, true), DesignGridify.Growth(tab));
        Assert.Equal((true, true), DesignGridify.Growth(tabs));
    }

    /// <summary>
    /// A container's child counts only when some of it is inside: x - 1 + w and y - 1 + h at least 0.
    /// A table 300 wide at x = -299 just reaches in; at x = -300 it does not, and the panel does not grow.
    /// </summary>
    [Fact]
    public void AChildEntirelyOutsideItsContainerDoesNotMakeItGrow()
    {
        DesignComponent reaching = Make("matlab.ui.container.Panel", "Reaching", 20, 20, 300, 221);
        reaching.Add(Make("matlab.ui.control.Table", "UITable", -299, 20, 300, 300));
        Assert.Equal((true, true), DesignGridify.Growth(reaching));

        DesignComponent outside = Make("matlab.ui.container.Panel", "Outside", 20, 20, 300, 221);
        outside.Add(Make("matlab.ui.control.Table", "UITable", -300, 20, 300, 300));
        Assert.Equal((false, false), DesignGridify.Growth(outside));

        DesignComponent below = Make("matlab.ui.container.Panel", "Below", 20, 20, 300, 221);
        below.Add(Make("matlab.ui.control.Table", "UITable", 20, -300, 300, 300));
        Assert.Equal((false, false), DesignGridify.Growth(below));
    }

    /// <summary>
    /// A grid grows across when any of its columns is a weight and up and down when any of its rows is:
    /// by default ({'1x', '1x'}) both ways; pixels and 'fit' are not weights.
    /// </summary>
    [Fact]
    public void AGridGrowsWhereItHasAWeightedTrack()
    {
        Assert.Equal((true, true), DesignGridify.Growth(Make("matlab.ui.container.GridLayout", "GridLayout")));

        DesignComponent rows = Make("matlab.ui.container.GridLayout", "Rows");
        rows.Set("ColumnWidth", CellValue.Row(NumericValue.Scalar(100), new CharValue("fit")));
        rows.Set("RowHeight", CellValue.Row(new CharValue("1x"), NumericValue.Scalar(22)));
        Assert.Equal((false, true), DesignGridify.Growth(rows));

        DesignComponent fixedGrid = Make("matlab.ui.container.GridLayout", "Fixed");
        fixedGrid.Set("ColumnWidth", NumericValue.Row(100, 200));
        fixedGrid.Set("RowHeight", CellValue.Row(NumericValue.Scalar(22), new CharValue("fit")));
        Assert.Equal((false, false), DesignGridify.Growth(fixedGrid));

        DesignComponent strings = Make("matlab.ui.container.GridLayout", "Strings");
        strings.Set("ColumnWidth", CellValue.Row(new StringValue("2x")));
        Assert.Equal((true, true), DesignGridify.Growth(strings));

        DesignComponent panel = Make("matlab.ui.container.Panel", "Panel", 20, 20, 300, 221);
        panel.Add(rows);
        Assert.Equal((false, true), DesignGridify.Growth(panel));
    }
}
