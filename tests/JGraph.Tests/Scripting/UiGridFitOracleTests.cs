using System.Globalization;
using System.Text.Json;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using Xunit;
using Xunit.Abstractions;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Open item 153: <c>UiGridMath</c> against R2025b's own runtime grid code - its
/// <c>GridController</c>, <c>GridLayoutUtils</c>, <c>Grid</c> view, <c>GridLayoutStrategy</c>, scroll
/// decision and design-time <c>GridController</c>, run unmodified under node with the MATLAB side's
/// <c>convert0xTo0px</c> first (GridFitOracle.json: the four differences' worked cases and seeded-random
/// grids). Each case checks the tracks and the spacing the view lays between them, every child's
/// rectangle, a grid's own fit size, and whether a scrollable grid scrolls and how far.
/// </summary>
public class UiGridFitOracleTests(ITestOutputHelper output)
{
    private const double Tolerance = 1e-6;

    private static readonly Lazy<JsonDocument> Oracle = new(() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scripting", "GridFitOracle.json"))));

    private static IEnumerable<JsonElement> Cases() => Oracle.Value.RootElement.GetProperty("cases").EnumerateArray();

    private static JsonElement Case(string name) => Cases().Single(c => c.GetProperty("name").GetString() == name);

    private static UiGridTrack Track(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number)
        {
            return new UiGridTrack(UiGridTrackKind.Fixed, value.GetDouble());
        }

        string word = value.GetString()!;
        return word == "fit"
            ? UiGridTrack.Fit
            : new UiGridTrack(UiGridTrackKind.Weight, double.Parse(word[..^1], CultureInfo.InvariantCulture));
    }

    private static (int From, int To) Run(JsonElement value) =>
        value.ValueKind == JsonValueKind.Array ? (value[0].GetInt32(), value[1].GetInt32()) : (value.GetInt32(), value.GetInt32());

    private static double[] Numbers(JsonElement value) => [.. value.EnumerateArray().Select(v => v.GetDouble())];

    private sealed record Grid(
        string Name,
        UiGridTrack[] Rows,
        UiGridTrack[] Columns,
        double[] Padding,
        double RowSpacing,
        double ColumnSpacing,
        Size2D? Size,
        UiGridItem[] Items,
        bool DesignTime,
        bool Scrollable,
        JsonElement Expect);

    private static Grid Read(JsonElement c)
    {
        UiGridItem[] items = [.. c.GetProperty("items").EnumerateArray().Select(item =>
        {
            (int r0, int r1) = Run(item.GetProperty("row"));
            (int c0, int c1) = Run(item.GetProperty("column"));
            double[] fit = Numbers(item.GetProperty("fit"));
            return new UiGridItem(new UiGridCell(r0, r1, c0, c1), new Size2D(fit[0], fit[1]));
        })];
        JsonElement size = c.GetProperty("size");
        return new Grid(
            c.GetProperty("name").GetString()!,
            [.. c.GetProperty("rows").EnumerateArray().Select(Track)],
            [.. c.GetProperty("columns").EnumerateArray().Select(Track)],
            Numbers(c.GetProperty("padding")),
            c.GetProperty("rowSpacing").GetDouble(),
            c.GetProperty("columnSpacing").GetDouble(),
            size.ValueKind == JsonValueKind.Null ? null : new Size2D(size[0].GetDouble(), size[1].GetDouble()),
            items,
            c.TryGetProperty("designTime", out JsonElement design) && design.GetBoolean(),
            c.TryGetProperty("scrollable", out JsonElement scroll) && scroll.GetBoolean(),
            c.GetProperty("expect"));
    }

