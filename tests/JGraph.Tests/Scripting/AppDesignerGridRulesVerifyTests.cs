using System.Globalization;
using System.Text.Json;
using JGraph.Core.Primitives;
using JGraph.Scripting.Designer;
using Xunit;
using Xunit.Abstractions;

namespace JGraph.Tests.Scripting;

/// <summary>
/// An independent check of DS4's grid arithmetic (<see cref="DesignGridRules"/>,
/// <see cref="GridSpanResize"/>) against a second corpus, GridRulesVerifyOracle.json: R2025b's own
/// App Designer modules run unaltered under node over grids and cases the first corpus did not hold -
/// spacing 0, runs of zero-size tracks, fractional sizes, single and six-track grids, boxes of no
/// width or height, every exact boundary, all nine label offsets, components inset in, spilling out of
/// or strayed far from their cells, labels spanning two tracks, App Designer's default handle, and
/// labelled draws on all four sides. The corrected rules are taken as the first corpus takes them: a
/// midline edge's expected cells are App Designer's for that edge moved a hair to the side the
/// half-open rule gives it (only where App Designer searches for that edge: an edge its first- or
/// last-centre test or its touching test settles keeps App Designer's cells, which with spacing 0 and a
/// zero-size first or last track can stand on a midline), a point where the bottom or right padding
/// tests disagree reads the corrected band, and a labelled click App Designer puts before the first
/// track is refused.
/// </summary>
public class AppDesignerGridRulesVerifyTests(ITestOutputHelper output)
{
    private static readonly Lazy<JsonDocument> Oracle = new(() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scripting", "Designer", "GridRulesVerifyOracle.json"))));

    private static readonly Lazy<GridCells[]> Grids = new(() => Section("grids").Select(GridOf).ToArray());

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

    private static double Number(JsonElement e) => e.ValueKind == JsonValueKind.String
        ? e.GetString() switch
        {
            "NaN" => double.NaN,
            "Inf" => double.PositiveInfinity,
            "-Inf" => double.NegativeInfinity,
            var other => throw new InvalidOperationException(other),
        }
        : e.GetDouble();

    private static double[] Numbers(JsonElement e) => e.EnumerateArray().Select(Number).ToArray();

    private static int[] Ints(JsonElement e) => e.EnumerateArray().Select(static v => v.GetInt32()).ToArray();

    private static int? IntOf(JsonElement e) => e.ValueKind == JsonValueKind.Null ? null : e.GetInt32();

    private static (int, int)? PairOf(JsonElement e) => e.ValueKind == JsonValueKind.Null ? null : (e[0].GetInt32(), e[1].GetInt32());

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

    private static string Pair((GridSpan Label, GridSpan Field)? pair) => pair is { } p ? $"{Text(p.Label)} / {Text(p.Field)}" : "none";

    /// <summary>Whether App Designer's labelled cells put the label or field before the first row or column.</summary>
    private static bool BeforeFirst(JsonElement labelled) =>
        Ints(labelled.GetProperty("label")).Concat(Ints(labelled.GetProperty("field"))).Any(static v => v < 1);

    private void Check(List<string> failures, int total, int minimum)
    {
        output.WriteLine($"{total - failures.Count} of {total} cases agree.");
        foreach (string failure in failures.Take(40))
        {
            output.WriteLine(failure);
        }

        Assert.True(failures.Count == 0, $"{failures.Count} of {total} differ; first: {string.Join(" || ", failures.Take(5))}");
        Assert.True(total >= minimum, $"only {total} cases");
    }

    /// <summary>Every box takes App Designer's cells, or with an edge on a midline, the corrected cells.</summary>
    [Fact]
    public void EveryBoxTakesAppDesignersCells()
    {
        var failures = new List<string>();
        int total = 0;
        foreach (JsonElement c in Section("bounds"))
        {
            total++;
            GridCells cells = Grids.Value[c.GetProperty("g").GetInt32()];
            Rect2D box = RectOf(c.GetProperty("b"));
            GridSpan? expected = SpanOf(c.TryGetProperty("fixed", out JsonElement f) ? f : c.GetProperty("ad"));
            GridSpan? actual = DesignGridRules.SpanOfBounds(box, cells);
            if (actual != expected)
            {
                failures.Add($"grid {c.GetProperty("g")} box {box}: {Text(actual)}, expected {Text(expected)} (App Designer {Text(SpanOf(c.GetProperty("ad")))})");
            }
        }

        Check(failures, total, 1000);
    }

    /// <summary>
    /// In App Designer's own frame at zoom 2 - cells in screen pixels, 200 wide with 20 px gaps, but
    /// the spacing as the property holds it, 10 - a box from 250 to 450 has its end edge in the part of
    /// the gap no zone covers. App Designer (verify-rules/probe_zoom.js) falls back to the first column
    /// and writes the backwards Column [2 1]; the midline divergence's swap gives Column 2, the column
    /// the box covers. Where the edges do not cross, App Designer's cells stand: Column 2 for 450 to
    /// 550, and Column 1 for a box from 447 to 451, wholly in that part of the gap.
    /// </summary>
    [Fact]
    public void AZoomedFrameGivesNoBackwardsSpan()
    {
        var cells = new GridCells(new Rect2D(0, 0, 680, 140), [20, 20, 20, 20], 10, 10, [(20, 100)], [(20, 200), (240, 200), (460, 200)]);
        Assert.Equal("R1 C2", Text(DesignGridRules.SpanOfBounds(new Rect2D(250, 30, 200, 40), cells)));
        Assert.Equal("R1 C2", Text(DesignGridRules.SpanOfBounds(new Rect2D(450, 30, 100, 40), cells)));
        Assert.Equal("R1 C1", Text(DesignGridRules.SpanOfBounds(new Rect2D(447, 30, 4, 40), cells)));
    }

    /// <summary>The selection form maps each box as the single form does, and nothing when one misses.</summary>
    [Fact]
    public void EverySelectionOfBoxesMapsAsItsMembers()
    {
        var failures = new List<string>();
        int total = 0;
        foreach (IGrouping<int, JsonElement> group in Section("bounds").GroupBy(static c => c.GetProperty("g").GetInt32()))
        {
            GridCells cells = Grids.Value[group.Key];
            JsonElement[] items = group.ToArray();
            for (int k = 0; k + 2 < items.Length; k += 3)
            {
                total++;
                Rect2D[] boxes = items.Skip(k).Take(3).Select(static c => RectOf(c.GetProperty("b"))).ToArray();
                GridSpan?[] each = items.Skip(k).Take(3).Select(static c => SpanOf(c.TryGetProperty("fixed", out JsonElement f) ? f : c.GetProperty("ad"))).ToArray();
                IReadOnlyList<GridSpan>? actual = DesignGridRules.SpansOfBounds(boxes, cells);
                string want = each.Any(static s => s is null) ? "none" : string.Join(" | ", each.Select(Text));
                string got = actual is null ? "none" : string.Join(" | ", actual.Select(static s => Text(s)));
                if (want != got)
                {
                    failures.Add($"grid {group.Key} boxes {string.Join(", ", boxes)}: {got}, expected {want}");
                }
            }
        }

        // An empty selection maps to an empty list (GridUtil's x gives [] for []), not to none.
        Assert.Empty(DesignGridRules.SpansOfBounds([], Grids.Value[0])!);
        Check(failures, total, 300);
    }

    /// <summary>Every labelled pair takes App Designer's cells (the corrected ones with a midline edge).</summary>
    [Fact]
    public void EveryLabelledPairTakesAppDesignersCells()
    {
        var failures = new List<string>();
        int total = 0;
        foreach (JsonElement c in Section("labeled"))
        {
            total++;
            GridCells cells = Grids.Value[c.GetProperty("g").GetInt32()];
            Rect2D label = RectOf(c.GetProperty("label"));
            Rect2D field = RectOf(c.GetProperty("field"));
            JsonElement ad = c.TryGetProperty("fixed", out JsonElement f) ? f : c.GetProperty("ad");
            string want = ad.ValueKind == JsonValueKind.Null ? "none" : $"{Text(SpanOf(ad.GetProperty("label")))} / {Text(SpanOf(ad.GetProperty("field")))}";
            string got = Pair(DesignGridRules.LabeledSpans(label, field, cells));
            if (want != got)
            {
                failures.Add($"grid {c.GetProperty("g")} label {label} field {field}: {got}, expected {want}");
            }
        }

        Check(failures, total, 300);
    }

    /// <summary>Every point reads as App Designer's menu reads it (the corrected band where the padding tests disagree).</summary>
    [Fact]
    public void EveryPointReadsAsAppDesignersMenuReadsIt()
    {
        var failures = new List<string>();
        int total = 0;
        foreach (JsonElement c in Section("points"))
        {
            total++;
            GridCells cells = Grids.Value[c.GetProperty("g").GetInt32()];
            double[] p = Numbers(c.GetProperty("p"));
            var point = new Point2D(p[0], p[1]);
            JsonElement[] info = c.GetProperty("info").EnumerateArray().ToArray();
            var expected = new GridPointInfo(IntOf(info[0]), IntOf(info[1]), PairOf(info[2]), PairOf(info[3]), IntOf(info[4]), IntOf(info[5]));
            int?[] adding = c.GetProperty("adding").EnumerateArray().Select(IntOf).ToArray();
            int?[] deleting = c.GetProperty("deleting").EnumerateArray().Select(IntOf).ToArray();
            GridSpan? cell = c.GetProperty("cell").ValueKind == JsonValueKind.Null ? null : CellOf(c.GetProperty("cell"));

            var problems = new List<string>();
            GridSpan? under = DesignGridRules.CellUnderPoint(point, cells);
            if (under != cell)
            {
                problems.Add($"cell {Text(under)}, expected {Text(cell)}");
            }

            GridPointInfo actual = DesignGridRules.PointInfo(point, cells);
            if (actual != expected)
            {
                problems.Add($"info {actual}, expected {expected}");
            }

            if (DesignGridRules.AddingRows(actual) != (adding[0], adding[1]) || DesignGridRules.AddingColumns(actual) != (adding[2], adding[3]))
            {
                problems.Add($"adding {DesignGridRules.AddingRows(actual)} {DesignGridRules.AddingColumns(actual)}, expected ({adding[0]}, {adding[1]}) ({adding[2]}, {adding[3]})");
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

        Check(failures, total, 900);
    }

    /// <summary>Every labelled click, every cell of nine grid sizes (1, 2 and 4 rows by 1, 2 and 4 columns), nine offsets, drawn or not, as CellMousePositionValidator gives it.</summary>
    [Fact]
    public void EveryLabelledClickIsAppDesigners()
    {
        var failures = new List<string>();
        int total = 0;
        foreach (JsonElement c in Section("clicks"))
        {
            total++;
            int[] d = Ints(c.GetProperty("d"));
            GridSpan cell = CellOf(c.GetProperty("cell"));
            JsonElement ad = c.GetProperty("ad");

            // The third divergence: a cell before the first track, which App Designer accepts, is refused.
            string want = ad.ValueKind == JsonValueKind.Null || BeforeFirst(ad)
                ? "none"
                : $"{Text(CellOf(ad.GetProperty("label")))} / {Text(CellOf(ad.GetProperty("field")))}";
            string got = Pair(DesignGridRules.LabeledClick(
                cell, d[0], d[1], c.GetProperty("draw").GetBoolean(), c.GetProperty("rows").GetInt32(), c.GetProperty("cols").GetInt32()));
            if (want != got)
            {
                failures.Add($"{c.GetProperty("rows")}x{c.GetProperty("cols")} cell {Text(cell)} offset [{d[0]} {d[1]}] draw {c.GetProperty("draw")}: {got}, expected {want}");
            }
        }

        Check(failures, total, 880);
    }

    /// <summary>Every span of one to seven tracks against insertion and deletion points 0 to 9, the other axis untouched, as GridUtil shifts them.</summary>
    [Fact]
    public void EveryShiftIsGridUtils()
    {
        var failures = new List<string>();
        int total = 0;
        foreach (JsonElement c in Section("shifts"))
        {
            total++;
            GridAxis axis = c.GetProperty("axis").GetString() == "Row" ? GridAxis.Row : GridAxis.Column;
            int[] s = Ints(c.GetProperty("span"));
            int other = c.GetProperty("other").GetInt32();
            int index = c.GetProperty("index").GetInt32();
            GridSpan span = axis == GridAxis.Row ? new GridSpan(s[0], s[1], other, other) : new GridSpan(other, other, s[0], s[1]);
            GridSpan? inserted = SpanOf(c.GetProperty("inserted"));
            GridSpan? removed = SpanOf(c.GetProperty("removed"));
            GridSpan gotInserted = DesignGridRules.WithTrackInserted(span, axis, index);
            GridSpan? gotRemoved = DesignGridRules.WithTrackRemoved(span, axis, index);
            if (gotInserted != inserted || gotRemoved != removed)
            {
                failures.Add($"{axis} {Text(span)} at {index}: {Text(gotInserted)} / {Text(gotRemoved)}, expected {Text(inserted)} / {Text(removed)}");
            }
        }

        Check(failures, total, 560);
    }

    /// <summary>Every field span against offsets -2 to 2 on both axes, as _adjustLabelLayoutContraints gives the label, none past a 5-by-5 grid.</summary>
    [Fact]
    public void EveryLabelFromItsFieldIsAppDesigners()
    {
        var failures = new List<string>();
        int total = 0;
        foreach (JsonElement c in Section("labelFromField"))
        {
            total++;
            int[] f = Ints(c.GetProperty("field"));
            int[] d = Ints(c.GetProperty("d"));
            int[] row = Ints(c.GetProperty("row"));
            int[] column = Ints(c.GetProperty("column"));
            GridSpan? expected = row[0] >= 1 && row[1] <= 5 && column[0] >= 1 && column[1] <= 5
                ? new GridSpan(row[0], row[1], column[0], column[1])
                : null;
            var field = new GridSpan(f[0], f[1], f[2], f[3]);
            GridSpan? actual = DesignGridRules.LabelFromField(field, d[0], d[1], 5, 5);
            if (actual != expected)
            {
                failures.Add($"field {Text(field)} offset [{d[0]} {d[1]}]: {Text(actual)}, expected {Text(expected)} (App Designer R{row[0]}..{row[1]} C{column[0]}..{column[1]})");
            }
        }

        Check(failures, total, 1350);
    }

    /// <summary>The names of the resize scenarios.</summary>
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
    /// Each resize - single components and labelled pairs over random spans of the corpus's grids, in
    /// or spilling out of their cells, by every handle and the default one, dragged onto, around and
    /// between every track edge and far outside - gives App Designer's spans and preview boxes.
    /// </summary>
    [Theory]
    [MemberData(nameof(ResizeScenarios))]
    public void EveryResizeIsAppDesigners(string name)
    {
        JsonElement r = Section("resizes").Single(e => e.GetProperty("name").GetString() == name);
        GridCells cells = Grids.Value[r.GetProperty("g").GetInt32()];
        GridResizeEntry[] entries = r.GetProperty("entries").EnumerateArray().Select(static e => new GridResizeEntry(
            SpanOf(e.GetProperty("span"))!.Value,
            RectOf(e.GetProperty("box")),
            e.GetProperty("canResize").GetBoolean(),
            e.GetProperty("isLabel").ValueKind == JsonValueKind.True,
            Side(e.GetProperty("h")),
            Side(e.GetProperty("v")))).ToArray();
        ResizeEdges edges = r.GetProperty("dir").GetString() switch
        {
            "" => ResizeEdges.None,
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

        Check(failures, total, 1);

        static GridLabelSide Side(JsonElement e) => e.ValueKind == JsonValueKind.Null ? GridLabelSide.None : e.GetString() switch
        {
            "WEST" => GridLabelSide.West,
            "EAST" => GridLabelSide.East,
            "NORTH" => GridLabelSide.North,
            "SOUTH" => GridLabelSide.South,
            var other => throw new InvalidOperationException(other),
        };
    }

    /// <summary>
    /// Every draw - presses on cell centres, edges and corners of the corpus's grids, pointers level
    /// with the press, far outside and on every track edge, plain and with labels on all four sides -
    /// gives the spans App Designer's componentdraw handlers give.
    /// </summary>
    [Fact]
    public void EveryDrawIsAppDesigners()
    {
        var failures = new List<string>();
        int total = 0;
        int refused = 0;
        foreach (JsonElement r in Section("draws"))
        {
            GridCells cells = Grids.Value[r.GetProperty("g").GetInt32()];
            double[] p = Numbers(r.GetProperty("press"));
            var press = new Point2D(p[0], p[1]);
            GridSpan pressCell = DesignGridRules.CellUnderPoint(press, cells)!.Value;
            bool labelled = r.TryGetProperty("d", out JsonElement dElement);
            int[] d = labelled ? Ints(dElement) : [0, 0];
            GridSpan label = default;
            GridSpan field = default;
            if (labelled)
            {
                (GridSpan Label, GridSpan Field)? click = DesignGridRules.LabeledClick(pressCell, d[0], d[1], componentDraw: true, cells.Rows, cells.Columns);
                if (BeforeFirst(r))
                {
                    // The third divergence: App Designer draws a label in column (row) 0; here the press is refused.
                    total++;
                    refused++;
                    if (click is not null)
                    {
                        failures.Add($"{r.GetProperty("name").GetString()} press: {Pair(click)}, expected none (App Designer {Text(CellOf(r.GetProperty("label")))} / {Text(CellOf(r.GetProperty("field")))})");
                    }

                    continue;
                }

                label = CellOf(r.GetProperty("label"));
                field = CellOf(r.GetProperty("field"));
                if (click != (label, field))
                {
                    failures.Add($"{r.GetProperty("name").GetString()} press: {Pair(click)}, expected {Text(label)} / {Text(field)}");
                    continue;
                }
            }
            else if (CellOf(r.GetProperty("start")) != pressCell)
            {
                failures.Add($"{r.GetProperty("name").GetString()} press cell {Text(pressCell)}, expected {Text(CellOf(r.GetProperty("start")))}");
                continue;
            }

            foreach (JsonElement move in r.GetProperty("moves").EnumerateArray())
            {
                total++;
                double[] q = Numbers(move.GetProperty("p"));
                var pointer = new Point2D(q[0], q[1]);
                string want;
                string got;
                try
                {
                    if (labelled)
                    {
                        want = $"{Text(SpanOf(move.GetProperty("label")))} / {Text(SpanOf(move.GetProperty("field")))}";
                        got = Pair(DesignGridRules.DrawnLabeledSpans(cells, label, field, d[0], d[1], press, pointer));
                    }
                    else
                    {
                        want = Text(SpanOf(move.GetProperty("span")));
                        got = Text(DesignGridRules.DrawnSpan(cells, pressCell, press, pointer));
                    }
                }
                catch (ArgumentException e)
                {
                    want = "a span";
                    got = e.GetType().Name + ": " + e.Message;
                }

                if (want != got)
                {
                    failures.Add($"{r.GetProperty("name").GetString()} (grid {r.GetProperty("g")}, offset [{d[0]} {d[1]}]) from ({p[0]}, {p[1]}) to ({q[0]}, {q[1]}): {got}, expected {want}");
                }
            }
        }

        output.WriteLine($"{refused} labelled presses App Designer accepts with a cell before the first track are refused.");
        Check(failures, total, 1500);
    }
}
