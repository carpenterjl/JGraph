using JGraph.Api;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Data;
using JGraph.Objects;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The leftovers of ADR 0372 on the making side (open item 343, ADR 0376): R2025b's factory edge on the
/// charts whose faces are filled.
/// </summary>
internal static partial class JgsBuiltins
{
    // --- the tiled layout as a node of the tree ---------------------------------------------------

    /// <summary>
    /// Where each layout's <c>nexttile</c> stands: the last tile it handed out. Until ADR 0376 this was
    /// one count for the whole session, reset by <c>tiledlayout</c>.
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TiledLayoutModel, System.Runtime.CompilerServices.StrongBox<int>> TileCursors = new();

    /// <summary>
    /// Hands out the next tile of <paramref name="layout"/> (or the one at <paramref name="location"/>):
    /// a fixed grid wraps, a flowing one grows until it holds it. <c>nexttile</c>, and <c>axes(t)</c> in
    /// the MATLAB dialect (open item 343, ADR 0376; probe p7fb), come here.
    /// </summary>
    private static AxesModel PlaceTile(TiledLayoutModel layout, int? location, int rowSpan, int columnSpan)
    {
        var cursor = TileCursors.GetValue(layout, static _ => new System.Runtime.CompilerServices.StrongBox<int>(0));
        if (location is { } asked)
        {
            cursor.Value = asked;
        }
        else if (layout.Flow)
        {
            // A flowing layout never wraps: it grows until it holds the tile just asked for. The
            // fixed grid's wrap would pin the cursor at 1 while a 1-by-1 grid stayed 1-by-1, so
            // four tiles would be one tile asked for four times.
            cursor.Value++;
            layout.GrowToHold(cursor.Value + ((rowSpan * columnSpan) - 1));
        }
        else
        {
            cursor.Value = (cursor.Value % layout.TileCount) + 1;
        }

        AxesModel placed = TileInContainer348(layout) // a panel's layout (open item 348, ADR 0382)
            ?? (layout.Parent is FigureModel home && !ReferenceEquals(home, JG.CurrentFigure)
                ? home.AddAxes()
                : JG.NewAxes());
        placed.LayoutTile = cursor.Value;
        placed.LayoutRowSpan = rowSpan;
        placed.LayoutColumnSpan = columnSpan;
        layout.Adopt(placed);

        // Every tile already handed out belongs to the grid as it is now rather than as it was
        // when that tile was made. Laying them all out again is what makes a flowing layout flow
        // instead of pile up, and costs nothing on a fixed one.
        layout.Arrange();
        return placed;
    }

    /// <summary>
    /// <c>axes(t, ...)</c> and <c>axes('Parent', t, ...)</c> naming a tiled layout: the layout's next tile,
    /// with the call's other properties set on it (R2025b, probe p7fb). Null when no layout is named.
    /// </summary>
    private static JgsValue? AxesInLayout(IReadOnlyList<JgsValue> args, int line, int col)
    {
        TiledLayoutModel? layout = null;
        int start = 0;
        if (args.Count >= 1 && !IsTextScalar(args[0]) && JgsHandleRegistry.TryGet(args[0], out JgsHandleEntry? first)
            && first.Target is TiledLayoutModel leading)
        {
            layout = leading;
            start = 1;
        }

        for (int i = start; i + 1 < args.Count; i += 2)
        {
            if (IsTextScalar(args[i]) && TextOf(args[i]).Equals("Parent", StringComparison.OrdinalIgnoreCase)
                && JgsHandleRegistry.TryGet(args[i + 1], out JgsHandleEntry? named))
            {
                layout = named.Target as TiledLayoutModel;
            }
        }

        if (layout is null)
        {
            return null;
        }

        AxesModel placed = PlaceTile(layout, null, 1, 1);
        JgsHandleEntry entry = JgsHandleRegistry.EntryFor(placed);
        for (int i = start; i + 1 < args.Count; i += 2)
        {
            if (!TextOf(args[i]).Equals("Parent", StringComparison.OrdinalIgnoreCase))
            {
                JgsGraphicsProperties.Set(entry, TextOf(args[i]), args[i + 1], line, col);
            }
        }

        return JgsHandleRegistry.For(placed);
    }

