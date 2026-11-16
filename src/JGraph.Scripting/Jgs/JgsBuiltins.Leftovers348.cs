using System.Runtime.CompilerServices;
using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Data;
using JGraph.Objects;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The graphics leftovers of ADR 0376 the builtins carry (open item 348, ADR 0382): what a replacing
/// plot resets, the names a table's lines take, and <c>plot3</c> of a table.
/// </summary>
internal static partial class JgsBuiltins
{
    // --- a replacing plot ------------------------------------------------------------------------------

    /// <summary>How deep this thread is in rebuilding a figure file (<c>openfig</c>, <c>hgload</c>): no reset then.</summary>
    [ThreadStatic]
    internal static int BuildingFigureFile348;

    /// <summary>
    /// What R2025b's replacing <c>newplot</c> resets besides what this build always did: the axes'
    /// <c>Tag</c> and <c>UserData</c>, in a MATLAB-dialect script (open item 348, ADR 0382; probe p8ea:
    /// plot, scatter, bar, surf, histogram, imagesc, area, stem, contour, fill, plot3, errorbar, stairs,
    /// semilogy, mesh, image, quiver and pie all reset a tag; line, patch, text and rectangle add
    /// without replacing and keep it; <c>'replacechildren'</c> and hold keep it). R2025b also resets the
    /// axes' colour, font size and line width there, which this build keeps (ADR 0382's still open).
    /// </summary>
    private static void ReplacingResets348(AxesModel axes)
    {
        if (!JgsRunningDialect.ThreadIsMatlab || axes.ReplaceChildrenOnly || BuildingFigureFile348 > 0)
        {
            return;
        }

        axes.Tag = string.Empty;
        axes.UserData = null;
    }

    // --- the names a table's lines take ------------------------------------------------------------------

    /// <summary>
    /// The channels that change from one line of a MATLAB-dialect <c>plot(tbl, ...)</c> or
    /// <c>plot3(tbl, ...)</c> to the next, by property name: a line's <c>DisplayName</c> is their
    /// variables' names joined by a comma, and follows a channel written later (probe p8ec).
    /// </summary>
    private static readonly ConditionalWeakTable<ScatterSource, string[]> VaryingChannels348 = new();