    /// <summary>What JGraph gives against what R2025b gives, as a list of differences (empty when they agree).</summary>
    private static List<string> Compare(Grid g)
    {
        var wrong = new List<string>();
        void Check(string what, double expected, double actual)
        {
            if (Math.Abs(expected - actual) > Tolerance)
            {
                wrong.Add(string.Create(CultureInfo.InvariantCulture, $"{g.Name} {what}: R2025b {expected}, JGraph {actual}"));
            }
        }

        if (g.Size is null)
        {
            UiGridArrangement own = UiGridMath.Arrange(g.Rows, g.Columns, g.Padding, g.RowSpacing, g.ColumnSpacing, null, g.Items, g.DesignTime);
            double[] fit = Numbers(g.Expect.GetProperty("fit"));
            Check("fit width", fit[0], own.Content.Width);
            Check("fit height", fit[1], own.Content.Height);
            return wrong;
        }

        UiGridArrangement arrangement;
        double lift = 0;
        if (g.Scrollable)
        {
            UiGridScrolled scrolled = UiGridMath.ArrangeScrolling(g.Rows, g.Columns, g.Padding, g.RowSpacing, g.ColumnSpacing, g.Size.Value, g.Items);
            JsonElement scroll = g.Expect.GetProperty("scroll");
            if (scroll.GetProperty("vertical").GetBoolean() != scrolled.Vertical || scroll.GetProperty("horizontal").GetBoolean() != scrolled.Horizontal)
            {
                wrong.Add($"{g.Name} bars: R2025b {scroll.GetProperty("vertical")} {scroll.GetProperty("horizontal")}, JGraph {scrolled.Vertical} {scrolled.Horizontal}");
                return wrong;
            }

            double[] content = Numbers(scroll.GetProperty("content"));
            Check("scroll content width", content[0], scrolled.Arrangement.Content.Width);
            Check("scroll content height", content[1], scrolled.Arrangement.Content.Height);
            arrangement = scrolled.Arrangement;

            // R2025b places a child in the whole grid's height; the arrangement is the viewport's,
            // which a bar along the bottom shortens (UiGridLayoutModel.Arrange lifts the cells).
            lift = scrolled.Horizontal ? UiGridMath.ScrollBarSize : 0;
        }
        else
        {
            arrangement = UiGridMath.Arrange(g.Rows, g.Columns, g.Padding, g.RowSpacing, g.ColumnSpacing, g.Size, g.Items, g.DesignTime);
        }

        CheckView("column", Numbers(g.Expect.GetProperty("columnsView")), arrangement.ColumnWidths, arrangement.ColumnStarts, Check);
        CheckView("row", Numbers(g.Expect.GetProperty("rowsView")), arrangement.RowHeights, arrangement.RowStarts, Check);
        JsonElement[] cells = [.. g.Expect.GetProperty("cells").EnumerateArray()];
        for (int i = 0; i < cells.Length; i++)
        {
            double[] r = Numbers(cells[i]);
            Rect2D cell = arrangement.Cells[i];
            Check($"cell {i + 1} x", r[0], cell.X - 1);
            Check($"cell {i + 1} y", r[1], cell.Y - 1 + lift);
            Check($"cell {i + 1} w", r[2], cell.Width);
            Check($"cell {i + 1} h", r[3], cell.Height);
        }

        return wrong;
    }

    /// <summary>The view's tracks with the spacing between them: each track's size, and where each begins.</summary>
    private static void CheckView(string what, double[] view, IReadOnlyList<double> sizes, IReadOnlyList<double> starts, Action<string, double, double> check)
    {
        Assert.Equal(view.Length == 0 ? 0 : (view.Length + 1) / 2, sizes.Count);
        double at = 0;
        for (int v = 0; v < view.Length; v++)
        {
            if (v % 2 == 0)
            {
                check($"{what} {(v / 2) + 1} size", view[v], sizes[v / 2]);
                check($"{what} {(v / 2) + 1} start", at, starts[v / 2]);
            }

            at += view[v];
        }
    }

    [Fact]
    public void EveryGridInTheOracleIsLaidOutAsR2025bLaysItOut()
    {
        var wrong = new List<string>();
        int count = 0;
        foreach (JsonElement c in Cases())
        {
            wrong.AddRange(Compare(Read(c)));
            count++;
        }

        foreach (string line in wrong.Take(40))
        {
            output.WriteLine(line);
        }

        Assert.True(count > 1500, $"{count} cases");
        Assert.Empty(wrong);
    }