    /// <summary>
    /// What R2025b's <c>tiledlayout</c> takes from the figure it is made in: the layout it held, with its
    /// tiles, and the axes placed in the figure itself (open item 343, ADR 0376; probes p7fb, p7fd).
    /// The JGS dialect keeps them, as it always has.
    /// </summary>
    private static void ClearForLayout(FigureModel figure)
    {
        if (!JgsRunningDialect.ThreadIsMatlab)
        {
            return;
        }

        if (figure.TiledLayout is { } old)
        {
            RemoveLayout(old);
        }

        foreach (AxesModel axes in figure.TopLevelAxes().ToList())
        {
            figure.Axes.Remove(axes);
        }
    }

    /// <summary>
    /// Deletes a tiled layout: its tiles leave the figure, their legends and colour bars with them, and
    /// the figure forgets it (open item 343, ADR 0376).
    /// </summary>
    internal static void RemoveLayout(TiledLayoutModel layout)
    {
        GraphObjectLifecycle.NotifyDeleting(layout);
        if (layout.Parent is FigureModel figure)
        {
            foreach (AxesModel tile in layout.Tiles.Where(t => ReferenceEquals(t.Parent, figure) && t.Container is null).ToList())
            {
                figure.Axes.Remove(tile);
            }

            if (ReferenceEquals(figure.TiledLayout, layout))
            {
                figure.TiledLayout = null;
            }
        }

        // A panel's or a tab's layout takes its tiles out of the container (open item 348, ADR 0382).
        if (JgsGraphicsProperties.ContainerOfLayout348(layout) is { Figure: { } holderFigure } container)
        {
            foreach (AxesModel tile in layout.Tiles.Where(t => ReferenceEquals(t.Container, container)).ToList())
            {
                holderFigure.Axes.Remove(tile);
            }

            JgsGraphicsProperties.ForgetContainerLayout348(layout);
        }

        JgsLayoutText.Forget(layout); // its text objects go with it (open item 348)
        layout.Clear();
    }

    /// <summary>R2025b's factory edge colour: 33/255 grey (probe p7fa), the patch's since ADR 0372.</summary>
    internal static Color MatlabFactoryEdge => Color.FromRgb(33, 33, 33);

    /// <summary>R2025b's factory edge width on a filled chart: half a point (probe p7fa).</summary>
    internal const double MatlabFactoryEdgeWidth = 0.5;

    /// <summary>
    /// An area's, a bar's, a histogram's and a rectangle's own edge in the MATLAB dialect: 33/255 grey,
    /// half a point (open item 343, ADR 0376). Applied before the call's options; the JGS dialect keeps
    /// the edge it has always drawn.
    /// </summary>
    private static void MatlabChartEdge(Action<Color> setColor, Action<double> setWidth)
    {
        if (JgsRunningDialect.ThreadIsMatlab)
        {
            setColor(MatlabFactoryEdge);
            setWidth(MatlabFactoryEdgeWidth);
        }
    }

