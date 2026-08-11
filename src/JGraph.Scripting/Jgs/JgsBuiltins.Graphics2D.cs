using JGraph.Api;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Data;
using JGraph.Objects;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M55: the everyday two-dimensional chart types, and the file every later chart verb is added to.
/// <para>
/// Each verb here follows the same shape, which is the pattern the milestone set out to establish
/// once: peel a leading axes handle without moving <c>gca</c>, read the positional arguments, read
/// the <c>'Name', value</c> tail against a spelled-out list, and hand back a handle (a column of
/// them when the call drew more than one series). Nothing here knows about rendering — a
/// two-dimensional plot draws itself through <c>IDrawable</c> — and nothing here knows about the
/// inspector, which reads the model's own metadata.
/// </para>
/// </summary>
internal static partial class JgsBuiltins
{
    private static void RegisterGraphics2DBuiltins(JgsEnvironment env, JgsDialect dialect)
    {
        void DefineSilent(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(
                new BuiltinFunction(name, body) { BindsAnsAsStatement = false }));

        DefineSilent("area", (args, line, col) => Area(args, line, col));
        DefineSilent("bar", (args, line, col) => BarChart("bar", horizontal: false, args, line, col));
        DefineSilent("barh", (args, line, col) => BarChart("barh", horizontal: true, args, line, col));

        DefineSilent("pie", (args, line, col) => Pie(args, line, col));
        DefineSilent("bubblechart", (args, line, col) => BubbleChart(args, line, col));
        DefineSilent("bubblelegend", (args, line, col) => BubbleLegendVerb(args, line, col));

        // bubblesize and bubblelim answer a question when handed nothing, so a bare name has to be
        // that answer rather than the function itself — the rule the ruler verbs already follow.
        env.Declare("bubblesize", JgsValue.Function(new BuiltinFunction("bubblesize",
            (args, line, col) => BubbleRange("bubblesize", args, line, col))
        { AutoCallsBare = true }));
        env.Declare("bubblelim", JgsValue.Function(new BuiltinFunction("bubblelim",
            (args, line, col) => BubbleRange("bubblelim", args, line, col))
        { AutoCallsBare = true }));
        DefineSilent("boxchart", (args, line, col) => BoxChartSeries(args, line, col));
        DefineSilent("heatmap", (args, line, col) => HeatmapChart(args, line, col));

        // stairs is the one verb here that can answer with data instead of drawing: asked for two
        // outputs it hands back the stairstep path, which is what a script wants when it means to
        // draw the steps itself or measure them.
        env.Declare("stairs", JgsValue.Function(new BuiltinFunction("stairs", (args, line, col) =>
            Stairs(args, dialect, line, col))
        {
            BindsAnsAsStatement = false,
            MultiOutput = (args, wanted, line, col) => wanted >= 2
                ? StairPath(args, dialect, line, col)
                : [Stairs(args, dialect, line, col)],
        }));
    }

    /// <summary>The properties <c>area</c> accepts after its data, in MATLAB's spellings.</summary>
    private static readonly string[] AreaOptionNames =
    [
        "FaceColor", "EdgeColor", "FaceAlpha", "LineWidth", "LineStyle",
        "BaseValue", "ShowBaseLine", "DisplayName",
    ];

    /// <summary>
    /// <c>area(Y)</c>, <c>area(X, Y)</c>, either followed by a base value and by name/value pairs.
    /// A matrix of Y stacks one band per column, which is what makes the verb worth having over a
    /// filled line: the floor of each band is the running total of the ones beneath it.
    /// </summary>
    private static JgsValue Area(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        return OnAxes(named, () =>
        {
            if (rest.Count == 0)
            {
                throw new JgsRuntimeException(line, col, "area expects the values to fill under: area(y).");
            }

            // The data runs until the first option name. A lone scalar at the end of it is the base
            // value — the only thing a trailing number can be, since x and y are read in pairs.
            int dataEnd = 0;
            while (dataEnd < rest.Count && rest[dataEnd].Type != JgsType.String)
            {
                dataEnd++;
            }

            // Where the option pairs begin stays put; only how much of the head is data moves.
            int optionStart = dataEnd;
            double? baseValue = null;
            if (dataEnd > 1 && rest[dataEnd - 1].Type is JgsType.Number or JgsType.Bool)
            {
                baseValue = rest[dataEnd - 1].AsNumber;
                dataEnd--;
            }

            if (dataEnd is < 1 or > 2)
            {
                throw new JgsRuntimeException(line, col,
                    "area takes area(y), area(x, y), and either one followed by a base value.");
            }

            IReadOnlyList<double[]> columns = SeriesColumns("area", rest[dataEnd - 1], line, col);
            double[] xs = dataEnd == 2
                ? ToDoubles("area", rest[0], line, col)
                : Counting(columns[0].Length);

            if (xs.Length != columns[0].Length)
            {
                throw new JgsRuntimeException(line, col,
                    $"area: x has {xs.Length} values but y has {columns[0].Length} per column.");
            }

            IReadOnlyList<AreaPlot> created = JG.Area(xs, columns);
            foreach (AreaPlot plot in created)
            {
                plot.FaceColor ??= PaletteColorFor(plot);
                if (baseValue is { } floor)
                {
                    plot.BaseValue = floor;
                }
            }

            AreaOptions(created, rest, optionStart, line, col);
            return HandlesFor(created);
        });
    }

    /// <summary>The properties <c>bar</c> and <c>barh</c> accept after their data.</summary>
    private static readonly string[] BarOptionNames =
    [
        "FaceColor", "EdgeColor", "FaceAlpha", "LineWidth", "LineStyle",
        "BarWidth", "BaseValue", "Horizontal", "DisplayName",
    ];

    /// <summary>The bar layout words, which stand alone rather than in a name/value pair.</summary>
    private static readonly string[] BarStyleWords = ["grouped", "stacked", "hist", "histc"];

    /// <summary>
    /// <c>bar(Y)</c>, <c>bar(X, Y)</c>, either followed by a width, a layout word, a colour, and
    /// name/value pairs. <c>barh</c> is the same chart with the axes swapped, which is a property of
    /// the plot rather than a kind of its own — so the two verbs are one function.
    /// <para>
    /// A matrix of Y draws one series per column. Grouped is the default: the series share the slot
    /// each position would otherwise fill alone. Stacked puts each series on the running total of the
    /// ones before it, and the legacy <c>hist</c> and <c>histc</c> words widen the slot until the
    /// bars touch — <c>histc</c> additionally starting them at their position rather than centering
    /// them there.
    /// </para>
    /// </summary>
    private static JgsValue BarChart(
        string name, bool horizontal, IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        return OnAxes(named, () =>
        {
            if (rest.Count == 0)
            {
                throw new JgsRuntimeException(line, col, $"{name} expects the values to draw: {name}(y).");
            }

            // A table names its own columns, which is this build's own form rather than MATLAB's.
            if (rest[0].Type == JgsType.Table)
            {
                JgsValue handle = XyOrTable(name, rest, line, col,
                    (x, y) => JG.Bar(x, y), (t, xc, yc) => JG.Bar(t, xc, yc), valuesAlone: true);
                if (horizontal && JgsHandleRegistry.Require(handle, line, col).Target is BarPlot table)
                {
                    table.Horizontal = true;
                }

                return handle;
            }

            int dataEnd = 0;
            while (dataEnd < rest.Count && rest[dataEnd].Type != JgsType.String)
            {
                dataEnd++;
            }

            // Where the option pairs begin stays put; only how much of the head is data moves.
            int optionStart = dataEnd;
            double? width = null;
            if (dataEnd > 1 && IsScalar(rest[dataEnd - 1]) && !IsScalar(rest[dataEnd - 2]))
            {
                // A trailing scalar is the bar width — unless everything is a scalar, in which case
                // the call is bar(x, y) drawing a single bar and there is no width to find.
                width = rest[dataEnd - 1].AsNumber;
                dataEnd--;
            }

            if (dataEnd is < 1 or > 2)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name} takes {name}(y), {name}(x, y), and either one followed by a bar width.");
            }

            IReadOnlyList<double[]> columns = SeriesColumns(name, rest[dataEnd - 1], line, col);
            double[] xs = dataEnd == 2
                ? ToDoubles(name, rest[0], line, col)
                : Counting(columns[0].Length);

            if (xs.Length != columns[0].Length)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: x has {xs.Length} values but y has {columns[0].Length} per column.");
            }

