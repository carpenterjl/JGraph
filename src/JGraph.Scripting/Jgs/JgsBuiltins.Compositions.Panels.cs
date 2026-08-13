using JGraph.Api;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Data;
using JGraph.Maths;
using JGraph.Objects;
using JGraph.Statistics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M57 wave F: <c>stackedplot</c> and <c>scatterhistogram</c> — two more charts that draw nothing of
/// their own.
/// <para>
/// Both are arrangements. A stacked plot is a column of ordinary axes sharing one x ruler, so that
/// several readings taken against the same run of time can be compared without any of them being
/// squashed by another's scale. A scatter histogram is a scatter with the distribution of each
/// coordinate drawn beside it, on the edge of the picture that coordinate runs along. Neither adds a
/// plot object, a DTO, or a line of rendering: what they add is where the parts go and what is
/// linked to what.
/// </para>
/// <para>
/// MATLAB makes both of these a single chart container object, so a script there writes
/// <c>s = stackedplot(t); s.LineWidth = 2</c>. Here they are real axes holding real plots, which is
/// the recorded divergence: the parts answer to <c>get</c>/<c>set</c> individually and every axes
/// verb works on them, and <c>set</c> takes a whole vector of handles, so the one-line form is
/// <c>set(s, 'LineWidth', 2)</c> over the handles the verb hands back.
/// </para>
/// </summary>
internal static partial class JgsBuiltins
{
    private static void RegisterPanelCompositionBuiltins(JgsEnvironment env)
    {
        DefineComposition(env, "stackedplot", StackedPlot);
        DefineComposition(env, "scatterhistogram", ScatterHistogram);
    }

    // --- stackedplot ----------------------------------------------------------------------------

    /// <summary>
    /// The option names <c>stackedplot</c> knows. They are also what tells a second argument naming
    /// one table variable apart from the start of the name/value tail.
    /// </summary>
    private static readonly string[] StackedPlotOptionNames =
    [
        "XVariable", "DisplayVariables", "DisplayLabels", "Title", "XLabel",
        "Color", "LineWidth", "LineStyle", "Marker", "MarkerSize", "GridVisible",
    ];