    [Theory]
    [InlineData("zero_mid")]
    [InlineData("zero_x")]
    [InlineData("zero_fixed")]
    [InlineData("zero_last")]
    [InlineData("zero_span_end")]
    [InlineData("zero_first")]
    [InlineData("zero_all")]
    [InlineData("empty_fit_middle")]
    [InlineData("span_prefers_empty")]
    [InlineData("span_two_sorted")]
    [InlineData("span_over_fixed")]
    [InlineData("span_over_weight")]
    [InlineData("span_all_alone")]
    [InlineData("fit_weighted")]
    [InlineData("fit_zero_x")]
    [InlineData("fit_weight_alone")]
    [InlineData("fit_empty_weight")]
    [InlineData("scroll_row2")]
    [InlineData("scroll_row5")]
    [InlineData("scroll_col3")]
    [InlineData("scroll_empty")]
    [InlineData("scroll_both")]
    [InlineData("scroll_minimum_fails")]
    [InlineData("design_empty_fit")]
    public void AWorkedCaseIsLaidOutAsR2025bLaysItOut(string name)
    {
        List<string> wrong = Compare(Read(Case(name)));
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public void AZeroPixelTrackGivesUpTheSpacingAfterIt_AndALastOneTheSpacingBeforeIt()
    {
        // {10, 0, '1x'} in 600 pixels, no padding: the second column's spacing goes, so the third
        // starts at 20 and has 580 (R2025b headless: [21 1 0 400] and [21 1 580 400]).
        UiGridArrangement middle = UiGridMath.Arrange(
            rows: [UiGridTrack.One],
            columns: [new UiGridTrack(UiGridTrackKind.Fixed, 10), new UiGridTrack(UiGridTrackKind.Fixed, 0), UiGridTrack.One],
            padding: [0, 0, 0, 0],
            rowSpacing: 10,
            columnSpacing: 10,
            size: new Size2D(600, 400),
            items: [new UiGridItem(new UiGridCell(1, 2), new Size2D(45, 23)), new UiGridItem(new UiGridCell(1, 3), new Size2D(45, 23))]);
        Assert.Equal((21, 0), (middle.Cells[0].X, middle.Cells[0].Width));
        Assert.Equal((21, 580), (middle.Cells[1].X, middle.Cells[1].Width));

        // {'1x', 10, 0}: the last column takes the spacing before it (R2025b: [1 1 580 400],
        // [591 1 10 400], [601 1 0 400]); a child spanning all three ends at the 10-pixel column.
        UiGridTrack[] last = [UiGridTrack.One, new UiGridTrack(UiGridTrackKind.Fixed, 10), new UiGridTrack(UiGridTrackKind.Fixed, 0)];
        UiGridArrangement end = UiGridMath.Arrange(
            [UiGridTrack.One], last, [0, 0, 0, 0], 10, 10, new Size2D(600, 400),
            [new UiGridItem(new UiGridCell(1, 1), default), new UiGridItem(new UiGridCell(1, 3), default), new UiGridItem(new UiGridCell(1, 1, 1, 3), default)]);
        Assert.Equal([580, 10, 0], end.ColumnWidths);
        Assert.Equal((601, 0), (end.Cells[1].X, end.Cells[1].Width));
        Assert.Equal((1, 600), (end.Cells[2].X, end.Cells[2].Width));

        // '0x' is 0 pixels, as R2025b's GridLayoutController sends it to the view.
        UiGridArrangement weightless = UiGridMath.Arrange(
            [UiGridTrack.One], [new UiGridTrack(UiGridTrackKind.Fixed, 10), new UiGridTrack(UiGridTrackKind.Weight, 0), UiGridTrack.One],
            [0, 0, 0, 0], 10, 10, new Size2D(600, 400), [new UiGridItem(new UiGridCell(1, 3), default)]);
        Assert.Equal((21, 580), (weightless.Cells[0].X, weightless.Cells[0].Width));
    }

    [Fact]
    public void ASpanningChildFillsTheFitTracksNoChildSitsInAlone_LargestChildFirst()
    {
        // R2025b headless: a label (16.77 high) alone in the first of three 'fit' rows keeps its
        // height under a list box (74 high) spanning all three; the list box's height is shared
        // between the other two, each taking a pixel first.
        UiGridTrack[] rows = [UiGridTrack.Fit, UiGridTrack.Fit, UiGridTrack.Fit, UiGridTrack.One];
        UiGridArrangement grid = UiGridMath.Arrange(
            rows, [UiGridTrack.One], [10, 10, 10, 10], 10, 10, new Size2D(600, 400),
            [
                new UiGridItem(new UiGridCell(1, 1), new Size2D(8.6875, 16.765625)),
                new UiGridItem(new UiGridCell(1, 3, 1, 1), new Size2D(44, 74)),
                new UiGridItem(new UiGridCell(4, 1), new Size2D(45, 23)),
            ]);
        Assert.Equal(16.765625, grid.RowHeights[0], 9);
        Assert.Equal(grid.RowHeights[1], grid.RowHeights[2], 9);
        Assert.Equal(74, grid.Cells[1].Height, 9);
        Assert.Equal(296, grid.Cells[2].Height, 9);
    }

    [Fact]
    public void AGridsOwnFitSizeTakesEachWeightedTracksContentTimesItsWeightOverTheSmallest()
    {
        // R2025b headless: a grid of {'1x','2x'} by {'0.5x','1x'} with a 45-by-23 button in (1,1)
        // and (2,2), in a 'fit' cell of another, is 165 by 99.
        UiGridArrangement own = UiGridMath.Arrange(
            rows: [UiGridTrack.One, new UiGridTrack(UiGridTrackKind.Weight, 2)],
            columns: [new UiGridTrack(UiGridTrackKind.Weight, 0.5), UiGridTrack.One],
            padding: [10, 10, 10, 10],
            rowSpacing: 10,
            columnSpacing: 10,
            size: null,
            items: [new UiGridItem(new UiGridCell(1, 1), new Size2D(45, 23)), new UiGridItem(new UiGridCell(2, 2), new Size2D(45, 23))]);
        Assert.Equal((165, 99), (own.Content.Width, own.Content.Height));
    }

    [Fact]
    public void AScrollableGridScrollsOnlyToItsLastOccupiedRowAndColumn()
    {
        UiGridTrack[] forty = [.. Enumerable.Repeat(new UiGridTrack(UiGridTrackKind.Fixed, 40), 5)];

        // R2025b headless: five 40-pixel rows in 200 by 150, a button in row 2 - no bar, and
        // scroll(g, 'bottom') leaves the location at [1 1].
        UiGridScrolled second = UiGridMath.ArrangeScrolling(
            forty, [UiGridTrack.One], [10, 10, 10, 10], 10, 10, new Size2D(200, 150),
            [new UiGridItem(new UiGridCell(2, 1), new Size2D(45, 23))]);
        Assert.False(second.Vertical);
        Assert.Equal(0, new UiScrollMeasure(second.Arrangement.Content, second.Viewport, second.Vertical, second.Horizontal).MaxY);

        // Five 60-pixel columns, a button in column 3: it scrolls 20 pixels across (R2025b: [21 1]).
        UiGridScrolled third = UiGridMath.ArrangeScrolling(
            [UiGridTrack.One], [.. Enumerable.Repeat(new UiGridTrack(UiGridTrackKind.Fixed, 60), 5)], [10, 10, 10, 10], 10, 10,
            new Size2D(200, 150), [new UiGridItem(new UiGridCell(1, 3), new Size2D(45, 23))]);
        Assert.True(third.Horizontal);
        Assert.False(third.Vertical);
        Assert.Equal(20, new UiScrollMeasure(third.Arrangement.Content, third.Viewport, third.Vertical, third.Horizontal).MaxX);
    }
}