            // A lone word among the trailing arguments is a layout or a colour rather than half of a
            // pair, and the count says which: what is left over after the pairs must be odd.
            string? style = null;
            JGraph.Core.Drawing.Color? color = null;
            while (optionStart < rest.Count
                && (rest.Count - optionStart) % 2 == 1
                && rest[optionStart].Type == JgsType.String)
            {
                string word = rest[optionStart].AsString;
                if (BarStyleWords.Contains(word, StringComparer.OrdinalIgnoreCase))
                {
                    style = word.ToLowerInvariant();
                }
                else
                {
                    color = OptionColor(rest[optionStart], line, col, name);
                }

                optionStart++;
            }

            IReadOnlyList<BarPlot> created = JG.Bar(xs, columns, stacked: style == "stacked");
            foreach (BarPlot plot in created)
            {
                plot.FillColor = color ?? PaletteColorFor(plot);
                plot.Horizontal = horizontal;
                if (width is { } fraction)
                {
                    plot.BarWidthFraction = fraction;
                }

                if (style is "hist" or "histc")
                {
                    plot.BarWidthFraction = 1.0;
                    plot.PositionOffset = style == "histc" ? 0.5 : 0.0;
                }
            }

            BarOptions(name, created, rest, optionStart, line, col);
            return HandlesFor(created);
        });
    }

    private static void BarOptions(
        string name, IReadOnlyList<BarPlot> plots, IReadOnlyList<JgsValue> args,
        int start, int line, int col)
    {
        if ((args.Count - start) % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, $"{name}: options come in 'Name', value pairs.");
        }

        for (int i = start; i < args.Count; i += 2)
        {
            string option = StrOf(name, args[i], line, col);
            JgsValue value = args[i + 1];

            foreach (BarPlot plot in plots)
            {
                switch (option.ToLowerInvariant())
                {
                    case "facecolor":
                        plot.FillColor = OptionColor(value, line, col, name);
                        break;
                    case "edgecolor":
                        plot.EdgeColor = OptionColor(value, line, col, name);
                        break;
                    case "facealpha":
                        plot.FaceAlpha = NumOf($"{name}: FaceAlpha", value, line, col);
                        break;
                    case "linewidth":
                        plot.EdgeWidth = NumOf($"{name}: LineWidth", value, line, col);
                        break;
                    case "linestyle":
                        plot.Dash = ParseDashWord(StrOf($"{name}: LineStyle", value, line, col), plot.Dash);
                        break;
                    case "barwidth":
                        plot.BarWidthFraction = NumOf($"{name}: BarWidth", value, line, col);
                        break;
                    case "basevalue":
                        plot.Baseline = NumOf($"{name}: BaseValue", value, line, col);
                        break;
                    case "horizontal":
                        plot.Horizontal = JgsGraphicsProperties.ToOnOff(
                            $"{name}: Horizontal", value, line, col);
                        break;
                    case "displayname":
                        SetDisplayName(plot, StrOf($"{name}: DisplayName", value, line, col));
                        break;
                    default:
                        throw new JgsRuntimeException(line, col,
                            $"{name} has no option '{option}'. It takes {string.Join(", ", BarOptionNames)}.");
                }
            }
        }
    }

    /// <summary>The properties <c>pie</c> accepts after its data.</summary>
    private static readonly string[] PieOptionNames =
    [
        "EdgeColor", "LineWidth", "FaceAlpha", "StartAngle", "Clockwise",
        "ShowLabels", "LabelRadius", "Colormap", "DisplayName",
    ];

    /// <summary>How far MATLAB pushes an exploded wedge out, as a fraction of the radius.</summary>
    private const double PieExplodeDistance = 0.1;

    /// <summary>
    /// <c>pie(X)</c>, <c>pie(X, explode)</c>, <c>pie(X, labels)</c>, <c>pie(X, explode, labels)</c>,
    /// any of them on a named axes and followed by name/value pairs.
    /// <para>
    /// The two optional positions tell themselves apart by type rather than by counting: labels
    /// arrive as a cell or a string array, and an explode vector is numbers. MATLAB's explode vector
    /// is a set of flags, so a nonzero entry becomes the tenth of a radius MATLAB pushes a wedge out
    /// by — the model itself takes a distance, the way the bar chart takes a slot shift.
    /// </para>
    /// </summary>
    private static JgsValue Pie(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        return OnAxes(named, () =>
        {
            if (rest.Count == 0)
            {
                throw new JgsRuntimeException(line, col, "pie expects the values to divide: pie(x).");
            }

            double[] values = ToDoubles("pie", rest[0], line, col);
            if (values.Length == 0)
            {
                throw new JgsRuntimeException(line, col, "pie needs at least one value.");
            }

            foreach (double value in values)
            {
                if (value < 0)
                {
                    throw new JgsRuntimeException(line, col,
                        "pie: a wedge cannot have a negative share, so every value must be zero or more.");
                }
            }

            double[]? explode = null;
            string[]? labels = null;
            int optionStart = 1;
            while (optionStart < rest.Count && rest[optionStart].Type != JgsType.String)
            {
                JgsValue next = rest[optionStart];
                if (IsTextList(next))
                {
                    labels = LabelWords(next, values.Length, line, col);
                }
                else if (explode is null && labels is null)
                {
                    explode = Exploded(ToDoubles("pie", next, line, col), values.Length, line, col);
                }
                else
                {
                    throw new JgsRuntimeException(line, col,
                        "pie takes pie(x), pie(x, explode), pie(x, labels), or pie(x, explode, labels).");
                }

                optionStart++;
            }

            PiePlot plot = JG.Pie(values);
            plot.Explode = explode;
            plot.Labels = labels;
            PieOptions(plot, rest, optionStart, line, col);
            return Handle(plot);
        });
    }

    private static void PieOptions(
        PiePlot plot, IReadOnlyList<JgsValue> args, int start, int line, int col)
    {
        if ((args.Count - start) % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, "pie: options come in 'Name', value pairs.");
        }

        for (int i = start; i < args.Count; i += 2)
        {
            string name = StrOf("pie", args[i], line, col);
            JgsValue value = args[i + 1];
            switch (name.ToLowerInvariant())
            {
                case "edgecolor":
                    plot.EdgeColor = OptionColor(value, line, col, "pie");
                    break;
                case "linewidth":
                    plot.LineWidth = NumOf("pie: LineWidth", value, line, col);
                    break;
                case "facealpha":
                    plot.FaceAlpha = NumOf("pie: FaceAlpha", value, line, col);
                    break;
                case "startangle":
                    plot.StartAngle = NumOf("pie: StartAngle", value, line, col);
                    break;
                case "clockwise":
                    plot.Clockwise = JgsGraphicsProperties.ToOnOff("pie: Clockwise", value, line, col);
                    break;
                case "showlabels":
                    plot.ShowLabels = JgsGraphicsProperties.ToOnOff("pie: ShowLabels", value, line, col);
                    break;
                case "labelradius":
                    plot.LabelRadius = NumOf("pie: LabelRadius", value, line, col);
                    break;
                case "colormap":
                    plot.Colormap = OptionColormap("pie", value, line, col);
                    break;
                case "displayname":
                    SetDisplayName(plot, StrOf("pie: DisplayName", value, line, col));
                    break;
                default:
                    throw new JgsRuntimeException(line, col,
                        $"pie has no option '{name}'. It takes {string.Join(", ", PieOptionNames)}.");
            }
        }
    }

    /// <summary>MATLAB's explode flags as the distances the model pushes each wedge out by.</summary>
    private static double[] Exploded(double[] flags, int slices, int line, int col)
    {
        if (flags.Length != slices)
        {
            throw new JgsRuntimeException(line, col,
                $"pie: the explode vector has {flags.Length} entries but there are {slices} values.");
        }

        var offsets = new double[flags.Length];
        for (int i = 0; i < flags.Length; i++)
        {
            offsets[i] = flags[i] != 0 ? PieExplodeDistance : 0;
        }

        return offsets;
    }

    /// <summary>Whether a value is a list of text — a cell, or an array of strings.</summary>
    private static bool IsTextList(JgsValue value)
    {
        if (value.Type == JgsType.Cell)
        {
            return true;
        }

        // A packed array is numbers by construction, so it is never labels — and asking it for
        // boxed elements would throw rather than answer.
        if (value.Type != JgsType.Array || value.IsPacked || value.IsPackedComplex)
        {
            return false;
        }

        JgsValue[] elements = value.BoxedElements();
        return elements.Length > 0 && Array.TrueForAll(elements, e => e.Type == JgsType.String);
    }

    private static string[] LabelWords(JgsValue value, int slices, int line, int col)
    {
        JgsValue[] elements = value.Type == JgsType.Cell ? value.AsCell : value.BoxedElements();
        if (elements.Length != slices)
        {
            throw new JgsRuntimeException(line, col,
                $"pie: there are {elements.Length} labels but {slices} values.");
        }

        var words = new string[elements.Length];
        for (int i = 0; i < elements.Length; i++)
        {
            if (elements[i].Type != JgsType.String)
            {
                throw new JgsRuntimeException(line, col,
                    $"pie: label {i + 1} is not text — labels come as a cell of char rows or a string array.");
            }

            words[i] = elements[i].AsString;
        }

        return words;
    }

    /// <summary>A colormap named by word, or given as an m-by-3 table of components in [0, 1].</summary>
    private static Colormap OptionColormap(string verb, JgsValue value, int line, int col)
    {
        try
        {
            if (value.Type != JgsType.String)
            {
                return Colormap.FromRows("custom", Matrix($"{verb}: Colormap", [value], 0, line, col));
            }

            string word = value.AsString;
            if (!Colormap.TryGetByName(word, out Colormap named))
            {
                throw new JgsRuntimeException(line, col,
                    $"{verb}: there is no colormap called '{word}'. "
                    + $"It knows {string.Join(", ", Colormap.KnownNames)}.");
            }

            return named;
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, ex.Message);
        }
    }

    /// <summary>The properties <c>boxchart</c> accepts after its data.</summary>
    private static readonly string[] BoxChartOptionNames =
    [
        "BoxFaceColor", "BoxFaceAlpha", "BoxEdgeColor", "BoxMedianLineColor", "BoxWidth",
        "LineWidth", "WhiskerLineColor", "WhiskerLineStyle", "MarkerStyle", "MarkerSize",
        "MarkerColor", "Notch", "JitterOutliers", "Orientation", "GroupByColor", "DisplayName",
    ];

    /// <summary>
    /// <c>boxchart(ydata)</c> and <c>boxchart(xgroupdata, ydata)</c>, either on a named axes and
    /// followed by name/value pairs.
    /// <para>
    /// A matrix of ydata alone draws one box per column, at the positions one upward. A grouping
    /// given as text puts the boxes on a category ruler in name order; given as numbers it puts each
    /// box at its own value, which is what lets boxes stand at uneven spacings. <c>GroupByColor</c>
    /// cuts the same observations a second way and draws one chart per colour group, which is why it
    /// is the one option here that answers with more than one handle.
    /// </para>
    /// </summary>
    private static JgsValue BoxChartSeries(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        return OnAxes(named, () =>
        {
            if (rest.Count == 0)
            {
                throw new JgsRuntimeException(line, col,
                    "boxchart expects the observations to summarize: boxchart(ydata).");
            }

            // Two leading values are a grouping and its data; one is the data alone. An option name
            // is text, and text in the first slot is a grouping only when something follows it.
            bool grouped = rest.Count >= 2 && rest[1].Type != JgsType.String;
            List<(string Name, JgsValue Value)> options = Pairs("boxchart", rest, grouped ? 2 : 1, line, col);

            double[] values;
            double[]? positions;
            string[]? names = null;
            if (grouped)
            {
                (positions, names) = BoxGrouping("boxchart: xgroupdata", rest[0], line, col);
                values = ToDoubles("boxchart", rest[1], line, col);
                if (positions.Length != values.Length)
                {
                    throw new JgsRuntimeException(line, col,
                        $"boxchart: the grouping has {positions.Length} values but ydata has "
                        + $"{values.Length}. Each observation names the group it falls in.");
                }
            }
            else
            {
                (positions, values) = BoxColumns(rest[0], line, col);
            }

            IReadOnlyList<BoxChartPlot> created = BoxSeries(positions, values, options, line, col);
            bool horizontal = false;
            foreach (BoxChartPlot plot in created)
            {
                BoxChartOptions(plot, options, line, col);
                horizontal = plot.Horizontal;
            }

            if (names is not null && created.Count > 0)
            {
                // The boxes stand on the group positions, so the ruler along them names the groups.
                AxesModel? axes = created[0].Axes;
                (horizontal ? axes?.PrimaryYAxis : axes?.PrimaryXAxis)?.UseCategories(names);
            }

            return HandlesFor(created);
        });
    }

    /// <summary>
    /// The one chart a plain call draws, or the one per colour group a <c>GroupByColor</c> call
    /// does. Splitting here rather than in the option loop is what keeps every other option applying
    /// to each of the charts the call made.
    /// </summary>
    private static IReadOnlyList<BoxChartPlot> BoxSeries(
        double[]? positions,
        double[] values,
        IReadOnlyList<(string Name, JgsValue Value)> options,
        int line,
        int col)
    {
        JgsValue? colors = null;
        foreach ((string name, JgsValue value) in options)
        {
            if (name.Equals("GroupByColor", StringComparison.OrdinalIgnoreCase))
            {
                colors = value;
            }
        }

        if (colors is not { } grouping)
        {
            return [JG.BoxChart(positions, values)];
        }

        (double[] index, string[]? colorNames) = BoxGrouping("boxchart: GroupByColor", grouping, line, col);
        if (index.Length != values.Length)
        {
            throw new JgsRuntimeException(line, col,
                $"boxchart: GroupByColor has {index.Length} values but ydata has {values.Length}.");
        }

        var levels = new SortedDictionary<double, (List<double> Positions, List<double> Values)>();
        for (int i = 0; i < values.Length; i++)
        {
            if (!levels.TryGetValue(index[i], out (List<double> Positions, List<double> Values) bucket))
            {
                levels[index[i]] = bucket = ([], []);
            }

            bucket.Positions.Add(positions is null ? 1 : positions[i]);
            bucket.Values.Add(values[i]);
        }

        var created = new List<BoxChartPlot>(levels.Count);
        foreach ((double level, (List<double> groupPositions, List<double> groupValues)) in levels)
        {
            BoxChartPlot plot = JG.BoxChart([.. groupPositions], [.. groupValues]);

            // Each colour group is its own series, so it takes its own colour from the palette and
            // its own legend entry — which is the whole point of asking for one.
            plot.BoxFaceColor ??= PaletteColorFor(plot);
            plot.DisplayName = colorNames is not null && level >= 0 && level < colorNames.Length
                ? colorNames[(int)level]
                : FormatNumber(level);
            created.Add(plot);
        }

        return created;
    }

    /// <summary>
    /// A grouping as positions along the ruler, and the names for them when it was written as text.
    /// Text goes on a category ruler at 0, 1, 2 … in name order; numbers stand where they say.
    /// </summary>
    private static (double[] Positions, string[]? Names) BoxGrouping(
        string what, JgsValue value, int line, int col)
    {
        if (value.Type != JgsType.String && !IsTextList(value))
        {
            return (ToDoubles(what, value, line, col), null);
        }

        JgsValue[] elements = value.Type switch
        {
            JgsType.String => [value],
            JgsType.Cell => value.AsCell,
            _ => value.BoxedElements(),
        };

        var labels = new string[elements.Length];
        for (int i = 0; i < elements.Length; i++)
        {
            labels[i] = elements[i].Type == JgsType.String
                ? elements[i].AsString
                : throw new JgsRuntimeException(line, col, $"{what}: element {i + 1} is not text.");
        }

        var distinct = new List<string>();
        foreach (string label in labels)
        {
            if (!distinct.Contains(label, StringComparer.Ordinal))
            {
                distinct.Add(label);
            }
        }

        distinct.Sort(StringComparer.Ordinal);
        return (Array.ConvertAll(labels, label => (double)distinct.IndexOf(label)), [.. distinct]);
    }

    /// <summary>
    /// The observations of a call with no grouping. A matrix is one box per column at the positions
    /// one upward — MATLAB's rule, and the reason a column of a table draws as a single box.
    /// </summary>
    private static (double[]? Positions, double[] Values) BoxColumns(JgsValue value, int line, int col)
    {
        double[] flat = ToDoubles("boxchart", value, line, col);
        if (flat.Length == 0)
        {
            throw new JgsRuntimeException(line, col, "boxchart needs at least one observation.");
        }

        int rows = JgsMatrix.RowCount(value);
        int columns = JgsMatrix.ColCount(value);
        if (rows <= 1 || columns <= 1 || rows * columns != flat.Length)
        {
            return (null, flat);
        }

        var positions = new double[flat.Length];
        for (int i = 0; i < flat.Length; i++)
        {
            positions[i] = (i / rows) + 1;
        }

        return (positions, flat);
    }

    private static void BoxChartOptions(
        BoxChartPlot plot, IReadOnlyList<(string Name, JgsValue Value)> options, int line, int col)
    {
        foreach ((string name, JgsValue value) in options)
        {
            switch (name.ToLowerInvariant())
            {
                case "boxfacecolor":
                    plot.BoxFaceColor = OptionColor(value, line, col, "boxchart");
                    break;
                case "boxfacealpha":
                    plot.BoxFaceAlpha = NumOf("boxchart: BoxFaceAlpha", value, line, col);
                    break;
                case "boxedgecolor":
                    plot.BoxEdgeColor = OptionColor(value, line, col, "boxchart");
                    break;
                case "boxmedianlinecolor":
                    plot.BoxMedianLineColor = OptionColor(value, line, col, "boxchart");
                    break;
                case "boxwidth":
                    plot.BoxWidth = NumOf("boxchart: BoxWidth", value, line, col);
                    break;
                case "linewidth":
                    plot.LineWidth = NumOf("boxchart: LineWidth", value, line, col);
                    break;
                case "whiskerlinecolor":
                    plot.WhiskerLineColor = OptionColor(value, line, col, "boxchart");
                    break;
                case "whiskerlinestyle":
                    plot.WhiskerLineStyle = ParseDashWord(
                        StrOf("boxchart: WhiskerLineStyle", value, line, col), plot.WhiskerLineStyle);
                    break;
                case "markerstyle":
                    plot.MarkerStyle = ParseMarkerWord(
                        StrOf("boxchart: MarkerStyle", value, line, col), plot.MarkerStyle);
                    break;
                case "markersize":
                    plot.MarkerSize = NumOf("boxchart: MarkerSize", value, line, col);
                    break;
                case "markercolor":
                    plot.MarkerColor = OptionColor(value, line, col, "boxchart");
                    break;
                case "notch":
                    plot.Notch = JgsGraphicsProperties.ToOnOff("boxchart: Notch", value, line, col);
                    break;
                case "jitteroutliers":
                    plot.JitterOutliers = JgsGraphicsProperties.ToOnOff(
                        "boxchart: JitterOutliers", value, line, col);
                    break;
                case "orientation":
                    plot.Horizontal = BoxOrientationWord(value, line, col);
                    break;
                case "displayname":
                    SetDisplayName(plot, StrOf("boxchart: DisplayName", value, line, col));
                    break;
                case "groupbycolor":
                    // Read before the charts were made; it says nothing about one of them afterwards.
                    break;
                default:
                    throw new JgsRuntimeException(line, col,
                        $"boxchart has no option '{name}'. It takes "
                        + $"{string.Join(", ", BoxChartOptionNames)}.");
            }
        }
    }

    /// <summary>MATLAB's two orientation words as the one property the model keeps.</summary>
    internal static bool BoxOrientationWord(JgsValue value, int line, int col) =>
        StrOf("boxchart: Orientation", value, line, col).ToLowerInvariant() switch
        {
            "vertical" => false,
            "horizontal" => true,
            var other => throw new JgsRuntimeException(line, col,
                $"boxchart: '{other}' is not an Orientation. It takes vertical or horizontal."),
        };

    /// <summary>The properties <c>heatmap</c> accepts after its data.</summary>
    private static readonly string[] HeatmapOptionNames =
    [
        "XData", "YData", "Colormap", "ColorLimits", "ColorScaling", "ColorMethod", "ColorVariable",
        "ColorbarVisible", "CellLabelColor", "CellLabelFormat", "FontName", "FontSize", "FontColor",
        "GridVisible", "MissingDataColor", "MissingDataLabel", "Title", "XLabel", "YLabel",
    ];

    /// <summary>The ways a table's rows are turned into the one number a cell is coloured by.</summary>
    private static readonly string[] HeatmapColorMethods = ["count", "mean", "sum", "median", "none"];

    /// <summary>
    /// <c>heatmap(cdata)</c>, <c>heatmap(xlabels, ylabels, cdata)</c>, and
    /// <c>heatmap(tbl, xvar, yvar)</c>, any of them on a named axes and followed by name/value pairs.
    /// <para>
    /// The table form is the one that does real work: it groups the rows by the two variables and
    /// reduces each group to a single number the way <c>ColorMethod</c> says, which is what makes a
    /// heatmap a summary of a list rather than a picture of a matrix. Counting is the default, and
    /// naming a <c>ColorVariable</c> makes it the mean of that variable instead — MATLAB's rule.
    /// </para>
    /// </summary>
    private static JgsValue HeatmapChart(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        return OnAxes(named, () =>
        {
            if (rest.Count == 0)
            {
                throw new JgsRuntimeException(line, col,
                    "heatmap expects the values to colour: heatmap(cdata).");
            }

            bool fromTable = rest[0].Type == JgsType.Table;
            bool labelled = !fromTable && rest.Count >= 3 && IsTextList(rest[0]);
            int optionStart = fromTable || labelled ? 3 : 1;
            if (rest.Count < optionStart)
            {
                throw new JgsRuntimeException(line, col,
                    "heatmap takes heatmap(cdata), heatmap(xlabels, ylabels, cdata), "
                    + "or heatmap(tbl, xvar, yvar).");
            }

            List<(string Name, JgsValue Value)> options = Pairs("heatmap", rest, optionStart, line, col);
            double[,] values;
            string[]? xLabels = null;
            string[]? yLabels = null;

            if (fromTable)
            {
                (values, xLabels, yLabels) = CountedTable(rest, options, line, col);
            }
            else
            {
                values = HeatmapGrid(rest[optionStart - 1], line, col);
                if (labelled)
                {
                    xLabels = LabelsOf("heatmap: xlabels", rest[0], values.GetLength(1), line, col);
                    yLabels = LabelsOf("heatmap: ylabels", rest[1], values.GetLength(0), line, col);
                }
            }

            HeatmapPlot plot = JG.Heatmap(values, xLabels, yLabels);
            HeatmapOptions(plot, options, line, col);

            // The names may have moved after the chart was made, and the rulers read them once.
            plot.Axes?.LabelCells(plot);
            return Handle(plot);
        });
    }

    private static void HeatmapOptions(
        HeatmapPlot plot, IReadOnlyList<(string Name, JgsValue Value)> options, int line, int col)
    {
        foreach ((string name, JgsValue value) in options)
        {
            switch (name.ToLowerInvariant())
            {
                case "xdata":
                    plot.XData = LabelsOf("heatmap: XData", value, plot.Columns, line, col);
                    break;
                case "ydata":
                    plot.YData = LabelsOf("heatmap: YData", value, plot.Rows, line, col);
                    break;
                case "colormap":
                    plot.Colormap = OptionColormap("heatmap", value, line, col);
                    break;
                case "colorlimits":
                    plot.ColorLimits = LimitPair("heatmap: ColorLimits", value, line, col);
                    break;
                case "colorscaling":
                    plot.ColorScaling = ScalingWord(value, line, col);
                    break;
                case "colorbarvisible":
                    if (plot.Axes is { } withBar)
                    {
                        withBar.Colorbar.Visible = JgsGraphicsProperties.ToOnOff(
                            "heatmap: ColorbarVisible", value, line, col);
                    }

                    break;
                case "celllabelcolor":
                    SetCellLabelColor(plot, value, line, col);
                    break;
                case "celllabelformat":
                    plot.CellLabelFormat = JgsRulerTicks.ToNetFormat(
                        "heatmap: CellLabelFormat", StrOf("heatmap: CellLabelFormat", value, line, col),
                        line, col);
                    break;
                case "fontname":
                    plot.CellLabelStyle = new TextStyle(
                        plot.CellLabelStyle.Color,
                        plot.CellLabelStyle.FontSize,
                        StrOf("heatmap: FontName", value, line, col),
                        plot.CellLabelStyle.Bold,
                        plot.CellLabelStyle.Italic);
                    break;
                case "fontsize":
                    plot.CellLabelStyle = plot.CellLabelStyle.WithSize(
                        NumOf("heatmap: FontSize", value, line, col));
                    break;
                case "fontcolor":
                    plot.CellLabelStyle = plot.CellLabelStyle.WithColor(
                        OptionColor(value, line, col, "heatmap"));
                    break;
                case "gridvisible":
                    plot.GridVisible = JgsGraphicsProperties.ToOnOff(
                        "heatmap: GridVisible", value, line, col);
                    break;
                case "missingdatacolor":
                    plot.MissingDataColor = OptionColor(value, line, col, "heatmap");
                    break;
                case "missingdatalabel":
                    plot.MissingDataLabel = StrOf("heatmap: MissingDataLabel", value, line, col);
                    break;
                case "title":
                    if (plot.Axes is { } titled)
                    {
                        titled.Title = StrOf("heatmap: Title", value, line, col);
                    }

                    break;
                case "xlabel":
                    if (plot.Axes is { } alongX)
                    {
                        alongX.PrimaryXAxis.Label = StrOf("heatmap: XLabel", value, line, col);
                    }

                    break;
                case "ylabel":
                    if (plot.Axes is { } alongY)
                    {
                        alongY.PrimaryYAxis.Label = StrOf("heatmap: YLabel", value, line, col);
                    }

                    break;
                case "colormethod" or "colorvariable":
                    // Both were read before the chart was built; they say nothing about it afterwards.
                    break;
                default:
                    throw new JgsRuntimeException(line, col,
                        $"heatmap has no option '{name}'. It takes {string.Join(", ", HeatmapOptionNames)}.");
            }
        }
    }

    /// <summary>
    /// MATLAB's three states for the cell text in one word: 'none' turns it off, 'auto' lets each
    /// cell pick black or white against its own fill, and a colour is used as it stands.
    /// </summary>
    /// <summary>
    /// The grid a heatmap is drawn from. A row of numbers is a chart one cell tall and a column is
    /// one cell wide — which is worth writing out, because a vector is the shape a script most often
    /// has to hand and the general matrix reader refuses it.
    /// </summary>
    internal static double[,] HeatmapGrid(JgsValue value, int line, int col)
    {
        int rows = JgsMatrix.RowCount(value);
        int columns = JgsMatrix.ColCount(value);
        if (rows > 1 && columns > 1)
        {
            return Matrix("heatmap", [value], 0, line, col);
        }

        double[] flat = ToDoubles("heatmap", value, line, col);
        if (flat.Length == 0)
        {
            throw new JgsRuntimeException(line, col, "heatmap needs at least one value to colour.");
        }

        // A one-row or one-column shape is kept as it was written; anything the shape does not say
        // (a bare list of numbers) reads across, the way a row does.
        if (rows * columns != flat.Length || rows < 1 || columns < 1)
        {
            rows = 1;
            columns = flat.Length;
        }

        var grid = new double[rows, columns];
        for (int c = 0; c < columns; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                grid[r, c] = flat[(c * rows) + r];
            }
        }

        return grid;
    }

    internal static void SetCellLabelColor(HeatmapPlot plot, JgsValue value, int line, int col)
    {
        if (value.Type == JgsType.String)
        {
            string word = value.AsString;
            if (word.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                plot.ShowCellLabels = false;
                return;
            }

            if (word.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                plot.ShowCellLabels = true;
                plot.CellLabelColor = null;
                return;
            }
        }

        plot.ShowCellLabels = true;
        plot.CellLabelColor = OptionColor(value, line, col, "heatmap");
    }

    /// <summary>
    /// The grid a table describes: one row per value of <c>yvar</c>, one column per value of
    /// <c>xvar</c>, and in each cell whatever <c>ColorMethod</c> makes of the rows that fall there.
    /// </summary>
    private static (double[,] Values, string[] XLabels, string[] YLabels) CountedTable(
        IReadOnlyList<JgsValue> rest, IReadOnlyList<(string Name, JgsValue Value)> options,
        int line, int col)
    {
        Table table = Tbl("heatmap", rest, 0, line, col);
        TableColumn xColumn = HeatmapColumn(table, Str("heatmap", rest, 1, line, col), line, col);
        TableColumn yColumn = HeatmapColumn(table, Str("heatmap", rest, 2, line, col), line, col);

        TableColumn? colorColumn = null;
        foreach ((string name, JgsValue value) in options)
        {
            if (name.Equals("ColorVariable", StringComparison.OrdinalIgnoreCase))
            {
                colorColumn = HeatmapColumn(
                    table, StrOf("heatmap: ColorVariable", value, line, col), line, col);
            }
        }

        string method = colorColumn is null ? "count" : "mean";
        foreach ((string name, JgsValue value) in options)
        {
            if (!name.Equals("ColorMethod", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            method = StrOf("heatmap: ColorMethod", value, line, col).ToLowerInvariant();
            if (!HeatmapColorMethods.Contains(method, StringComparer.Ordinal))
            {
                throw new JgsRuntimeException(line, col,
                    $"heatmap: '{method}' is not a ColorMethod. It takes "
                    + $"{string.Join(", ", HeatmapColorMethods)}.");
            }
        }

        if (method != "count" && colorColumn is null)
        {
            throw new JgsRuntimeException(line, col,
                $"heatmap: ColorMethod '{method}' needs a ColorVariable to work on. "
                + "Without one the only method is count.");
        }

        string[] xLabels = Categories(xColumn);
        string[] yLabels = Categories(yColumn);
        var groups = new List<double>[yLabels.Length, xLabels.Length];
        for (int row = 0; row < table.RowCount; row++)
        {
            int c = Array.IndexOf(xLabels, xColumn.GetText(row));
            int r = Array.IndexOf(yLabels, yColumn.GetText(row));
            if (c < 0 || r < 0)
            {
                continue;
            }

            (groups[r, c] ??= []).Add(colorColumn?.GetNumber(row) ?? 1);
        }

        var values = new double[yLabels.Length, xLabels.Length];
        for (int r = 0; r < yLabels.Length; r++)
        {
            for (int c = 0; c < xLabels.Length; c++)
            {
                values[r, c] = Reduced(method, groups[r, c], line, col);
            }
        }

        return (values, xLabels, yLabels);
    }

    /// <summary>What one cell's worth of rows comes to. An empty cell counts zero but averages nothing.</summary>
    private static double Reduced(string method, List<double>? rows, int line, int col)
    {
        if (method == "count")
        {
            return rows?.Count ?? 0;
        }

        if (rows is null || rows.Count == 0)
        {
            return double.NaN;
        }

        switch (method)
        {
            case "sum":
                return rows.Sum();
            case "mean":
                return rows.Average();
            case "median":
                rows.Sort();
                return rows.Count % 2 == 1
                    ? rows[rows.Count / 2]
                    : (rows[(rows.Count / 2) - 1] + rows[rows.Count / 2]) / 2;
            default:
                return rows.Count == 1
                    ? rows[0]
                    : throw new JgsRuntimeException(line, col,
                        "heatmap: ColorMethod 'none' needs one row per cell, but a cell has "
                        + $"{rows.Count}. Use mean, sum, median, or count.");
        }
    }

    /// <summary>The distinct values of a column, in the order a category axis puts them.</summary>
    private static string[] Categories(TableColumn column)
    {
        var seen = new List<string>();
        var known = new HashSet<string>(StringComparer.Ordinal);
        for (int row = 0; row < column.RowCount; row++)
        {
            string text = column.GetText(row);
            if (known.Add(text))
            {
                seen.Add(text);
            }
        }

        // Numbers sort by value and everything else by its text, so a column of 2, 10, 1 does not
        // come out as 1, 10, 2.
        if (column.Type == ColumnType.Number)
        {
            seen.Sort((a, b) => Numeric(a).CompareTo(Numeric(b)));
        }
        else
        {
            seen.Sort(StringComparer.Ordinal);
        }

        return seen.ToArray();
    }

    private static double Numeric(string text) =>
        double.TryParse(text, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double value)
            ? value
            : double.MaxValue;

    private static TableColumn HeatmapColumn(Table table, string name, int line, int col)
    {
        if (!table.TryGetColumn(name, out TableColumn column))
        {
            throw new JgsRuntimeException(line, col,
                $"heatmap: the table has no variable '{name}'. It has "
                + $"{string.Join(", ", table.ColumnNames)}.");
        }

        return column;
    }

    /// <summary>A list of names, which has to have one per column or row of the grid it names.</summary>
    private static string[] LabelsOf(string what, JgsValue value, int expected, int line, int col)
    {
        JgsValue[] elements = value.Type switch
        {
            JgsType.Cell => value.AsCell,
            JgsType.Array when !value.IsPacked && !value.IsPackedComplex => value.BoxedElements(),
            _ => throw new JgsRuntimeException(line, col,
                $"{what}: names come in a cell of char rows or an array of strings."),
        };

        if (elements.Length != expected)
        {
            throw new JgsRuntimeException(line, col,
                $"{what}: there are {elements.Length} names but {expected} of them are needed.");
        }

        var names = new string[elements.Length];
        for (int i = 0; i < elements.Length; i++)
        {
            names[i] = elements[i].Type == JgsType.String
                ? elements[i].AsString
                : throw new JgsRuntimeException(line, col, $"{what}: name {i + 1} is not text.");
        }

        return names;
    }

    /// <summary>A two-number low/high pair, which is how every limit in MATLAB is written.</summary>
    private static DataRange LimitPair(string what, JgsValue value, int line, int col)
    {
        double[] pair = ToDoubles(what, value, line, col);
        if (pair.Length != 2 || !(pair[0] < pair[1]))
        {
            throw new JgsRuntimeException(line, col,
                $"{what} is two increasing numbers, such as [0 10].");
        }

        return new DataRange(pair[0], pair[1]);
    }

    private static HeatmapScaling ScalingWord(JgsValue value, int line, int col) =>
        StrOf("heatmap: ColorScaling", value, line, col).ToLowerInvariant() switch
        {
            "scaled" => HeatmapScaling.Scaled,
            "scaledcolumns" => HeatmapScaling.ScaledColumns,
            "scaledrows" => HeatmapScaling.ScaledRows,
            "log" => HeatmapScaling.Log,
            var other => throw new JgsRuntimeException(line, col,
                $"heatmap: '{other}' is not a ColorScaling. It takes scaled, scaledcolumns, "
                + "scaledrows, log."),
        };

    /// <summary>The trailing 'Name', value pairs of a call, as pairs.</summary>
    private static List<(string Name, JgsValue Value)> Pairs(
        string verb, IReadOnlyList<JgsValue> args, int start, int line, int col)
    {
        if ((args.Count - start) % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, $"{verb}: options come in 'Name', value pairs.");
        }

        var pairs = new List<(string, JgsValue)>();
        for (int i = start; i < args.Count; i += 2)
        {
            pairs.Add((StrOf(verb, args[i], line, col), args[i + 1]));
        }

        return pairs;
    }

    /// <summary>The properties <c>bubblechart</c> accepts after its data, in MATLAB's spellings.</summary>
    private static readonly string[] BubbleChartOptionNames =
    [
        "MarkerFaceColor", "MarkerEdgeColor", "MarkerFaceAlpha", "MarkerEdgeAlpha",
        "LineWidth", "Marker", "SizeData", "DisplayName",
    ];

    /// <summary>
    /// <c>bubblechart(x, y, sz)</c> and <c>bubblechart(x, y, sz, c)</c>, on a named axes or the current
    /// one, followed by name/value pairs.
    /// <para>
    /// The sizes are data values, not marker areas: they are read against the axes' bubble scale, so
    /// two charts drawn together are comparable and <c>bubblesize</c>/<c>bubblelim</c> re-scale both.
    /// That is the whole difference between this verb and <c>scatter(x, y, sz)</c>, which reads the
    /// same array as areas in points squared.
    /// </para>
    /// </summary>
    private static JgsValue BubbleChart(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        return OnAxes(named, () =>
        {
            int dataEnd = 0;
            while (dataEnd < rest.Count && rest[dataEnd].Type != JgsType.String)
            {
                dataEnd++;
            }

            if (dataEnd < 3)
            {
                throw new JgsRuntimeException(line, col,
                    "bubblechart expects the positions and their sizes: bubblechart(x, y, sz).");
            }

            if (dataEnd > 4)
            {
                throw new JgsRuntimeException(line, col,
                    "bubblechart takes bubblechart(x, y, sz) and bubblechart(x, y, sz, c).");
            }

            // A colour can be written as a word, so the fourth argument being a string does not make it
            // an option name. Options arrive in pairs, so an odd count after the sizes can only mean the
            // extra one is c — the same reading bubblelegend gives its title.
            int optionStart = dataEnd;
            JgsValue? colorArg = null;
            if (dataEnd == 4)
            {
                colorArg = rest[3];
            }
            else if (rest.Count > 3 && (rest.Count - 3) % 2 == 1)
            {
                colorArg = rest[3];
                optionStart = 4;
            }

            double[] xs = ToDoubles("bubblechart", rest[0], line, col);
            double[] ys = ToDoubles("bubblechart", rest[1], line, col);
            double[] sizes = ToDoubles("bubblechart", rest[2], line, col);

            if (ys.Length != xs.Length)
            {
                throw new JgsRuntimeException(line, col,
                    $"bubblechart: x has {xs.Length} values but y has {ys.Length}.");
            }

            if (sizes.Length == 1 && xs.Length != 1)
            {
                // One size for every bubble is a legal call and says "these are all the same".
                sizes = [.. Enumerable.Repeat(sizes[0], xs.Length)];
            }

            if (sizes.Length != xs.Length)
            {
                throw new JgsRuntimeException(line, col,
                    $"bubblechart: sz has {sizes.Length} values but x has {xs.Length}.");
            }

            ScatterPlot plot = JG.BubbleChart(xs, ys, sizes);
            Color seriesColor = PaletteColorFor(plot);
            plot.Color = seriesColor;

            // MATLAB draws bubbles part-transparent by default, and it is not a decoration: overlapping
            // bubbles are the normal case, and opaque ones would hide each other.
            plot.Fill = WithAlpha(seriesColor, 0.6);

            if (colorArg is { } c)
            {
                BubbleColors(plot, c, line, col);
            }

            BubbleChartOptions(plot, Pairs("bubblechart", rest, optionStart, line, col), line, col);
            return JgsHandleRegistry.For(plot);
        });
    }

    /// <summary>
    /// The fourth argument of <c>bubblechart</c>: either one colour for the whole chart or a value per
    /// bubble taken through the colormap.
    /// <para>
    /// Three numbers are read as red, green and blue — MATLAB's reading, and the common one — except on
    /// a chart of exactly three bubbles whose numbers are outside [0, 1], where a colour is the one
    /// thing they cannot be.
    /// </para>
    /// </summary>
    private static void BubbleColors(ScatterPlot plot, JgsValue value, int line, int col)
    {
        int count = plot.Data.Count;
        if (value.Type == JgsType.String)
        {
            Color named = OptionColor(value, line, col, "bubblechart");
            plot.Color = named;
            plot.Fill = WithAlpha(named, (plot.Fill?.A ?? 153) / 255.0);
            return;
        }

        double[] numbers = ToDoubles("bubblechart", value, line, col);
        bool couldBeColor = numbers.Length == 3 && Array.TrueForAll(numbers, v => v is >= 0 and <= 1);
        if (numbers.Length == 3 && (count != 3 || couldBeColor))
        {
            Color rgb = OptionColor(value, line, col, "bubblechart");
            plot.Color = rgb;
            plot.Fill = WithAlpha(rgb, (plot.Fill?.A ?? 153) / 255.0);
            return;
        }

        if (numbers.Length != count)
        {
            throw new JgsRuntimeException(line, col,
                $"bubblechart: c is one colour, or one value per bubble ({count}), but got {numbers.Length}.");
        }

        plot.ColorData = numbers;
    }

    private static void BubbleChartOptions(
        ScatterPlot plot, IReadOnlyList<(string Name, JgsValue Value)> options, int line, int col)
    {
        foreach ((string name, JgsValue value) in options)
        {
            switch (name.ToLowerInvariant())
            {
                case "markerfacecolor":
                    plot.Fill = WithAlpha(
                        OptionColor(value, line, col, "bubblechart"), (plot.Fill?.A ?? 255) / 255.0);
                    break;
                case "markeredgecolor":
                    plot.Color = WithAlpha(
                        OptionColor(value, line, col, "bubblechart"), (plot.Color?.A ?? 255) / 255.0);
                    break;
                case "markerfacealpha":
                    plot.Fill = WithAlpha(
                        plot.Fill ?? plot.Color ?? PaletteColorFor(plot),
                        Fraction("bubblechart: MarkerFaceAlpha", value, line, col));
                    break;
                case "markeredgealpha":
                    plot.Color = WithAlpha(
                        plot.Color ?? PaletteColorFor(plot),
                        Fraction("bubblechart: MarkerEdgeAlpha", value, line, col));
                    break;
                case "linewidth":
                    plot.EdgeWidth = NumOf("bubblechart: LineWidth", value, line, col);
                    break;
                case "marker":
                    plot.Marker = ParseMarkerWord(
                        StrOf("bubblechart: Marker", value, line, col), plot.Marker);
                    break;
                case "sizedata":
                    plot.SizeData = ToDoubles("bubblechart: SizeData", value, line, col);
                    break;
                case "displayname":
                    SetDisplayName(plot, StrOf("bubblechart: DisplayName", value, line, col));
                    break;
                default:
                    throw new JgsRuntimeException(line, col,
                        $"bubblechart has no option '{name}'. It takes "
                        + $"{string.Join(", ", BubbleChartOptionNames)}.");
            }
        }
    }

    /// <summary>A colour at a stated alpha, rather than at its own alpha scaled by one.</summary>
    internal static Color WithAlpha(Color color, double alpha) => new(
        color.R, color.G, color.B, (byte)System.Math.Clamp(System.Math.Round(alpha * 255), 0, 255));

    /// <summary>A number that has to be a fraction, because an alpha outside [0, 1] means nothing.</summary>
    private static double Fraction(string what, JgsValue value, int line, int col)
    {
        double number = NumOf(what, value, line, col);
        if (number is < 0 or > 1)
        {
            throw new JgsRuntimeException(line, col, $"{what} is between 0 and 1, but got {number:G6}.");
        }

        return number;
    }

    /// <summary>
    /// <c>bubblesize</c> and <c>bubblelim</c>: the diameters bubbles are drawn between, and the values
    /// mapped onto them. Both answer with the pair in force when asked nothing, which is what lets a
    /// script read a scale it did not set.
    /// </summary>
    private static JgsValue BubbleRange(string verb, IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        ArityRange(verb, rest, 0, 2, line, col);

        AxesModel axes = named ?? JG.Gca();
        bool sizes = verb == "bubblesize";

        if (rest.Count == 0)
        {
            DataRange current = sizes ? axes.BubbleSizeRange : axes.ResolveBubbleLimits();
            return JgsGraphicsProperties.Row(current.Min, current.Max);
        }

        // Only the value limits can be handed back to the data: the diameters were never automatic.
        if (rest.Count == 1 && rest[0].Type == JgsType.String)
        {
            string word = StrOf(verb, rest[0], line, col);
            if (sizes)
            {
                throw new JgsRuntimeException(line, col,
                    $"bubblesize takes two diameters in points, such as bubblesize([4 25]), but got '{word}'.");
            }

            switch (word.ToLowerInvariant())
            {
                case "auto":
                    axes.BubbleSizeLimits = null;
                    return JgsValue.Null;
                case "manual":
                    axes.BubbleSizeLimits = axes.ResolveBubbleLimits();
                    return JgsValue.Null;
                default:
                    throw new JgsRuntimeException(line, col,
                        $"bubblelim expects 'auto' or 'manual', but got '{word}'.");
            }
        }

        double[] pair = rest.Count == 2
            ? [NumOf(verb, rest[0], line, col), NumOf(verb, rest[1], line, col)]
            : ToDoubles(verb, rest[0], line, col);

        if (pair.Length != 2 || !(pair[1] > pair[0]))
        {
            throw new JgsRuntimeException(line, col,
                $"{verb} takes two increasing numbers, such as {verb}([{(sizes ? "4 25" : "0 100")}]).");
        }

        if (sizes)
        {
            if (pair[0] <= 0)
            {
                throw new JgsRuntimeException(line, col,
                    "bubblesize: the smallest diameter is in points and must be above zero.");
            }

            axes.BubbleSizeRange = new DataRange(pair[0], pair[1]);
        }
        else
        {
            axes.BubbleSizeLimits = new DataRange(pair[0], pair[1]);
        }

        return JgsValue.Null;
    }

    /// <summary>The properties <c>bubblelegend</c> accepts.</summary>
    private static readonly string[] BubbleLegendOptionNames =
    [
        "Location", "NumBubbles", "Style", "Title", "Box", "LimitLabels", "FontSize",
    ];

    /// <summary>
    /// <c>bubblelegend</c>, <c>bubblelegend(title)</c> and either followed by name/value pairs. A lone
    /// leading string is the title rather than an option name, which is why the head is read by count:
    /// options arrive in pairs, so an odd one out can only be the title.
    /// </summary>
    private static JgsValue BubbleLegendVerb(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        AxesModel axes = named ?? JG.Gca();
        BubbleLegendModel legend = axes.BubbleLegend;
        legend.Visible = true;

        int start = 0;
        if (rest.Count % 2 == 1)
        {
            legend.Title = StrOf("bubblelegend", rest[0], line, col);
            start = 1;
        }

        foreach ((string name, JgsValue value) in Pairs("bubblelegend", rest, start, line, col))
        {
            switch (name.ToLowerInvariant())
            {
                case "location":
                    legend.Position = ParseLegendLocation(
                        StrOf("bubblelegend: Location", value, line, col), line, col);
                    break;
                case "numbubbles":
                    legend.NumBubbles = (int)System.Math.Round(
                        NumOf("bubblelegend: NumBubbles", value, line, col));
                    break;
                case "style":
                    legend.Style = BubbleLegendStyleWord(value, line, col);
                    break;
                case "title":
                    legend.Title = StrOf("bubblelegend: Title", value, line, col);
                    break;
                case "box":
                    legend.ShowBorder = JgsGraphicsProperties.ToOnOff("bubblelegend: Box", value, line, col);
                    break;
                case "limitlabels":
                    legend.LimitLabels = JgsGraphicsProperties.ToOnOff(
                        "bubblelegend: LimitLabels", value, line, col);
                    break;
                case "fontsize":
                    legend.TextStyle = legend.TextStyle.WithSize(
                        NumOf("bubblelegend: FontSize", value, line, col));
                    break;
                default:
                    throw new JgsRuntimeException(line, col,
                        $"bubblelegend has no option '{name}'. It takes "
                        + $"{string.Join(", ", BubbleLegendOptionNames)}.");
            }
        }

        return JgsHandleRegistry.For(legend);
    }

    /// <summary>The three words a bubble legend's arrangement goes by, shared with the property table.</summary>
    internal static BubbleLegendStyle BubbleLegendStyleWord(JgsValue value, int line, int col) =>
        StrOf("bubblelegend: Style", value, line, col).ToLowerInvariant() switch
        {
            "vertical" => BubbleLegendStyle.Vertical,
            "horizontal" => BubbleLegendStyle.Horizontal,
            "telescopic" => BubbleLegendStyle.Telescopic,
            var other => throw new JgsRuntimeException(line, col,
                $"bubblelegend: Style is vertical, horizontal or telescopic, but got '{other}'."),
        };

    /// <summary>
    /// <c>stairs</c> draws a line whose samples are joined as steps, so it is the plot verb with one
    /// property set afterwards rather than a chart of its own. Going through <c>plot</c> is what
    /// gives it the whole line surface — line specs, matrix columns, every name/value option — with
    /// nothing to keep in step by hand.
    /// </summary>
    private static JgsValue Stairs(IReadOnlyList<JgsValue> args, JgsDialect dialect, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        return OnAxes(named, () =>
        {
            JgsValue handles = PlotCore(rest, dialect, line, col);
            foreach (JgsValue handle in HandleColumn(handles))
            {
                if (JgsHandleRegistry.Require(handle, line, col).Target is LinePlot plot)
                {
                    plot.Steps = StepMode.Post;
                    plot.Name = "Stairs";
                }
            }

            return handles;
        });
    }

    /// <summary>
    /// <c>[xb, yb] = stairs(...)</c> — the stairstep path as data, drawing nothing. Only the plain
    /// data forms have a path to describe, so a line spec or an option tail is refused rather than
    /// quietly ignored.
    /// </summary>
    private static JgsValue[] StairPath(IReadOnlyList<JgsValue> args, JgsDialect dialect, int line, int col)
    {
        (_, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        if (rest.Count is < 1 or > 2 || rest.Any(value => value.Type == JgsType.String))
        {
            throw new JgsRuntimeException(line, col,
                "[xb, yb] = stairs(y) or stairs(x, y) — the two-output form takes data alone.");
        }

        IReadOnlyList<double[]> columns = SeriesColumns("stairs", rest[^1], line, col);
        double[] xs = rest.Count == 2
            ? ToDoubles("stairs", rest[0], line, col)
            : ImplicitX(dialect, columns[0].Length);

        if (xs.Length != columns[0].Length)
        {
            throw new JgsRuntimeException(line, col,
                $"stairs: x has {xs.Length} values but y has {columns[0].Length} per column.");
        }

        var steppedX = new List<double[]>(columns.Count);
        var steppedY = new List<double[]>(columns.Count);
        foreach (double[] column in columns)
        {
            (double[] pathX, double[] pathY) = StairSteps.Build(xs, column, StepMode.Post);
            steppedX.Add(pathX);
            steppedY.Add(pathY);
        }

        return [Columns(steppedX), Columns(steppedY)];
    }

    /// <summary>One column as a plain row of numbers, several as a matrix of columns.</summary>
    private static JgsValue Columns(IReadOnlyList<double[]> columns)
    {
        if (columns.Count == 1)
        {
            return Numbers(columns[0]);
        }

        return JgsMatrix.Build(columns[0].Length, columns.Count, (r, c) => columns[c][r]);
    }

    /// <summary>The handles a drawing verb answered with, one or many, as a list.</summary>
    private static IReadOnlyList<JgsValue> HandleColumn(JgsValue handles)
    {
        if (handles.Type != JgsType.Array)
        {
            return [handles];
        }

        var list = new List<JgsValue>();
        foreach (JgsValue handle in handles.AsArray)
        {
            list.Add(handle);
        }

        return list;
    }

    /// <summary>Whether a value is a single number rather than an array of them.</summary>
    private static bool IsScalar(JgsValue value) => value.Type is JgsType.Number or JgsType.Bool;

    /// <summary>The columns of a matrix, or the one column a vector is.</summary>
    private static IReadOnlyList<double[]> SeriesColumns(string name, JgsValue value, int line, int col)
    {
        double[] flat = ToDoubles(name, value, line, col);
        if (flat.Length == 0)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs at least one value.");
        }

        int rows = JgsMatrix.RowCount(value);
        int columns = JgsMatrix.ColCount(value);
        if (rows <= 1 || columns <= 1)
        {
            return [flat];
        }

        // Storage is column-major, so a column is already contiguous.
        var result = new List<double[]>(columns);
        for (int c = 0; c < columns; c++)
        {
            var column = new double[rows];
            Array.Copy(flat, c * rows, column, 0, rows);
            result.Add(column);
        }

        return result;
    }

    private static void AreaOptions(
        IReadOnlyList<AreaPlot> plots, IReadOnlyList<JgsValue> args, int start, int line, int col)
    {
        if ((args.Count - start) % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, "area: options come in 'Name', value pairs.");
        }

        for (int i = start; i < args.Count; i += 2)
        {
            string name = StrOf("area", args[i], line, col);
            JgsValue value = args[i + 1];

            // Every band the call drew takes the option, which is what a script means by
            // area(x, Y, 'FaceAlpha', 0.5) — MATLAB applies it to each object it returns.
            foreach (AreaPlot plot in plots)
            {
                switch (name.ToLowerInvariant())
                {
                    case "facecolor":
                        plot.FaceColor = OptionColor(value, line, col, "area");
                        break;
                    case "edgecolor":
                        plot.EdgeColor = OptionColor(value, line, col, "area");
                        break;
                    case "facealpha":
                        plot.FaceAlpha = NumOf("area: FaceAlpha", value, line, col);
                        break;
                    case "linewidth":
                        plot.LineWidth = NumOf("area: LineWidth", value, line, col);
                        break;
                    case "linestyle":
                        plot.Dash = ParseDashWord(StrOf("area: LineStyle", value, line, col), plot.Dash);
                        break;
                    case "basevalue":
                        plot.BaseValue = NumOf("area: BaseValue", value, line, col);
                        break;
                    case "showbaseline":
                        plot.ShowBaseLine = JgsGraphicsProperties.ToOnOff(
                            "area: ShowBaseLine", value, line, col);
                        break;
                    case "displayname":
                        SetDisplayName(plot, StrOf("area: DisplayName", value, line, col));
                        break;
                    default:
                        throw new JgsRuntimeException(line, col,
                            $"area has no option '{name}'. It takes {string.Join(", ", AreaOptionNames)}.");
                }
            }
        }
    }
}