    /// <summary>
    /// <c>stackedplot(tbl)</c>, <c>stackedplot(tbl, vars)</c>, <c>stackedplot(Y)</c> and
    /// <c>stackedplot(X, Y)</c>: one panel per variable, stacked down the figure over a shared x.
    /// <para>
    /// The panels are linked along x rather than merely given the same limits, so panning one pans
    /// all of them — which is the whole point of the chart, since the readings are only comparable
    /// while they are looking at the same stretch of x.
    /// </para>
    /// </summary>
    private static JgsValue[] StackedPlot(IReadOnlyList<JgsValue> args, int line, int col)
    {
        RefuseAimedAxes("stackedplot", "column of axes", args, line, col);
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col,
                "stackedplot takes stackedplot(tbl), stackedplot(tbl, vars), stackedplot(Y), "
                + "or stackedplot(X, Y).");
        }

        bool fromTable = args[0].Type == JgsType.Table;
        int optionStart = 1;
        if (fromTable)
        {
            // A second argument written in a cell is always the variables to draw. One written as a
            // bare word is the variables only when the table has a variable by that name and no
            // option is called that — so 'Title' after a table is the option it looks like, and a
            // misspelled option is an unknown option rather than a missing variable.
            optionStart = args.Count > 1 && NamesVariables(args[1], Tbl("stackedplot", args, 0, line, col))
                ? 2
                : 1;
        }
        else if (args.Count > 1 && args[1].Type != JgsType.String)
        {
            optionStart = 2;
        }

        List<(string Name, JgsValue Value)> options = Pairs("stackedplot", args, optionStart, line, col);
        double[] xs;
        var labels = new List<string>();
        var series = new List<double[]>();

        if (fromTable)
        {
            Table table = Tbl("stackedplot", args, 0, line, col);
            string? xVariable = OptionText("stackedplot: XVariable", options, "xvariable", line, col);
            xs = xVariable is null
                ? Counting(table.RowCount)
                : ColumnNumbers(TableVariable("stackedplot", table, xVariable, line, col));

            string[] chosen = optionStart == 2
                ? TableVariableNames("stackedplot", args[1], line, col)
                : OptionNameList("stackedplot: DisplayVariables", options, "displayvariables", line, col)
                    ?? [.. table.ColumnNames];

            foreach (string name in chosen)
            {
                // The variable the panels are drawn against is not itself a panel, whether it was
                // named in the list or simply left in it by taking the whole table.
                if (xVariable is not null && name.Equals(xVariable, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                TableColumn column = TableVariable("stackedplot", table, name, line, col);
                labels.Add(column.Name);
                series.Add(ColumnNumbers(column));
            }
        }
        else
        {
            IReadOnlyList<double[]> columns = SeriesColumns("stackedplot", args[optionStart - 1], line, col);
            xs = optionStart == 2
                ? ToDoubles("stackedplot", args[0], line, col)
                : Counting(columns[0].Length);

            for (int i = 0; i < columns.Count; i++)
            {
                // An array has no names in it, so a panel is known by which column it is — which is
                // also all MATLAB can say about one until DisplayLabels names it.
                labels.Add((i + 1).ToString("G6", System.Globalization.CultureInfo.InvariantCulture));
                series.Add(columns[i]);
            }
        }

        if (series.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "stackedplot needs at least one variable to draw.");
        }

        foreach (double[] values in series)
        {
            if (values.Length != xs.Length)
            {
                throw new JgsRuntimeException(line, col,
                    $"stackedplot: x has {xs.Length} values but a variable has {values.Length}, "
                    + "and a point needs both.");
            }
        }

        string[]? given = OptionNameList("stackedplot: DisplayLabels", options, "displaylabels", line, col);
        if (given is not null)
        {
            if (given.Length != labels.Count)
            {
                throw new JgsRuntimeException(line, col,
                    $"stackedplot: there are {given.Length} display labels but {labels.Count} panels.");
            }

            labels.Clear();
            labels.AddRange(given);
        }

        // The column replaces whatever the figure held, the way drawing any other chart into it does.
        JG.Clf();

        var panels = new List<AxesModel>(series.Count);
        var curves = new List<PlotObject>(series.Count);
        for (int i = 0; i < series.Count; i++)
        {
            AxesModel panel = JG.Subplot(series.Count, 1, i + 1);
            LinePlot curve = panel.AddLine(xs, series[i]);
            curve.Color = PaletteColorFor(curve);
            SetDisplayName(curve, labels[i]);

            // The name of the reading goes down the side of its own panel, which is where MATLAB
            // puts it and the only place it can go when every panel has a scale of its own.
            panel.ActiveYAxis.Label = labels[i];
            panel.Grid.Visible = true;

            // Only the bottom panel says what x is. The others are looking at the same stretch of
            // it, so repeating the numbers four times over would only cost the readings their room.
            panel.PrimaryXAxis.ShowTickLabels = i == series.Count - 1;

            panels.Add(panel);
            curves.Add(curve);
        }

        StackedPlotOptions(curves, panels, options, line, col);

        // Linked rather than merely equal: pan one panel and the rest follow, which is what makes
        // the column one chart instead of several charts that happen to line up today.
        JG.LinkAxes(AxisLinkMode.X, [.. panels]);

        return [HandlesFor(curves), AxesHandles(panels)];
    }

    private static void StackedPlotOptions(
        IReadOnlyList<PlotObject> curves,
        IReadOnlyList<AxesModel> panels,
        IReadOnlyList<(string Name, JgsValue Value)> options,
        int line,
        int col)
    {
        foreach ((string name, JgsValue value) in options)
        {
            switch (name.ToLowerInvariant())
            {
                case "xvariable":
                case "displayvariables":
                case "displaylabels":
                    break;
                case "title":
                    JG.SgTitle(StrOf("stackedplot: Title", value, line, col));
                    break;
                case "xlabel":
                    panels[^1].PrimaryXAxis.Label = StrOf("stackedplot: XLabel", value, line, col);
                    break;
                case "gridvisible":
                    bool on = OnOffWord("stackedplot: GridVisible", value, line, col);
                    foreach (AxesModel panel in panels)
                    {
                        panel.Grid.Visible = on;
                    }

                    break;
                default:
                    foreach (PlotObject plot in curves)
                    {
                        var curve = (LinePlot)plot;
                        switch (name.ToLowerInvariant())
                        {
                            case "color":
                                curve.Color = OptionColor(value, line, col, "stackedplot");
                                break;
                            case "linewidth":
                                curve.LineWidth = NumOf("stackedplot: LineWidth", value, line, col);
                                break;
                            case "linestyle":
                                curve.DashStyle = ParseDashWord(
                                    StrOf("stackedplot: LineStyle", value, line, col), curve.DashStyle);
                                break;
                            case "marker":
                                curve.Marker = ParseMarkerWord(
                                    StrOf("stackedplot: Marker", value, line, col), curve.Marker);
                                break;
                            case "markersize":
                                curve.MarkerSize = NumOf("stackedplot: MarkerSize", value, line, col);
                                break;
                            default:
                                throw new JgsRuntimeException(line, col,
                                    $"stackedplot has no '{name}' option. It knows "
                                    + $"{string.Join(", ", StackedPlotOptionNames)}.");
                        }
                    }

                    break;
            }
        }
    }

    // --- scatterhistogram -----------------------------------------------------------------------

    /// <summary>The option names <c>scatterhistogram</c> knows.</summary>
    private static readonly string[] ScatterHistogramOptionNames =
    [
        "GroupVariable", "GroupData", "NumBins", "Color", "HistogramDisplayStyle",
        "ScatterPlotLocation", "MarkerStyle", "MarkerSize", "MarkerAlpha", "LineStyle", "LineWidth",
        "Title", "XLabel", "YLabel", "XLimits", "YLimits", "LegendVisible",
    ];

    /// <summary>
    /// <c>scatterhistogram(x, y)</c> and <c>scatterhistogram(tbl, xvar, yvar)</c>: the points, with
    /// how each coordinate is distributed drawn along the edge of the picture that coordinate runs
    /// along.
    /// <para>
    /// Each marginal is linked to the scatter along the one ruler they share, so zooming into a
    /// cluster narrows the distributions to the same readings — a marginal that went on describing
    /// the whole sample while the scatter showed part of it would be answering a different question
    /// from the one being looked at.
    /// </para>
    /// </summary>
    private static JgsValue[] ScatterHistogram(IReadOnlyList<JgsValue> args, int line, int col)
    {
        RefuseAimedAxes("scatterhistogram", "grid of axes", args, line, col);
        bool fromTable = args.Count > 0 && args[0].Type == JgsType.Table;
        int optionStart = fromTable ? 3 : 2;
        if (args.Count < optionStart)
        {
            throw new JgsRuntimeException(line, col,
                "scatterhistogram takes scatterhistogram(x, y) or scatterhistogram(tbl, xvar, yvar).");
        }

        List<(string Name, JgsValue Value)> options = Pairs("scatterhistogram", args, optionStart, line, col);
        foreach ((string name, _) in options)
        {
            if (!NamesAnOption(name, ScatterHistogramOptionNames))
            {
                throw new JgsRuntimeException(line, col,
                    $"scatterhistogram has no '{name}' option. It knows "
                    + $"{string.Join(", ", ScatterHistogramOptionNames)}.");
            }
        }

        double[] xs;
        double[] ys;
        string xLabel;
        string yLabel;
        string[]? groups = null;

        if (fromTable)
        {
            Table table = Tbl("scatterhistogram", args, 0, line, col);
            TableColumn xColumn = TableVariable(
                "scatterhistogram", table, Str("scatterhistogram", args, 1, line, col), line, col);
            TableColumn yColumn = TableVariable(
                "scatterhistogram", table, Str("scatterhistogram", args, 2, line, col), line, col);
            xs = ColumnNumbers(xColumn);
            ys = ColumnNumbers(yColumn);
            xLabel = xColumn.Name;
            yLabel = yColumn.Name;

            string? grouping = OptionText("scatterhistogram: GroupVariable", options, "groupvariable", line, col);
            if (grouping is not null)
            {
                TableColumn column = TableVariable("scatterhistogram", table, grouping, line, col);
                groups = new string[column.RowCount];
                for (int row = 0; row < column.RowCount; row++)
                {
                    groups[row] = column.GetText(row);
                }
            }
        }
        else
        {
            xs = ToDoubles("scatterhistogram", args[0], line, col);
            ys = ToDoubles("scatterhistogram", args[1], line, col);
            xLabel = string.Empty;
            yLabel = string.Empty;
            if (OptionValue(options, "groupvariable") is not null)
            {
                throw new JgsRuntimeException(line, col,
                    "scatterhistogram: GroupVariable names a variable of a table. "
                    + "For x and y given as arrays the groups come in GroupData.");
            }
        }

        if (OptionValue(options, "groupdata") is JgsValue grouped)
        {
            groups = GroupWords("scatterhistogram: GroupData", grouped, xs.Length, line, col);
        }

        Paired("scatterhistogram", xs, ys, line, col);
        if (xs.Length == 0)
        {
            throw new JgsRuntimeException(line, col, "scatterhistogram needs at least one point.");
        }

        if (groups is not null && groups.Length != xs.Length)
        {
            throw new JgsRuntimeException(line, col,
                $"scatterhistogram: there are {groups.Length} group labels but {xs.Length} points.");
        }

        string style = OptionWord(
            "scatterhistogram: HistogramDisplayStyle", options, "histogramdisplaystyle", "bar",
            ["bar", "stairs", "smooth"], line, col);
        string location = OptionWord(
            "scatterhistogram: ScatterPlotLocation", options, "scatterplotlocation", "southwest",
            ["southwest", "southeast", "northeast", "northwest"], line, col);

        int[] bins = BinCounts(options, xs.Length, line, col);

        JG.Clf();
        (Cells scatterCell, Cells acrossCell, Cells besideCell) = ScatterHistogramCells(location);
        AxesModel points = JG.Subplot(4, 4, scatterCell.First, scatterCell.Last);
        AxesModel across = JG.Subplot(4, 4, acrossCell.First, acrossCell.Last);
        AxesModel beside = JG.Subplot(4, 4, besideCell.First, besideCell.Last);

        double[] xEdges = SpanningEdges(xs, bins[0]);
        double[] yEdges = SpanningEdges(ys, bins[1]);

        string[] names = groups is null ? [string.Empty] : DistinctInOrder(groups);
        var drawn = new List<PlotObject>(names.Length);
        for (int g = 0; g < names.Length; g++)
        {
            (double[] gx, double[] gy) = groups is null
                ? (xs, ys)
                : Members(xs, ys, groups, names[g]);

            ScatterPlot cloud = points.AddScatter(gx, gy);
            cloud.Color = PaletteColorFor(cloud);
            cloud.Marker = MarkerType.Circle;
            cloud.MarkerSize = 5;
            if (groups is not null)
            {
                SetDisplayName(cloud, names[g]);
            }

            drawn.Add(cloud);
            Color shade = cloud.Color ?? PaletteColorFor(cloud);
            Marginal(across, gx, xEdges, style, shade, sideways: false);
            Marginal(beside, gy, yEdges, style, shade, sideways: true);
        }

        points.PrimaryXAxis.Label = xLabel;
        points.ActiveYAxis.Label = yLabel;

        // Each marginal already stands against the scatter's own ruler, so its copy of the numbers
        // would say the same thing twice; what it has to say is the counts, on its other ruler.
        across.PrimaryXAxis.ShowTickLabels = false;
        beside.ActiveYAxis.ShowTickLabels = false;

        ScatterHistogramOptions(drawn, points, options, line, col);

        JG.LinkAxes(AxisLinkMode.X, points, across);
        JG.LinkAxes(AxisLinkMode.Y, points, beside);
        ScatterHistogramLimits(points, options, line, col);

        if (groups is not null)
        {
            points.Legend.Visible = !options.Any(pair =>
                pair.Name.Equals("LegendVisible", StringComparison.OrdinalIgnoreCase)
                && !OnOffWord("scatterhistogram: LegendVisible", pair.Value, line, col));
        }

        // The scatter is the chart; the marginals describe it. Leaving the scatter current is what
        // makes a following xlabel or title land on the picture rather than on one of its margins.
        JG.Subplot(4, 4, scatterCell.First, scatterCell.Last);

        return [HandlesFor(drawn), AxesHandles([points, across, beside])];
    }

    /// <summary>A rectangular block of the four-by-four grid the chart is laid out on.</summary>
    private readonly record struct Cells(int First, int Last);

    /// <summary>
    /// Where the three parts go for each corner the scatter may sit in. The scatter takes three
    /// quarters of the grid each way; the distribution of a coordinate goes on the far side of the
    /// picture from that corner, so it lies along the ruler it describes and never over the points.
    /// </summary>
    private static (Cells Scatter, Cells Across, Cells Beside) ScatterHistogramCells(string location) =>
        location switch
        {
            // Rows and columns are counted from one; a cell is (row − 1) × 4 + column.
            "southeast" => (Block(2, 4, 2, 4), Block(1, 1, 2, 4), Block(2, 4, 1, 1)),
            "northeast" => (Block(1, 3, 2, 4), Block(4, 4, 2, 4), Block(1, 3, 1, 1)),
            "northwest" => (Block(1, 3, 1, 3), Block(4, 4, 1, 3), Block(1, 3, 4, 4)),
            _ => (Block(2, 4, 1, 3), Block(1, 1, 1, 3), Block(2, 4, 4, 4)),
        };

    private static Cells Block(int firstRow, int lastRow, int firstColumn, int lastColumn) =>
        new(((firstRow - 1) * 4) + firstColumn, ((lastRow - 1) * 4) + lastColumn);

    /// <summary>
    /// Draws one coordinate's distribution beside the scatter. Sideways is the one that runs up the
    /// page: its readings are on y and its counts on x, which is the same chart turned a quarter
    /// turn and not a different one.
    /// </summary>
    private static void Marginal(
        AxesModel axes, double[] values, double[] edges, string style, Color shade, bool sideways)
    {
        double[] counts = Binning.Counts(values, edges);
        switch (style)
        {
            case "stairs":
            {
                (double[] along, double[] height) = Staircase(edges, counts);
                LinePlot steps = sideways ? axes.AddLine(height, along) : axes.AddLine(along, height);
                steps.Color = shade;
                break;
            }

            case "smooth":
            {
                (double[] at, double[] density) = SmoothedShape(values, edges, counts);
                LinePlot curve = sideways ? axes.AddLine(density, at) : axes.AddLine(at, density);
                curve.Color = shade;
                break;
            }

            default:
            {
                var centres = new double[counts.Length];
                for (int i = 0; i < counts.Length; i++)
                {
                    centres[i] = (edges[i] + edges[i + 1]) / 2;
                }

                BarPlot bars = axes.AddBar(centres, counts);
                bars.FillColor = shade;
                bars.BarWidthFraction = 1;
                bars.Horizontal = sideways;
                break;
            }
        }
    }

    /// <summary>
    /// The outline of a histogram as a single polyline: up the left edge of a bin, across its top,
    /// down the right edge. Built here rather than by the stairstep line mode because the sideways
    /// marginal needs the same staircase lying on its side, and a staircase written out as points is
    /// the same points either way round.
    /// </summary>
    private static (double[] Along, double[] Height) Staircase(double[] edges, double[] counts)
    {
        var along = new List<double>((counts.Length * 2) + 2);
        var height = new List<double>((counts.Length * 2) + 2);
        along.Add(edges[0]);
        height.Add(0);
        for (int i = 0; i < counts.Length; i++)
        {
            along.Add(edges[i]);
            height.Add(counts[i]);
            along.Add(edges[i + 1]);
            height.Add(counts[i]);
        }

        along.Add(edges[^1]);
        height.Add(0);
        return ([.. along], [.. height]);
    }

    /// <summary>
    /// The kernel-smoothed density, scaled to the counts it stands in for. A density integrates to
    /// one and a histogram of counts does not, so drawing the raw density beside the bars it
    /// replaces would put a curve a hundred times too short on the same ruler; multiplying by the
    /// sample size and the bin width is what puts the two on the same footing.
    /// </summary>
    private static (double[] At, double[] Density) SmoothedShape(
        double[] values, double[] edges, double[] counts)
    {
        double total = 0;
        foreach (double count in counts)
        {
            total += count;
        }

        double width = (edges[^1] - edges[0]) / (edges.Length - 1);
        // Spanning counts bins and answers with their edges, so 99 of them is the hundred points
        // the curve is drawn through.
        double[] at = Binning.Spanning(edges[0], edges[^1], 99);
        double[] density = EmpiricalDistribution.KernelDensity(
            values,
            weights: null,
            at,
            bandwidth: 0,
            EmpiricalDistribution.Kernel.Normal,
            EmpiricalDistribution.SmoothedKind.Pdf,
            double.NegativeInfinity,
            double.PositiveInfinity,
            EmpiricalDistribution.BoundaryRule.Reflection);

        var scaled = new double[density.Length];
        for (int i = 0; i < density.Length; i++)
        {
            scaled[i] = double.IsFinite(density[i]) ? density[i] * total * width : 0;
        }

        return (at, scaled);
    }

    private static void ScatterHistogramOptions(
        IReadOnlyList<PlotObject> clouds,
        AxesModel points,
        IReadOnlyList<(string Name, JgsValue Value)> options,
        int line,
        int col)
    {
        foreach ((string name, JgsValue value) in options)
        {
            switch (name.ToLowerInvariant())
            {
                case "title":
                    JG.SgTitle(StrOf("scatterhistogram: Title", value, line, col));
                    break;
                case "xlabel":
                    points.PrimaryXAxis.Label = StrOf("scatterhistogram: XLabel", value, line, col);
                    break;
                case "ylabel":
                    points.ActiveYAxis.Label = StrOf("scatterhistogram: YLabel", value, line, col);
                    break;
                case "color":
                    Recolour(clouds, value, line, col);
                    break;
                case "markerstyle":
                    foreach (PlotObject plot in clouds)
                    {
                        ((ScatterPlot)plot).Marker = ParseMarkerWord(
                            StrOf("scatterhistogram: MarkerStyle", value, line, col),
                            ((ScatterPlot)plot).Marker);
                    }

                    break;
                case "markersize":
                    foreach (PlotObject plot in clouds)
                    {
                        ((ScatterPlot)plot).MarkerSize = NumOf(
                            "scatterhistogram: MarkerSize", value, line, col);
                    }

                    break;
                case "markeralpha":
                    foreach (PlotObject plot in clouds)
                    {
                        plot.Opacity = NumOf("scatterhistogram: MarkerAlpha", value, line, col);
                    }

                    break;
                case "linewidth":
                    foreach (PlotObject plot in clouds)
                    {
                        ((ScatterPlot)plot).EdgeWidth = NumOf(
                            "scatterhistogram: LineWidth", value, line, col);
                    }

                    break;
                default:
                    break;
            }
        }
    }

    /// <summary>
    /// Paints the clouds. One colour is every group's colour, which is what MATLAB does with a
    /// single colour and a grouped chart; a list of them is read a group at a time, and runs out by
    /// starting again rather than by refusing to draw.
    /// </summary>
    private static void Recolour(IReadOnlyList<PlotObject> clouds, JgsValue value, int line, int col)
    {
        List<Color> given = ColorList(value, line, col);
        if (given.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "scatterhistogram: Color needs at least one colour.");
        }

        for (int i = 0; i < clouds.Count; i++)
        {
            ((ScatterPlot)clouds[i]).Color = given[i % given.Count];
        }
    }

    private static void ScatterHistogramLimits(
        AxesModel points, IReadOnlyList<(string Name, JgsValue Value)> options, int line, int col)
    {
        foreach ((string name, JgsValue value) in options)
        {
            switch (name.ToLowerInvariant())
            {
                case "xlimits":
                    Pin(points.PrimaryXAxis, LimitPair("scatterhistogram: XLimits", value, line, col));
                    break;
                case "ylimits":
                    Pin(points.ActiveYAxis, LimitPair("scatterhistogram: YLimits", value, line, col));
                    break;
                default:
                    break;
            }
        }
    }

    private static void Pin(AxisModel ruler, DataRange range) => Pin(ruler, range.Min, range.Max);

    // --- shared reading of the arguments both verbs take ------------------------------------------

    /// <summary>The bins each coordinate is counted into: one number for both, or one apiece.</summary>
    private static int[] BinCounts(
        IReadOnlyList<(string Name, JgsValue Value)> options, int count, int line, int col)
    {
        int automatic = Binning.SquareRootChoice(count);
        if (OptionValue(options, "numbins") is not JgsValue given)
        {
            return [automatic, automatic];
        }

        double[] asked = ToDoubles("scatterhistogram: NumBins", given, line, col);
        if (asked.Length is not (1 or 2))
        {
            throw new JgsRuntimeException(line, col,
                "scatterhistogram: NumBins is one number for both coordinates, or [nx ny].");
        }

        var bins = new int[2];
        for (int i = 0; i < 2; i++)
        {
            double value = asked[asked.Length == 1 ? 0 : i];
            if (!(value >= 1) || value != System.Math.Floor(value))
            {
                throw new JgsRuntimeException(line, col,
                    "scatterhistogram: NumBins is a whole number of one or more.");
            }

            bins[i] = (int)value;
        }

        return bins;
    }

    /// <summary>
    /// Bin edges spanning the readings. A sample that is all one value has no span to divide, so it
    /// is given a unit one around itself rather than a run of zero-width bins nothing can fall in.
    /// </summary>
    private static double[] SpanningEdges(IReadOnlyList<double> values, int bins)
    {
        double low = double.PositiveInfinity;
        double high = double.NegativeInfinity;
        foreach (double value in values)
        {
            if (double.IsFinite(value))
            {
                low = System.Math.Min(low, value);
                high = System.Math.Max(high, value);
            }
        }

        if (!double.IsFinite(low) || !double.IsFinite(high))
        {
            (low, high) = (0, 1);
        }

        if (!(high > low))
        {
            (low, high) = (low - 0.5, low + 0.5);
        }

        return Binning.Spanning(low, high, bins);
    }

    /// <summary>The x and y of the points belonging to one group.</summary>
    private static (double[] X, double[] Y) Members(
        double[] xs, double[] ys, string[] groups, string name)
    {
        var x = new List<double>();
        var y = new List<double>();
        for (int i = 0; i < groups.Length; i++)
        {
            if (string.Equals(groups[i], name, StringComparison.Ordinal))
            {
                x.Add(xs[i]);
                y.Add(ys[i]);
            }
        }

        return ([.. x], [.. y]);
    }

    /// <summary>The distinct group names, in the order the readings first mention them.</summary>
    private static string[] DistinctInOrder(string[] groups)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (string name in groups)
        {
            if (seen.Add(name))
            {
                order.Add(name);
            }
        }

        return [.. order];
    }

    /// <summary>
    /// A grouping written as text or as numbers, read as one label per reading. Numbers group by
    /// what they say, so a column of 1s and 2s makes two groups called 1 and 2.
    /// </summary>
    private static string[] GroupWords(string what, JgsValue value, int expected, int line, int col)
    {
        if (value.Type == JgsType.Cell || IsTextList(value))
        {
            return LabelsOf(what, value, expected, line, col);
        }

        double[] numbers = ToDoubles(what, value, line, col);
        return [.. numbers.Select(n => n.ToString("G6", System.Globalization.CultureInfo.InvariantCulture))];
    }

    /// <summary>One named column of a table, or a refusal that says which ones there are.</summary>
    private static TableColumn TableVariable(string verb, Table table, string name, int line, int col)
    {
        if (!table.TryGetColumn(name, out TableColumn column))
        {
            throw new JgsRuntimeException(line, col,
                $"{verb}: the table has no variable '{name}'. It has {string.Join(", ", table.ColumnNames)}.");
        }

        return column;
    }

    /// <summary>A column read as numbers — which is what a text column's category index is for.</summary>
    private static double[] ColumnNumbers(TableColumn column)
    {
        var values = new double[column.RowCount];
        for (int row = 0; row < column.RowCount; row++)
        {
            values[row] = column.GetNumber(row);
        }

        return values;
    }

    /// <summary>
    /// Whether a second positional argument is naming table variables rather than starting the
    /// name/value tail. A cell says so on its own; a bare word has to be a variable of the table and
    /// not the name of an option, because those are the two ways it could have been meant.
    /// </summary>
    private static bool NamesVariables(JgsValue value, Table table)
    {
        if (value.Type == JgsType.Cell || IsTextList(value))
        {
            return true;
        }

        return value.Type == JgsType.String
            && !NamesAnOption(value.AsString, StackedPlotOptionNames)
            && table.TryGetColumn(value.AsString, out _);
    }

    /// <summary>The variable names a second positional argument names, written as text or in a cell.</summary>
    private static string[] TableVariableNames(string verb, JgsValue value, int line, int col) =>
        value.Type == JgsType.String
            ? [value.AsString]
            : LabelsOf($"{verb}: variables", value, ValueCount(value), line, col);

    private static int ValueCount(JgsValue value) => value.Type switch
    {
        JgsType.Cell => value.AsCell.Length,
        JgsType.Array => value.ArrayLength,
        _ => 1,
    };

    /// <summary>The value given for a name/value option, or null when the call did not name it.</summary>
    private static JgsValue? OptionValue(IReadOnlyList<(string Name, JgsValue Value)> options, string wanted)
    {
        foreach ((string name, JgsValue value) in options)
        {
            if (name.Equals(wanted, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }

    private static string? OptionText(
        string what, IReadOnlyList<(string Name, JgsValue Value)> options, string wanted, int line, int col) =>
        OptionValue(options, wanted) is JgsValue value ? StrOf(what, value, line, col) : null;

    private static string[]? OptionNameList(
        string what, IReadOnlyList<(string Name, JgsValue Value)> options, string wanted, int line, int col)
    {
        if (OptionValue(options, wanted) is not JgsValue value)
        {
            return null;
        }

        return value.Type == JgsType.String
            ? [value.AsString]
            : LabelsOf(what, value, ValueCount(value), line, col);
    }

    /// <summary>One of a fixed set of words, or a refusal naming every one of them.</summary>
    private static string OptionWord(
        string what,
        IReadOnlyList<(string Name, JgsValue Value)> options,
        string wanted,
        string fallback,
        string[] allowed,
        int line,
        int col)
    {
        if (OptionValue(options, wanted) is not JgsValue value)
        {
            return fallback;
        }

        string word = StrOf(what, value, line, col).ToLowerInvariant();
        return allowed.Contains(word)
            ? word
            : throw new JgsRuntimeException(line, col,
                $"{what} is one of {string.Join(", ", allowed)}, but got '{word}'.");
    }

    /// <summary>Whether a word is one of a verb's option names.</summary>
    private static bool NamesAnOption(string word, string[] names) =>
        names.Contains(word, StringComparer.OrdinalIgnoreCase);

    /// <summary>The <c>'on'</c>/<c>'off'</c> a switch is written as, which may also be a true or false.</summary>
    private static bool OnOffWord(string what, JgsValue value, int line, int col)
    {
        if (value.Type is JgsType.Bool or JgsType.Number)
        {
            return NumOf(what, value, line, col) != 0;
        }

        string word = StrOf(what, value, line, col);
        return word.ToLowerInvariant() switch
        {
            "on" => true,
            "off" => false,
            _ => throw new JgsRuntimeException(line, col, $"{what} is 'on' or 'off', but got '{word}'."),
        };
    }

    /// <summary>The handles on a set of axes, as the row of numbers a script indexes into.</summary>
    private static JgsValue AxesHandles(IReadOnlyList<AxesModel> axes)
    {
        var handles = new double[axes.Count];
        for (int i = 0; i < axes.Count; i++)
        {
            handles[i] = JgsHandleRegistry.For(axes[i]).AsNumber;
        }

        return JgsMatrix.FromColumnMajor(handles, 1, handles.Length);
    }
}
