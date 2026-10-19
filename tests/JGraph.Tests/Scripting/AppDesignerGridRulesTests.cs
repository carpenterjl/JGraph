using System.Globalization;
using System.Text.Json;
using JGraph.Core.Primitives;
using JGraph.Scripting.Designer;
using Xunit;
using Xunit.Abstractions;

namespace JGraph.Tests.Scripting;

/// <summary>
/// DS4 of the designer plan: App Designer's grid arithmetic - a box to its cells, a labelled pair's
/// cells, the cell under a click, what the grid's context menu reads under the pointer, the shift of
/// a child's span when a track is added or deleted, a child's span resize and a click-and-draw - each
/// against what R2025b's own modules give, run unaltered under node (GridRulesOracle.json: the specs'
/// worked cases and seeded-random grids), and the two rules JGraph corrects against their corrected
/// values.
/// </summary>
public class AppDesignerGridRulesTests(ITestOutputHelper output)
{
    private static readonly Lazy<JsonDocument> Oracle = new(() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scripting", "Designer", "GridRulesOracle.json"))));

    private static readonly Lazy<GridCells[]> OracleGrids = new(() =>
        Section("grids").Select(GridOf).ToArray());

    /// <summary>The placement spec's grid: 3 by 3 cells of 100 by 50, spacing 10, padding 10, at the origin.</summary>
    private static GridCells Placement() => Layout(0, 0, [10, 10, 10, 10], 10, 10, [100, 100, 100], [50, 50, 50]);

    /// <summary>The commands spec's grid: 3 rows by 2 columns of 100 by 40, spacing 10, padding [10 20 30 40].</summary>
    private static GridCells Commands() => Layout(0, 0, [10, 20, 30, 40], 10, 10, [100, 100], [40, 40, 40]);

    /// <summary>The childresize spec's grid G4: 4 by 4 cells of 100 by 50, spacing 10, padding 10.</summary>
    private static GridCells G4() => Layout(0, 0, [10, 10, 10, 10], 10, 10, [100, 100, 100, 100], [50, 50, 50, 50]);

    /// <summary>A grid's cells laid out as the oracle lays them: padding outside, spacing between tracks.</summary>
    private static GridCells Layout(double ox, double oy, double[] padding, double columnSpacing, double rowSpacing, double[] widths, double[] heights)
    {
        var columns = new List<(double, double)>();
        double x = ox + padding[0];
        foreach (double w in widths)
        {
            columns.Add((x, w));
            x += w + columnSpacing;
        }

        var rows = new List<(double, double)>();
        double y = oy + padding[3];
        foreach (double h in heights)
        {
            rows.Add((y, h));
            y += h + rowSpacing;
        }

        double width = padding[0] + padding[2] + widths.Sum() + (columnSpacing * Math.Max(widths.Length - 1, 0));
        double height = padding[1] + padding[3] + heights.Sum() + (rowSpacing * Math.Max(heights.Length - 1, 0));
        return new GridCells(new Rect2D(ox, oy, width, height), padding, rowSpacing, columnSpacing, rows, columns);
    }

    // --- the oracle file -----------------------------------------------------------------------------

    private static JsonElement.ArrayEnumerator Section(string name) => Oracle.Value.RootElement.GetProperty(name).EnumerateArray();

    private static GridCells GridOf(JsonElement g)
    {
        double[] box = Numbers(g.GetProperty("box"));
        return new GridCells(
            new Rect2D(box[0], box[1], box[2], box[3]),
            Numbers(g.GetProperty("padding")),
            g.GetProperty("rowSpacing").GetDouble(),
            g.GetProperty("columnSpacing").GetDouble(),
            g.GetProperty("rows").EnumerateArray().Select(static t => (t[0].GetDouble(), t[1].GetDouble())).ToArray(),
            g.GetProperty("columns").EnumerateArray().Select(static t => (t[0].GetDouble(), t[1].GetDouble())).ToArray());
    }

    private static double[] Numbers(JsonElement e) => e.EnumerateArray().Select(static v => v.GetDouble()).ToArray();

    private static int[] Ints(JsonElement e) => e.EnumerateArray().Select(static v => v.GetInt32()).ToArray();

    private static Rect2D RectOf(JsonElement e)
    {
        double[] v = Numbers(e);
        return new Rect2D(v[0], v[1], v[2], v[3]);
    }

    private static GridSpan? SpanOf(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        int[] v = Ints(e);
        return new GridSpan(v[0], v[1], v[2], v[3]);
    }

    private static int? IntOf(JsonElement e) => e.ValueKind == JsonValueKind.Null ? null : e.GetInt32();

    private static GridSpan CellOf(JsonElement e)
    {
        int[] v = Ints(e);
        return GridSpan.Cell(v[0], v[1]);
    }

    private static string Text(GridSpan? span) => span is { } s
        ? string.Create(CultureInfo.InvariantCulture, $"R{Track(s.RowStart, s.RowEnd)} C{Track(s.ColumnStart, s.ColumnEnd)}")
        : "none";

    private static string Track(int start, int end) => start == end
        ? start.ToString(CultureInfo.InvariantCulture)
        : string.Create(CultureInfo.InvariantCulture, $"[{start} {end}]");

    private void Report(List<string> failures, int total)
    {
        output.WriteLine($"{total - failures.Count} of {total} cases agree.");
        foreach (string failure in failures.Take(20))
        {
            output.WriteLine(failure);
        }
    }

    // --- spans and cells ------------------------------------------------------------------------------

    /// <summary>A span writes a single track as a number and a span as a row, never [a a].</summary>
    [Fact]
    public void ASpanWritesOneTrackAsANumberAndASpanAsARow()
    {
        var span = new GridSpan(2, 2, 1, 3);
        Assert.Equal("2", DesignValueText.Format(span.RowValue));
        Assert.Equal("[1 3]", DesignValueText.Format(span.ColumnValue));
        Assert.Equal(span, GridSpan.FromValues(span.RowValue, span.ColumnValue));
        Assert.Equal(GridSpan.Cell(2, 2), GridSpan.FromValues(NumericValue.Row(2, 2), NumericValue.Scalar(2)));
        Assert.True(span.Covers(2, 2));
        Assert.False(span.Covers(1, 2));
        Assert.False(span.Covers(2, 4));
    }

    /// <summary>A missing value, a column, a decreasing pair, a fraction, zero or text is no span.</summary>
    [Fact]
    public void AMalformedLayoutValueGivesNoSpan()
    {
        NumericValue one = NumericValue.Scalar(1);
        Assert.Null(GridSpan.FromValues(null, one));
        Assert.Null(GridSpan.FromValues(one, null));
        Assert.Null(GridSpan.FromValues(new NumericValue(2, 1, [1, 2]), one));
        Assert.Null(GridSpan.FromValues(NumericValue.Row(3, 2), one));
        Assert.Null(GridSpan.FromValues(NumericValue.Scalar(1.5), one));
        Assert.Null(GridSpan.FromValues(NumericValue.Scalar(0), one));
        Assert.Null(GridSpan.FromValues(NumericValue.Row(1, 2, 3), one));
        Assert.Null(GridSpan.FromValues(new CharValue("1"), one));
    }

    /// <summary>A cell is its row's and column's track; a span's box runs from its first cell to its last, the gaps inside.</summary>
    [Fact]
    public void ACellIsItsTracksAndASpanRunsFromItsFirstCellToItsLast()
    {
        GridCells cells = Placement();
        Assert.Equal(3, cells.Rows);
        Assert.Equal(3, cells.Columns);
        Assert.Equal(new Rect2D(120, 70, 100, 50), cells.Cell(2, 2));
        Assert.Equal(new Rect2D(10, 70, 210, 110), cells.SpanRect(new GridSpan(2, 3, 1, 2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => cells.Cell(4, 1));
    }

    // --- a box to its cells ---------------------------------------------------------------------------

    /// <summary>
    /// Placement spec 6.3: a box dropped or moved into the 3-by-3 grid takes the cells App Designer's
    /// CellBoundsValidator gives it - a cell counted when the box covers its centre, a narrow box in a
    /// gap or straddling two centres keeping both, a box beyond the cells none.
    /// </summary>
    [Theory]
    [InlineData(20, 15, 80, 22, "R1 C1")]
    [InlineData(61, 15, 80, 22, "R1 C[1 2]")]
    [InlineData(60, 15, 80, 22, "R1 C1")]
    [InlineData(10, 10, 100, 50, "R1 C1")]
    [InlineData(10, 10, 210, 50, "R1 C[1 2]")]
    [InlineData(70, 15, 100, 22, "R1 C2")]
    [InlineData(100, 15, 50, 22, "R1 C[1 2]")]
    [InlineData(112, 15, 6, 22, "R1 C[1 2]")]
    [InlineData(116, 15, 84, 22, "R1 C2")]
    [InlineData(331, 15, 5, 22, "none")]
    [InlineData(330, 15, 5, 22, "R1 C3")]
    [InlineData(-50, 15, 90, 22, "R1 C1")]
    [InlineData(-50, -50, 500, 500, "R[1 3] C[1 3]")]
    [InlineData(20, 40, 80, 60, "R2 C1")]
    [InlineData(130, 15, 96, 22, "R1 C2")]
    [InlineData(20, 15, 95, 22, "R1 C1")]
    [InlineData(20, 65, 80, 40, "R2 C1")]
    public void ABoxTakesTheCellsAppDesignerGivesIt(double x, double y, double w, double h, string expected)
    {
        Assert.Equal(expected, Text(DesignGridRules.SpanOfBounds(new Rect2D(x, y, w, h), Placement())));
    }

    /// <summary>
    /// The first divergence (placement spec 17.1): an edge exactly on a gap's midline belongs to one
    /// side - a start edge to the later cell, an end edge to the earlier - where App Designer leaves it
    /// in neither and falls back to the first cell, giving Column [1 2] for a box in column 2, [2 3]
    /// for one in column 3, the backwards Column [2 1] for one in column 2, and Row [2 1] likewise.
    /// </summary>
    [Theory]
    [InlineData(115, 15, 50, 22, "R1 C2")]
    [InlineData(225, 15, 75, 22, "R1 C3")]
    [InlineData(130, 15, 95, 22, "R1 C2")]
    [InlineData(20, 80, 80, 45, "R2 C1")]
    [InlineData(20, 125, 80, 45, "R3 C1")]
    public void ABoxEdgeOnAGapMidlineBelongsToOneSide(double x, double y, double w, double h, string expected)
    {
        Assert.Equal(expected, Text(DesignGridRules.SpanOfBounds(new Rect2D(x, y, w, h), Placement())));
    }

    /// <summary>A box of no width standing on a midline lies in the gap, and takes both cells, as a narrow box in a gap does.</summary>
    [Fact]
    public void ABoxOfNoWidthOnAMidlineTakesBothCells()
    {
        Assert.Equal("R1 C[1 2]", Text(DesignGridRules.SpanOfBounds(new Rect2D(115, 15, 0, 22), Placement())));
        Assert.Equal("R1 C[1 2]", Text(DesignGridRules.SpanOfBounds(new Rect2D(114.5, 15, 1, 22), Placement())));
    }

    /// <summary>
    /// Every box of the oracle - the spec's and the seeded-random grids', with zero-width tracks, odd
    /// spacings and edges on every track edge, centre and midline - takes App Designer's cells, or,
    /// with an edge on a midline, the cells App Designer gives that edge a hair to the side the
    /// corrected rule puts it.
    /// </summary>
    [Fact]
    public void EveryBoxInTheOracleTakesAppDesignersCells()
    {
        var failures = new List<string>();
        int total = 0;
        int corrected = 0;
        foreach (JsonElement c in Section("bounds"))
        {
            total++;
            GridCells cells = OracleGrids.Value[c.GetProperty("g").GetInt32()];
            Rect2D box = RectOf(c.GetProperty("b"));
            GridSpan? expected = SpanOf(c.TryGetProperty("fixed", out JsonElement f) ? f : c.GetProperty("ad"));
            if (c.TryGetProperty("fixed", out _) && Text(expected) != Text(SpanOf(c.GetProperty("ad"))))
            {
                corrected++;
            }

            GridSpan? actual = DesignGridRules.SpanOfBounds(box, cells);
            if (actual != expected)
            {
                failures.Add($"grid {c.GetProperty("g")} box {box}: {Text(actual)}, expected {Text(expected)}");
            }
        }

        output.WriteLine($"{corrected} cases take the corrected midline rule.");
        Report(failures, total);
        Assert.Empty(failures);
        Assert.True(total > 500);
    }

    /// <summary>A moved selection maps each member by its own box, and nothing when one member misses the cells.</summary>
    [Fact]
    public void ASelectionMapsEachMemberAndNoneWhenOneMisses()
    {
        GridCells cells = Placement();
        IReadOnlyList<GridSpan>? spans = DesignGridRules.SpansOfBounds([new Rect2D(20, 15, 80, 22), new Rect2D(240, 140, 80, 22)], cells);
        Assert.NotNull(spans);
        Assert.Equal(["R1 C1", "R3 C3"], spans.Select(static s => Text(s)));
        Assert.Null(DesignGridRules.SpansOfBounds([new Rect2D(20, 15, 80, 22), new Rect2D(331, 15, 5, 22)], cells));
    }

    // --- labelled components --------------------------------------------------------------------------

    /// <summary>
    /// Placement spec 6.4: a labelled pair's label and field take side-by-side cells as the label
    /// stands to the field; an overlapping pair shares its cells (App Designer's tie-break never runs);
    /// a pair that would leave the grid takes none.
    /// </summary>
    [Theory]
    [InlineData(20, 20, 60, 22, 130, 20, 80, 22, "R1 C1", "R1 C2")]
    [InlineData(130, 80, 60, 22, 130, 20, 80, 22, "R2 C2", "R1 C2")]
    [InlineData(130, 20, 60, 22, 130, 80, 80, 22, "R1 C2", "R2 C2")]
    [InlineData(130, 20, 60, 22, 150, 25, 80, 22, "R1 C2", "R1 C2")]
    [InlineData(240, 20, 60, 22, 130, 20, 80, 22, "R1 C3", "R1 C2")]
    [InlineData(240, 20, 60, 22, 335, 20, 80, 22, "none", "none")]
    [InlineData(20, 15, 60, 22, 20, 0, 60, 8, "none", "none")]
    public void ALabelledPairTakesSideBySideCells(
        double lx, double ly, double lw, double lh, double fx, double fy, double fw, double fh, string label, string field)
    {
        (GridSpan Label, GridSpan Field)? spans = DesignGridRules.LabeledSpans(new Rect2D(lx, ly, lw, lh), new Rect2D(fx, fy, fw, fh), Placement());
        Assert.Equal(label, Text(spans?.Label));
        Assert.Equal(field, Text(spans?.Field));
    }

    /// <summary>Every labelled pair of the oracle takes the cells App Designer's CellBoundsValidator gives it.</summary>
    [Fact]
    public void EveryLabelledPairInTheOracleTakesAppDesignersCells()
    {
        var failures = new List<string>();
        int total = 0;
        foreach (JsonElement c in Section("labeled"))
        {
            total++;
            GridCells cells = OracleGrids.Value[c.GetProperty("g").GetInt32()];
            Rect2D label = RectOf(c.GetProperty("label"));
            Rect2D field = RectOf(c.GetProperty("field"));
            JsonElement ad = c.GetProperty("ad");
            (GridSpan Label, GridSpan Field)? actual = DesignGridRules.LabeledSpans(label, field, cells);
            string want = ad.ValueKind == JsonValueKind.Null ? "none" : $"{Text(SpanOf(ad.GetProperty("label")))} / {Text(SpanOf(ad.GetProperty("field")))}";
            string got = actual is { } a ? $"{Text(a.Label)} / {Text(a.Field)}" : "none";
            if (want != got)
            {
                failures.Add($"grid {c.GetProperty("g")} label {label} field {field}: {got}, expected {want}");
            }
        }

        Report(failures, total);
        Assert.Empty(failures);
        Assert.True(total > 150);
    }

    /// <summary>
    /// A labelled library entry's label takes the field's cells moved by the entry's offset - one
    /// track before the field's first to the west, after its last to the south, the field's span kept
    /// on the other axis - as LabeledComponentEntryDefinition gives them; none when that leaves the grid.
    /// </summary>
    [Fact]
    public void ALabelTakesItsFieldsCellsMovedByTheEntrysOffset()
    {
        Assert.Equal("R[2 3] C1", Text(DesignGridRules.LabelFromField(new GridSpan(2, 3, 2, 2), -1, 0, 4, 4)));
        Assert.Equal("R4 C2", Text(DesignGridRules.LabelFromField(new GridSpan(2, 3, 2, 2), 0, 1, 4, 4)));
        Assert.Null(DesignGridRules.LabelFromField(GridSpan.Cell(1, 1), -1, 0, 4, 4));

        var failures = new List<string>();
        int total = 0;
        foreach (JsonElement c in Section("labelFromField"))
        {
            total++;
            int[] f = Ints(c.GetProperty("field"));
            int[] d = Ints(c.GetProperty("d"));
            int[] row = Ints(c.GetProperty("row"));
            int[] column = Ints(c.GetProperty("column"));
            GridSpan? expected = row[0] >= 1 && row[1] <= 4 && column[0] >= 1 && column[1] <= 4
                ? new GridSpan(row[0], row[1], column[0], column[1])
                : null;
            GridSpan? actual = DesignGridRules.LabelFromField(new GridSpan(f[0], f[1], f[2], f[3]), d[0], d[1], 4, 4);
            if (actual != expected)
            {
                failures.Add($"field {Text(new GridSpan(f[0], f[1], f[2], f[3]))} offset [{d[0]} {d[1]}]: {Text(actual)}, expected {Text(expected)}");
            }
        }

        Report(failures, total);
        Assert.Empty(failures);
    }

    // --- clicks ---------------------------------------------------------------------------------------

    /// <summary>Placement spec 8: a click lands in the cell holding it, edges included; in a gap, the padding or outside, in none.</summary>
    [Theory]
    [InlineData(150, 90, "R2 C2")]
    [InlineData(115, 90, "none")]
    [InlineData(110, 90, "R2 C1")]
    [InlineData(5, 90, "none")]
    [InlineData(500, 90, "none")]
    public void AClickLandsInTheCellHoldingIt(double x, double y, string expected)
    {
        Assert.Equal(expected, Text(DesignGridRules.CellUnderPoint(new Point2D(x, y), Placement())));
    }

    /// <summary>
    /// Placement spec 8: a click-and-draw of a labelled entry with a west label puts the label in the
    /// clicked cell and the field after it, both moved back from the last column; a south label goes
    /// below the clicked field, and none fits under the last row.
    /// </summary>
    [Theory]
    [InlineData(2, 2, -1, 0, true, "R2 C2", "R2 C3")]
    [InlineData(2, 3, -1, 0, true, "R2 C2", "R2 C3")]
    [InlineData(2, 1, -1, 0, true, "R2 C1", "R2 C2")]
    [InlineData(2, 1, -1, 0, false, "R2 C1", "R2 C2")]
    [InlineData(2, 2, 0, 1, true, "R3 C2", "R2 C2")]
    [InlineData(3, 2, 0, 1, true, "none", "none")]
    public void ALabelledClickPutsTheLabelBesideItsField(int row, int column, int dColumn, int dRow, bool draw, string label, string field)
    {
        (GridSpan Label, GridSpan Field)? cells = DesignGridRules.LabeledClick(GridSpan.Cell(row, column), dColumn, dRow, draw, 3, 3);
        Assert.Equal(label, Text(cells?.Label));
        Assert.Equal(field, Text(cells?.Field));
    }

    /// <summary>
    /// Every labelled click of the oracle, every cell of five grid sizes, both offsets, drawn or not,
    /// as CellMousePositionValidator gives it - but for the third divergence: where App Designer puts
    /// the label in column 0 (a west label drawn in a one-column grid), the click is refused.
    /// </summary>
    [Fact]
    public void EveryLabelledClickInTheOracleIsAppDesigners()
    {
        var failures = new List<string>();
        int total = 0;
        int refused = 0;
        foreach (JsonElement c in Section("clicks"))
        {
            total++;
            int[] d = Ints(c.GetProperty("d"));
            GridSpan cell = CellOf(c.GetProperty("cell"));
            JsonElement ad = c.GetProperty("ad");
            bool beforeFirst = ad.ValueKind != JsonValueKind.Null
                && Ints(ad.GetProperty("label")).Concat(Ints(ad.GetProperty("field"))).Any(static v => v < 1);
            refused += beforeFirst ? 1 : 0;
            string want = ad.ValueKind == JsonValueKind.Null || beforeFirst ? "none" : $"{Text(CellOf(ad.GetProperty("label")))} / {Text(CellOf(ad.GetProperty("field")))}";
            (GridSpan Label, GridSpan Field)? actual = DesignGridRules.LabeledClick(
                cell, d[0], d[1], c.GetProperty("draw").GetBoolean(), c.GetProperty("rows").GetInt32(), c.GetProperty("cols").GetInt32());
            string got = actual is { } a ? $"{Text(a.Label)} / {Text(a.Field)}" : "none";
            if (want != got)
            {
                failures.Add($"{c.GetProperty("rows")}x{c.GetProperty("cols")} cell {Text(cell)} offset [{d[0]} {d[1]}] draw {c.GetProperty("draw")}: {got}, expected {want}");
            }
        }

        Report(failures, total);
        Assert.Empty(failures);
        Assert.Equal(108, total);
        Assert.Equal(1, refused);
    }

    /// <summary>
    /// The third divergence: a click-and-draw of an entry with a west label in a one-column grid
    /// (or a north label in a one-row grid) has no room for the field beside the label; App Designer
    /// moves both back one, puts the label in column 0 and draws it there, a layout MATLAB refuses.
    /// Here the click is refused, as App Designer refuses the same entry at the same cell outside a draw.
    /// </summary>
    [Fact]
    public void ALabelledDrawWithNoRoomBeforeTheFieldIsRefused()
    {
        Assert.Null(DesignGridRules.LabeledClick(GridSpan.Cell(2, 1), -1, 0, componentDraw: true, rows: 3, columns: 1));
        Assert.Null(DesignGridRules.LabeledClick(GridSpan.Cell(2, 1), -1, 0, componentDraw: false, rows: 3, columns: 1));
        Assert.Null(DesignGridRules.LabeledClick(GridSpan.Cell(1, 2), 0, -1, componentDraw: true, rows: 1, columns: 3));
        Assert.Equal((GridSpan.Cell(2, 1), GridSpan.Cell(2, 2)), DesignGridRules.LabeledClick(GridSpan.Cell(2, 2), -1, 0, componentDraw: true, rows: 3, columns: 2));
    }

    // --- the context menu -------------------------------------------------------------------------------

    /// <summary>
    /// Commands spec 3.1, on its 3-by-2 grid: a row or column under the pointer adds before and after
    /// it and is the one deleted; a gap adds between its two tracks; the top and left padding add
    /// before the first; rows and columns are read apart.
    /// </summary>
    [Theory]
    [InlineData(50, 60, 1, 2, 1, 2, 1, 1)]
    [InlineData(50, 85, 2, 2, 1, 2, null, 1)]
    [InlineData(115, 60, 1, 2, 2, 2, 1, null)]
    [InlineData(50, 20, 1, 1, 1, 2, null, 1)]
    [InlineData(5, 60, 1, 2, 1, 1, 1, null)]
    [InlineData(50, 175, 3, 4, 1, 2, 3, 1)]
    [InlineData(50, 205, null, null, 1, 2, null, 1)]
    public void TheMenuReadsTheTrackGapOrPaddingUnderThePointer(
        double x, double y, int? above, int? below, int? before, int? after, int? deleteRow, int? deleteColumn)
    {
        GridPointInfo info = DesignGridRules.PointInfo(new Point2D(x, y), Commands());
        Assert.Equal((above, below), DesignGridRules.AddingRows(info));
        Assert.Equal((before, after), DesignGridRules.AddingColumns(info));
        Assert.Equal((deleteRow, deleteColumn), DesignGridRules.Deleting(info));
    }

    /// <summary>
    /// The second divergence (commands spec 3.1): the bottom and right padding bands start at the last
    /// track's bottom and right edges, so a right-click there adds a row or a column after the last,
    /// where App Designer, testing from the last track's top and left, finds nothing and disables the
    /// item - and reports the padding as well inside the last row's top band.
    /// </summary>
    [Fact]
    public void TheBottomAndRightPaddingAddARowAndAColumnAfterTheLast()
    {
        GridCells commands = Commands();
        GridPointInfo bottom = DesignGridRules.PointInfo(new Point2D(50, 195), commands);
        Assert.Equal(4, bottom.RowPadding);
        Assert.Equal((4, 4), DesignGridRules.AddingRows(bottom));
        GridPointInfo right = DesignGridRules.PointInfo(new Point2D(235, 60), commands);
        Assert.Equal(3, right.ColumnPadding);
        Assert.Equal((3, 3), DesignGridRules.AddingColumns(right));
        Assert.Equal((3, 3), DesignGridRules.AddingColumns(DesignGridRules.PointInfo(new Point2D(250, 60), commands)));
        Assert.Equal((null, null), DesignGridRules.AddingColumns(DesignGridRules.PointInfo(new Point2D(251, 60), commands)));

        // The controllers spec's 2-by-3 grid of 50 px cells: (35,75) is in the last row, and only there.
        GridCells small = Layout(0, 0, [10, 10, 10, 10], 10, 10, [50, 50, 50], [50, 50]);
        Assert.Equal(new GridPointInfo(2, 1, null, null, null, null), DesignGridRules.PointInfo(new Point2D(35, 75), small));
        Assert.Equal(new GridPointInfo(null, 1, null, null, 3, null), DesignGridRules.PointInfo(new Point2D(35, 125), small));
        Assert.Equal(new GridPointInfo(1, null, null, null, null, 4), DesignGridRules.PointInfo(new Point2D(185, 35), small));
    }

    /// <summary>
    /// Every point of the oracle reads as App Designer's getColumnRowUnderPoint, parseAddingInfo and
    /// parseDeletingInfo read it; in the bottom or right band where the two padding tests differ, that
    /// axis's padding is the corrected one, the band from the last track's far edge.
    /// </summary>
    [Fact]
    public void EveryPointInTheOracleReadsAsAppDesignersMenuReadsIt()
    {
        var failures = new List<string>();
        int total = 0;
        int quirks = 0;
        foreach (JsonElement c in Section("points"))
        {
            total++;
            GridCells cells = OracleGrids.Value[c.GetProperty("g").GetInt32()];
            double[] p = Numbers(c.GetProperty("p"));
            var point = new Point2D(p[0], p[1]);
            JsonElement[] info = c.GetProperty("info").EnumerateArray().ToArray();
            int?[] adding = c.GetProperty("adding").EnumerateArray().Select(IntOf).ToArray();
            int?[] deleting = c.GetProperty("deleting").EnumerateArray().Select(IntOf).ToArray();
            bool quirkRow = c.TryGetProperty("quirkRow", out _);
            bool quirkColumn = c.TryGetProperty("quirkColumn", out _);
            quirks += (quirkRow || quirkColumn) ? 1 : 0;

            GridSpan? cell = c.GetProperty("cell").ValueKind == JsonValueKind.Null ? null : CellOf(c.GetProperty("cell"));
            GridPointInfo actual = DesignGridRules.PointInfo(point, cells);
            int? rowGap = IntOf(info[2]);
            int? columnGap = IntOf(info[3]);
            var expected = new GridPointInfo(
                IntOf(info[0]),
                IntOf(info[1]),
                rowGap is { } r ? (r, r + 1) : null,
                columnGap is { } k ? (k, k + 1) : null,
                quirkRow ? BottomBand(cells, p[1]) : IntOf(info[4]),
                quirkColumn ? RightBand(cells, p[0]) : IntOf(info[5]));
            var problems = new List<string>();
            if (DesignGridRules.CellUnderPoint(point, cells) != cell)
            {
                problems.Add($"cell {Text(DesignGridRules.CellUnderPoint(point, cells))}, expected {Text(cell)}");
            }

            if (actual != expected)
            {
                problems.Add($"info {actual}, expected {expected}");
            }

            if (!quirkRow && DesignGridRules.AddingRows(actual) != (adding[0], adding[1]))
            {
                problems.Add($"adding rows {DesignGridRules.AddingRows(actual)}");
            }

            if (!quirkColumn && DesignGridRules.AddingColumns(actual) != (adding[2], adding[3]))
            {
                problems.Add($"adding columns {DesignGridRules.AddingColumns(actual)}");
            }

            if (DesignGridRules.Deleting(actual) != (deleting[0], deleting[1]))
            {
                problems.Add($"deleting {DesignGridRules.Deleting(actual)}");
            }

            if (problems.Count > 0)
            {
                failures.Add($"grid {c.GetProperty("g")} point ({p[0]}, {p[1]}): {string.Join("; ", problems)}");
            }
        }

        output.WriteLine($"{quirks} points are in a band where App Designer's padding test differs.");
        Report(failures, total);
        Assert.Empty(failures);

        // The corrected bands: from the last track's far edge to the padding's outer edge.
        static int? BottomBand(GridCells cells, double y)
        {
            double bottom = cells.Cell(cells.Rows, 1).Bottom;
            return y > bottom && y <= bottom + cells.Padding[1] ? cells.Rows + 1 : null;
        }

        static int? RightBand(GridCells cells, double x)
        {
            double right = cells.Cell(1, cells.Columns).Right;
            return x > right && x <= right + cells.Padding[2] ? cells.Columns + 1 : null;
        }
    }

    // --- adding and deleting tracks ---------------------------------------------------------------------

    /// <summary>
    /// Commands spec 4.1 and 4.2, on its 4-by-3 grid (a R1 C1, b R[1 2] C2, c R2 C[1 3], d R[2 4] C3,
    /// e R3 C[1 2], f R4 C1): an inserted track moves what is at and after it and grows a span across
    /// it; a deleted track takes the children in it alone, shrinks the spans over it and moves what is after.
    /// </summary>
    [Theory]
    [InlineData("add", GridAxis.Row, 2, "a:R1 C1|b:R[1 3] C2|c:R3 C[1 3]|d:R[3 5] C3|e:R4 C[1 2]|f:R5 C1")]
    [InlineData("add", GridAxis.Row, 5, "a:R1 C1|b:R[1 2] C2|c:R2 C[1 3]|d:R[2 4] C3|e:R3 C[1 2]|f:R4 C1")]
    [InlineData("add", GridAxis.Column, 3, "a:R1 C1|b:R[1 2] C2|c:R2 C[1 4]|d:R[2 4] C4|e:R3 C[1 2]|f:R4 C1")]
    [InlineData("delete", GridAxis.Row, 2, "a:R1 C1|b:R1 C2|d:R[2 3] C3|e:R2 C[1 2]|f:R3 C1")]
    [InlineData("delete", GridAxis.Row, 4, "a:R1 C1|b:R[1 2] C2|c:R2 C[1 3]|d:R[2 3] C3|e:R3 C[1 2]")]
    [InlineData("delete", GridAxis.Column, 1, "b:R[1 2] C1|c:R2 C[1 2]|d:R[2 4] C2|e:R3 C1")]
    [InlineData("delete", GridAxis.Column, 3, "a:R1 C1|b:R[1 2] C2|c:R2 C[1 2]|e:R3 C[1 2]|f:R4 C1")]
    public void AnAddedOrDeletedTrackShiftsTheChildrensSpans(string action, GridAxis axis, int index, string expected)
    {
        (string Name, GridSpan Span)[] children =
        [
            ("a", new GridSpan(1, 1, 1, 1)), ("b", new GridSpan(1, 2, 2, 2)), ("c", new GridSpan(2, 2, 1, 3)),
            ("d", new GridSpan(2, 4, 3, 3)), ("e", new GridSpan(3, 3, 1, 2)), ("f", new GridSpan(4, 4, 1, 1)),
        ];
        IEnumerable<string> after = children
            .Select(child => (child.Name, Span: action == "add"
                ? DesignGridRules.WithTrackInserted(child.Span, axis, index)
                : DesignGridRules.WithTrackRemoved(child.Span, axis, index)))
            .Where(static child => child.Span is not null)
            .Select(static child => $"{child.Name}:{Text(child.Span)}");
        Assert.Equal(expected, string.Join('|', after));
    }

    /// <summary>Every span of one to five tracks against every insertion and deletion point from 1 to 6, rows and columns, as GridUtil shifts them.</summary>
    [Fact]
    public void EveryShiftInTheOracleIsGridUtils()
    {
        var failures = new List<string>();
        int total = 0;
        foreach (JsonElement c in Section("shifts"))
        {
            total++;
            GridAxis axis = c.GetProperty("axis").GetString() == "Row" ? GridAxis.Row : GridAxis.Column;
            int[] s = Ints(c.GetProperty("span"));
            int index = c.GetProperty("index").GetInt32();
            GridSpan span = axis == GridAxis.Row ? new GridSpan(s[0], s[1], 1, 1) : new GridSpan(1, 1, s[0], s[1]);
            GridSpan inserted = Along(axis, Ints(c.GetProperty("inserted")));
            GridSpan? removed = c.GetProperty("removed").ValueKind == JsonValueKind.Null ? null : Along(axis, Ints(c.GetProperty("removed")));
            if (DesignGridRules.WithTrackInserted(span, axis, index) != inserted || DesignGridRules.WithTrackRemoved(span, axis, index) != removed)
            {
                failures.Add($"{axis} [{s[0]} {s[1]}] at {index}");
            }
        }

        Report(failures, total);
        Assert.Empty(failures);
        Assert.Equal(180, total);

        static GridSpan Along(GridAxis axis, int[] v) => axis == GridAxis.Row ? new GridSpan(v[0], v[1], 1, 1) : new GridSpan(1, 1, v[0], v[1]);
    }

    // --- resizing a child's span -------------------------------------------------------------------------

    /// <summary>
    /// Childresize spec 13, its grid G4: dragging the east handle of a child in (2,2) takes column 3
    /// from +55 (the midpoint of column 2's and 3's right edges; +54.5 is short of it) and column 4
    /// from +165, the preview's edge stopping at column 4's right edge.
    /// </summary>
    [Fact]
    public void AChildsEastEdgeSnapsToTheNearestRightEdge()
    {
        GridCells cells = G4();
        var resize = new GridSpanResize(cells, ResizeEdges.East, [new GridResizeEntry(GridSpan.Cell(2, 2), cells.Cell(2, 2), CanResize: true, IsLabel: false)]);
        Assert.Equal("R2 C2", Text(resize.Spans(54.5, 0)[0]));
        Assert.Equal("R2 C[2 3]", Text(resize.Spans(55, 0)[0]));
        Assert.Equal("R2 C[2 4]", Text(resize.Spans(165, 0)[0]));
        Assert.Equal("R2 C2", Text(resize.Spans(-200, 0)[0]));
        Assert.Equal(new Rect2D(120, 70, 320, 50), resize.LiveBoxes(1000, 0)[0]);
        Assert.Equal(new Rect2D(120, 70, 100, 50), resize.LiveBoxes(-1000, 0)[0]);
    }

    /// <summary>
    /// A labelled pair whose label stands east of its field: the east handle grows the field and the
    /// pair and carries the label along (+60: label column 4, field [2 3]), as App Designer does.
    /// </summary>
    [Fact]
    public void ALabelOnTheDraggedSideMovesWithTheEdge()
    {
        GridCells cells = G4();
        IReadOnlyList<GridResizeEntry> entries = GridSpanResize.LabeledEntries(
            GridSpan.Cell(2, 3), cells.Cell(2, 3), GridSpan.Cell(2, 2), cells.Cell(2, 2), cells.SpanRect(new GridSpan(2, 2, 2, 3)));
        Assert.Equal(GridLabelSide.East, entries[1].Horizontal);
        var resize = new GridSpanResize(cells, ResizeEdges.East, entries);
        Assert.Equal(["R2 C[2 4]", "R2 C4", "R2 C[2 3]"], resize.Spans(60, 0).Select(static s => Text(s)));
        Assert.Equal(["R2 C[2 3]", "R2 C3", "R2 C2"], resize.Spans(54, 0).Select(static s => Text(s)));
    }

    /// <summary>The names of the oracle's resize scenarios.</summary>
    public static TheoryData<string> ResizeScenarios()
    {
        var data = new TheoryData<string>();
        foreach (JsonElement r in Section("resizes"))
        {
            data.Add(r.GetProperty("name").GetString()!);
        }

        return data;
    }

    /// <summary>
    /// Each resize of the oracle - App Designer's own gridchildresize handlers driven over single
    /// cells, spans, corners, uneven and zero-width columns, zoomed pixels, a component inset in its
    /// cells and labelled pairs on every side - gives App Designer's span for every entry and App
    /// Designer's preview box at every step recorded (each sweep's ends, both sides of every change,
    /// every kink and every tenth pixel).
    /// </summary>
    [Theory]
    [MemberData(nameof(ResizeScenarios))]
    public void EveryResizeInTheOracleIsAppDesigners(string name)
    {
        JsonElement r = Section("resizes").Single(e => e.GetProperty("name").GetString() == name);
        GridCells cells = OracleGrids.Value[r.GetProperty("g").GetInt32()];
        GridResizeEntry[] entries = r.GetProperty("entries").EnumerateArray().Select(static e => new GridResizeEntry(
            SpanOf(e.GetProperty("span"))!.Value,
            RectOf(e.GetProperty("box")),
            e.GetProperty("canResize").GetBoolean(),
            e.GetProperty("isLabel").GetBoolean(),
            Side(e.GetProperty("h")),
            Side(e.GetProperty("v")))).ToArray();
        ResizeEdges edges = r.GetProperty("dir").GetString() switch
        {
            "n" => ResizeEdges.North,
            "s" => ResizeEdges.South,
            "e" => ResizeEdges.East,
            "w" => ResizeEdges.West,
            "ne" => ResizeEdges.NorthEast,
            "nw" => ResizeEdges.NorthWest,
            "se" => ResizeEdges.SouthEast,
            "sw" => ResizeEdges.SouthWest,
            var other => throw new InvalidOperationException(other),
        };

        if (entries.Length == 3)
        {
            // The pair's entries as BeginGridChildResize gathered them, from the members alone.
            IReadOnlyList<GridResizeEntry> pair = GridSpanResize.LabeledEntries(entries[1].Span, entries[1].Box, entries[2].Span, entries[2].Box, entries[0].Box);
            Assert.Equal(entries, pair);
        }

        var resize = new GridSpanResize(cells, edges, entries);
        var failures = new List<string>();
        int total = 0;
        foreach (JsonElement move in r.GetProperty("moves").EnumerateArray())
        {
            total++;
            double[] d = Numbers(move.GetProperty("d"));
            string want = string.Join(" | ", move.GetProperty("spans").EnumerateArray().Select(static s => Text(SpanOf(s))));
            string got = string.Join(" | ", resize.Spans(d[0], d[1]).Select(static s => Text(s)));
            Rect2D[] live = move.GetProperty("live").EnumerateArray().Select(RectOf).ToArray();
            IReadOnlyList<Rect2D> preview = resize.LiveBoxes(d[0], d[1]);
            if (want != got || !live.SequenceEqual(preview))
            {
                failures.Add($"drag ({d[0]}, {d[1]}): {got} [{string.Join(", ", preview)}], expected {want} [{string.Join(", ", live)}]");
            }
        }

        Report(failures, total);
        Assert.Empty(failures);

        static GridLabelSide Side(JsonElement e) => e.ValueKind == JsonValueKind.Null ? GridLabelSide.None : e.GetString() switch
        {
            "WEST" => GridLabelSide.West,
            "EAST" => GridLabelSide.East,
            "NORTH" => GridLabelSide.North,
            "SOUTH" => GridLabelSide.South,
            var other => throw new InvalidOperationException(other),
        };
    }

    // --- click-and-draw ---------------------------------------------------------------------------------

    /// <summary>
    /// A click-and-draw from (2,2) of G4 takes column 3 once the pointer passes the midpoint of
    /// column 2's and 3's right edges (275; 279 does, 225 does not), and the whole quadrant of the
    /// grid toward a pointer far outside it, as App Designer's componentdraw handlers give it.
    /// </summary>
    [Fact]
    public void ADrawnSpanGrowsFromThePressCellTowardThePointer()
    {
        GridCells cells = G4();
        var press = new Point2D(170, 95);
        GridSpan start = DesignGridRules.CellUnderPoint(press, cells)!.Value;
        Assert.Equal("R2 C2", Text(DesignGridRules.DrawnSpan(cells, start, press, new Point2D(225, 95))));
        Assert.Equal("R2 C[2 3]", Text(DesignGridRules.DrawnSpan(cells, start, press, new Point2D(279, 95))));
        Assert.Equal("R2 C[1 2]", Text(DesignGridRules.DrawnSpan(cells, start, press, new Point2D(60, 95))));
        Assert.Equal("R[2 4] C[2 4]", Text(DesignGridRules.DrawnSpan(cells, start, press, new Point2D(500, 400))));
        Assert.Equal("R[1 2] C[1 2]", Text(DesignGridRules.DrawnSpan(cells, start, press, new Point2D(-100, -100))));
    }

    /// <summary>
    /// Every draw of the oracle - plain and labelled (west and south labels), from the press cell App
    /// Designer's CellMousePositionValidator chose, over G4, uneven and zero-width columns - gives the
    /// span (or the label's and field's cells) App Designer's componentdraw handlers give.
    /// </summary>
    [Fact]
    public void EveryDrawInTheOracleIsAppDesigners()
    {
        var failures = new List<string>();
        int total = 0;
        foreach (JsonElement r in Section("draws"))
        {
            GridCells cells = OracleGrids.Value[r.GetProperty("g").GetInt32()];
            double[] p = Numbers(r.GetProperty("press"));
            var press = new Point2D(p[0], p[1]);
            GridSpan pressCell = DesignGridRules.CellUnderPoint(press, cells)!.Value;
            bool labelled = r.TryGetProperty("d", out JsonElement dElement);
            int[] d = labelled ? Ints(dElement) : [0, 0];
            GridSpan label = default;
            GridSpan field = default;
            if (labelled)
            {
                label = CellOf(r.GetProperty("label"));
                field = CellOf(r.GetProperty("field"));
                Assert.Equal((label, field), DesignGridRules.LabeledClick(pressCell, d[0], d[1], componentDraw: true, cells.Rows, cells.Columns));
            }
            else
            {
                Assert.Equal(CellOf(r.GetProperty("start")), pressCell);
            }

            foreach (JsonElement move in r.GetProperty("moves").EnumerateArray())
            {
                total++;
                double[] q = Numbers(move.GetProperty("p"));
                var pointer = new Point2D(q[0], q[1]);
                string want;
                string got;
                if (labelled)
                {
                    want = $"{Text(SpanOf(move.GetProperty("label")))} / {Text(SpanOf(move.GetProperty("field")))}";
                    (GridSpan Label, GridSpan Field) drawn = DesignGridRules.DrawnLabeledSpans(cells, label, field, d[0], d[1], press, pointer);
                    got = $"{Text(drawn.Label)} / {Text(drawn.Field)}";
                }
                else
                {
                    want = Text(SpanOf(move.GetProperty("span")));
                    got = Text(DesignGridRules.DrawnSpan(cells, pressCell, press, pointer));
                }

                if (want != got)
                {
                    failures.Add($"{r.GetProperty("name").GetString()} to ({q[0]}, {q[1]}): {got}, expected {want}");
                }
            }
        }

        Report(failures, total);
        Assert.Empty(failures);
        Assert.True(total > 300);
    }
}