    /// <summary>
    /// <c>plot(tbl, xvar, yvar)</c> in the MATLAB dialect (open item 343, ADR 0376; probe p7fa): each
    /// channel a name, a variable number or a logical vector picking variables; several y variables make
    /// one line each. Every line keeps its table and reads its channels back as given (a number as the
    /// number), and writing <c>XVariable</c> or <c>YVariable</c> redraws it.
    /// </summary>
    private static List<LinePlot> MatlabTablePlot(string verb, IReadOnlyList<JgsValue> args, int line, int col)
    {
        Table table = Tbl(verb, args, 0, line, col);
        List<(string Name, JgsValue? Given)> xs = TablePlotChannels(verb, table, args[1], "X", line, col);
        List<(string Name, JgsValue? Given)> ys = TablePlotChannels(verb, table, args[2], "Y", line, col);
        int lines = System.Math.Max(xs.Count, ys.Count);
        if (xs.Count == 0 || ys.Count == 0 || (xs.Count != lines && xs.Count != 1) || (ys.Count != lines && ys.Count != 1))
        {
            // R2025b's words when both name several (open item 348, ADR 0382; probe p8ea2).
            if (xs.Count > 1 && ys.Count > 1)
            {
                TableLineCount348(line, col, ("X", xs.Count), ("Y", ys.Count));
            }

            throw new JgsRuntimeException(line, col, "MATLAB:graphics:chart:InvalidSubscript",
                "The X and Y subscripts must name the same number of variables, or one of them a single variable.");
        }
        string? spec = args.Count == 4 ? Str(verb, args, 3, line, col) : null;

        var created = new List<LinePlot>();
        var sources = new List<ScatterSource>();
        bool holding = JG.IsHolding;
        try
        {
            for (int i = 0; i < lines; i++)
            {
                (string xName, JgsValue? xGiven) = xs[xs.Count == 1 ? 0 : i];
                (string yName, JgsValue? yGiven) = ys[ys.Count == 1 ? 0 : i];
                // R2025b labels nothing from the table's variable names (probe p7fa); the model's
                // table form names the rulers, so what they said before is put back.
                AxesModel? before = JG.CurrentAxesOrNull;
                (string X, string Y) labels = before is null ? (string.Empty, string.Empty)
                    : (before.PrimaryXAxis.Label, before.PrimaryYAxis.Label);
                LinePlot plot = JG.Plot(table, xName, yName, spec);
                if (plot.Axes is { } drawnIn)
                {
                    drawnIn.PrimaryXAxis.Label = ReferenceEquals(drawnIn, before) ? labels.X : string.Empty;
                    drawnIn.PrimaryYAxis.Label = ReferenceEquals(drawnIn, before) ? labels.Y : string.Empty;
                }

                var source = new ScatterSource { Table = args[0], XVariable = xName, YVariable = yName };
                source.Remember("XVariable", xGiven);
                source.Remember("YVariable", yGiven);
                JgsHandleRegistry.EntryFor(plot).ScatterSource = source;
                created.Add(plot);
                sources.Add(source);
                JG.Hold(true);
            }
        }
        finally
        {
            JG.Hold(holding);
        }

        // One line is unnamed, several take the varying variables' names (open item 348, ADR 0382).
        NameTableLines348(created, sources, VaryingOf348(("XVariable", xs.Count), ("YVariable", ys.Count)));
        return created;
    }

    /// <summary>
    /// The variables one channel of <c>plot(tbl, ...)</c> names, each with what it reads back when that is
    /// not the name: a name (or a cell or string array of them), variable numbers, or a logical vector.
    /// A number past the table's width or not a positive whole one is refused in R2025b's words.
    /// </summary>
    private static List<(string Name, JgsValue? Given)> TablePlotChannels(
        string verb, Table table, JgsValue value, string axis, int line, int col)
    {
        var picked = new List<(string Name, JgsValue? Given)>();
        if (value.Type is JgsType.Number or JgsType.Array && !value.IsCharMatrix && !value.IsStringArray && !value.IsCategorical)
        {
            double[] numbers = ToDoubles(verb, value, line, col);
            if (IsLogicalValue(value))
            {
                if (numbers.Length > table.ColumnNames.Count)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:graphics:chart:SubscriptOutOfBounds",
                        $"Invalid subscript for {axis}. Index exceeds array bounds.");
                }

                for (int i = 0; i < numbers.Length; i++)
                {
                    if (numbers[i] != 0)
                    {
                        picked.Add((table.ColumnNames[i], JgsValue.Number(i + 1)));
                    }
                }

                return picked;
            }

            foreach (double number in numbers)
            {
                if (!(number >= 1) || number != System.Math.Floor(number) || double.IsInfinity(number))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:graphics:chart:InvalidSubscriptValue",
                        $"Invalid subscript for {axis}. Specify a numeric subscript as a positive integer.");
                }

                if (number > table.ColumnNames.Count)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:graphics:chart:SubscriptOutOfBounds",
                        $"Invalid subscript for {axis}. Index exceeds array bounds.");
                }

                picked.Add((table.ColumnNames[(int)number - 1], JgsValue.Number(number)));
            }

            return picked;
        }

        if (value.Type == JgsType.Cell || (value.IsStringArray && !IsTextScalar(value)))
        {
            foreach (string name in FieldNameList(verb, value, line, col))
            {
                picked.Add((TableVariable348(verb, table, name, line, col), null));
            }

            return picked;
        }

        picked.Add((TableVariable348(verb, table, TextOf(value), line, col), null));
        return picked;
    }
}