    /// <summary>
    /// Names a table's lines as R2025b does (open item 348, ADR 0382; probes p8ea, p8ec): one line is
    /// unnamed (its legend entry is <c>data1</c>), several take the names of the variables that vary,
    /// <c>'a, b'</c> when two channels vary.
    /// </summary>
    private static void NameTableLines348(IReadOnlyList<PlotObject> lines, IReadOnlyList<ScatterSource> sources, string[] varying)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines.Count > 1 && varying.Length > 0)
            {
                VaryingChannels348.AddOrUpdate(sources[i], varying);
                SetDisplayName(lines[i], TableLineName348(sources[i], varying));
            }
            else
            {
                // Unnamed by its owner, so a legend calls it data1 (probe p8ea).
                lines[i].RestoreDisplayName(string.Empty, manual: false);
                lines[i].Name = string.Empty;
            }
        }
    }

    private static string TableLineName348(ScatterSource source, string[] varying) =>
        string.Join(", ", varying.Select(channel => channel switch
        {
            "XVariable" => source.XVariable,
            "YVariable" => source.YVariable,
            _ => source.ZVariable,
        }));

    /// <summary>A table's line whose channel was written: its name follows when its channels vary (probe p8ec).</summary>
    internal static void RenameTableLine348(JgsHandleEntry entry)
    {
        if (entry.ScatterSource is { } source && VaryingChannels348.TryGetValue(source, out string[]? varying)
            && entry.Target is PlotObject plot)
        {
            SetDisplayName(plot, TableLineName348(source, varying));
        }
    }

    /// <summary>
    /// The channels of a table plot whose variable counts agree, in R2025b's words when they do not:
    /// each channel names one variable or the same number as the others (probe p8ea2).
    /// </summary>
    private static int TableLineCount348(int line, int col, params (string Axis, int Count)[] channels)
    {
        int lines = 1;
        bool fixedCount = false;
        foreach ((string axis, int count) in channels)
        {
            if (count == 1)
            {
                continue;
            }

            if (!fixedCount)
            {
                lines = count;
                fixedCount = true;
            }
            else if (count != lines)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:graphics:chart:NumVariablesMismatch",
                    $"The number of variables specified for '{axis}' did not match the number of variables specified in one of the previous arguments.");
            }
        }

        return lines;
    }

    /// <summary>A variable a table plot names, refused in R2025b's words when the table has none of that name (probe p8ea).</summary>
    private static string TableVariable348(string verb, Table table, string name, int line, int col) =>
        table.TryGetColumn(name, out _)
            ? name
            : throw new JgsRuntimeException(line, col, "MATLAB:graphics:chart:VariableNotFound", $"The variable '{name}' was not found.");

    /// <summary>The channels whose list names more than one variable, by property name.</summary>
    private static string[] VaryingOf348(params (string Property, int Count)[] channels) =>
        [.. channels.Where(static c => c.Count > 1).Select(static c => c.Property)];

    // --- a tiled layout in a panel ---------------------------------------------------------------------

    /// <summary>
    /// <c>tiledlayout(p, m, n, ...)</c> and <c>tiledlayout(p, 'flow', ...)</c> with a panel or a tab
    /// for its parent (open item 348, ADR 0382; probe p8eb): the layout is the container's child and
    /// lays its tiles out in the container's own box, as a figure's lays them out in the figure; a
    /// second layout in the same container takes the first with its tiles, and in the MATLAB dialect
    /// the axes the container held. Null when the call names no container.
    /// </summary>
    private static JgsValue? TiledLayoutInContainer348(IReadOnlyList<JgsValue> args, int line, int col)
    {
        bool parented = args.Count switch
        {
            >= 3 => args[1].Type != JgsType.String && args[2].Type != JgsType.String,
            2 => args[1].Type == JgsType.String,
            _ => false,
        };
        if (!parented || !JgsHandleRegistry.TryGet(args[0], out JgsHandleEntry? held)
            || held.Target is not UiContainerModel container || container.Figure is not { } figure)
        {
            return null;
        }

        IReadOnlyList<JgsValue> rest = [.. args.Skip(1)];
        var layout = new TiledLayoutModel();
        int given;
        if (rest[0].Type == JgsType.String)
        {
            string word = Str("tiledlayout", rest, 0, line, col);
            if (!word.Equals("flow", StringComparison.OrdinalIgnoreCase))
            {
                throw new JgsRuntimeException(line, col,
                    $"tiledlayout takes a row and column count, or 'flow', but got '{word}'.");
            }

            layout.Flow = true;
            given = 1;
        }
        else
        {
            layout.Rows = Count("tiledlayout", rest, 0, line, col);
            layout.Columns = Count("tiledlayout", rest, 1, line, col);
            given = 2;
        }

        JgsHandleEntry entry = JgsHandleRegistry.EntryFor(layout);
        foreach ((string name, JgsValue value) in Pairs("tiledlayout", rest, given, line, col))
        {
            JgsGraphicsProperties.Set(entry, name, value, line, col);
        }

        if (JgsGraphicsProperties.LayoutIn348(container) is { } old)
        {
            RemoveLayout(old);
        }

        if (JgsRunningDialect.ThreadIsMatlab)
        {
            foreach (AxesModel axes in container.ContainedAxes())
            {
                figure.Axes.Remove(axes);
            }
        }

        JgsGraphicsProperties.PlaceLayoutIn348(layout, container);
        JgsGraphicsProperties.LayoutMade(layout);
        JgsGraphicsProperties.MatlabLayoutTextStyles348(layout);
        JG.TouchFigure(figure);
        return JgsHandleRegistry.For(layout);
    }

    /// <summary>
    /// A tile of a layout in a panel or a tab: an axes of the container's figure placed in the
    /// container, made current when its figure is (open item 348, ADR 0382). Null for a figure's layout.
    /// </summary>
    private static AxesModel? TileInContainer348(TiledLayoutModel layout)
    {
        if (JgsGraphicsProperties.ContainerOfLayout348(layout) is not { Figure: { } figure } container)
        {
            return null;
        }

        AxesModel placed = figure.AddAxes();
        placed.Container = container;
        JgsGraphicsProperties.GridMembershipChanged(placed);
        if (JG.CurrentFigureNumberOrZero > 0 && ReferenceEquals(JG.CurrentFigure, figure))
        {
            JG.MakeCurrent(placed);
        }
        else
        {
            JG.TouchFigure(figure);
        }

        return placed;
    }

    // --- a tiled layout's text ---------------------------------------------------------------------------

    /// <summary>
    /// <c>title(t, ...)</c>, <c>subtitle(t, ...)</c>, <c>xlabel(t, ...)</c> and <c>ylabel(t, ...)</c> naming
    /// a tiled layout (open item 348, ADR 0382; probes p8eb, p8ed): the words, then the text's own
    /// properties as name-value pairs. A MATLAB-dialect script gets the layout's
    /// <c>matlab.graphics.layout.Text</c> back; the JGS dialect the layout, as ADR 0376's title did.
    /// Null when the first argument names no layout.
    /// </summary>
    private static JgsValue? LayoutTextCall348(string role, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0 || IsTextScalar(args[0]) || !JgsHandleRegistry.TryGet(args[0], out JgsHandleEntry? grid)
            || grid.Target is not TiledLayoutModel layout)
        {
            return null;
        }

        if (args.Count == 1)
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:{role.ToLowerInvariant()}:InvalidNumberOfInputs", "Not enough input arguments.");
        }

        if (!JgsRunningDialect.ThreadIsMatlab)
        {
            if (args.Count != 2)
            {
                throw new JgsRuntimeException(line, col, $"{role.ToLowerInvariant()} of a tiled layout takes the layout and its text: {role.ToLowerInvariant()}(t, txt).");
            }

            JgsGraphicsProperties.Set(grid, role, args[1], line, col);
            return JgsHandleRegistry.For(layout);
        }

        JgsLayoutText text = JgsLayoutText.For(layout, role);
        text.WriteString(args[1], line, col);
        JgsValue handle = JgsHandleRegistry.For(text);
        JgsHandleEntry entry = JgsHandleRegistry.Require(handle, line, col);
        for (int i = 2; i + 1 < args.Count; i += 2)
        {
            string name = StrOf("text option", args[i], line, col);
            if (!JgsGraphicsProperties.IsTextPropertyName348(name))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:hg:InvalidProperty", $"Unrecognized property {name} for class Text.");
            }

            JgsGraphicsProperties.Set(entry, name, args[i + 1], line, col);
        }

        return handle;
    }

    // --- plot3 of a table ----------------------------------------------------------------------------

    /// <summary>
    /// <c>plot3(tbl, xvar, yvar, zvar, Name, Value)</c> in the MATLAB dialect (open item 348, ADR 0382;
    /// probes p8ea, p8ea2, p8ec): each channel a name, names, variable numbers or a logical vector, as
    /// <c>plot(tbl, ...)</c> reads them; a line per variable where a channel names several; no line
    /// spec (R2025b refuses anything but three subscripts and name-value pairs); no ruler labelled; each
    /// line keeps its table and answers <c>SourceTable</c>, <c>XVariable</c>, <c>YVariable</c> and
    /// <c>ZVariable</c>. Null when the call is not that form.
    /// </summary>
    private static JgsValue? MatlabTablePlot3_348(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (!JgsRunningDialect.ThreadIsMatlab || args.Count == 0 || args[0].Type != JgsType.Table)
        {
            return null;
        }

        int rest = args.Count - 4;
        bool pairs = rest >= 0 && rest % 2 == 0;
        for (int i = 4; pairs && i < args.Count; i += 2)
        {
            pairs = IsTextScalar(args[i]) && LineOptionNames.Contains(TextOf(args[i]));
        }

        if (!pairs)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:plot3:InvalidTableArguments",
                "Invalid number of input arguments. Specify three table subscripts after the table.");
        }

        Table table = Tbl("plot3", args, 0, line, col);
        List<(string Name, JgsValue? Given)> xs = TablePlotChannels("plot3", table, args[1], "X", line, col);
        List<(string Name, JgsValue? Given)> ys = TablePlotChannels("plot3", table, args[2], "Y", line, col);
        List<(string Name, JgsValue? Given)> zs = TablePlotChannels("plot3", table, args[3], "Z", line, col);
        int lines = TableLineCount348(line, col, ("X", xs.Count), ("Y", ys.Count), ("Z", zs.Count));
        if (xs.Count == 0 || ys.Count == 0 || zs.Count == 0)
        {
            return JgsValue.Array([]);
        }

        var created = new List<Line3DPlot>();
        var sources = new List<ScatterSource>();
        bool holding = JG.IsHolding;
        try
        {
            for (int i = 0; i < lines; i++)
            {
                (string xName, JgsValue? xGiven) = xs[xs.Count == 1 ? 0 : i];
                (string yName, JgsValue? yGiven) = ys[ys.Count == 1 ? 0 : i];
                (string zName, JgsValue? zGiven) = zs[zs.Count == 1 ? 0 : i];
                Line3DPlot plot;
                try
                {
                    plot = JG.Plot3(
                        TableSeries.GetNumbers(table, xName),
                        TableSeries.GetNumbers(table, yName),
                        TableSeries.GetNumbers(table, zName));
                }
                catch (ArgumentException ex)
                {
                    throw new JgsRuntimeException(line, col, ex.Message);
                }

                var source = new ScatterSource { Table = args[0], XVariable = xName, YVariable = yName, ZVariable = zName };
                source.Remember("XVariable", xGiven);
                source.Remember("YVariable", yGiven);
                source.Remember("ZVariable", zGiven);
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

        NameTableLines348(created, sources, VaryingOf348(("XVariable", xs.Count), ("YVariable", ys.Count), ("ZVariable", zs.Count)));
        for (int i = 4; i + 1 < args.Count; i += 2)
        {
            foreach (Line3DPlot plot in created)
            {
                ApplyLineOption("plot3", plot, TextOf(args[i]), args[i + 1], line, col);
            }
        }

        return HandlesFor(created);
    }
}
